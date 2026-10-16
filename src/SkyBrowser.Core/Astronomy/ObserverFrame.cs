using CosineKitty;

namespace SkyBrowser.Core.Astronomy;

public sealed record ObserverLocation(double Latitude, double Longitude)
{
    public void Validate()
    {
        if (!double.IsFinite(Latitude) ||
            Latitude is < -90 or > 90 ||
            !double.IsFinite(Longitude) ||
            Longitude is < -180 or > 180)
        {
            throw new ArgumentException("Latitude must be between −90° and +90°, and longitude between −180° and +180°.");
        }
    }
}

public sealed record ObserverSettings(ObserverLocation Location, bool UseSystemTime, DateTimeOffset TimeUtc)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Location);
        Location.Validate();
        if (TimeUtc.UtcDateTime.Year is < 1900 or > 2100)
        {
            throw new ArgumentException("Choose an observing date between 1900 and 2100.");
        }
    }

    public DateTimeOffset ResolveTime(TimeProvider? clock = null) => UseSystemTime ? (clock ?? TimeProvider.System).GetUtcNow() : TimeUtc.ToUniversalTime();
}

public readonly record struct HorizontalCoordinate(double Azimuth, double Altitude);

/// <summary>Local horizon expressed in the atlas's J2000 coordinates, including precession and nutation.</summary>
public sealed class ObserverFrame
{
    public ObserverLocation Location { get; }

    public DateTimeOffset TimeUtc { get; }

    public SkyVector North { get; }

    public SkyVector East { get; }

    public SkyVector Zenith { get; }

    public ObserverFrame(ObserverLocation location, DateTimeOffset instant)
    {
        location.Validate();
        Location = location;
        TimeUtc = instant.ToUniversalTime();
        var time = new AstroTime(TimeUtc.UtcDateTime);
        var rotation = CosineKitty.Astronomy.Rotation_HOR_EQJ(time, new Observer(location.Latitude, location.Longitude, 0));

        // Astronomy Engine uses HOR = north/west/up and EQJ = RA 0h/RA 6h/north pole.
        // SkyBrowser's 3D axes are RA 0h/north pole/RA 18h.
        SkyVector Axis(double x, double y, double z)
        {
            var vector = CosineKitty.Astronomy.RotateVector(rotation, new AstroVector(x, y, z, time));
            return new SkyVector(vector.x, vector.z, -vector.y).Normalized;
        }

        North = Axis(1, 0, 0);
        East = Axis(0, -1, 0);
        Zenith = Axis(0, 0, 1);
    }

    public SkyCoordinate FromHorizontal(double azimuth, double altitude)
    {
        if (!double.IsFinite(azimuth) || !double.IsFinite(altitude) || altitude is < -90 or > 90)
        {
            throw new ArgumentException("Enter a finite azimuth and altitude between −90° and +90°.");
        }

        var (sinAz, cosAz) = Math.SinCos(azimuth * SkyCoordinate.Radians);
        var (sinAlt, cosAlt) = Math.SinCos(altitude * SkyCoordinate.Radians);
        return SkyCoordinate.FromVector(North * (cosAlt * cosAz) + East * (cosAlt * sinAz) + Zenith * sinAlt);
    }

    public HorizontalCoordinate ToHorizontal(SkyCoordinate coordinate)
    {
        var vector = coordinate.Vector;
        return new(
            SkyCoordinate.Normalize(Math.Atan2(SkyVector.Dot(vector, East), SkyVector.Dot(vector, North)) / SkyCoordinate.Radians),
            Math.Asin(Math.Clamp(SkyVector.Dot(vector, Zenith), -1, 1)) / SkyCoordinate.Radians
        );
    }

    public SkyVector UpAt(SkyVector forward)
    {
        var up = Zenith - forward * SkyVector.Dot(Zenith, forward);
        if (up.Length < 1e-9)
        {
            // At the zenith or nadir, projecting Zenith onto the view plane collapses to zero.
            // North still provides a stable camera orientation there.
            up = North - forward * SkyVector.Dot(North, forward);
        }

        return up.Normalized;
    }
}
