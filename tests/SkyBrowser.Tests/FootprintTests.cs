using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class FootprintTests
{
    [Theory]
    [InlineData(359.9, 0)]
    [InlineData(45, 89.9)]
    [InlineData(90, -89.9)]
    public void NestedFootprintsOverlapAcrossTheRaSeamAndPoles(double ra, double dec)
    {
        var background = ImagePlacement.FromManual(new(ra, dec, 4, 37), 2000, 1000);
        var center = background.At(0.6, 0.4);
        var inset = ImagePlacement.FromManual(new(center.RightAscension, center.Declination, 0.4, -20, true), 1000, 1000);
        var big = new SkyFootprint(background);
        var small = new SkyFootprint(inset);
        Assert.True(big.AreaSteradians > small.AreaSteradians * 20);
        Assert.True(big.Overlaps(small));
        Assert.True(small.Overlaps(big));
    }

    [Fact]
    public void CrossingEdgesOverlapEvenWhenNeitherCenterIsInsideTheOtherImage()
    {
        var horizontal = new SkyFootprint(ImagePlacement.FromManual(new(0, 0, 6, 0), 6000, 600));
        var vertical = new SkyFootprint(ImagePlacement.FromManual(new(2, 2, 0.6, 0), 600, 6000));
        Assert.True(horizontal.Overlaps(vertical));
        Assert.True(vertical.Overlaps(horizontal));
    }

    [Fact]
    public void DisjointOrMerelyTouchingFootprintsDoNotOverlap()
    {
        var first = new SkyFootprint(ImagePlacement.FromManual(new(0, 0, 4, 0), 1000, 1000));
        var touching = new SkyFootprint(ImagePlacement.FromManual(new(4, 0, 4, 0), 1000, 1000));
        var antipodal = new SkyFootprint(ImagePlacement.FromManual(new(180, 0, 4, 0), 1000, 1000));
        Assert.False(first.Overlaps(touching));
        Assert.False(touching.Overlaps(first));
        Assert.False(first.Overlaps(antipodal));
        var wide = new SkyFootprint(ImagePlacement.FromManual(new(0, 0, 6, 0), 6000, 600));
        var above = new SkyFootprint(ImagePlacement.FromManual(new(0, 2, 6, 0), 6000, 600));
        Assert.False(wide.Overlaps(above));
    }

    [Fact]
    public void FootprintAreaDependsOnSkyCoverageInsteadOfPixelCountOrRotation()
    {
        var a = new SkyFootprint(ImagePlacement.FromManual(new(83, -2, 2, 0), 100, 50));
        var b = new SkyFootprint(ImagePlacement.FromManual(new(83, -2, 2, 72, true), 4000, 2000));
        Assert.Equal(a.AreaSteradians, b.AreaSteradians, 10);
        Assert.True(a.Overlaps(b));
    }
}
