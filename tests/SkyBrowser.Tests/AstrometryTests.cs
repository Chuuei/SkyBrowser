using SkyBrowser.Core.Astronomy;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class AstrometryTests
{
    [Fact]
    public void SkyRecordsHaveFiniteReadableTextRepresentations()
    {
        Assert.Equal("(3, 4, 0)", new SkyVector(3, 4, 0).ToString());
        Assert.Equal("(0.6, 0.8, 0)", new SkyVector(3, 4, 0).Normalized.ToString());
        var coordinate = new SkyCoordinate(10.6847, 41.2688);
        Assert.Equal($"{coordinate.RaText} {coordinate.DecText}", coordinate.ToString());
        Assert.Equal("M31", new SkyLabel("M31", coordinate).ToString());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(359.99, 40)]
    [InlineData(3, 89.99)]
    [InlineData(125, -89.9)]
    public void PerspectiveCameraRoundTripsAcrossSeamAndPoles(double ra, double dec)
    {
        var camera = new SkyCamera
        {
            Center = new(ra, dec),
            FieldOfView = 45
        };
        var point = camera.Unproject(390, 270, 1200, 800);
        var screen = camera.Project(point.Vector, 1200, 800)!.Value;
        Assert.Equal(390, screen.X, 7);
        Assert.Equal(270, screen.Y, 7);
        Assert.Null(camera.Project(camera.Forward * -1, 1200, 800));
    }

    [Fact]
    public void LookingNorthPlacesEastOnTheLeft()
    {
        var camera = new SkyCamera
        {
            Center = new(0, 0)
        };
        Assert.True(camera.Project(new SkyCoordinate(1, 0).Vector, 1000, 600)!.Value.X < 500);
        Assert.True(camera.Project(new SkyCoordinate(0, 1).Vector, 1000, 600)!.Value.Y < 300);
    }

    [Fact]
    public void CachedCameraBasisTracksPositionZoomAndObserverChanges()
    {
        var camera = new SkyCamera();
        var direction = new SkyCoordinate(30, 40).Vector;

        void Compare()
        {
            var fresh = new SkyCamera
            {
                Center = camera.Center,
                FieldOfView = camera.FieldOfView,
                Observer = camera.Observer
            };
            Assert.Equal(fresh.Project(direction, 1100, 750), camera.Project(direction, 1100, 750));
            Assert.Equal(fresh.Unproject(420, 310, 1100, 750), camera.Unproject(420, 310, 1100, 750));
        }

        Compare();
        camera.Center = new(359.9, 80);
        Compare();
        camera.FieldOfView = 4;
        Compare();
        camera.Observer = new(new(52, 13), new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero));
        Compare();
        camera.Observer = new(new(-34, 151), new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero));
        Compare();
        camera.Observer = null;
        camera.Pan(12, -8, 1100);
        Compare();
    }

    [Fact]
    public void ReusedAstrometryGridsRetainDefensiveArrayOwnership()
    {
        var native = XisfAstrometricSolution.CreateLinear("Gnomonic", [83.822, -5.391], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } });
        var placement = ImagePlacement.FromNative(native, 64, 48);
        var first = placement.CaptureSampleGrid();
        var expected = first[0];
        first[0] = new(0, 0);
        Assert.Equal(expected, placement.CaptureSampleGrid()[0]);
        Assert.Equal(expected, placement.At(0, 0));
        var restored = ImagePlacement.FromSampleGrid("Saved", placement.CaptureSampleGrid());
        var copy = restored.CaptureSampleGrid();
        copy[0] = new(1, 2);
        Assert.Equal(expected, restored.CaptureSampleGrid()[0]);
        Parallel.For(0, 12, _ => Assert.Equal(expected, placement.CaptureSampleGrid()[0]));
    }

    [Fact]
    public void FitsAndNativeAdaptersRespectTheirDifferentPixelOrigins()
    {
        var image = TestImages.Create();
        Assert.True(CelestialWorldCoordinateSystem.TryCreate(image.FitsKeywords, out var wcs));
        var fits = ImagePlacement.FromFits(wcs!, 64, 48);
        Assert.True(fits.Center.DistanceTo(new(83.822, -5.391)) < 1e-10);
        // FITS x = 0 is the first pixel center; its normalized texture coordinate is .5/width.
        var expected = wcs!.PixelToSky(0, 0);
        Assert.True(fits.At(0.5 / 64, 0.5 / 48).DistanceTo(new(expected.RightAscension, expected.Declination)) < 1e-10);
        var native = XisfAstrometricSolution.CreateLinear("Gnomonic", [83.822, -5.391], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } });
        var placement = ImagePlacement.FromNative(native, 64, 48);
        Assert.True(placement.Center.DistanceTo(new(83.822, -5.391)) < 1e-10);
        var first = native.ImageToCelestial(0.5, 0.5);
        Assert.True(placement.At(0.5 / 64, 0.5 / 48).DistanceTo(new(first.RightAscension, first.Declination)) < 1e-10);
    }

    [Theory]
    [InlineData("RA---TAN-SIP", "DEC--TAN-SIP")]
    [InlineData("RA---MOL", "DEC--MOL")]
    [InlineData("GLON-TAN", "GLAT-TAN")]
    public void UnsupportedWcsIsNeverSilentlyTreatedAsTan(string first, string second)
    {
        var image = TestImages.Create();
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE1", first));
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE2", second));
        Assert.Null(ImagePlacement.TryFromMetadata(image, null, out var warning));
        Assert.Contains("unsupported", warning);
    }

    [Theory]
    [InlineData("EQUINOX", "1950")]
    [InlineData("RADESYS", "'FK4'")]
    [InlineData("CPDIS1", "'LOOKUP'")]
    [InlineData("CUNIT1", "'rad'")]
    [InlineData("LONPOLE", "0")]
    [InlineData("RADECSYS", "'FK4'")]
    public void UnsupportedReferenceFrameOrDistortionRequiresManualPlacement(string key, string value)
    {
        var image = TestImages.Create();
        image.FitsKeywords.Add(new(key, value, null));
        Assert.Null(ImagePlacement.TryFromMetadata(image, null, out _));
    }

    [Fact]
    public void AcquisitionCenterAloneIsNotAnAstrometricSolution()
    {
        var image = TestImages.Create();
        image.FitsKeywords.Clear();
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("RA", 83.822));
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("DEC", -5.391));
        Assert.Null(ImagePlacement.TryFromMetadata(image, null, out var warning));
        Assert.Contains("No usable", warning);
    }

    [Fact]
    public void MissingOffDiagonalCdTermsHaveTheFitsZeroDefault()
    {
        var image = TestImages.Create();
        foreach (var key in image.FitsKeywords.Where(k => k.Name is "CD1_2" or "CD2_1").ToArray())
        {
            image.FitsKeywords.Remove(key);
        }

        var placement = ImagePlacement.TryFromMetadata(image, null, out _);
        Assert.NotNull(placement);
        Assert.True(placement.Center.DistanceTo(new(83.822, -5.391)) < 1e-10);
    }

    [Fact]
    public void ManualPlacementHonorsWidthAspectMirrorAndRotation()
    {
        var normal = ImagePlacement.FromManual(new(359.99, 65, 4, 0), 2000, 1000);
        Assert.Equal(4, normal.AngularWidth, 8);
        Assert.True(normal.At(0.5, 0).Declination > normal.Center.Declination);
        var camera = new SkyCamera
        {
            Center = normal.Center,
            FieldOfView = 10
        };
        Assert.True(camera.Project(normal.At(0, 0.5).Vector, 1000, 600)!.Value.X < 500);
        var mirrored = ImagePlacement.FromManual(new(359.99, 65, 4, 0, true), 2000, 1000);
        Assert.True(camera.Project(mirrored.At(0, 0.5).Vector, 1000, 600)!.Value.X > 500);
        var rotated = ImagePlacement.FromManual(new(359.99, 65, 4, 90), 2000, 1000);
        Assert.True(camera.Project(rotated.At(0, 0.5).Vector, 1000, 600)!.Value.Y > 300);
    }

    [Theory]
    [InlineData(double.NaN, 0, 2)]
    [InlineData(0, 91, 2)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 180)]
    public void InvalidManualPlacementsAreRejected(double ra, double dec, double width) => Assert.Throws<ArgumentException>(() => ImagePlacement.FromManual(new(ra, dec, width, 0), 64, 48));

    [Fact]
    public void EmbeddedCatalogueCoversBothHemispheres()
    {
        var catalog = new SkyCatalog();
        Assert.True(catalog.Stars.Count > 5000);
        Assert.Contains(catalog.Stars, s => s.Position.Declination < -80);
        Assert.Contains(catalog.Stars, s => s.Position.Declination > 80);
        Assert.Contains(catalog.ConstellationNames, c => c.Name == "Orion");
        Assert.True(catalog.ConstellationLines.Count > 100);
    }
}
