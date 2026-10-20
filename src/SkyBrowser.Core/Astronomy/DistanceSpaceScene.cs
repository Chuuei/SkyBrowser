namespace SkyBrowser.Core.Astronomy;

/// <summary>Transient selection, measurement and navigation state for a distance map, independent of its UI.</summary>
public sealed class DistanceSpaceScene
{
    private TargetDistanceEstimate? _selected;

    public DistanceSpaceCamera Camera { get; } = new();
    public SkyVector ReferenceOrigin => Selected is { } selected ? DistanceSpaceCamera.Position(selected) : default;
    public string ReferenceOriginName => Selected?.Target.Name ?? "Solar System";
    public TargetDistanceEstimate? ComparisonTarget { get; private set; }
    public SkyMeasurement? PairMeasurement { get; private set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? ComparisonChanged;
    public event EventHandler? ViewChanged;

    public TargetDistanceEstimate? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                if (value is not null)
                {
                    FocusSelected(keepScale: true);
                }
                return;
            }

            _selected = value;
            ClearComparison();
            if (value is not null)
            {
                FocusSelected();
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SelectTarget(TargetDistanceEstimate? target, bool second = false)
    {
        if (!second || Selected is null)
        {
            Selected = target;
            return;
        }
        if (target is null)
        {
            return;
        }

        ComparisonTarget = target.Target.Id == Selected.Target.Id ? null : target;
        PairMeasurement = ComparisonTarget is { } comparison
            ? TargetDistances.Measure(Selected.Target.Id, comparison.Target.Id) : null;
        ComparisonChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearComparison()
    {
        if (ComparisonTarget is null)
        {
            return;
        }
        ComparisonTarget = null;
        PairMeasurement = null;
        ComparisonChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusSelected(bool keepScale = false)
    {
        if (Selected is not { } selected)
        {
            return;
        }
        Camera.Frame(DistanceSpaceCamera.Position(selected), keepScale ? Camera.Distance : Math.Max(50, selected.LightYears * 0.18));
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FramePair()
    {
        if (Selected is not { } a || ComparisonTarget is not { } b)
        {
            return;
        }
        var start = DistanceSpaceCamera.Position(a);
        var end = DistanceSpaceCamera.Position(b);
        Camera.Frame((start + end) / 2, Math.Max(50, (end - start).Length * 1.7));
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToSun()
    {
        Selected = null;
        Camera.Home();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
