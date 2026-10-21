using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Controls;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static void VerifyMoon(MainWindow window)
    {
        var sky = window.SkyView;
        var observer = sky.Observer;
        var center = sky.Camera.Center;
        double fov = sky.Camera.FieldOfView;
        var toggle = (CheckBox)window.FindName("MoonToggle");
        bool show = sky.ShowMoon;
        static byte[] Pixels(BitmapSource source)
        {
            byte[] data = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(data, source.PixelWidth * 4, 0);
            return data;
        }
        try
        {
            var time = new DateTimeOffset(2024, 4, 15, 19, 13, 0, TimeSpan.Zero);
            window.ApplyObserver(new(new(0, 0), false, time), resetView: false);
            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(sky.ShowMoon);
            Assert.True(((Button)window.FindName("GoMoonButton")).IsVisible);
            ((Button)window.FindName("GoMoonButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.InRange(sky.Camera.Center.DistanceTo(sky.CurrentMoon.Position), 0, 1e-9);
            var first = sky.CurrentMoon;
            var enabled = sky.CaptureExport(new() { Width = 800, Height = 600, Coordinates = false, Labels = false });
            sky.UpdateHover(new Point(sky.ActualWidth / 2, sky.ActualHeight / 2));
            Assert.Contains("First quarter", sky.ToolTip?.ToString());
            Assert.Contains("2024-04-15", sky.ToolTip?.ToString());
            Assert.Same(first, sky.CurrentMoon); // Panning/export don't recalculate or change the scene time.
            sky.ShowMoon = false;
            Assert.Null(sky.ToolTip);
            var disabled = sky.CaptureExport(new() { Width = 800, Height = 600, Coordinates = false, Labels = false });
            Assert.False(Pixels(enabled).SequenceEqual(Pixels(disabled)));
            sky.ShowMoon = true;
            sky.SetSceneTime(time.AddDays(8));
            Assert.NotEqual(first.Position, sky.CurrentMoon.Position);
            Assert.True(sky.CurrentMoon.IlluminatedFraction > .99);
            window.ApplyObserver(null, resetView: false);
            sky.AdvanceObserverTime(time);
            Assert.False(sky.CurrentMoon.IsTopocentric);
            Assert.Equal(time, sky.CurrentMoon.TimeUtc);
            sky.AdvanceObserverTime(time.AddDays(8));
            Assert.Equal(time.AddDays(8), sky.CurrentMoon.TimeUtc);

            var firstQuarter = Pixels(MoonGlyph.Create(LunarState.Calculate(time)));
            var lastQuarter = Pixels(MoonGlyph.Create(LunarState.Calculate(new(2024, 5, 1, 11, 27, 0, TimeSpan.Zero))));
            double Half(byte[] pixels, bool right) => Enumerable.Range(0, 96 * 96)
                .Where(i => (i % 96 >= 48) == right).Average(i => pixels[i * 4 + 1]);
            Assert.True(Half(firstQuarter, true) > Half(firstQuarter, false) * 2);
            Assert.True(Half(lastQuarter, false) > Half(lastQuarter, true) * 2);
        }
        finally
        {
            toggle.IsChecked = show;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.ApplyObserver(observer, resetView: false);
            sky.CenterOn(center, fov);
        }
    }
}
