using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Tests;

public sealed class ObserverTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(52.52, 13.405)]
    [InlineData(-33.87, 151.21)]
    [InlineData(0, 180)]
    [InlineData(90, -180)]
    [InlineData(-90, 0)]
    public void HorizontalFrameRoundTripsAndHasLevelHorizon(double latitude, double longitude)
    {
        var frame = new ObserverFrame(new(latitude, longitude), Instant);
        foreach (var direction in new[]
        {
            new HorizontalCoordinate(0, 0),
            new(90, 0),
            new(180, 20),
            new(359.99, -20),
            new(270, 89)
        }

        )
        {
            var sky = frame.FromHorizontal(direction.Azimuth, direction.Altitude);
            var actual = frame.ToHorizontal(sky);
            Assert.True(Math.Abs((actual.Azimuth - direction.Azimuth + 540) % 360 - 180) < 1e-7);
            Assert.Equal(direction.Altitude, actual.Altitude, 7);
            var camera = new SkyCamera
            {
                Center = sky,
                Observer = frame
            };
            Assert.Equal(0, SkyVector.Dot(camera.Forward, camera.Up), 10);
            Assert.Equal(1, camera.Up.Length, 10);
        }

        var south = new SkyCamera
        {
            Center = frame.FromHorizontal(180, 20),
            Observer = frame,
            FieldOfView = 90
        };
        var left = south.Project(frame.FromHorizontal(170, 0).Vector, 1000, 700)!.Value;
        var right = south.Project(frame.FromHorizontal(190, 0).Vector, 1000, 700)!.Value;
        Assert.Equal(left.Y, right.Y, 7);
        Assert.True(left.X < right.X);
        foreach (double altitude in new[]
        {
            -90d,
            90d
        }

        )
        {
            var camera = new SkyCamera
            {
                Center = frame.FromHorizontal(0, altitude),
                Observer = frame
            };
            Assert.True(double.IsFinite(camera.Up.Length));
            Assert.Equal(1, camera.Up.Length, 9);
        }
    }

    [Fact]
    public void J2000ReferenceHasCorrectSiderealTimeLongitudeAndEastDirection()
    {
        // USNO/Meeus GMST at J2000 noon is 280.46061837°. Nutation adds a few arcseconds.
        var noon = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var greenwich = new ObserverFrame(new(0, 0), noon);
        Assert.True(greenwich.FromHorizontal(0, 90).DistanceTo(new(280.46061837, 0)) < 0.01);
        Assert.True(greenwich.FromHorizontal(90, 0).DistanceTo(new(10.46061837, 0)) < 0.01);
        var eastLongitude = new ObserverFrame(new(0, 90), noon);
        Assert.True(eastLongitude.FromHorizontal(0, 90).DistanceTo(new(10.46061837, 0)) < 0.01);
    }

    [Fact]
    public void SystemClockAndFixedTimeAreExplicit()
    {
        var clock = new FrozenClock(Instant.AddHours(6));
        var settings = new ObserverSettings(new(52, 13), true, Instant);
        Assert.Equal(Instant.AddHours(6), settings.ResolveTime(clock));
        Assert.Equal(Instant, (settings with { UseSystemTime = false }).ResolveTime(clock));
        var camera = new SkyCamera();
        Assert.Null(camera.Observer);
        Assert.Equal(camera.Center.North, camera.Up);
    }

    [Theory]
    [InlineData(0, 0, 2000)]
    [InlineData(50, 10, 2026)]
    [InlineData(-34, 151, 2026)]
    [InlineData(80, -179, 1900)]
    [InlineData(-80, 179, 2100)]
    public void ObserverFrameMatchesIndependentHourAngleEquations(double latitude, double longitude, int year)
    {
        var instant = new DateTimeOffset(year, 4, 17, 21, 30, 0, TimeSpan.Zero);
        var time = new CosineKitty.AstroTime(instant.UtcDateTime);
        var frame = new ObserverFrame(new(latitude, longitude), instant);
        var toDate = CosineKitty.Astronomy.Rotation_EQJ_EQD(time);
        foreach (var coordinate in new[] { new SkyCoordinate(37.9545, 89.2641), new(101.287, -16.716), new(279.235, 38.784), new(359.9, -70) })
        {
            // The catalogue is J2000; hour-angle equations require equator-of-date RA/Dec.
            // This route does not use the HOR rotation matrix or the app's inverse transform.
            var vector = coordinate.Vector;
            var equatorial = CosineKitty.Astronomy.EquatorFromVector(CosineKitty.Astronomy.RotateVector(
                toDate, new CosineKitty.AstroVector(vector.X, -vector.Z, vector.Y, time)));
            double hourAngle = (15 * CosineKitty.Astronomy.SiderealTime(time) + longitude - 15 * equatorial.ra) * SkyCoordinate.Radians;
            var (sinLatitude, cosLatitude) = Math.SinCos(latitude * SkyCoordinate.Radians);
            var (sinDeclination, cosDeclination) = Math.SinCos(equatorial.dec * SkyCoordinate.Radians);
            double altitude = Math.Asin(sinLatitude * sinDeclination + cosLatitude * cosDeclination * Math.Cos(hourAngle)) / SkyCoordinate.Radians;
            double azimuth = SkyCoordinate.Normalize(Math.Atan2(
                -cosDeclination * Math.Sin(hourAngle),
                cosLatitude * sinDeclination - sinLatitude * cosDeclination * Math.Cos(hourAngle)) / SkyCoordinate.Radians);
            var actual = frame.ToHorizontal(coordinate);
            Assert.Equal(altitude, actual.Altitude, 7);
            Assert.InRange(Math.Abs((actual.Azimuth - azimuth + 540) % 360 - 180), 0, 1e-7);
        }
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(90)]
    public void PolarisAltitudeFollowsLatitudeRatherThanZenith(double latitude)
    {
        var polaris = new SkyCatalog().Stars.Single(star => star.Id == 11767).Position;
        for (int hour = 0; hour < 24; hour += 3)
        {
            var frame = new ObserverFrame(new(latitude, 0), Instant.AddHours(hour));
            var position = frame.ToHorizontal(polaris);
            Assert.InRange(position.Altitude, latitude - 1.1, Math.Min(90, latitude + 1.1));
            Assert.InRange(polaris.DistanceTo(SkyCoordinate.FromVector(frame.Zenith)), 90 - latitude - 1.1, 90 - latitude + 1.1);
        }
    }

    [Fact]
    public void FixedNorthFacingCameraShowsCircularDiurnalMotion()
    {
        var start = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var star = new SkyCoordinate(0, 65);
        var points = new List<(double X, double Y)>();
        for (int hour = 0; hour <= 24; hour += 6)
        {
            var frame = new ObserverFrame(new(50, 0), start.AddHours(hour));
            var camera = new SkyCamera { Observer = frame, Center = frame.FromHorizontal(0, 50), FieldOfView = 100 };
            var point = camera.Project(star.Vector, 1000, 700)!.Value;
            points.Add((point.X - 500, point.Y - 350));
            var zenith = camera.Project(frame.Zenith, 1000, 700)!.Value;
            Assert.True(zenith.Y < 10); // Zenith is 40 degrees above the pole, not at the view center.
        }

        double radius = Math.Sqrt(points[0].X * points[0].X + points[0].Y * points[0].Y);
        foreach (var point in points)
        {
            Assert.InRange(Math.Sqrt(point.X * point.X + point.Y * point.Y), radius - 0.05, radius + 0.05);
        }
        for (int index = 1; index < points.Count; index++)
        {
            var before = points[index - 1];
            var after = points[index];
            double angle = Math.Atan2(before.X * after.Y - before.Y * after.X, before.X * after.X + before.Y * after.Y) / SkyCoordinate.Radians;
            Assert.InRange(angle, -91, -89); // Counterclockwise when facing north; about a quarter-turn per six hours.
        }
    }

    [Fact]
    public void LocalTimeConversionHandlesUtcOffsetsAndDaylightSavingAmbiguities()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Observer test zone",
            TimeSpan.FromHours(1),
            "Test",
            "Test standard",
            "Test daylight",
            [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new(2026, 1, 1),
                new(2026, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 2, 0, 0), 3, 29),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 3, 0, 0), 10, 25)
            )]
        );
        Assert.Equal(Instant, ObserverTimeInput.Resolve(new(2026, 10, 15), TimeSpan.FromHours(20), zone));
        Assert.Equal(Instant, ObserverTimeInput.Resolve(new(2026, 10, 15), TimeSpan.FromHours(18), TimeZoneInfo.Utc));
        Assert.Throws<ArgumentException>(() => ObserverTimeInput.Resolve(new(2026, 3, 29), TimeSpan.FromHours(2.5), zone));
        Assert.Throws<ArgumentException>(() => ObserverTimeInput.Resolve(new(2026, 10, 25), TimeSpan.FromHours(2.5), zone));
        Assert.Throws<ArgumentException>(() => ObserverTimeInput.Resolve(new(2026, 10, 15), TimeSpan.FromHours(24), zone));
    }

    [Fact]
    public void ObserverLocationPersistsAndCanBeClearedWithoutSelectingADefault()
    {
        string directory = TestImages.NewDirectory(), path = System.IO.Path.Combine(directory, "observer.json");
        try
        {
            var store = new ObserverPreferences(path);
            Assert.Null(store.Read());
            var location = new ObserverLocation(52.52, 13.405);
            store.Write(location);
            Assert.Equal(location, new ObserverPreferences(path).Read());
            store.Write(null);
            Assert.Null(new ObserverPreferences(path).Read());
            System.IO.File.WriteAllText(path, "{broken");
            Assert.Null(store.Read());
            Assert.Throws<ArgumentException>(() => new ObserverLocation(91, 0).Validate());
            Assert.Throws<ArgumentException>(() => new ObserverLocation(0, double.NaN).Validate());
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    private sealed class FrozenClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
