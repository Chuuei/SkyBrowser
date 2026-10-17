using System.IO;
using System.Text.Json;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class DistortedAstrometryTests
{
    [Fact]
    public void ForwardCoordinatesMatchIndependentAstropyWcslibReferences()
    {
        using var references = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "astrometry-reference.json")));
        foreach (var reference in references.RootElement.GetProperty("cases").EnumerateArray())
        {
            var image = XisfImageData.FromSamples(new float[128 * 96], [128, 96], 1);
            foreach (var card in reference.GetProperty("header").EnumerateObject())
            {
                image.FitsKeywords.Add(new(card.Name, card.Value.ToString(), null));
            }
            var placement = ImagePlacement.TryFromMetadata(image, null, out var warning);
            Assert.True(placement is not null, $"{reference.GetProperty("name")}: {warning}");
            foreach (var sample in reference.GetProperty("samples").EnumerateArray())
            {
                var actual = placement.At((sample.GetProperty("x").GetDouble() + 0.5) / 128,
                    (sample.GetProperty("y").GetDouble() + 0.5) / 96);
                var expected = new SkyCoordinate(sample.GetProperty("ra").GetDouble(), sample.GetProperty("dec").GetDouble());
                Assert.True(actual.DistanceTo(expected) < 2e-9,
                    $"{reference.GetProperty("name")}: {actual.DistanceTo(expected):G17} degrees from WCSLIB");
            }
        }
    }

    [Theory]
    [InlineData("A_ORDER", "10")]
    [InlineData("A_ORDER", "2.5")]
    [InlineData("A_2_0", "NaN")]
    [InlineData("A_2147483647_1", "1")]
    [InlineData("A_1_0", "0.5")]
    [InlineData("PV1_1", "1")]
    [InlineData("CPDIS1", "LOOKUP")]
    public void InvalidOrMixedDistortionNeverFallsBackToAnUndistortedFootprint(string key, string value)
    {
        var source = SipImage();
        source.FitsKeywords.Add(new(key, value, null));
        Assert.Null(ImagePlacement.TryFromMetadata(source, null, out var warning));
        Assert.Contains("invalid or unsupported", warning);
    }

    [Fact]
    public void UnregisteredSwappedTpvAxesAreRejectedInsteadOfGuessed()
    {
        var source = TestImages.Create();
        source.FitsKeywords.Add(new("CTYPE1", "DEC--TPV", null));
        source.FitsKeywords.Add(new("CTYPE2", "RA---TPV", null));
        source.FitsKeywords.Add(new("CRVAL1", "-5.391", null));
        source.FitsKeywords.Add(new("CRVAL2", "83.822", null));
        Assert.Null(ImagePlacement.TryFromMetadata(source, null, out var warning));
        Assert.Contains("RA on the first", warning);
    }

    [Fact]
    public void OmittedTpvCoefficientsUseRegisteredIdentityDefaults()
    {
        var source = TestImages.Create();
        var expected = ImagePlacement.TryFromMetadata(source, null, out _)!;
        source.FitsKeywords.Add(new("CTYPE1", "RA---TPV", null));
        source.FitsKeywords.Add(new("CTYPE2", "DEC--TPV", null));
        var actual = ImagePlacement.TryFromMetadata(source, null, out _)!;
        Assert.True(actual.At(0.17, 0.84).DistanceTo(expected.At(0.17, 0.84)) < 1e-12);
    }

    [Fact]
    public void WarmCacheKeepsExactPolynomialCoordinatesBetweenMeshVertices()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, "distorted.xisf");
            XisfWriter.Write(file, [SipImage()]);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var cold = new CachedSkyImageLoader(cache).Load(file, DisplayMode.Linear, SkyFinishSettings.Default)[0];
            var warm = new CachedSkyImageLoader(cache, (_, _, _, _, _, _) => throw new InvalidOperationException("Unexpected source decode"))
                .Load(file, DisplayMode.Linear, SkyFinishSettings.Default)[0];
            Assert.True(warm.IsFromCache);
            Assert.Contains("SIP", warm.Placement!.Description);
            Assert.True(cold.Placement!.At(0.237, 0.817).DistanceTo(warm.Placement.At(0.237, 0.817)) < 1e-12);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LegacyLinearPropertiesRespectNativePixelCentersAndPrecedence()
    {
        var source = TestImages.Create();
        const string prefix = "PCL:AstrometricSolution:";
        source.Properties[prefix + "ProjectionSystem"] = "Gnomonic";
        source.Properties[prefix + "ReferenceCelestialCoordinates"] = new double[] { 359.99, 80 };
        source.Properties[prefix + "ReferenceImageCoordinates"] = new double[] { 32, 24 };
        source.Properties[prefix + "LinearTransformationMatrix"] = new double[,] { { -0.01, 0.002 }, { 0.003, 0.01 } };
        var legacy = ImagePlacement.TryFromMetadata(source, null, out var warning);
        Assert.Null(warning);
        Assert.Contains("legacy", legacy!.Description);
        Assert.True(legacy.Center.DistanceTo(new(359.99, 80)) < 1e-10);
        var native = XisfAstrometricSolution.CreateLinear("Gnomonic", [120, 10], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } });
        Assert.True(ImagePlacement.TryFromMetadata(source, native, out _)!.Center.DistanceTo(new(120, 10)) < 1e-10);
        source.Properties[prefix + "SplineWorldTransformation:Version"] = "2.0";
        Assert.Null(ImagePlacement.TryFromMetadata(source, null, out warning));
        Assert.Contains("linear approximation is not used", warning);
    }

    [Theory]
    [InlineData(".xisf")]
    [InlineData(".xish")]
    public void SerializedLegacyLinearFilesImportWithTheirNativeGeometry(string extension)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = TestImages.Create();
            const string prefix = "PCL:AstrometricSolution:";
            source.Properties[prefix + "ProjectionSystem"] = "Gnomonic";
            source.Properties[prefix + "ReferenceCelestialCoordinates"] = new double[] { 10, 60 };
            source.Properties[prefix + "ReferenceImageCoordinates"] = new double[] { 32, 24 };
            source.Properties[prefix + "LinearTransformationMatrix"] = new double[,] { { -0.01, 0 }, { 0, -0.01 } };
            string file = Path.Combine(directory, "legacy" + extension);
            if (extension == ".xish")
            {
                XisfWriter.WriteDistributed(file, Path.Combine(directory, "pixels.fits"), [source]);
            }
            else
            {
                XisfWriter.Write(file, [source]);
            }
            byte[] original = File.ReadAllBytes(file);
            var loaded = SkyImageLoader.Load(file, DisplayMode.Linear)[0];
            Assert.Contains("legacy", loaded.Placement!.Description);
            Assert.True(loaded.Placement.Center.DistanceTo(new(10, 60)) < 1e-10);
            Assert.True(loaded.Placement.At(0.5, 0).Declination > 60);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static XisfImageData SipImage()
    {
        var image = TestImages.Create();
        image.FitsKeywords.Add(new("CTYPE1", "RA---TAN-SIP", null));
        image.FitsKeywords.Add(new("CTYPE2", "DEC--TAN-SIP", null));
        image.FitsKeywords.Add(new("A_ORDER", "3", null));
        image.FitsKeywords.Add(new("B_ORDER", "3", null));
        image.FitsKeywords.Add(new("A_2_0", "0.003", null));
        image.FitsKeywords.Add(new("B_0_3", "0.00002", null));
        return image;
    }
}
