using System.Windows;
using System.Windows.Media;

namespace SkyBrowser.Desktop.Controls;

public sealed class ScientificHistogram : FrameworkElement
{
    public IReadOnlyList<long>? Bins
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 18, 29)), null, new(0, 0, ActualWidth, ActualHeight));
        if (Bins is not { Count: > 0 } bins || bins.Max() is not > 0)
        {
            return;
        }
        double maximum = bins.Max();
        var brush = new SolidColorBrush(Color.FromRgb(139, 226, 195));
        for (int i = 0; i < bins.Count; i++)
        {
            double height = bins[i] / maximum * ActualHeight;
            context.DrawRectangle(brush, null, new(i * ActualWidth / bins.Count, ActualHeight - height,
                ActualWidth / bins.Count, height));
        }
    }
}
