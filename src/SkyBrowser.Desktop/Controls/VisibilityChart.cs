using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

public sealed class VisibilityChart : FrameworkElement
{
    public VisibilityReport? Report
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    public VisibilityChart()
    {
        MinHeight = 260;
        MouseMove += ReadTime;
        ToolTip = "Calculate a visibility report to inspect the altitude curves.";
    }

    private void ReadTime(object sender, MouseEventArgs e)
    {
        if (Report is not { } report)
        {
            return;
        }
        double fraction = Math.Clamp((e.GetPosition(this).X - 42) / Math.Max(1, ActualWidth - 60), 0, 1);
        var sample = report.Samples[(int)Math.Round(fraction * (report.Samples.Length - 1))];
        ToolTip = $"{sample.TimeUtc:yyyy-MM-dd HH:mm} UTC\nTarget {sample.TargetAltitude:0.0}° · Sun {sample.SunAltitude:0.0}° · Moon {sample.MoonAltitude:0.0}°\n" +
            $"Moon separation {sample.MoonSeparation:0.0}° · illuminated {sample.MoonIllumination:P0}";
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 17, 27)), null, new Rect(RenderSize));
        if (Report is not { } report || ActualWidth < 100 || ActualHeight < 100)
        {
            return;
        }
        double width = ActualWidth - 60, height = ActualHeight - 50;
        double X(DateTimeOffset time) => 42 + (time - report.StartUtc).TotalHours / 24 * width;
        double Y(double altitude) => 12 + (90 - altitude) / 180 * height;
        var darkness = new SolidColorBrush(Color.FromRgb(5, 9, 17));
        var twilight = new SolidColorBrush(Color.FromRgb(23, 34, 49));
        for (int i = 0; i < report.Samples.Length - 1; i++)
        {
            var a = report.Samples[i];
            double x = X(a.TimeUtc), end = X(report.Samples[i + 1].TimeUtc);
            if (a.SunAltitude < -6)
            {
                dc.DrawRectangle(a.SunAltitude <= -18 ? darkness : twilight, null, new(x, 12, end - x + 0.5, height));
            }
        }
        foreach (var interval in report.ImagingWindows)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 24, 83, 66)), null,
                new(X(interval.StartUtc), 12, X(interval.EndUtc) - X(interval.StartUtc), height));
        }
        void Text(string value, Point point, Brush brush)
        {
            dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
        }
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(46, 60, 76)), 1);
        for (int altitude = -90; altitude <= 90; altitude += 30)
        {
            dc.DrawLine(grid, new(42, Y(altitude)), new(42 + width, Y(altitude)));
            Text(altitude + "°", new(5, Y(altitude) - 7), Brushes.LightSlateGray);
        }
        for (int hour = 0; hour <= 24; hour += 4)
        {
            var time = report.StartUtc.AddHours(hour);
            double x = X(time);
            dc.DrawLine(grid, new(x, 12), new(x, 12 + height));
            Text(time.ToString("HH:mm", CultureInfo.InvariantCulture), new(x - 15, 17 + height), Brushes.LightSlateGray);
        }
        var minimum = new Pen(new SolidColorBrush(Color.FromRgb(80, 123, 109)), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(minimum, new(42, Y(report.Criteria.MinimumAltitude)), new(42 + width, Y(report.Criteria.MinimumAltitude)));
        void Curve(Func<VisibilitySample, double> value, Color color)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new(X(report.Samples[0].TimeUtc), Y(value(report.Samples[0]))), false, false);
                context.PolyLineTo(report.Samples.Skip(1).Select(s => new Point(X(s.TimeUtc), Y(value(s)))).ToArray(), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.6), geometry);
        }
        Curve(s => s.TargetAltitude, Color.FromRgb(139, 226, 195));
        Curve(s => s.SunAltitude, Color.FromRgb(224, 180, 96));
        Curve(s => s.MoonAltitude, Color.FromRgb(147, 179, 231));
    }
}
