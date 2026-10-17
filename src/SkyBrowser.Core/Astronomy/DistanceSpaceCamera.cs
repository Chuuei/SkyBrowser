namespace SkyBrowser.Core.Astronomy;

public readonly record struct DistanceSpaceProjection(double X, double Y, double Depth);

/// <summary>Perspective camera in linear light-years, using the atlas J2000 axes (Y north).</summary>
public sealed class DistanceSpaceCamera
{
    public SkyVector Center { get; private set; }
    public double Distance { get; private set; } = 180_000;
    public double Yaw { get; private set; } = 35;
    public double Pitch { get; private set; } = 32;
    public const double FieldOfView = 55;
    public const double MinimumDistance = 10;
    public const double MaximumDistance = 1e12;

    public SkyVector Back => new SkyCoordinate(Yaw, Pitch).Vector;
    public SkyVector Right => SkyVector.Cross(new(0, 1, 0), Back).Normalized;
    public SkyVector Up => SkyVector.Cross(Back, Right).Normalized;
    public SkyVector Eye => Center + Back * Distance;
    public double ViewHeight => 2 * Distance * Math.Tan(FieldOfView * SkyCoordinate.Radians / 2);

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
    }

    public void Orbit(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch)) { return; }
        Yaw = SkyCoordinate.Normalize(Yaw + yaw);
        Pitch = Math.Clamp(Pitch + pitch, -89.5, 89.5);
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
        var relative = point - Eye;
        double depth = -SkyVector.Dot(relative, Back);
        // Relative clipping planes retain precision from nearby nebulae to distant galaxies.
        if (depth <= Distance * 1e-5 || depth > Distance * 8) { return null; }
        double scale = height / (2 * Math.Tan(FieldOfView * SkyCoordinate.Radians / 2) * depth);
        double x = width / 2 + SkyVector.Dot(relative, Right) * scale;
        double y = height / 2 - SkyVector.Dot(relative, Up) * scale;
        return double.IsFinite(x) && double.IsFinite(y) ? new(x, y, depth) : null;
    }
}
