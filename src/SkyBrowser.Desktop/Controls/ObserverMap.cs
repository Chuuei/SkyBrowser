using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

/// <summary>Offline, zoomable longitude/latitude map. Coordinates use east-positive longitude.</summary>
public sealed class ObserverMap : FrameworkElement
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new Services.AccessibleViewPeer(this, () => Selection is { } location
            ? $"Latitude {location.Latitude:0.######} degrees north; longitude {location.Longitude:0.######} degrees east. Zoom {_zoom:0.##} times."
            : "No observing position selected. Enter latitude and longitude in the named fields or use arrow keys to select a position.");
    private sealed record Country(StreamGeometry Shape, string Name, Point Label);

    private static readonly Lazy<Country[]> Countries = new(LoadCountries);
    private static readonly Brush Ocean = FrozenBrush("#091827"), Land = FrozenBrush("#203C4D"), Accent = FrozenBrush("#8BE2C3");
    private static readonly Brush LabelBrush = FrozenBrush("#A4BAC7");
    private static readonly Typeface Font = new("Segoe UI");
    private double _zoom = 1;
    private Point _center;
    private Point? _drag;
    private double _dragDistance;

    private double Scale => Math.Max(0.001, Math.Min(ActualWidth / 360, ActualHeight / 180) * _zoom);

    public ObserverLocation? Selection
    {
        get;
        set
        {
            value?.Validate();
            field = value;
            InvalidateVisual();
            Services.AccessibleViewPeer.Notify(this);
        }
    }

    public event EventHandler<ObserverLocation>? LocationSelected;

    public ObserverMap()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetHelpText(this,
            "Arrow keys change the selected latitude and longitude. Plus/minus zoom; Home shows the world. Exact coordinates can be entered in the latitude and longitude fields.");
    }

    public Point Project(ObserverLocation location) => new(
        ActualWidth / 2 + (location.Longitude - _center.X) * Scale,
        ActualHeight / 2 + (_center.Y - location.Latitude) * Scale
    );

    public ObserverLocation? LocationAt(Point point)
    {
        double lon = _center.X + (point.X - ActualWidth / 2) / Scale;
        double lat = _center.Y - (point.Y - ActualHeight / 2) / Scale;
        return lon is >= -180 and <= 180 && lat is >= -90 and <= 90 ? new(lat, lon) : null;
    }

    public void Zoom(double factor, Point? anchor = null)
    {
        var screen = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var location = LocationAt(screen);
        _zoom = Math.Clamp(_zoom * factor, 1, 32);
        if (location is not null)
        {
            _center = new(
                location.Longitude - (screen.X - ActualWidth / 2) / Scale,
                location.Latitude + (screen.Y - ActualHeight / 2) / Scale
            );
        }

        Constrain();
        InvalidateVisual();
    }

    public void ResetView()
    {
        _zoom = 1;
        _center = new();
        InvalidateVisual();
    }

    public void CenterOnSelection()
    {
        if (Selection is not { } location)
        {
            return;
        }

        _zoom = Math.Max(4, _zoom);
        _center = new(location.Longitude, location.Latitude);
        Constrain();
        InvalidateVisual();
    }

    private void Constrain() => _center = new(
        Math.Clamp(_center.X, -Math.Max(0, 180 - ActualWidth / (2 * Scale)), Math.Max(0, 180 - ActualWidth / (2 * Scale))),
        Math.Clamp(_center.Y, -Math.Max(0, 90 - ActualHeight / (2 * Scale)), Math.Max(0, 90 - ActualHeight / (2 * Scale)))
    );

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Constrain();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        bool highContrast = Services.AccessibilityPreferences.HighContrast;
        Brush ocean = highContrast ? SystemColors.WindowBrush : Ocean;
        Brush land = highContrast ? SystemColors.WindowBrush : Land;
        Brush ink = highContrast ? SystemColors.WindowTextBrush : Accent;
        dc.DrawRectangle(ocean, null, new Rect(RenderSize));
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var transform = new MatrixTransform(Scale, 0, 0, Scale, ActualWidth / 2 - _center.X * Scale, ActualHeight / 2 + _center.Y * Scale);
        dc.PushTransform(transform);
        var gridPen = new Pen(highContrast ? SystemColors.GrayTextBrush : FrozenBrush("#1A3142"), 0.65 / Scale);
        double spacing = _zoom < 3 ? 30 : _zoom < 8 ? 10 : 2;
        for (double lon = -180; lon <= 180; lon += spacing)
        {
            dc.DrawLine(gridPen, new(lon, -90), new(lon, 90));
        }

        for (double lat = -90; lat <= 90; lat += spacing)
        {
            dc.DrawLine(gridPen, new(-180, lat), new(180, lat));
        }

        var borderPen = new Pen(highContrast ? SystemColors.WindowTextBrush : FrozenBrush("#507183"), 0.7 / Scale);
        foreach (var country in Countries.Value)
        {
            dc.DrawGeometry(land, borderPen, country.Shape);
        }

        dc.Pop();
        if (_zoom >= 2)
        {
            List<Rect> labels = [];
            foreach (var country in Countries.Value)
            {
                var point = transform.Transform(country.Label);
                if (point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight)
                {
                    continue;
                }

                var label = new FormattedText(
                    country.Name,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Font,
                    10,
                    highContrast ? SystemColors.WindowTextBrush : LabelBrush,
                    VisualTreeHelper.GetDpi(this)
                    .PixelsPerDip
                );
                var bounds = new Rect(point.X - label.Width / 2, point.Y - label.Height / 2, label.Width, label.Height);
                bounds.Inflate(3, 2);
                if (labels.Any(existing => existing.IntersectsWith(bounds)))
                {
                    continue;
                }

                labels.Add(bounds);
                dc.DrawText(label, new(point.X - label.Width / 2, point.Y - label.Height / 2));
            }
        }

        if (Selection is { } location)
        {
            var point = Project(location);
            dc.DrawEllipse(ocean, new Pen(ink, 2), point, 7, 7);
            dc.DrawEllipse(ink, null, point, 3, 3);
            dc.DrawLine(new Pen(ink, 1), point + new Vector(-13, 0), point + new Vector(-9, 0));
            dc.DrawLine(new Pen(ink, 1), point + new Vector(9, 0), point + new Vector(13, 0));
            dc.DrawLine(new Pen(ink, 1), point + new Vector(0, -13), point + new Vector(0, -9));
            dc.DrawLine(new Pen(ink, 1), point + new Vector(0, 9), point + new Vector(0, 13));
        }

        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _drag = e.GetPosition(this);
        _dragDistance = 0;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is not { } previous)
        {
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - previous;
        _dragDistance += delta.Length;
        _center = new(_center.X - delta.X / Scale, _center.Y + delta.Y / Scale);
        _drag = current;
        Cursor = Cursors.ScrollAll;
        Constrain();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Cross;
        if (_dragDistance < 4 && LocationAt(e.GetPosition(this)) is { } location)
        {
            Select(location);
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = null;
        Cursor = Cursors.Cross;
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Zoom(Math.Pow(1.35, e.Delta / 120d), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus or Key.Home)
        {
            if (e.Key == Key.Home)
            { ResetView(); }
            else
            { Zoom(e.Key is Key.Add or Key.OemPlus ? 1.35 : 1 / 1.35); }
            Services.AccessibleViewPeer.Notify(this);
            e.Handled = true;
            return;
        }
        var current = Selection ?? new ObserverLocation(_center.Y, _center.X);
        double step = 1 / _zoom;
        ObserverLocation? next = e.Key switch
        {
            Key.Left => current with
            {
                Longitude = Math.Max(-180, current.Longitude - step)
            },
            Key.Right => current with
            {
                Longitude = Math.Min(180, current.Longitude + step)
            },
            Key.Up => current with
            {
                Latitude = Math.Min(90, current.Latitude + step)
            },
            Key.Down => current with
            {
                Latitude = Math.Max(-90, current.Latitude - step)
            },
            _ => null
        };
        if (next is not null)
        {
            Select(next);
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private void Select(ObserverLocation location)
    {
        Selection = location;
        LocationSelected?.Invoke(this, location);
    }

    private static Brush FrozenBrush(string color)
    {
        var brush = (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
        brush.Freeze();
        return brush;
    }

    private static Country[] LoadCountries()
    {
        using var stream = typeof(ObserverMap).Assembly.GetManifestResourceStream("SkyBrowser.Desktop.Data.world-countries.geojson") ?? throw new InvalidOperationException("The offline observer map is missing.");
        using var document = JsonDocument.Parse(stream);
        List<Country> countries = [];
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry");
            var shape = new StreamGeometry
            {
                FillRule = FillRule.EvenOdd
            };
            using (var context = shape.Open())
            {

                void Polygon(JsonElement rings)
                {
                    foreach (var ring in rings.EnumerateArray())
                    {
                        var points = ring.EnumerateArray().Select(p => new Point(p[0].GetDouble(), -p[1].GetDouble())).ToArray();
                        if (points.Length < 3)
                        {
                            continue;
                        }

                        context.BeginFigure(points[0], true, true);
                        context.PolyLineTo(points.Skip(1).ToArray(), true, false);
                    }
                }

                var coordinates = geometry.GetProperty("coordinates");
                if (geometry.GetProperty("type").GetString() == "Polygon")
                {
                    Polygon(coordinates);
                }
                else
                {
                    foreach (var polygon in coordinates.EnumerateArray())
                    {
                        Polygon(polygon);
                    }
                }
            }

            shape.Freeze();
            var properties = feature.GetProperty("properties");
            string name = properties.GetProperty("NAME_EN").GetString() ?? "";
            var label = new Point(properties.GetProperty("LABEL_X").GetDouble(), -properties.GetProperty("LABEL_Y").GetDouble());
            countries.Add(new(shape, name, label));
        }

        return countries.OrderByDescending(country => country.Shape.Bounds.Width * country.Shape.Bounds.Height).ToArray();
    }
}
