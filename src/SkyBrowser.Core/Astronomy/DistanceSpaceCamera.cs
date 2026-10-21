namespace SkyBrowser.Core.Astronomy;

public readonly record struct DistanceSpaceProjection(double X, double Y, double Depth);

/// <summary>Perspective camera in linear light-years, using the atlas J2000 axes (Y north).</summary>
public sealed class DistanceSpaceCamera
{
    private bool _basisValid;
    private SkyVector _back, _right, _up;

    public SkyVector Center { get; private set; }
    public double Distance { get; private set; } = 180_000;
    public double Yaw { get; private set; } = 35;
    public double Pitch { get; private set; } = 32;
    public const double FieldOfView = 55;
    public const double MinimumDistance = 10;
    public const double MaximumViewHeight = 13_800_000_000;
    private static readonly double ProjectionHeightFactor = 2 * Math.Tan(FieldOfView * SkyCoordinate.Radians / 2);
    // Limit the visible focus span consistently across zoom, presets and framing.
    public static readonly double MaximumDistance = MaximumViewHeight / ProjectionHeightFactor;

    public SkyVector Back
    {
        get
        {
            EnsureBasis();
            return _back;
        }
    }

    public SkyVector Right
    {
        get
        {
            EnsureBasis();
            return _right;
        }
    }

    public SkyVector Up
    {
        get
        {
            EnsureBasis();
            return _up;
        }
    }
    public SkyVector Eye => Center + Back * Distance;
    public double ViewHeight => Math.Min(MaximumViewHeight, Distance * ProjectionHeightFactor);

    private void EnsureBasis()
    {
        if (_basisValid)
        {
            return;
        }

        // A frame projects thousands of catalogue points and guide vertices. Its
        // orientation changes only when orbiting or returning to the home view.
        _back = new SkyCoordinate(Yaw, Pitch).Vector;
        _right = SkyVector.Cross(new(0, 1, 0), _back).Normalized;
        _up = SkyVector.Cross(_back, _right).Normalized;
        _basisValid = true;
    }

    public static SkyVector Position(TargetDistanceEstimate estimate) => estimate.Target.Position.Vector * estimate.LightYears;

    public void Frame(SkyVector center, double distance)
    {
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(center.Z) || !double.IsFinite(distance) || distance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }
        Center = center;
        Distance = Math.Clamp(distance, MinimumDistance, MaximumDistance);
    }

    public void Home(double distance = 180_000)
    {
        Frame(default, distance);
        Yaw = 35;
        Pitch = 32;
        _basisValid = false;
    }

    public void Orbit(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch)) { return; }
        Yaw = SkyCoordinate.Normalize(Yaw + yaw);
        Pitch = Math.Clamp(Pitch + pitch, -89.5, 89.5);
        _basisValid = false;
    }

    public void Zoom(double steps)
    {
        if (!double.IsFinite(steps)) { return; }
        Distance = Math.Clamp(Distance * Math.Pow(0.82, Math.Clamp(steps, -100, 100)), MinimumDistance, MaximumDistance);
    }

    public void Move(double right, double up, double forward)
    {
        if (!double.IsFinite(right) || !double.IsFinite(up) || !double.IsFinite(forward)) { return; }
        Center += Right * right + Up * up - Back * forward;
    }

    public DistanceSpaceProjection? Project(SkyVector point, double width, double height)
    {
        if (width <= 0 || height <= 0) { return null; }
        EnsureBasis();
        var relative = point - (Center + _back * Distance);
        double depth = -SkyVector.Dot(relative, _back);
        // Relative clipping planes retain precision from nearby nebulae to distant galaxies.
        if (depth <= Distance * 1e-5 || depth > Distance * 8) { return null; }
        double scale = height / (ProjectionHeightFactor * depth);
        double x = width / 2 + SkyVector.Dot(relative, _right) * scale;
        double y = height / 2 - SkyVector.Dot(relative, _up) * scale;
        return double.IsFinite(x) && double.IsFinite(y) ? new(x, y, depth) : null;
    }
}
