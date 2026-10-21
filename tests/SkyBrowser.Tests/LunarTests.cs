using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class LunarTests
{
    // Independent phase dates: NASA GSFC, Phases of the Moon 2001–2100 (UTC).
    // https://eclipse.gsfc.nasa.gov/phase/phases2001.html
    [Theory]
    [InlineData("2024-04-08T18:21:00Z", 0, "New Moon", 0)]
    [InlineData("2024-04-15T19:13:00Z", 90, "First quarter", .5)]
    [InlineData("2024-04-23T23:49:00Z", 180, "Full Moon", 1)]
    [InlineData("2024-05-01T11:27:00Z", 270, "Last quarter", .5)]
    public void KnownPhasesHaveCorrectIlluminationAndBrightSide(string date, double phase, string name, double fraction)
    {
        var moon = LunarState.Calculate(DateTimeOffset.Parse(date));
        Assert.InRange(Math.Abs(SkyCoordinate.Normalize(moon.PhaseDegrees - phase + 180) - 180), 0, .02);
        Assert.Equal(name, moon.PhaseName);
        Assert.InRange(Math.Abs(moon.IlluminatedFraction - fraction), 0, .004);
        Assert.Equal(1, moon.DiskLight.Length, 10);
        Assert.Equal(2 * moon.IlluminatedFraction - 1, moon.DiskLight.Z, 10);
        if (phase == 90) { Assert.True(moon.DiskLight.X > .8); }
        if (phase == 270) { Assert.True(moon.DiskLight.X < -.8); }
    }

    [Fact]
    public void LunarOverlayAgreesWithTheVisibilityPlanner()
    {
        var time = new DateTimeOffset(2024, 4, 15, 19, 13, 0, TimeSpan.Zero);
        var location = new ObserverLocation(45, -73);
        var moon = LunarState.Calculate(time, location);
        var report = TargetVisibility.Calculate(moon.Position, location, time);
        Assert.InRange(report.Samples[0].MoonSeparation, 0, 1e-7);
    }

    [Fact]
    public void ObserverParallaxAndUtcOffsetsArePreserved()
    {
        var instant = new DateTimeOffset(2024, 4, 15, 19, 13, 0, TimeSpan.Zero);
        var geocentric = LunarState.Calculate(instant);
        var first = LunarState.Calculate(instant, new(0, 0));
        var opposite = LunarState.Calculate(instant, new(0, 180));
        Assert.False(geocentric.IsTopocentric);
        Assert.True(first.IsTopocentric);
        Assert.InRange(first.Position.DistanceTo(opposite.Position), .5, 2.2);
        Assert.Equal(first, LunarState.Calculate(instant.ToOffset(TimeSpan.FromHours(8)), new(0, 0)));
        Assert.InRange(first.Position.RightAscension, 90, 130);
        Assert.InRange(first.Position.Declination, 15, 35);
        Assert.Throws<ArgumentOutOfRangeException>(() => LunarState.Calculate(instant.AddYears(-200)));
        Assert.Throws<ArgumentException>(() => LunarState.Calculate(instant, new(91, 0)));
    }
}
