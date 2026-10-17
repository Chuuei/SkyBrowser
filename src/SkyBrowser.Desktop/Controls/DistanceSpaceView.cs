using System.Windows;
using System.Windows.Input;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

/// <summary>Depth-sorted perspective point cloud, with screen-sized symbols and collision-limited labels.</summary>
public sealed partial class DistanceSpaceView : FrameworkElement
{
    private Point? _down, _previous;
    private bool _dragged, _shiftClick;
    private readonly DistanceSpaceScene _scene = new();
    private TargetCatalog _catalog;
    private bool _guides = true, _labels = true;

    public DistanceSpaceCamera Camera => _scene.Camera;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ComparisonChanged;
    public event EventHandler? ViewChanged;
    public bool SelectSecondTarget { get; set; }
    public TargetDistanceEstimate? ComparisonTarget => _scene.ComparisonTarget;
    public SkyMeasurement? PairMeasurement => _scene.PairMeasurement;
    public int VisibleObjectCount => _projected.Count;
    public TargetDistanceEstimate? Selected
    {
        get => _scene.Selected;
        set => _scene.Selected = value;
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
        _scene.SelectionChanged += (_, _) =>
        {
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _scene.ComparisonChanged += (_, _) =>
        {
            InvalidateVisual();
            ComparisonChanged?.Invoke(this, EventArgs.Empty);
        };
        _scene.ViewChanged += (_, _) => RefreshCamera();
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

    public void RefreshCamera()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusSelected(bool keepScale = false) => _scene.FocusSelected(keepScale);

    public void ResetToSun()
    {
        SelectSecondTarget = false;
        _scene.ResetToSun();
    }

    public void SelectTarget(TargetDistanceEstimate? target, bool second = false) => _scene.SelectTarget(target, second);

    public void ClearComparison() => _scene.ClearComparison();

    public void FramePair() => _scene.FramePair();

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

}
