using System.Globalization;

namespace SkyBrowser.Core.Astronomy;

public static class SkyGeometry
{
    public static void Validate(SkyCoordinate coordinate)
    {
        if (!double.IsFinite(coordinate.RightAscension) || !double.IsFinite(coordinate.Declination) || coordinate.Declination is < -90 or > 90)
        {
            throw new ArgumentException("Enter finite J2000 coordinates with declination between -90 and 90 degrees.");
        }
    }

    public static SkyCoordinate Offset(SkyCoordinate origin, double positionAngle, double distance)
    {
        Validate(origin);
        if (!double.IsFinite(positionAngle) || !double.IsFinite(distance))
        {
            throw new ArgumentException("Angles must be finite.");
        }
        var east = SkyVector.Cross(origin.North, origin.Vector);
        double angle = positionAngle * SkyCoordinate.Radians;
        double radius = distance * SkyCoordinate.Radians;
        return SkyCoordinate.FromVector(origin.Vector * Math.Cos(radius) +
            (origin.North * Math.Cos(angle) + east * Math.Sin(angle)) * Math.Sin(radius));
    }

    public static SkyCoordinate Interpolate(SkyCoordinate start, SkyCoordinate end, double fraction)
    {
        Validate(start);
        Validate(end);
        if (!double.IsFinite(fraction) || fraction is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }
        double angle = start.DistanceTo(end) * SkyCoordinate.Radians;
        if (angle < 1e-12)
        {
            return start;
        }
        var tangent = end.Vector - start.Vector * Math.Cos(angle);
        // Antipodes have no unique shortest arc. Choose the meridian deterministically.
        tangent = tangent.Length < 1e-12 ? start.North : tangent.Normalized;
        return SkyCoordinate.FromVector(start.Vector * Math.Cos(angle * fraction) + tangent * Math.Sin(angle * fraction));
    }

    public static string FormatAngle(double degrees) => degrees >= 1
        ? degrees.ToString("0.###", CultureInfo.InvariantCulture) + "°"
        : degrees >= 1d / 60 ? (degrees * 60).ToString("0.##", CultureInfo.InvariantCulture) + "′"
        : (degrees * 3600).ToString("0.##", CultureInfo.InvariantCulture) + "″";

    public static double SpatialSeparation(SkyCoordinate start, double startDistance, SkyCoordinate end, double endDistance)
    {
        Validate(start);
        Validate(end);
        ValidateObjectDistance(startDistance);
        ValidateObjectDistance(endDistance);
        // The half-angle form of the cosine rule avoids subtracting nearly equal squares.
        // Scaling first also avoids overflow for large but representable distances.
        double scale = Math.Max(startDistance, endDistance);
        double radial = (startDistance - endDistance) / scale;
        double transverse = 2 * Math.Sqrt(startDistance / scale) * Math.Sqrt(endDistance / scale) *
            Math.Sin(start.DistanceTo(end) * SkyCoordinate.Radians / 2);
        double result = scale * double.Hypot(radial, transverse);
        return double.IsFinite(result) ? result : throw new ArgumentException("The resulting separation is too large to represent.");
    }

    public static void ValidateObjectDistance(double distance)
    {
        if (!double.IsFinite(distance) || distance <= 0)
        {
            throw new ArgumentException("Enter a positive, finite distance from us in light-years for each object.");
        }
    }
}

public sealed record SkyBookmark(string Id, string Name, SkyCoordinate Position, double FieldOfView, string Notes)
{
    public void Validate()
    {
        SkyGeometry.Validate(Position);
        if (!Guid.TryParse(Id, out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 160 || Notes.Length > 8192 ||
            !double.IsFinite(FieldOfView) || FieldOfView is < 0.05 or > 120)
        {
            throw new ArgumentException("Use a bookmark name up to 160 characters, notes up to 8,192 characters and a field from 0.05 to 120 degrees.");
        }
    }

    public override string ToString() => Name;
}

public sealed record SkyMeasurement(SkyCoordinate Start, SkyCoordinate End)
{
    public double? StartDistanceLightYears { get; init; }
    public double? EndDistanceLightYears { get; init; }
    public TargetDistanceEstimate? StartDistanceSource { get; init; }
    public TargetDistanceEstimate? EndDistanceSource { get; init; }
    public double Degrees => Start.DistanceTo(End);
    public string Label => SkyGeometry.FormatAngle(Degrees);
    public double? SeparationLightYears => StartDistanceLightYears is { } a && EndDistanceLightYears is { } b
        ? SkyGeometry.SpatialSeparation(Start, a, End, b) : null;
    public string DisplayLabel => SeparationLightYears is { } distance
        ? Label + " · " + (StartDistanceSource is not null || EndDistanceSource is not null
            ? TargetDistances.FormatApproximate(distance) : distance.ToString("G6", CultureInfo.InvariantCulture)) + " ly" : Label;
}
