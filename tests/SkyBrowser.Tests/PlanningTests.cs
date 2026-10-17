using SkyBrowser.Core.Astronomy;
using CosineKitty;
using Engine = CosineKitty.Astronomy;

namespace SkyBrowser.Tests;

public sealed class PlanningTests
{
    [Fact]
    public void EquatorialTargetHasIndependentGeometricRiseSetTransitAndWindowCriteria()
    {
        var start = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var report = TargetVisibility.Calculate(new(0, 0), new(0, 0), start, new(25, -18, 20));
        Assert.Equal(721, report.Samples.Length);
        Assert.Contains(report.HorizonEvents, e => e.Rising);
        Assert.Contains(report.HorizonEvents, e => !e.Rising);
        Assert.InRange(report.MaximumAltitude, 89.99, 90);
        Assert.NotNull(report.TransitUtc);
        Assert.InRange(report.TransitUtc.Value.Hour + report.TransitUtc.Value.Minute / 60d, 17.1, 17.5);
        foreach (var crossing in report.HorizonEvents)
        {
            double altitude = new ObserverFrame(report.Location, crossing.TimeUtc).ToHorizontal(report.Target).Altitude;
            Assert.InRange(Math.Abs(altitude), 0, 0.002);
        }
        // At the equator, a zero-declination target remains up for half a sidereal day.
        var rise = report.HorizonEvents.Single(e => e.Rising).TimeUtc;
        var set = report.HorizonEvents.Single(e => !e.Rising).TimeUtc;
        double hours = (set - rise).TotalHours;
        if (hours < 0)
        {
            hours += 23.9344696;
        }
        Assert.InRange(hours, 11.96, 11.98);
        foreach (var interval in report.ImagingWindows)
        {
            Assert.True(interval.EndUtc > interval.StartUtc);
            var instant = interval.StartUtc + interval.Duration / 2;
            var sample = report.Samples.MinBy(s => Math.Abs((s.TimeUtc - instant).TotalSeconds))!;
            Assert.True(sample.TargetAltitude >= 24.9);
            Assert.True(sample.SunAltitude <= -17.9);
            Assert.True(sample.MoonAltitude < 0 || sample.MoonSeparation >= 19.9);
        }
    }

    [Theory]
    [InlineData(75, 80, true)]
    [InlineData(75, -80, false)]
    [InlineData(-75, -80, true)]
    [InlineData(-75, 80, false)]
    public void CircumpolarAndNeverRisingTargetsDoNotInventHorizonEvents(double latitude, double dec, bool above)
    {
        var report = TargetVisibility.Calculate(new(359.99, dec), new(latitude, 170), new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        Assert.Empty(report.HorizonEvents);
        Assert.All(report.Samples, s => Assert.Equal(above, s.TargetAltitude > 0));
        Assert.Contains(above ? "Above" : "Below", report.HorizonStatus);
        if (!above)
        {
            Assert.Empty(report.ImagingWindows);
        }
    }

    [Theory]
    [InlineData(6, 21, false)]
    [InlineData(12, 21, true)]
    public void PolarSummerAndWinterHaveCorrectAstronomicalDarkness(int month, int day, bool dark)
    {
        var report = TargetVisibility.Calculate(new(45, 85), new(89, 0), new(2024, month, day, 0, 0, 0, TimeSpan.Zero));
        if (dark)
        {
            Assert.Equal(TimeSpan.FromDays(1), Assert.Single(report.Darkness).Duration);
        }
        else
        {
            Assert.Empty(report.Darkness);
        }
    }

    [Theory]
    [InlineData(2024, 4, 8, 6, "New Moon", false)]
    [InlineData(2024, 3, 24, 19, "Full Moon", true)]
    public void LunarIlluminationMatchesPublishedEclipseDates(int year, int month, int day, int hour, string name, bool full)
    {
        // NASA: total solar eclipse 2024-04-08 and penumbral lunar eclipse 2024-03-25.
        // https://eclipse.gsfc.nasa.gov/SKYCAL/SKYCAL.html?cal=2024
        var report = TargetVisibility.Calculate(new(30, 40), new(0, 0), new(year, month, day, hour, 0, 0, TimeSpan.Zero));
        double illuminated = report.Samples[360].MoonIllumination;
        Assert.Equal(name, report.MoonPhase);
        Assert.True(full ? illuminated > 0.999 : illuminated < 0.001);
        var sample = report.Samples[123];
        var time = new AstroTime(sample.TimeUtc.UtcDateTime);
        var moon = Engine.Equator(Body.Moon, time, new Observer(0, 0, 0), EquatorEpoch.OfDate, Aberration.Corrected);
        var horizon = Engine.Horizon(time, new Observer(0, 0, 0), moon.ra, moon.dec, Refraction.None);
        Assert.Equal(horizon.altitude, sample.MoonAltitude, 7);
    }

    [Fact]
    public void PlanningUsesUtcAndValidatesDatesCoordinatesAndCancellation()
    {
        var start = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
        var report = TargetVisibility.Calculate(new(359.9, 20), new(45, -180), start);
        Assert.Equal(start.ToUniversalTime(), report.StartUtc);
        Assert.Equal(TimeSpan.FromDays(1), report.EndUtc - report.StartUtc);
        Assert.Throws<ArgumentException>(() => TargetVisibility.Calculate(new(0, 91), new(0, 0), start));
        Assert.Throws<ArgumentException>(() => TargetVisibility.Calculate(new(0, 0), new(0, 0), start, new(MinimumAltitude: double.NaN)));
        Assert.ThrowsAny<OperationCanceledException>(() => TargetVisibility.Calculate(new(0, 0), new(0, 0), start, token: new(true)));
    }
}
