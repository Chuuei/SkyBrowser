using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

/// <summary>Depth-sorted perspective point cloud, with screen-sized symbols and collision-limited labels.</summary>
public sealed class DistanceSpaceView : FrameworkElement
{
    private sealed record Projected(TargetDistanceEstimate Estimate, Point Screen, double Depth);
    private readonly (TargetDistanceEstimate Estimate, SkyVector Position)[] _objects = TargetDistances.All
        .Select(estimate => (estimate, DistanceSpaceCamera.Position(estimate))).ToArray();
    private readonly Typeface _font = new("Segoe UI");
    private readonly Brush _above = FrozenBrush(102, 210, 229);
    private readonly Brush _below = FrozenBrush(238, 171, 104);
    private readonly Brush _muted = FrozenBrush(132, 156, 178);
    private readonly Brush _solar = FrozenBrush(255, 228, 155);
    private readonly Pen _grid = new(FrozenBrush(41, 67, 85), 1);
    private List<Projected> _projected = [];
    private Point? _down, _previous;
    private bool _dragged, _shiftClick;
    private TargetDistanceEstimate? _selected;
    private TargetDistanceEstimate? _comparisonTarget;
    private TargetCatalog _catalog;
    private bool _guides = true, _labels = true;

    public DistanceSpaceCamera Camera { get; } = new();
    public event EventHandler? SelectionChanged;
    public event EventHandler? ComparisonChanged;
    public event EventHandler? ViewChanged;
    public bool SelectSecondTarget { get; set; }
    public TargetDistanceEstimate? ComparisonTarget => _comparisonTarget;
    public SkyMeasurement? PairMeasurement => Selected is { } a && ComparisonTarget is { } b
        ? TargetDistances.Measure(a.Target.Id, b.Target.Id) : null;
    public int VisibleObjectCount => _projected.Count;
    public TargetDistanceEstimate? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                if (value is not null) { FocusSelected(keepScale: true); }
                return;
            }
            _selected = value;
            ClearComparison();
            if (value is not null) { FocusSelected(); }
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public TargetCatalog Catalog
    {
        get => _catalog;
        set
        {
            _catalog = value;
            InvalidateVisual();
        }
    }
    public bool ShowGuides
    {
        get => _guides;
        set
        {
            _guides = value;
            InvalidateVisual();
        }
    }
    public bool ShowLabels
    {
        get => _labels;
        set
        {
            _labels = value;
            InvalidateVisual();
        }
    }

    public DistanceSpaceView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = "Click to centre navigation · Shift-click a second object to measure · Drag to orbit · Right-drag or Shift-drag to pan · Wheel to zoom";
        SizeChanged += (_, _) => InvalidateVisual();
        LostMouseCapture += (_, _) =>
        {
            _down = _previous = null;
            Cursor = Cursors.Hand;
        };
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public void RefreshCamera()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusSelected(bool keepScale = false)
    {
        if (Selected is not { } selected) { return; }
        Camera.Frame(DistanceSpaceCamera.Position(selected), keepScale ? Camera.Distance : Math.Max(50, selected.LightYears * 0.18));
        RefreshCamera();
    }

    public void ResetToSun()
    {
        SelectSecondTarget = false;
        Selected = null;
        Camera.Home();
        RefreshCamera();
    }

    public void SelectTarget(TargetDistanceEstimate? target, bool second = false)
    {
        if (!second || Selected is null)
        {
            Selected = target;
            return;
        }
        if (target is null) { return; }
        _comparisonTarget = target.Target.Id == Selected.Target.Id ? null : target;
        InvalidateVisual();
        ComparisonChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearComparison()
    {
        if (_comparisonTarget is null) { return; }
        _comparisonTarget = null;
        InvalidateVisual();
        ComparisonChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FramePair()
    {
        if (Selected is not { } a || ComparisonTarget is not { } b) { return; }
        var start = DistanceSpaceCamera.Position(a);
        var end = DistanceSpaceCamera.Position(b);
        Camera.Frame((start + end) / 2, Math.Max(50, (end - start).Length * 1.7));
        RefreshCamera();
    }

    public TargetDistanceEstimate? HitTarget(Point point) => _projected
        .Where(item => (item.Screen - point).Length <= 9)
        .OrderBy(item => (item.Screen - point).LengthSquared).ThenBy(item => item.Depth)
        .Select(item => item.Estimate).FirstOrDefault();

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) { return; }
        Focus();
        _down = _previous = e.GetPosition(this);
        _dragged = false;
        _shiftClick = SelectSecondTarget || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        if (e.ChangedButton == MouseButton.Left && !_shiftClick && e.ClickCount == 2 && HitTarget(_down.Value) is { } target)
        {
            Selected = target;
            FocusSelected();
            _dragged = true;
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_previous is not { } previous || _down is not { } down) { return; }
        var point = e.GetPosition(this);
        if (!_dragged && (point - down).Length < 4) { return; }
        _dragged = true;
        var delta = point - previous;
        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double scale = Camera.ViewHeight / Math.Max(1, ActualHeight);
            Camera.Move(-delta.X * scale, delta.Y * scale, 0);
        }
        else
        {
            Camera.Orbit(-delta.X * 0.3, delta.Y * 0.3);
        }
        _previous = point;
        RefreshCamera();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_down is null) { return; }
        if (!_dragged && e.ChangedButton == MouseButton.Left) { SelectTarget(HitTarget(e.GetPosition(this)), _shiftClick); }
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Camera.Zoom(e.Delta / 120d);
        RefreshCamera();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Camera.Distance * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.15 : 0.04);
        switch (e.Key)
        {
            case Key.W:
                Camera.Move(0, 0, step);
                break;
            case Key.S:
                Camera.Move(0, 0, -step);
                break;
            case Key.A:
                Camera.Move(-step, 0, 0);
                break;
            case Key.D:
                Camera.Move(step, 0, 0);
                break;
            case Key.Q:
                Camera.Move(0, -step, 0);
                break;
            case Key.E:
                Camera.Move(0, step, 0);
                break;
            case Key.Left:
                Camera.Orbit(-5, 0);
                break;
            case Key.Right:
                Camera.Orbit(5, 0);
                break;
            case Key.Up:
                Camera.Orbit(0, 5);
                break;
            case Key.Down:
                Camera.Orbit(0, -5);
                break;
            case Key.Add:
            case Key.OemPlus:
                Camera.Zoom(1);
                break;
            case Key.Subtract:
            case Key.OemMinus:
                Camera.Zoom(-1);
                break;
            case Key.Home:
                ResetToSun();
                break;
            case Key.F:
                FocusSelected(keepScale: true);
                break;
            case Key.Escape:
                ClearComparison();
                break;
            default:
                return;
        }
        RefreshCamera();
        e.Handled = true;
    }

    private FormattedText Text(string text, Brush brush, double size = 11) => new(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, _font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(FrozenBrush(6, 13, 23), null, new Rect(RenderSize));
        if (ActualWidth < 20 || ActualHeight < 20) { return; }
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        DistanceSpaceProjection? Project(SkyVector point) => Camera.Project(point, ActualWidth, ActualHeight);
        void Line(SkyVector a, SkyVector b, Pen pen)
        {
            if (Project(a) is { } p && Project(b) is { } q)
            {
                dc.DrawLine(pen, new(p.X, p.Y), new(q.X, q.Y));
            }
        }
        double radius = NiceRadius(Camera.Distance * 0.55);
        if (ShowGuides)
        {
            foreach (double fraction in new[] { 0.25, 0.5, 0.75, 1d })
            {
                double r = radius * fraction;
                for (int i = 0; i < 128; i++)
                {
                    double a = i * Math.Tau / 128, b = (i + 1) * Math.Tau / 128;
                    Line(new(r * Math.Cos(a), 0, r * Math.Sin(a)), new(r * Math.Cos(b), 0, r * Math.Sin(b)), _grid);
                }
                if (fraction is 0.5 or 1 && Project(new(fraction == 0.5 ? -r : r, 0, 0)) is { } tick)
                {
                    dc.DrawText(Text(FormatScale(r) + " ly", _muted), new(tick.X + 5, tick.Y + 3));
                }
            }
            Line(new(-radius, 0, 0), new(radius, 0, 0), _grid);
            Line(new(0, 0, -radius), new(0, 0, radius), _grid);
            Line(new(0, -radius * 0.35, 0), new(0, radius * 0.6, 0), _grid);
            if (Project(new(0, radius * 0.6, 0)) is { } north)
            {
                dc.DrawText(Text("J2000 north", _muted), new(north.X + 6, north.Y));
            }
        }

        _projected = _objects.Where(item => Catalog == TargetCatalog.All || (item.Estimate.Target.Catalogs & Catalog) != 0)
            .Select(item => (item.Estimate, Projection: Project(item.Position)))
            .Where(item => item.Projection is { } p && p.X >= 8 && p.Y >= 8 && p.X < ActualWidth - 8 && p.Y < ActualHeight - 30)
            .Select(item => new Projected(item.Estimate, new(item.Projection!.Value.X, item.Projection.Value.Y), item.Projection.Value.Depth))
            .OrderByDescending(item => item.Depth).ToList();

        if (Selected is { } first && ComparisonTarget is { } second)
        {
            Line(DistanceSpaceCamera.Position(first), DistanceSpaceCamera.Position(second),
                new Pen(_solar, 1.8) { DashStyle = DashStyles.Dash });
        }
        foreach (var item in _projected)
        {
            var brush = item.Estimate.Target.Position.Declination >= 0 ? _above : _below;
            double size = item.Estimate == Selected || item.Estimate == ComparisonTarget ? 4 : 2.2;
            dc.DrawEllipse(brush, null, item.Screen, size, size);
            if (item.Estimate == Selected) { dc.DrawEllipse(null, new Pen(Brushes.White, 1), item.Screen, 7, 7); }
            if (item.Estimate == ComparisonTarget) { dc.DrawEllipse(null, new Pen(_solar, 1.5), item.Screen, 8, 8); }
        }
        var occupied = new List<Rect>();
        if (PairMeasurement?.SeparationLightYears is { } separation &&
            Project(DistanceSpaceCamera.Position(Selected!)) is { } pa &&
            Project(DistanceSpaceCamera.Position(ComparisonTarget!)) is { } pb)
        {
            var text = Text(TargetDistances.FormatApproximate(separation) + " light-years", _solar, 12);
            var label = new Rect(new Point((pa.X + pb.X) / 2 - text.Width / 2, (pa.Y + pb.Y) / 2 - text.Height - 8),
                new Size(text.Width + 8, text.Height + 4));
            if (label.Left >= 0 && label.Right <= ActualWidth && label.Top >= 0 && label.Bottom <= ActualHeight - 30)
            {
                dc.DrawRectangle(FrozenBrush(6, 13, 23), null, label);
                dc.DrawText(text, new(label.X + 4, label.Y + 2));
                occupied.Add(label);
            }
        }
        if (Project(default) is { } sun && sun.X >= 0 && sun.Y >= 0 && sun.X <= ActualWidth && sun.Y <= ActualHeight)
        {
            var point = new Point(sun.X, sun.Y);
            var glow = new RadialGradientBrush(Color.FromArgb(140, 255, 222, 145), Colors.Transparent);
            dc.DrawEllipse(glow, null, point, 18, 18);
            dc.DrawEllipse(_solar, null, point, 4, 4);
            var text = Text("Solar System", _solar, 12);
            var label = new Rect(new Point(point.X + 9, point.Y - 18), new Size(text.Width + 6, text.Height));
            dc.DrawText(text, label.TopLeft);
            occupied.Add(label);
        }
        // Always prioritize the selection; sparse labels keep the cloud legible while navigating.
        foreach (var item in _projected.OrderBy(item => item.Estimate == Selected ? 0 : item.Estimate == ComparisonTarget ? 1 : 2)
            .ThenBy(item => item.Estimate.Target.Catalogs.HasFlag(TargetCatalog.Messier) ? 0 : 1).ThenBy(item => item.Depth))
        {
            bool selected = item.Estimate == Selected || item.Estimate == ComparisonTarget;
            if (!selected && (!ShowLabels || occupied.Count >= 32)) { continue; }
            var brush = item.Estimate == ComparisonTarget ? _solar : selected ? Brushes.White : item.Estimate.Target.Position.Declination >= 0 ? _above : _below;
            string name = selected ? (ComparisonTarget is not null ? item.Estimate == Selected ? "A · " : "B · " : "") + item.Estimate.Target.Name : item.Estimate.Target.PreferredDesignation;
            var text = Text(name, brush, selected ? 12 : 11);
            var label = new Rect(new Point(item.Screen.X + 8, item.Screen.Y - text.Height / 2), new Size(text.Width + 5, text.Height + 3));
            if (label.Right > ActualWidth - 5) { label.X = item.Screen.X - label.Width - 8; }
            if (label.Left < 0 || label.Top < 0 || label.Bottom > ActualHeight - 30 || !selected && occupied.Any(rect => rect.IntersectsWith(label))) { continue; }
            if (ShowGuides && (selected || occupied.Count < 18))
            {
                var position = DistanceSpaceCamera.Position(item.Estimate);
                Line(position, new(position.X, 0, position.Z), new Pen(brush, selected ? 1.3 : 0.5));
            }
            dc.DrawRectangle(FrozenBrush(6, 13, 23), null, label);
            dc.DrawText(text, label.TopLeft);
            occupied.Add(label);
        }
        dc.DrawRectangle(FrozenBrush(6, 13, 23), null, new Rect(0, ActualHeight - 27, ActualWidth, 27));
        dc.DrawText(Text($"{_projected.Count:N0} in view · Linear scale · View height at focus: {FormatScale(Camera.ViewHeight)} ly", _muted), new(12, ActualHeight - 21));
        dc.Pop();
    }

    public static string FormatScale(double value) => value >= 1e9 ? $"{value / 1e9:0.##} billion" :
        value >= 1e6 ? $"{value / 1e6:0.##} million" : value >= 1e3 ? $"{value / 1e3:0.##} thousand" : $"{value:0.##}";

    private static double NiceRadius(double value)
    {
        double exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double fraction = value / exponent;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * exponent;
    }
}
