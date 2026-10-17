using CosineKitty;
using Engine = CosineKitty.Astronomy;

namespace SkyBrowser.Core.Astronomy;

public sealed record VisibilityCriteria(double MinimumAltitude = 30, double MaximumSunAltitude = -18,
    double MinimumMoonSeparation = 30, bool IgnoreMoonBelowHorizon = true)
{
    public void Validate()
    {
        if (!double.IsFinite(MinimumAltitude) || MinimumAltitude is < 0 or > 89 ||
            !double.IsFinite(MaximumSunAltitude) || MaximumSunAltitude is < -30 or > 0 ||
            !double.IsFinite(MinimumMoonSeparation) || MinimumMoonSeparation is < 0 or > 180)
        {
            throw new ArgumentException("Choose target altitude 0–89°, Sun altitude -30–0°, and Moon separation 0–180°.");
        }
    }
}

public sealed record VisibilitySample(DateTimeOffset TimeUtc, double TargetAltitude, double SunAltitude,
    double MoonAltitude, double MoonSeparation, double MoonIllumination);
public sealed record VisibilityInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}
public sealed record HorizonEvent(DateTimeOffset TimeUtc, bool Rising);
public sealed record VisibilityReport(SkyCoordinate Target, ObserverLocation Location, DateTimeOffset StartUtc,
    VisibilityCriteria Criteria, VisibilitySample[] Samples, HorizonEvent[] HorizonEvents,
    DateTimeOffset? TransitUtc, double MaximumAltitude, string HorizonStatus,
    VisibilityInterval[] Darkness, VisibilityInterval[] ImagingWindows)
{
    public DateTimeOffset EndUtc => StartUtc.AddDays(1);
    public string MoonPhase { get; init; } = "";
}

/// <summary>Unrefracted, topocentric planning in UTC over one day; target coordinates are J2000.</summary>
public static class TargetVisibility
{
    public static VisibilityReport Calculate(SkyCoordinate target, ObserverLocation location, DateTimeOffset start,
        VisibilityCriteria? criteria = null, CancellationToken token = default)
    {
        SkyGeometry.Validate(target);
        location.Validate();
        criteria ??= new();
        criteria.Validate();
        start = start.ToUniversalTime();
        var end = start.AddDays(1);
        if (start.Year < 1900 || end.Year > 2100)
        {
            throw new ArgumentException("The complete planning day must lie between 1900 and 2100.");
        }
        var observer = new Observer(location.Latitude, location.Longitude, 0);
        var evaluations = new Dictionary<long, VisibilitySample>();
        VisibilitySample At(DateTimeOffset instant)
        {
            token.ThrowIfCancellationRequested();
            if (evaluations.TryGetValue(instant.UtcTicks, out var cached))
            {
                return cached;
            }
            var time = new AstroTime(instant.UtcDateTime);
            var frame = new ObserverFrame(location, instant);
            SkyCoordinate Position(Body body)
            {
                var position = Engine.Equator(body, time, observer, EquatorEpoch.J2000, Aberration.Corrected);
                return new(position.ra * 15, position.dec);
            }
            var moon = Position(Body.Moon);
            var sample = new VisibilitySample(instant, frame.ToHorizontal(target).Altitude,
                frame.ToHorizontal(Position(Body.Sun)).Altitude, frame.ToHorizontal(moon).Altitude,
                target.DistanceTo(moon), Engine.Illumination(Body.Moon, time).phase_fraction);
            evaluations.Add(instant.UtcTicks, sample);
            return sample;
        }

        const int steps = 720; // Two-minute chart samples. Event boundaries are refined separately.
        var samples = Enumerable.Range(0, steps + 1).Select(i => At(start.AddMinutes(i * 2))).ToArray();
        List<DateTimeOffset> extrema = [];
        for (int i = 1; i < samples.Length - 1; i++)
        {
            double before = samples[i - 1].TargetAltitude, current = samples[i].TargetAltitude, after = samples[i + 1].TargetAltitude;
            if ((current > before && current >= after) || (current < before && current <= after))
            {
                bool maximum = current > before;
                var left = samples[i - 1].TimeUtc;
                var right = samples[i + 1].TimeUtc;
                while ((right - left).TotalSeconds > 0.25)
                {
                    var a = left + (right - left) * 0.38196601125;
                    var b = left + (right - left) * 0.61803398875;
                    if ((At(a).TargetAltitude < At(b).TargetAltitude) == maximum)
                    {
                        left = a;
                    }
                    else
                    {
                        right = b;
                    }
                }
                extrema.Add(left + (right - left) / 2);
            }
        }
        var nodes = samples.Select(s => s.TimeUtc).Concat(extrema).Distinct().Order().ToArray();
        var transits = extrema.Where(t => At(t).TargetAltitude > At(t.AddSeconds(-1)).TargetAltitude &&
            At(t).TargetAltitude > At(t.AddSeconds(1)).TargetAltitude).Order().ToArray();

        List<HorizonEvent> events = [];
        for (int i = 1; i < nodes.Length; i++)
        {
            var a = At(nodes[i - 1]);
            var b = At(nodes[i]);
            if ((a.TargetAltitude >= 0) != (b.TargetAltitude >= 0))
            {
                var crossing = Boundary(nodes[i - 1], nodes[i], t => At(t).TargetAltitude >= 0);
                events.Add(new(crossing, b.TargetAltitude > a.TargetAltitude));
            }
        }

        DateTimeOffset Boundary(DateTimeOffset a, DateTimeOffset b, Func<DateTimeOffset, bool> predicate)
        {
            bool initial = predicate(a);
            while ((b - a).TotalSeconds > 0.5)
            {
                var middle = a + (b - a) / 2;
                if (predicate(middle) == initial)
                {
                    a = middle;
                }
                else
                {
                    b = middle;
                }
            }
            return a + (b - a) / 2;
        }

        VisibilityInterval[] Windows(Func<VisibilitySample, bool> predicate)
        {
            List<VisibilityInterval> windows = [];
            DateTimeOffset? began = predicate(At(start)) ? start : null;
            for (int i = 1; i < nodes.Length; i++)
            {
                bool previous = predicate(At(nodes[i - 1])), current = predicate(At(nodes[i]));
                if (previous == current)
                {
                    continue;
                }
                var boundary = Boundary(nodes[i - 1], nodes[i], t => predicate(At(t)));
                if (current)
                {
                    began = boundary;
                }
                else if (began is { } from)
                {
                    windows.Add(new(from, boundary));
                    began = null;
                }
            }
            if (began is { } last)
            {
                windows.Add(new(last, end));
            }
            return windows.ToArray();
        }

        double maximumAltitude = nodes.Max(t => At(t).TargetAltitude);
        double minimumAltitude = nodes.Min(t => At(t).TargetAltitude);
        string status = minimumAltitude >= 0 ? "Above the horizon throughout this period" :
            maximumAltitude < 0 ? "Below the horizon throughout this period" : "Rises and sets in this period";
        string[] phases = ["New Moon", "Waxing crescent", "First quarter", "Waxing gibbous", "Full Moon", "Waning gibbous", "Last quarter", "Waning crescent"];
        double phase = Engine.MoonPhase(new AstroTime(start.AddHours(12).UtcDateTime));
        return new(target, location, start, criteria, samples, events.ToArray(), transits.Length > 0 ? transits[0] : null,
            maximumAltitude, status, Windows(s => s.SunAltitude <= criteria.MaximumSunAltitude),
            Windows(s => s.TargetAltitude >= criteria.MinimumAltitude && s.SunAltitude <= criteria.MaximumSunAltitude &&
                (s.MoonSeparation >= criteria.MinimumMoonSeparation || (criteria.IgnoreMoonBelowHorizon && s.MoonAltitude < 0))))
        {
            MoonPhase = phases[(int)((phase + 22.5) % 360 / 45)]
        };
    }
}
