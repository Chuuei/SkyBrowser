using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class AstapHintTests
{
    [Theory]
    [InlineData("RA", "DEC", "303.30834", "38.425")]
    [InlineData("ra", "dec", "3.0330834D2", "38.425")]
    [InlineData("RA", "DEC", "'20:13:14.0016'", "'+38:25:30'")]
    [InlineData("OBJCTRA", "OBJCTDEC", "'20 13 14.0016'", "'38 25 30'")]
    [InlineData("OBJCTRA", "OBJCTDEC", "20.220556", "38.425")]
    public void PositionHintsRespectNumericAndSexagesimalUnits(string raKey, string decKey, string ra, string dec)
    {
        var source = Image();
        source.FitsKeywords.Add(new(raKey, ra, null));
        source.FitsKeywords.Add(new(decKey, dec, null));
        var hint = AstapPlateSolver.ReadPositionHint(source);
        Assert.NotNull(hint);
        Assert.Equal(303.30834, hint.Value.RightAscension, 8);
        Assert.Equal(38.425, hint.Value.Declination, 8);
    }

    [Theory]
    [InlineData("'00:00:00'", "'-00:30:00'", 0, -.5)]
    [InlineData("'24:00:00'", "'+90:00:00'", 0, 90)]
    [InlineData("360", "-90", 0, -90)]
    [InlineData("12", "0", 12, 0)]
    public void BoundariesAndNegativeZeroDeclinationAreUnambiguous(string ra, string dec, double expectedRa, double expectedDec)
    {
        var source = Image();
        source.FitsKeywords.Add(new("RA", ra, null));
        source.FitsKeywords.Add(new("DEC", dec, null));
        Assert.Equal(new SkyCoordinate(expectedRa, expectedDec), AstapPlateSolver.ReadPositionHint(source));
    }

    [Theory]
    [InlineData("NaN", "38")]
    [InlineData("Infinity", "38")]
    [InlineData("361", "38")]
    [InlineData("-1", "38")]
    [InlineData("303", "91")]
    [InlineData("303", "NaN")]
    [InlineData("'20:60:00'", "38")]
    [InlineData("'20:13:60'", "38")]
    [InlineData("'20::13:14'", "38")]
    [InlineData("'24:00:01'", "38")]
    [InlineData("'-00:00:01'", "38")]
    [InlineData("303", "'-90:00:01'")]
    [InlineData("'20,5'", "38")]
    public void MalformedOrOutOfRangeMetadataFallsBackToBlindSolving(string ra, string dec)
    {
        var source = Image();
        source.FitsKeywords.Add(new("RA", ra, null));
        source.FitsKeywords.Add(new("DEC", dec, null));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
    }

    [Fact]
    public void IncompleteDuplicateAndNonEquatorialMetadataAreNotCombinedOrGuessed()
    {
        var source = Image();
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
        source.FitsKeywords.Add(new("RA", "303", null));
        source.FitsKeywords.Add(new("OBJCTDEC", "38", null));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
        source.FitsKeywords.Add(new("DEC", "38", null));
        source.FitsKeywords.Add(new("RA", "304", null));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
        source.FitsKeywords.Clear();
        source.FitsKeywords.Add(new("CRVAL1", "303", null));
        source.FitsKeywords.Add(new("CRVAL2", "38", null));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE1", "GLON-TAN"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE2", "GLAT-TAN"));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquatorialWcsReferenceHintsRespectSwappedAxesAndDegreeUnits(bool swapped)
    {
        var source = Image();
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE1", swapped ? "DEC--TAN" : "RA---TAN"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE2", swapped ? "RA---TAN" : "DEC--TAN"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CRVAL1", swapped ? 38.425 : 303.30834));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CRVAL2", swapped ? 303.30834 : 38.425));
        Assert.Equal(new SkyCoordinate(303.30834, 38.425), AstapPlateSolver.ReadPositionHint(source));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("CUNIT1", "rad"));
        Assert.Null(AstapPlateSolver.ReadPositionHint(source));
    }

    [Fact]
    public void NativeXisfCentersTakePrecedenceAndInvalidNativeCentersAllowFitsFallback()
    {
        var source = Image();
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("RA", 303.30834));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("DEC", 38.425));
        source.Properties.Set(XisfKnownProperties.Observation.Center.RA, 10d);
        source.Properties.Set(XisfKnownProperties.Observation.Center.Dec, -20d);
        Assert.Equal(new SkyCoordinate(10, -20), AstapPlateSolver.ReadPositionHint(source));
        source.Properties.Set(XisfKnownProperties.Observation.Center.Dec, double.NaN);
        Assert.Equal(new SkyCoordinate(303.30834, 38.425), AstapPlateSolver.ReadPositionHint(source));
    }

    [Fact]
    public void CommandLineConvertsDegreesToHoursAndSouthPoleDistanceWithoutRestrictingSearch()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var start = AstapPlateSolver.CreateStartInfo(AstapTests.FakeOptions(), "C:\\Temp\\input.fits", "C:\\Temp\\solution", new(303.30834, 38.425));
            var args = start.ArgumentList.ToArray();
            string Value(string option) => args[Array.IndexOf(args, option) + 1];
            Assert.Equal(20.220556, double.Parse(Value("-ra"), CultureInfo.InvariantCulture), 10);
            Assert.Equal(128.425, double.Parse(Value("-spd"), CultureInfo.InvariantCulture), 10);
            Assert.Equal("180", Value("-r"));
            Assert.Equal("0", Value("-fov"));
            Assert.DoesNotContain("-update", args);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NearbySearchUsesMetadataScaleUnlessUserSpecifiesAFieldHeight()
    {
        var options = AstapTests.FakeOptions();
        string Value(System.Diagnostics.ProcessStartInfo start, string option)
        {
            var args = start.ArgumentList.ToArray();
            return args[Array.IndexOf(args, option) + 1];
        }
        var nearby = AstapPlateSolver.CreateStartInfo(options, "C:\\Temp\\input.fits", "C:\\Temp\\nearby", new(303, 38), true, 1.08);
        Assert.Equal("5", Value(nearby, "-r"));
        Assert.Equal("1000", Value(nearby, "-s"));
        Assert.Equal(1.08, double.Parse(Value(nearby, "-fov"), CultureInfo.InvariantCulture));
        var userScale = AstapPlateSolver.CreateStartInfo(options with { AstapFieldHeightDegrees = 2 },
            "C:\\Temp\\input.fits", "C:\\Temp\\nearby", new(303, 38), true, 1.08);
        Assert.Equal("2", Value(userScale, "-fov"));
        var blind = AstapPlateSolver.CreateStartInfo(options, "C:\\Temp\\input.fits", "C:\\Temp\\solution", fieldHeightHint: 1.08);
        Assert.Equal("0", Value(blind, "-fov"));
        Assert.Equal("180", Value(blind, "-r"));
        Assert.Equal("500", Value(blind, "-s"));
        Assert.Throws<ArgumentException>(() => AstapPlateSolver.CreateStartInfo(options, "input", "output", nearbySearch: true));
    }

    [Fact]
    public void FieldHintsUseStoredHeightAndCorrectFitsAndXisfUnits()
    {
        var source = XisfImageData.FromSamples(new float[918 * 1632], [918, 1632], 1);
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("YPIXSZ", 2.9));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("FOCALLEN", 250));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("YBINNING", 2));
        double expected = 2 * Math.Atan(1632 * 2.9 / 1000 / 500) * 180 / Math.PI;
        Assert.Equal(expected, AstapPlateSolver.ReadFieldHeightHint(source)!.Value, 10);
        source.FitsKeywords.Clear();
        source.Properties.Set(XisfKnownProperties.Instrument.Sensor.YPixelSize, 2.9f);
        source.Properties.Set(XisfKnownProperties.Instrument.Telescope.FocalLength, .25f);
        Assert.InRange(Math.Abs(expected - AstapPlateSolver.ReadFieldHeightHint(source)!.Value), 0, 1e-6);
        source.Properties.Set(XisfKnownProperties.Instrument.Camera.YBinning, 2);
        double binned = 2 * Math.Atan(1632 * 5.8 / 1000 / 500) * 180 / Math.PI;
        Assert.InRange(Math.Abs(binned - AstapPlateSolver.ReadFieldHeightHint(source)!.Value), 0, 1e-6);
        source.Properties.Set(XisfKnownProperties.Instrument.Camera.YBinning, 0);
        Assert.Null(AstapPlateSolver.ReadFieldHeightHint(source));
    }

    [Theory]
    [InlineData("NaN", "250")]
    [InlineData("-2.9", "250")]
    [InlineData("2.9", "0")]
    [InlineData("2.9", "Infinity")]
    public void InvalidInstrumentMetadataDoesNotCreateScaleHints(string pixel, string focal)
    {
        var source = Image();
        source.FitsKeywords.Add(new("YPIXSZ", pixel, null));
        source.FitsKeywords.Add(new("FOCALLEN", focal, null));
        Assert.Null(AstapPlateSolver.ReadFieldHeightHint(source));
    }

    [Theory]
    [InlineData(.5f)]
    [InlineData(.6f)]
    public async Task FailedOrSlowMetadataAttemptFallsBackWithoutItsPositionOrScale(float sample)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = XisfImageData.FromSamples(Enumerable.Repeat(sample, 64 * 48).ToArray(), [64, 48], 1);
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("RA", 303.30834));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("DEC", 38.425));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("YPIXSZ", 2.9));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("FOCALLEN", 250));
            string file = Path.Combine(directory, "stale-metadata.xisf");
            XisfWriter.Write(file, [source]);
            var image = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            var progress = new Stages();
            var result = await new AstapPlateSolver(new ImageLibrary(Path.Combine(directory, "library.sqlite3")))
                .SolveAsync(image, AstapTests.FakeOptions(), progress: progress).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.InRange(result.Placement.Center.DistanceTo(new(83.822, -5.391)), 0, 1e-8);
            Assert.Contains(progress.Values, stage => stage.Contains("trying metadata hints", StringComparison.Ordinal));
            Assert.Contains(progress.Values, stage => stage.Contains("searching the whole sky", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataAttemptsRespectCancellationAndOneOverallTimeLimit(bool cancel)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = XisfImageData.FromSamples(Enumerable.Repeat(.3f, 64 * 48).ToArray(), [64, 48], 1);
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("RA", 303.30834));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("DEC", 38.425));
            string file = Path.Combine(directory, "timeout.xisf");
            XisfWriter.Write(file, [source]);
            var image = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            using var cancellation = new CancellationTokenSource();
            if (cancel)
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(1));
            }
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var solve = new AstapPlateSolver(library).SolveAsync(image, AstapTests.FakeOptions(), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(15));
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solve);
            }
            else
            {
                var error = await Assert.ThrowsAsync<TimeoutException>(() => solve);
                Assert.Contains("10-second", error.Message);
            }
            Assert.Empty(library.Search());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Stages : IProgress<ImageProcessingProgress>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Values { get; } = new();
        public void Report(ImageProcessingProgress update) => Values.Enqueue(update.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceMetadataReachesSolverWithoutEnteringTemporaryFitsOrChangingOriginal(bool fits)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = Image();
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("RA", 303.30834));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("DEC", 38.425));
            string file = Path.Combine(directory, fits ? "source.fits" : "source.xisf");
            if (fits)
            {
                FitsWriter.Write(file, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
            }
            else
            {
                XisfWriter.Write(file, [source]);
            }
            byte[] before = SHA256.HashData(File.ReadAllBytes(file));
            var image = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            Assert.Null(image.Placement);
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var result = await new AstapPlateSolver(library).SolveAsync(image, AstapTests.FakeOptions());
            Assert.NotNull(result.Placement);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(file)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static XisfImageData Image() => XisfImageData.FromSamples(Enumerable.Repeat(.4f, 64 * 48).ToArray(), [64, 48], 1);
}
