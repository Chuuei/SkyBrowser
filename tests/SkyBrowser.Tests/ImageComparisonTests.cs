using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class ImageComparisonTests
{
    [Theory]
    [InlineData(359.9, 0)]
    [InlineData(45, 89.9)]
    [InlineData(90, -89.9)]
    public void OverlappingRotatedFieldsCanCompareAndBothFitTheViewport(double ra, double dec)
    {
        var first = ImagePlacement.FromManual(new(ra, dec, 4, 37), 2000, 1000);
        var insetCenter = first.At(0.8, 0.2);
        var second = ImagePlacement.FromManual(new(insetCenter.RightAscension, insetCenter.Declination, 0.4, -20, true), 1000, 1000);
        Assert.True(ImageComparisonGeometry.CanCompare([first, second]));
        foreach (double aspect in new[] { 0.7, 1.0, 2.0, 3.0 })
        {
            var frame = ImageComparisonGeometry.FramePair(first, second, aspect);
            Assert.True(frame.Center.DistanceTo(insetCenter) < 1e-8);
            var camera = new SkyCamera { Center = frame.Center, FieldOfView = frame.FieldOfView };
            foreach (var point in first.CaptureSampleGrid().Concat(second.CaptureSampleGrid()))
            {
                var projected = camera.Project(point.Vector, 1000 * aspect, 1000);
                Assert.NotNull(projected);
                Assert.InRange(projected.Value.X, 0, 1000 * aspect);
                Assert.InRange(projected.Value.Y, 0, 1000);
            }
        }
    }

    [Fact]
    public void NearbyCentresWithoutSharedSkyAndMixedSelectionsCannotCompare()
    {
        var first = ImagePlacement.FromManual(new(0, 0, 6, 0), 6000, 600);
        var above = ImagePlacement.FromManual(new(0, 2, 6, 0), 6000, 600);
        var duplicateField = ImagePlacement.FromManual(new(0, 0, 6, 0), 6000, 600);
        Assert.False(ImageComparisonGeometry.CanCompare([]));
        Assert.False(ImageComparisonGeometry.CanCompare([first]));
        Assert.False(ImageComparisonGeometry.CanCompare([first, above]));
        Assert.False(ImageComparisonGeometry.CanCompare([first, duplicateField, above]));
        Assert.True(ImageComparisonGeometry.CanCompare([first, duplicateField]));
    }

    [Fact]
    public void CrossingFieldsStartWithTheDividerInTheirSharedArea()
    {
        var first = ImagePlacement.FromManual(new(0, 0, 6, 0), 6000, 600);
        var second = ImagePlacement.FromManual(new(2, 2, 0.6, 0), 600, 6000);
        Assert.True(ImageComparisonGeometry.CanCompare([first, second]));
        var center = new SkyFootprint(first).OverlapCenter(new(second));
        Assert.NotNull(center);
        Assert.InRange(center.Value.RightAscension, 1.7, 2.3);
        Assert.InRange(center.Value.Declination, -0.3, 0.3);
        Assert.Equal(center.Value, ImageComparisonGeometry.FramePair(first, second, 2).Center);
    }
}
