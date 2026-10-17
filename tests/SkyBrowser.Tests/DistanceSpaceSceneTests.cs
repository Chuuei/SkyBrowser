using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class DistanceSpaceSceneTests
{
    private static readonly TargetDistanceEstimate M31 = TargetDistances.Find("M31")!;
    private static readonly TargetDistanceEstimate M81 = TargetDistances.Find("M81")!;
    private static readonly TargetDistanceEstimate M82 = TargetDistances.Find("M82")!;

    [Fact]
    public void SecondTargetPreservesNavigationAndProducesOneStableMeasurement()
    {
        var scene = new DistanceSpaceScene { Selected = M81 };
        var center = scene.Camera.Center;
        double distance = scene.Camera.Distance;
        int updates = 0;
        scene.ComparisonChanged += (_, _) => updates++;
        scene.SelectTarget(M82, second: true);

        Assert.Same(M81, scene.Selected);
        Assert.Same(M82, scene.ComparisonTarget);
        Assert.Equal(center, scene.Camera.Center);
        Assert.Equal(distance, scene.Camera.Distance);
        Assert.InRange(scene.PairMeasurement!.SeparationLightYears!.Value, 419_000, 421_000);
        Assert.Equal(1, updates);
    }

    [Fact]
    public void MissingPrimaryAndEmptySecondClicksFollowTheSameSelectionRules()
    {
        var scene = new DistanceSpaceScene();
        scene.SelectTarget(M81, second: true);
        Assert.Same(M81, scene.Selected);
        Assert.Null(scene.ComparisonTarget);
        scene.SelectTarget(M82, second: true);
        var measurement = scene.PairMeasurement;
        scene.SelectTarget(null, second: true);
        Assert.Same(measurement, scene.PairMeasurement);
        scene.SelectTarget(M81, second: true);
        Assert.Null(scene.ComparisonTarget);
        Assert.Null(scene.PairMeasurement);
    }

    [Fact]
    public void SelectingTheSamePrimaryRecentresWithoutLosingZoomOrComparison()
    {
        var scene = new DistanceSpaceScene { Selected = M81 };
        scene.SelectTarget(M82, second: true);
        var measurement = scene.PairMeasurement;
        scene.Camera.Zoom(3);
        double distance = scene.Camera.Distance;
        scene.Camera.Move(200, 100, 0);
        scene.Selected = M81;

        Assert.Equal(DistanceSpaceCamera.Position(M81), scene.Camera.Center);
        Assert.Equal(distance, scene.Camera.Distance);
        Assert.Same(M82, scene.ComparisonTarget);
        Assert.Same(measurement, scene.PairMeasurement);
    }

    [Fact]
    public void ChangingOrClearingThePrimaryCannotLeaveAStalePair()
    {
        var scene = new DistanceSpaceScene { Selected = M81 };
        scene.SelectTarget(M82, second: true);
        scene.ComparisonChanged += (_, _) => Assert.Null(scene.PairMeasurement);
        scene.Selected = M31;
        Assert.Null(scene.PairMeasurement);
        Assert.Null(scene.ComparisonTarget);
        Assert.Equal(DistanceSpaceCamera.Position(M31), scene.Camera.Center);
        scene.Selected = null;
        Assert.Null(scene.Selected);
    }

    [Fact]
    public void FramePairAndResetAreAvailableWithoutAWindow()
    {
        var scene = new DistanceSpaceScene { Selected = M81 };
        scene.SelectTarget(M82, second: true);
        scene.FramePair();
        Assert.Equal((DistanceSpaceCamera.Position(M81) + DistanceSpaceCamera.Position(M82)) / 2, scene.Camera.Center);
        scene.ResetToSun();
        Assert.Null(scene.Selected);
        Assert.Null(scene.ComparisonTarget);
        Assert.Null(scene.PairMeasurement);
        Assert.Equal(default, scene.Camera.Center);
        Assert.Equal(new DistanceSpaceCamera().Distance, scene.Camera.Distance);
    }

    [Fact]
    public void ClearingAComparisonKeepsThePrimaryAndCamera()
    {
        var scene = new DistanceSpaceScene { Selected = M81 };
        scene.SelectTarget(M82, second: true);
        scene.FramePair();
        var center = scene.Camera.Center;
        double distance = scene.Camera.Distance;
        scene.ClearComparison();
        scene.ClearComparison();
        Assert.Null(scene.PairMeasurement);
        Assert.Same(M81, scene.Selected);
        Assert.Equal(center, scene.Camera.Center);
        Assert.Equal(distance, scene.Camera.Distance);
    }
}
