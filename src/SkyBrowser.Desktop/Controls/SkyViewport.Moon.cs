using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

public sealed partial class SkyViewport
{
    private DateTimeOffset _atlasTimeUtc = DateTimeOffset.UtcNow;
    private LunarState? _moon;
    private ObserverLocation? _moonLocation;
    private BitmapSource? _moonImage;

    public bool ShowMoon
    {
        get;
        set
        {
            field = value;
            if (Observer is null) { _atlasTimeUtc = DateTimeOffset.UtcNow; }
            if (!value) { ToolTip = null; }
            Refresh();
        }
    }

    public LunarState CurrentMoon
    {
        get
        {
            var time = Camera.Observer?.TimeUtc ?? _atlasTimeUtc;
            var location = Camera.Observer?.Location;
            if (_moon is null || _moon.TimeUtc != time || _moonLocation != location)
            {
                _moon = LunarState.Calculate(time, location);
                _moonLocation = location;
                _moonImage = null;
            }
            return _moon;
        }
    }

    private BitmapSource MoonImage => _moonImage ??= MoonGlyph.Create(CurrentMoon);

    private void UpdateMoonTooltip(Point? point)
    {
        if (ShowMoon && point is { } cursor && Camera.Project(CurrentMoon.Position.Vector, ActualWidth, ActualHeight) is { } projected &&
            (cursor - new Point(projected.X, projected.Y)).Length <= MoonGlyph.Diameter / 2 + 5)
        {
            var moon = CurrentMoon;
            ToolTip = string.Create(CultureInfo.InvariantCulture,
                $"Moon · {moon.PhaseName} · {moon.IlluminatedFraction:P0} illuminated\nJ2000 {moon.Position}\n{moon.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC · {(moon.IsTopocentric ? "observer location" : "Earth-centered")}\nIllustration, not angular scale. No eclipse or libration simulation.");
        }
        else { ToolTip = null; }
    }
}
