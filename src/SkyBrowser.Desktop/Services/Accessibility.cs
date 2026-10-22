using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SkyBrowser.Desktop.Services;

/// <summary>Shared focus restoration, custom-view focus outlines and debounced screen-reader announcements.</summary>
public static class Accessibility
{
    private static readonly ConditionalWeakTable<FrameworkElement, DispatcherTimer> Announcements = new();

    /// <summary>Debounces an explicitly requested readout; pointer hover never calls this method.</summary>
    public static void Announce(FrameworkElement element)
    {
        var timer = Announcements.GetValue(element, target =>
        {
            AutomationProperties.SetLiveSetting(target, AutomationLiveSetting.Polite);
            var delay = new DispatcherTimer(DispatcherPriority.Background, target.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                if (target.IsVisible && Window.GetWindow(target)?.IsActive == true &&
                    AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                {
                    UIElementAutomationPeer.CreatePeerForElement(target)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            };
            return delay;
        });
        timer.Stop();
        timer.Start();
    }

    /// <summary>Releases native providers before the owning WPF dispatcher is shut down, including when hosted by tests.</summary>
    public static void ReleaseAutomationProviders() => _ = UiaDisconnectAllProviders();

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    private static extern int UiaDisconnectAllProviders();

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Accessibility), new PropertyMetadata(false, EnabledChanged));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static readonly DependencyProperty LiveSettingProperty = DependencyProperty.RegisterAttached(
        "LiveSetting", typeof(AutomationLiveSetting), typeof(Accessibility),
        new PropertyMetadata(AutomationLiveSetting.Off, LiveSettingChanged));
    public static AutomationLiveSetting GetLiveSetting(DependencyObject element) => (AutomationLiveSetting)element.GetValue(LiveSettingProperty);
    public static void SetLiveSetting(DependencyObject element, AutomationLiveSetting value) => element.SetValue(LiveSettingProperty, value);

    private static void EnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Window window || !(bool)args.NewValue)
        {
            return;
        }

        IInputElement? returnFocus = null;
        bool wasActive = false;
        window.SourceInitialized += (_, _) => returnFocus = Keyboard.FocusedElement;
        window.Loaded += (_, _) => AccessibilityPreferences.ApplyToWindow(window);
        window.Closing += (_, _) => wasActive = window.IsActive;
        window.Closed += (_, _) =>
        {
            if (wasActive && window.Owner is { IsVisible: true } owner)
            {
                owner.Dispatcher.BeginInvoke(() =>
                {
                    if (owner.IsVisible && !owner.Dispatcher.HasShutdownStarted)
                    {
                        owner.Activate();
                        if (returnFocus is FrameworkElement { IsVisible: true, IsEnabled: true } target)
                        {
                            target.Focus();
                        }
                    }
                }, DispatcherPriority.Input);
            }
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F1 && window is not AccessibilityHelpWindow)
            {
                new AccessibilityHelpWindow { Owner = window }.ShowDialog();
                e.Handled = true;
            }
        };

        FocusOutline? outline = null;
        AdornerLayer? layer = null;
        window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (outline is not null)
            {
                layer?.Remove(outline);
                outline = null;
            }
            if (e.NewFocus is FrameworkElement target && target is not Control &&
                UIElementAutomationPeer.CreatePeerForElement(target) is AccessibleViewPeer)
            {
                layer = AdornerLayer.GetAdornerLayer(target);
                if (layer is not null)
                {
                    outline = new FocusOutline(target);
                    outline.IsHitTestVisible = false;
                    layer.Add(outline);
                }
            }
        }));
    }

    private static void LiveSettingChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is TextBlock text && (AutomationLiveSetting)args.NewValue != AutomationLiveSetting.Off)
        {
            AutomationProperties.SetLiveSetting(text, (AutomationLiveSetting)args.NewValue);
            _ = new LiveText(text);
        }
    }

    private sealed class LiveText
    {
        private static readonly DependencyPropertyDescriptor TextProperty =
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        private readonly TextBlock _text;
        private readonly DispatcherTimer _timer;
        private string _last = "";

        public LiveText(TextBlock text)
        {
            _text = text;
            _timer = new DispatcherTimer(DispatcherPriority.Background, text.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _timer.Tick += Announce;
            text.Loaded += Loaded;
            text.Unloaded += Unloaded;
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            _last = _text.Text;
            TextProperty.AddValueChanged(_text, Changed);
        }
        private void Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            TextProperty.RemoveValueChanged(_text, Changed);
        }
        private void Changed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }
        private void Announce(object? sender, EventArgs e)
        {
            _timer.Stop();
            string message = _text.Text;
            if (message != _last && !string.IsNullOrWhiteSpace(message) && _text.IsVisible &&
                Window.GetWindow(_text)?.IsActive == true && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                UIElementAutomationPeer.CreatePeerForElement(_text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            _last = message;
        }
    }

    private sealed class FocusOutline(UIElement element) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            var brush = AccessibilityPreferences.HighContrast ? SystemColors.WindowTextBrush : Brushes.Aquamarine;
            drawingContext.DrawRectangle(null, new Pen(brush, 3), new Rect(1.5, 1.5,
                Math.Max(0, ActualWidth - 3), Math.Max(0, ActualHeight - 3)));
        }
    }
}
