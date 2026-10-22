using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static void VerifyAccessibility(MainWindow window)
    {
        var original = window.Options;
        try
        {
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)window.FindName("StatusText")));

            AccessibilityPreferences.Apply(original with { InterfaceScale = 2, UseHighContrast = true, ReduceMotion = true });
            window.UpdateLayout();
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            var root = Assert.IsAssignableFrom<FrameworkElement>(scroll.Content);
            Assert.True(double.IsFinite(root.Width) && double.IsFinite(root.Height));
            Assert.Equal(2, Assert.IsType<ScaleTransform>(root.LayoutTransform).ScaleX);
            Assert.True(AccessibilityPreferences.MotionReduced);
            Capture(window, "accessibility-high-contrast.png");
            Assert.Equal(SystemColors.WindowColor, ((SolidColorBrush)Application.Current.FindResource("UiBrush101722")).Color);
            Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)Application.Current.FindResource("UiBrushDFE8F2")).Color);

            AccessibilityPreferences.Apply(original with { InterfaceScale = 1, UseHighContrast = false });
            window.UpdateLayout();
            Assert.IsType<Grid>(window.Content);
            if (!SystemParameters.HighContrast)
            {
                var foreground = ((SolidColorBrush)Application.Current.FindResource("UiBrush8D9AAD")).Color;
                var background = ((SolidColorBrush)Application.Current.FindResource("UiBrush101722")).Color;
                Assert.True(Contrast(foreground, background) >= 4.5, "Secondary interface text must meet normal-text contrast.");
            }
        }
        finally
        {
            AccessibilityPreferences.Apply(original);
            window.UpdateLayout();
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c)
        {
            double value = c / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
