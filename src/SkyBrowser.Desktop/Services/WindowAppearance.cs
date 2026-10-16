using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SkyBrowser.Desktop.Services;

/// <summary>Matches native window captions to the application without replacing Windows chrome.</summary>
public static class WindowAppearance
{
    public static readonly DependencyProperty UseDarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "UseDarkTitleBar",
        typeof(bool),
        typeof(WindowAppearance),
        new PropertyMetadata(false, UseDarkTitleBarChanged));

    public static bool GetUseDarkTitleBar(DependencyObject target) => (bool)target.GetValue(UseDarkTitleBarProperty);

    public static void SetUseDarkTitleBar(DependencyObject target, bool value) => target.SetValue(UseDarkTitleBarProperty, value);

    private static void UseDarkTitleBarChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window)
        {
            return;
        }

        window.SourceInitialized -= WindowSourceInitialized;
        bool enabled = (bool)e.NewValue;
        if (enabled)
        {
            window.SourceInitialized += WindowSourceInitialized;
        }

        // Styles can be applied before the HWND exists, or changed on an open window.
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle != 0)
        {
            Configure(handle, enabled);
        }
    }

    private static void WindowSourceInitialized(object? sender, EventArgs e)
    {
        Configure(new WindowInteropHelper((Window)sender!).Handle, true);
    }

    private static void Configure(nint handle, bool enabled)
    {
        if (HwndSource.FromHwnd(handle) is { } source)
        {
            source.RemoveHook(WindowMessage);
            if (enabled)
            {
                // The HWND owns this hook; disposing the window releases it without
                // retaining the window through a global system-theme event subscription.
                source.AddHook(WindowMessage);
            }
        }

        ApplyColors(handle, enabled);
    }

    private static nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int settingsChanged = 0x001A;
        const int themeChanged = 0x031A;
        if (message is settingsChanged or themeChanged)
        {
            ApplyColors(handle, true);
        }

        return 0;
    }

    private static void ApplyColors(nint handle, bool enabled)
    {
        bool useDarkColors = enabled && !SystemParameters.HighContrast;
        int darkMode = useDarkColors ? 1 : 0;
        // DWM uses COLORREF (0x00BBGGRR), not WPF's ARGB ordering.
        int captionColor = useDarkColors ? 0x00221710 : -1; // #101722, matching the header/panels.
        int textColor = useDarkColors ? 0x00F8F2ED : -1; // #EDF2F8, matching application text.

        // Unsupported attributes on older Windows return an HRESULT and leave the
        // native frame intact. High contrast restores Windows' default colors.
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        _ = DwmSetWindowAttribute(handle, DwmAttribute.UseImmersiveDarkMode, in darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmAttribute.CaptionColor, in captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmAttribute.TextColor, in textColor, sizeof(int));
    }

    private enum DwmAttribute
    {
        UseImmersiveDarkMode = 20,
        CaptionColor = 35,
        TextColor = 36
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, DwmAttribute attribute, in int value, int valueSize);
}
