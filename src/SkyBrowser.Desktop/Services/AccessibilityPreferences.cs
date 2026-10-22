using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkyBrowser.Core.Configuration;

namespace SkyBrowser.Desktop.Services;

/// <summary>Applies persisted readability preferences and follows Windows contrast and animation settings.</summary>
public static class AccessibilityPreferences
{
    public static readonly DependencyProperty MotionReducedProperty = DependencyProperty.RegisterAttached(
        "MotionReduced", typeof(bool), typeof(AccessibilityPreferences),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetMotionReduced(DependencyObject element) => (bool)element.GetValue(MotionReducedProperty);
    public static void SetMotionReduced(DependencyObject element, bool value) => element.SetValue(MotionReducedProperty, value);
    private static readonly ConditionalWeakTable<Window, WindowLayout> Layouts = new();
    private static Dictionary<string, Color>? _palette;
    public static double InterfaceScale { get; private set; } = 1;
    public static bool UseHighContrast { get; private set; }
    public static bool ReduceMotion { get; private set; }
    public static bool HighContrast => UseHighContrast || SystemParameters.HighContrast;
    public static bool MotionReduced => ReduceMotion || !SystemParameters.ClientAreaAnimation;

    static AccessibilityPreferences()
    {
        // The handler retains no windows; the application owns the lifetime.
        SystemParameters.StaticPropertyChanged += SystemSettingsChanged;
    }

    private static void SystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
        {
            if (Application.Current is { Dispatcher.HasShutdownStarted: false } application)
            {
                application.Dispatcher.BeginInvoke(Refresh);
            }
        }
    }

    public static void Apply(ApplicationOptions options)
    {
        InterfaceScale = options.InterfaceScale;
        UseHighContrast = options.UseHighContrast;
        ReduceMotion = options.ReduceMotion;
        Refresh();
    }

    private static void Refresh()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var palette = application.Resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Palette.xaml", StringComparison.Ordinal) == true);
        if (palette is not null)
        {
            _palette ??= palette.Keys.OfType<string>().Where(key => key.StartsWith("UiColor", StringComparison.Ordinal))
                .ToDictionary(key => key, key => (Color)palette[key]);
            foreach (var (key, original) in _palette)
            {
                string hex = key[7..];
                Color color = original;
                if (HighContrast)
                {
                    bool surface = hex is "24443F" or "1B302F" or "3B6F61";
                    bool border = hex is "293548" or "344256" or "253044" or "29394D" or "344C5E" or "2A3A4D";
                    bool darkInk = hex is "0A2925" or "142131";
                    color = !surface && !darkInk && (border || Math.Max(original.R, Math.Max(original.G, original.B)) >= 100)
                        ? SystemColors.WindowTextColor : SystemColors.WindowColor;
                }
                else if (hex is "8D9AAD" or "73879C" or "7D8CA1" or "65758A" or "64758C" or "677B91" or "6F8298")
                {
                    // Secondary text must remain readable at the application's small text sizes.
                    color = Color.FromRgb(180, 195, 212);
                }
                palette[key] = color;
                // Resource brushes may have been frozen by WPF after template
                // sharing. Replace them so DynamicResource consumers refresh too.
                palette["UiBrush" + hex] = new SolidColorBrush(color);
            }

            var controls = application.Resources.MergedDictionaries.FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.EndsWith("Controls.xaml", StringComparison.Ordinal) == true);
            if (controls is not null)
            {
                foreach (var (key, hex) in new Dictionary<string, string>
                {
                    ["PanelBrush"] = "101722",
                    ["MutedBrush"] = "8D9AAD",
                    ["AccentBrush"] = "8BE2C3",
                    ["ControlSurfaceBrush"] = "182333",
                    ["ControlHoverBrush"] = "223347",
                    ["ControlBorderBrush"] = "344256",
                    ["ControlTextBrush"] = "DFE8F2",
                    ["SelectionBrush"] = "24443F",
                    ["RailBrush"] = "2C3D52",
                    ["ScrollThumbBrush"] = "526B80"
                })
                {
                    controls[key] = palette["UiBrush" + hex];
                }
            }
        }

        foreach (Window window in application.Windows)
        {
            if (window.IsLoaded)
            {
                ApplyToWindow(window);
            }
        }
    }

    internal static void ApplyToWindow(Window window)
    {
        SetMotionReduced(window, MotionReduced);
        if (!Layouts.TryGetValue(window, out var layout))
        {
            if (InterfaceScale == 1 || window.Content is not FrameworkElement root)
            {
                return;
            }
            layout = new WindowLayout(window, root);
            Layouts.Add(window, layout);
        }
        layout.Apply(InterfaceScale);
    }

    private sealed class WindowLayout
    {
        private readonly Window _window;
        private readonly FrameworkElement _root;
        private readonly double _originalWidth, _originalHeight, _baselineWidth, _baselineHeight;
        private readonly Transform _originalTransform;
        private ScrollViewer? _scroll;

        public WindowLayout(Window window, FrameworkElement root)
        {
            _window = window;
            _root = root;
            _originalWidth = root.Width;
            _originalHeight = root.Height;
            _originalTransform = root.LayoutTransform;
            _baselineWidth = Math.Max(1, root.ActualWidth);
            _baselineHeight = Math.Max(1, root.ActualHeight);
        }

        public void Apply(double scale)
        {
            if (scale == 1)
            {
                if (_scroll is not null)
                {
                    _scroll.Content = null;
                    _window.Content = _root;
                    _scroll = null;
                }
                _root.Width = _originalWidth;
                _root.Height = _originalHeight;
                _root.LayoutTransform = _originalTransform;
                return;
            }

            if (_scroll is null)
            {
                _window.Content = null;
                _scroll = new ScrollViewer
                {
                    Content = _root,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    CanContentScroll = false,
                    Focusable = false
                };
                _scroll.SizeChanged += (_, _) => Resize();
                _window.Content = _scroll;
            }
            _root.LayoutTransform = new ScaleTransform(scale, scale);
            Resize();
        }

        private void Resize()
        {
            if (_scroll is null)
            {
                return;
            }
            // Keep list/metadata viewports finite so zoom cannot disable virtualization.
            _root.Width = Math.Max(_baselineWidth, _scroll.ActualWidth / InterfaceScale - _root.Margin.Left - _root.Margin.Right);
            _root.Height = Math.Max(_baselineHeight, _scroll.ActualHeight / InterfaceScale - _root.Margin.Top - _root.Margin.Bottom);
        }
    }
}
