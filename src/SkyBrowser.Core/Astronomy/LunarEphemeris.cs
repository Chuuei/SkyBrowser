using CosineKitty;
using Engine = CosineKitty.Astronomy;

namespace SkyBrowser.Core.Astronomy;

/// <summary>Apparent J2000 Moon position and sunlight direction; no terrain, refraction or eclipse shading.</summary>
public sealed record LunarState(DateTimeOffset TimeUtc, SkyCoordinate Position, SkyVector SunlightDirection,
    double PhaseDegrees, double IlluminatedFraction, bool IsTopocentric)
{
    public string PhaseName => PhaseDegrees switch
    {
        < 22.5 or >= 337.5 => "New Moon",
        < 67.5 => "Waxing crescent",
        < 112.5 => "First quarter",
        < 157.5 => "Waxing gibbous",
        < 202.5 => "Full Moon",
        < 247.5 => "Waning gibbous",
        < 292.5 => "Last quarter",
        _ => "Waning crescent"
    };

    public static LunarState Calculate(DateTimeOffset instant, ObserverLocation? location = null)
    {
        if (instant.UtcDateTime.Year is < 1900 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(instant), "Choose a date between 1900 and 2100.");
        }
        location?.Validate();
        var time = new AstroTime(instant.UtcDateTime);
        // Astronomy Engine EQJ axes = RA 0h / RA 6h / north. Atlas axes = RA 0h / north / RA 18h.
        static SkyVector Convert(AstroVector vector) => new(vector.x, vector.z, -vector.y);
        var moon = Convert(Engine.GeoVector(Body.Moon, time, Aberration.Corrected));
        var sun = Convert(Engine.GeoVector(Body.Sun, time, Aberration.Corrected));
        var observer = location is null ? new SkyVector() : Convert(Engine.ObserverVector(time,
            new Observer(location.Latitude, location.Longitude, 0), EquatorEpoch.J2000));
        var position = SkyCoordinate.FromVector(moon - observer);
        var sunlight = (sun - moon).Normalized;
        double illuminated = (1 + SkyVector.Dot(sunlight, (observer - moon).Normalized)) / 2;
        return new(instant.ToUniversalTime(), position, sunlight, Engine.MoonPhase(time),
            Math.Clamp(illuminated, 0, 1), location is not null);
    }

    /// <summary>Sunlight in an illustrative disk: x is celestial west, y south, z toward the observer.</summary>
    public SkyVector DiskLight
    {
        get
        {
            var east = SkyVector.Cross(Position.North, Position.Vector);
            return new(-SkyVector.Dot(SunlightDirection, east), -SkyVector.Dot(SunlightDirection, Position.North),
                -SkyVector.Dot(SunlightDirection, Position.Vector));
        }
    }
}
