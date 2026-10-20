using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class DistanceSpaceTests
{
    [Theory]
    [InlineData(0, 0, 100, 0, 0)]
    [InlineData(90, 0, 0, 0, -100)]
    [InlineData(0, 90, 0, 100, 0)]
    [InlineData(180, 0, -100, 0, 0)]
    public void ObjectPositionsUseLinearLightYearsAndJ2000Axes(double ra, double dec, double x, double y, double z)
    {
        var original = TargetDistances.Find("M81")!;
        var estimate = original with { LightYears = 100, Target = original.Target with { Position = new(ra, dec) } };
        var position = DistanceSpaceCamera.Position(estimate);
        Assert.InRange((position - new SkyVector(x, y, z)).Length, 0, 1e-10);
    }

    [Fact]
    public void CartesianSeparationMatchesKnownTargetMeasurement()
    {
        var a = TargetDistances.Find("M81")!;
        var b = TargetDistances.Find("M82")!;
        double separation = (DistanceSpaceCamera.Position(a) - DistanceSpaceCamera.Position(b)).Length;
        Assert.InRange(Math.Abs(separation - TargetDistances.Measure("M81", "M82").SeparationLightYears!.Value), 0, 1e-6);
        Assert.All(TargetDistances.All, estimate => Assert.InRange(
            Math.Abs(DistanceSpaceCamera.Position(estimate).Length / estimate.LightYears - 1), 0, 1e-12));
    }

    [Fact]
    public void PerspectiveHasExpectedHandednessAndRejectsClippedPoints()
    {
        var camera = new DistanceSpaceCamera();
        camera.Home(1000);
        camera.Orbit(-35, -32); // Eye on +X, looking at the origin; north is up and −Z is right.
        var center = camera.Project(default, 800, 600)!.Value;
        Assert.Equal(400, center.X, 8);
        Assert.Equal(300, center.Y, 8);
        Assert.True(camera.Project(new(0, 100, 0), 800, 600)!.Value.Y < 300);
        Assert.True(camera.Project(new(0, 0, -100), 800, 600)!.Value.X > 400);
        Assert.Null(camera.Project(camera.Eye, 800, 600));
        Assert.Null(camera.Project(new(1100, 0, 0), 800, 600));
        Assert.Null(camera.Project(new(-8000, 0, 0), 800, 600));
        Assert.Null(camera.Project(default, 0, 600));
    }

    [Theory]
    [InlineData(1e3)]
    [InlineData(1e8)]
    public void ProjectionIsScaleInvariantAcrossGalacticAndExtragalacticDistances(double scale)
    {
        var camera = new DistanceSpaceCamera();
        camera.Frame(default, 100);
        var a = camera.Project(new(15, 3, -9), 800, 600)!.Value;
        camera.Frame(default, 100 * scale);
        var b = camera.Project(new(15 * scale, 3 * scale, -9 * scale), 800, 600)!.Value;
        Assert.Equal(a.X, b.X, 8);
        Assert.Equal(a.Y, b.Y, 8);
    }

    [Fact]
    public void CameraCanTravelOrbitZoomAndRecoverAtTheSolarSystem()
    {
        var camera = new DistanceSpaceCamera();
        double original = camera.Distance;
        camera.Zoom(-1);
        Assert.True(camera.Distance > original);
        camera.Zoom(1);
        Assert.Equal(original, camera.Distance, 8);
        var expected = camera.Right * 30 + camera.Up * 40 - camera.Back * 50;
        camera.Move(30, 40, 50);
        Assert.InRange((camera.Center - expected).Length, 0, 1e-9);
        camera.Orbit(4000, 2000);
        Assert.InRange(camera.Pitch, -89.5, 89.5);
        Assert.Equal(1, camera.Right.Length, 10);
        Assert.Equal(1, camera.Up.Length, 10);
        camera.Zoom(1e20);
        Assert.Equal(DistanceSpaceCamera.MinimumDistance, camera.Distance);
        camera.Home();
        Assert.Equal(default, camera.Center);
        Assert.Equal(original, camera.Distance);
        camera.Zoom(double.NaN);
        Assert.Equal(original, camera.Distance);
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.Frame(default, double.NaN));
    }

    [Fact]
    public void ZoomFramingAndHomeCannotExceedTheMaximumFocusSpan()
    {
        var camera = new DistanceSpaceCamera();
        camera.Zoom(-1e20);
        Assert.Equal(DistanceSpaceCamera.MaximumDistance, camera.Distance);
        Assert.InRange(camera.ViewHeight, 13_800_000_000d - 0.001, 13_800_000_000d);
        for (int i = 0; i < 10; i++)
        {
            camera.Zoom(-100);
            Assert.Equal(DistanceSpaceCamera.MaximumDistance, camera.Distance);
        }
        var center = new SkyVector(100, -200, 300);
        camera.Frame(center, double.MaxValue);
        Assert.Equal(center, camera.Center);
        Assert.Equal(DistanceSpaceCamera.MaximumDistance, camera.Distance);
        camera.Home(double.MaxValue);
        Assert.Equal(default, camera.Center);
        Assert.Equal(DistanceSpaceCamera.MaximumDistance, camera.Distance);
        camera.Zoom(1);
        Assert.True(camera.ViewHeight < DistanceSpaceCamera.MaximumViewHeight);
        Assert.True(camera.Distance < DistanceSpaceCamera.MaximumDistance);
    }
}
