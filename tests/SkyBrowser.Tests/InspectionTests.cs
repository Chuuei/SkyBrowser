using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class InspectionTests
{
    [Theory]
    [InlineData(DisplayMode.Linear)]
    [InlineData(DisplayMode.Automatic)]
    [InlineData(DisplayMode.Finished)]
    public void FullResolutionRetainsSmallDetailsBeyondPreviewLimit(DisplayMode mode)
    {
        InDirectory(directory =>
        {
            const int width = 2052, height = 4;
            float[] samples = Enumerable.Repeat(0.002f, width * height).ToArray();
            samples[2050] = 0.75f;
            string file = Path.Combine(directory, "full.xisf");
            XisfWriter.Write(file, [XisfImageData.FromSamples(samples, [width, height], 1)]);
            byte[] original = SHA256.HashData(File.ReadAllBytes(file));
            var selected = Assert.Single(SkyImageLoader.Load(file, mode));
            Assert.Equal(2048, selected.Preview.Width);
            var inspection = ScientificImageInspection.Load(selected);
            Assert.Equal(width, inspection.Image.Preview.Width);
            Assert.Equal(height, inspection.Image.Preview.Height);
            Assert.Equal("0.75", inspection.ReadSample(2050, 0, 0));
            if (mode == DisplayMode.Linear)
            {
                Assert.Equal(191, inspection.Image.Preview.Bgra[2050 * 4 + 2]);
                Assert.Equal(1, inspection.Image.Preview.Bgra[2049 * 4 + 2]);
            }
            Assert.Equal(width * height, inspection.Statistics[0].Histogram.Sum());
            Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(file)));
            Assert.Throws<ArgumentOutOfRangeException>(() => inspection.ReadSample(width, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => inspection.SkyAtPixel(0, -1));
        });
    }

    [Theory]
    [InlineData(false, XisfPixelLayout.Planar)]
    [InlineData(true, XisfPixelLayout.Planar)]
    [InlineData(false, XisfPixelLayout.Interleaved)]
    [InlineData(true, XisfPixelLayout.Interleaved)]
    public void OriginalIntegerSamplesRespectByteOrderLayoutAndAlpha(bool big, XisfPixelLayout layout)
    {
        InDirectory(directory =>
        {
            ushort[] planar = [0, 2, 4, 6, 10, 12, 14, 16, 20, 22, 24, 26, 0, 65535, 0, 65535];
            ushort[] samples = layout == XisfPixelLayout.Planar ? planar
                : Enumerable.Range(0, 16).Select(i => planar[(i % 4) * 4 + i / 4]).ToArray();
            var source = XisfImageData.FromSamples(samples, [2, 2], 4, layout, big);
            source.Orientation = "90;flip";
            var inspection = SaveAndInspect(directory, source);
            Assert.Equal("6", inspection.ReadSample(1, 1, 0));
            Assert.Equal("16", inspection.ReadSample(1, 1, 1));
            Assert.Equal("26", inspection.ReadSample(1, 1, 2));
            Assert.Equal("65535", inspection.ReadSample(1, 1, 3));
            Assert.Equal("Alpha", inspection.Statistics[3].Channel);
            Assert.Equal(3, inspection.Statistics[0].Mean, 12);
            Assert.Equal(Math.Sqrt(5), inspection.Statistics[0].StandardDeviation, 12);
            Assert.All(inspection.Statistics, s => Assert.Equal(4, s.FiniteCount));
        });
    }

    [Fact]
    public void FitsPhysicalReadoutsApplyScalingAndBlankBeforeStatistics()
    {
        InDirectory(directory =>
        {
            byte[] raw = new byte[8];
            short[] samples = [-2, 0, 2, short.MinValue];
            for (int i = 0; i < samples.Length; i++)
            {
                BinaryPrimitives.WriteInt16BigEndian(raw.AsSpan(i * 2, 2), samples[i]);
            }
            var header = new FitsHeader([
                new("SIMPLE", true), new("BITPIX", 16), new("NAXIS", 2), new("NAXIS1", 2), new("NAXIS2", 2),
                new("BSCALE", 2), new("BZERO", 10), new("BLANK", (long)short.MinValue)
            ]);
            string file = Path.Combine(directory, "scaled.fits");
            FitsWriter.Write(file, new FitsDocument([new FitsHdu(FitsHduType.Primary, header, raw)]));
            var selected = SkyImageLoader.Load(file, DisplayMode.Linear)[0];
            var inspection = ScientificImageInspection.Load(selected);
            Assert.Equal("6", inspection.ReadSample(0, 0, 0));
            Assert.Equal("14", inspection.ReadSample(0, 1, 0));
            Assert.Equal("NaN", inspection.ReadSample(1, 1, 0));
            var stats = Assert.Single(inspection.Statistics);
            Assert.Equal(3, stats.FiniteCount);
            Assert.Equal(1, stats.NonFiniteCount);
            Assert.Equal(10, stats.Mean, 12);
            Assert.Equal(Math.Sqrt(32d / 3), stats.StandardDeviation, 12);
        });
    }

    [Fact]
    public void WideIntegerReadoutsPreserveEveryBit()
    {
        InDirectory(directory =>
        {
            var source = XisfImageData.FromSamples<ulong>([9007199254740993, ulong.MaxValue], [2, 1], 1, isBigEndian: true);
            var inspection = SaveAndInspect(directory, source);
            Assert.Equal("9007199254740993", inspection.ReadSample(0, 0, 0));
            Assert.Equal("18446744073709551615", inspection.ReadSample(1, 0, 0));
        });
    }

    [Fact]
    public void HistogramsExcludeInvalidSamplesAndHandleConstantEmptyAndExtremeRanges()
    {
        InDirectory(directory =>
        {
            var inspection = SaveAndInspect(directory, XisfImageData.FromSamples<double>([0, 2, double.NaN, double.PositiveInfinity, double.NegativeInfinity], [5, 1], 1));
            var stats = Assert.Single(inspection.Statistics);
            Assert.Equal(2, stats.FiniteCount);
            Assert.Equal(3, stats.NonFiniteCount);
            Assert.Equal(1, stats.Mean);
            Assert.Equal(1, stats.StandardDeviation);
            Assert.Equal(1, stats.Histogram[0]);
            Assert.Equal(1, stats.Histogram[255]);
            Assert.Equal("NaN", inspection.ReadSample(2, 0, 0));
            stats = Assert.Single(SaveAndInspect(directory, XisfImageData.FromSamples<double>([7, 7], [2, 1], 1)).Statistics);
            Assert.Equal(0, stats.StandardDeviation);
            Assert.Equal(2, stats.Histogram[0]);
            stats = Assert.Single(SaveAndInspect(directory, XisfImageData.FromSamples<double>([double.NaN, double.PositiveInfinity], [2, 1], 1)).Statistics);
            Assert.Equal(0, stats.FiniteCount);
            Assert.True(double.IsNaN(stats.Minimum));
            Assert.Equal(0, stats.Histogram.Sum());
            stats = Assert.Single(SaveAndInspect(directory, XisfImageData.FromSamples<double>([-double.MaxValue, double.MaxValue], [2, 1], 1)).Statistics);
            Assert.Equal(0, stats.Mean);
            Assert.Equal(double.MaxValue, stats.StandardDeviation);
            Assert.Equal(2, stats.Histogram.Sum());
        });
    }

    [Theory]
    [InlineData(".fits")]
    [InlineData(".xisf")]
    [InlineData(".xish")]
    public void PixelCentersMatchStoredAstrometryAndManualOverrides(string extension)
    {
        InDirectory(directory =>
        {
            string file = Path.Combine(directory, "multi" + extension);
            var source = TestImages.Create();
            if (extension == ".fits")
            {
                FitsWriter.Write(file, FitsConverter.FromXisf([source, source], ConversionLossPolicy.Allow));
            }
            else if (extension == ".xish")
            {
                XisfWriter.WriteDistributed(file, Path.Combine(directory, "pixels.fits"), [source, source]);
            }
            else
            {
                XisfAstrometricSolution.CreateLinear("Gnomonic", [359.99, 80], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } }).ApplyTo(source);
                XisfWriter.Write(file, [source, source]);
            }
            var selected = SkyImageLoader.Load(file, DisplayMode.Linear)[1];
            var inspection = ScientificImageInspection.Load(selected);
            Assert.Equal(1, inspection.Image.ImageIndex);
            Assert.True(inspection.SkyAtPixel(0, 0)!.Value.DistanceTo(selected.Placement!.At(0.5 / 64, 0.5 / 48)) < 1e-10);
            var manual = new ManualPlacement(0.01, 89, 2, 30, true);
            inspection = ScientificImageInspection.Load(selected, manual);
            Assert.True(inspection.SkyAtPixel(63, 47)!.Value.DistanceTo(ImagePlacement.FromManual(manual, 64, 48).At(63.5 / 64, 47.5 / 48)) < 1e-10);
            Assert.Throws<OperationCanceledException>(() => ScientificImageInspection.Load(selected, token: new(true)));
        });
    }

    [Fact]
    public void ChangedSidecarsAndCancellationCannotPublishInspectionData()
    {
        InDirectory(directory =>
        {
            string file = Path.Combine(directory, "source.xish"), sidecar = Path.Combine(directory, "data.fits");
            XisfWriter.WriteDistributed(file, sidecar, [TestImages.Create()]);
            var selected = SkyImageLoader.Load(file, DisplayMode.Linear)[0];
            var changed = new CallbackProgress(update =>
            {
                if (update.Stage.StartsWith("Measuring", StringComparison.Ordinal))
                {
                    using var stream = File.Open(sidecar, FileMode.Append);
                    stream.WriteByte(0);
                }
            });
            Assert.Throws<IOException>(() => ScientificImageInspection.Load(selected, progress: changed));
            using var cancellation = new CancellationTokenSource();
            var cancel = new CallbackProgress(update =>
            {
                if (update.Stage.StartsWith("Measuring", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            });
            Assert.Throws<OperationCanceledException>(() => ScientificImageInspection.Load(selected, token: cancellation.Token, progress: cancel));
        });
    }

    private sealed class CallbackProgress(Action<ImageProcessingProgress> report) : IProgress<ImageProcessingProgress>
    {
        public void Report(ImageProcessingProgress value) => report(value);
    }

    [Fact]
    public void BayerDisplayKeepsOriginalMosaicReadouts()
    {
        InDirectory(directory =>
        {
            var source = XisfImageData.FromSamples<ushort>([60000, 30000, 30000, 10000], [2, 2], 1);
            source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            var inspection = SaveAndInspect(directory, source);
            Assert.Single(inspection.Statistics);
            Assert.Equal("60000", inspection.ReadSample(0, 0, 0));
            Assert.Equal("10000", inspection.ReadSample(1, 1, 0));
            Assert.Contains("original Bayer mosaic", inspection.SampleDescription);
            Assert.True(inspection.Image.Preview.Bgra[2] > inspection.Image.Preview.Bgra[1]);
        });
    }

    [Fact]
    public async Task SavedAstapCoordinatesAreValidatedAgainstTheCurrentSource()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, "solved.xisf");
            XisfWriter.Write(file, [TestImages.Create()]);
            var selected = SkyImageLoader.Load(file, DisplayMode.Linear)[0];
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var solved = await new AstapPlateSolver(library).SolveAsync(selected, AstapTests.FakeOptions());
            var inspection = ScientificImageInspection.Load(selected, library: library);
            Assert.Contains("ASTAP", inspection.Placement!.Description);
            Assert.True(inspection.SkyAtPixel(32, 24)!.Value.DistanceTo(solved.Placement.At(32.5 / 64, 24.5 / 48)) < 1e-8);
            var changed = TestImages.Create();
            changed.FitsKeywords.Add(XisfFitsKeyword.FromValue("COMMENT", "Changed source"));
            XisfWriter.Write(file, [changed]);
            inspection = ScientificImageInspection.Load(selected, library: library);
            Assert.DoesNotContain("ASTAP", inspection.Placement!.Description);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ScientificImageInspection SaveAndInspect(string directory, XisfImageData source)
    {
        string file = Path.Combine(directory, "source.xisf");
        XisfWriter.Write(file, [source]);
        return ScientificImageInspection.Load(SkyImageLoader.Load(file, DisplayMode.Linear)[0]);
    }

    private static void InDirectory(Action<string> action)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
