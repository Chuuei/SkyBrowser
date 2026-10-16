using System.IO;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class DebayerTests
{
    [Theory]
    [InlineData("RGGB", false)]
    [InlineData("BGGR", false)]
    [InlineData("GRBG", false)]
    [InlineData("GBRG", false)]
    [InlineData("RGGB", true)]
    [InlineData("BGGR", true)]
    [InlineData("GRBG", true)]
    [InlineData("GBRG", true)]
    public void NativeXisfAndFitsMosaicsReconstructAllColorsIncludingBorders(string pattern, bool fits)
    {
        InDirectory(directory =>
        {
            var image = Mosaic(pattern);
            if (fits)
            {
                image.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", pattern.ToLowerInvariant()));
            }
            else
            {
                image.ColorFilterArray = new(pattern, 2, 2, "Bayer");
            }

            image.Orientation = "90;flip";
            string file = Path.Combine(directory, fits ? "mosaic.fits" : "mosaic.xisf");
            Write(file, image);
            byte[] original = File.ReadAllBytes(file);
            var loaded = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            AssertColor(loaded, 204, 102, 51);
            Assert.Equal(7, loaded.Width);
            Assert.Equal(5, loaded.Height);
            Assert.Contains($"Automatically debayered {pattern}", loaded.DisplayDescription);
            Assert.Contains("1 channel(s)", loaded.Metadata);
            Assert.Contains(loaded.Details.Entries, entry => entry.Name == "Channels" && entry.Value == "1");
            if (!fits)
            {
                Assert.Contains(loaded.Details.Entries, entry => entry.Name == "CFA pattern" && entry.Value == pattern);
            }
            Assert.Equal(original, File.ReadAllBytes(file));
        });
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(2050, 2)]
    public void DebayeringPrecedesPreviewResizingAndSupportsTheSmallestTile(int width, int height)
    {
        InDirectory(directory =>
        {
            var source = Mosaic("RGGB", width, height);
            source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            string file = Path.Combine(directory, "resized.xisf");
            Write(file, source);
            var loaded = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            Assert.Equal(Math.Min(width, 2048), loaded.Preview.Width);
            Assert.Equal(width, loaded.Width);
            AssertColor(loaded, 204, 102, 51);
        });
    }

    [Theory]
    [InlineData(0, 0, "RGGB", false)]
    [InlineData(1, 0, "GRBG", false)]
    [InlineData(0, 1, "GBRG", false)]
    [InlineData(1, 1, "BGGR", false)]
    [InlineData(-1, -3, "BGGR", false)]
    [InlineData(2, 4, "RGGB", false)]
    [InlineData(1, 1, "BGGR", true)]
    public void FitsOffsetsSelectTheEffectiveStoredPattern(int x, int y, string effective, bool longNames)
    {
        InDirectory(directory =>
        {
            var image = Mosaic(effective);
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue(longNames ? "XBAYEROFF" : "XBAYROFF", x));
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue(longNames ? "YBAYEROFF" : "YBAYROFF", y));
            string file = Path.Combine(directory, "offset.fits");
            Write(file, image);
            var loaded = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            AssertColor(loaded, 204, 102, 51);
            Assert.Contains($"debayered {effective}", loaded.DisplayDescription);
        });
    }

    [Theory]
    [InlineData("TOP-DOWN", 6, "RGGB")]
    [InlineData("BOTTOM-UP", 6, "GBRG")]
    [InlineData("BOTTOM-UP", 5, "RGGB")]
    public void ExplicitRowOrderAdjustsTheTileWithoutFlippingPixels(string rowOrder, int height, string effective)
    {
        InDirectory(directory =>
        {
            var image = Mosaic(effective, height: height);
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", rowOrder));
            string file = Path.Combine(directory, "row-order.fits");
            Write(file, image);
            AssertColor(Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear)), 204, 102, 51);
        });
    }

    [Theory]
    [InlineData(DisplayMode.Linear)]
    [InlineData(DisplayMode.Automatic)]
    [InlineData(DisplayMode.AutoStretch)]
    [InlineData(DisplayMode.StoredDisplay)]
    [InlineData(DisplayMode.Finished)]
    public void AllDisplayModesProcessReconstructedRgbAndRetainSkyPlacement(DisplayMode mode)
    {
        InDirectory(directory =>
        {
            var source = FinishingTests.LinearField(64, 48);
            var colors = source.GetSamples<float>(XisfPixelLayout.Interleaved);
            float[] mosaic = Enumerable.Range(0, 64 * 48).Select(i =>
                colors[i * 3 + "RGB".IndexOf("RGGB"[((i / 64) & 1) * 2 + (i & 1)])]).ToArray();
            var image = XisfImageData.FromSamples(mosaic, [64, 48], 1);
            image.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            image.Orientation = "90;flip";
            XisfAstrometricSolution.CreateLinear("Gnomonic", [10.68, 41.27], [32, 24],
                new double[,] { { -0.01, 0 }, { 0, 0.01 } }).ApplyTo(image);
            string file = Path.Combine(directory, "linear.xisf");
            Write(file, image);
            byte[] original = File.ReadAllBytes(file);
            var loaded = Assert.Single(SkyImageLoader.Load(file, mode));
            Assert.Contains("Automatically debayered RGGB", loaded.DisplayDescription);
            Assert.Equal(64, loaded.Preview.Width);
            Assert.Equal(48, loaded.Preview.Height);
            Assert.NotNull(loaded.Placement);
            Assert.True(loaded.Placement.Center.DistanceTo(new(10.68, 41.27)) < 1e-10);
            Assert.Contains(loaded.Details.Entries, entry => entry.Name == "File orientation" && entry.Value == "90;flip");
            Assert.Equal(original, File.ReadAllBytes(file));
            if (mode == DisplayMode.Automatic)
            {
                Assert.Contains("stretch only", loaded.DisplayDescription);
                Assert.DoesNotContain("noise cleanup", loaded.DisplayDescription);
            }
        });
    }

    [Theory]
    [InlineData(XisfSampleFormat.UInt8, false)]
    [InlineData(XisfSampleFormat.UInt16, true)]
    [InlineData(XisfSampleFormat.UInt32, false)]
    [InlineData(XisfSampleFormat.UInt64, true)]
    [InlineData(XisfSampleFormat.Float32, true)]
    [InlineData(XisfSampleFormat.Float64, false)]
    public void ReconstructionPreservesRealSampleFormatsAndByteOrder(XisfSampleFormat format, bool bigEndian)
    {
        InDirectory(directory =>
        {
            var bytes = Mosaic("RGGB").GetSamples<byte>();
            XisfImageData image = format switch
            {
                XisfSampleFormat.UInt8 => XisfImageData.FromSamples(bytes, [7, 5], 1),
                XisfSampleFormat.UInt16 => XisfImageData.FromSamples(bytes.Select(v => (ushort)(v * 257)).ToArray(), [7, 5], 1, isBigEndian: bigEndian),
                XisfSampleFormat.UInt32 => XisfImageData.FromSamples(bytes.Select(v => v * (uint.MaxValue / 255)).ToArray(), [7, 5], 1, isBigEndian: bigEndian),
                XisfSampleFormat.UInt64 => XisfImageData.FromSamples(bytes.Select(v => v * (ulong.MaxValue / 255)).ToArray(), [7, 5], 1, isBigEndian: bigEndian),
                XisfSampleFormat.Float32 => XisfImageData.FromSamples(bytes.Select(v => v / 255f).ToArray(), [7, 5], 1, isBigEndian: bigEndian),
                _ => XisfImageData.FromSamples(bytes.Select(v => v / 255d).ToArray(), [7, 5], 1, isBigEndian: bigEndian)
            };
            image.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            string file = Path.Combine(directory, "precision.xisf");
            Write(file, image);
            AssertColor(Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear)), 204, 102, 51);
        });
    }

    [Fact]
    public void MissingColorsUseLocalBilinearNeighborsAndMeasuredSamplesStayUnchanged()
    {
        InDirectory(directory =>
        {
            byte[] samples = [10, 20, 30, 40, 50, 60, 70, 80, 90];
            var image = XisfImageData.FromSamples(samples, [3, 3], 1);
            image.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            string file = Path.Combine(directory, "neighbors.xisf");
            Write(file, image);
            var pixels = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear)).Preview.Bgra;
            Assert.Equal(new byte[] { 50, 30, 10, 255 }, pixels[..4]);
            Assert.Equal(new byte[] { 50, 50, 50, 255 }, pixels[16..20]);
            Assert.Equal(new byte[] { 50, 70, 90, 255 }, pixels[32..36]);
        });
    }

    [Fact]
    public void NativeCfaTakesPrecedenceAndGrayStoredDisplayIsLinkedAcrossColors()
    {
        InDirectory(directory =>
        {
            var image = Mosaic("GBRG");
            image.ColorFilterArray = new("GBRG", 2, 2, "Bayer");
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("XBAYROFF", "invalid"));
            image.DisplayFunction = new([0.25, 0.5, 0.5, 0.5], [0, 0, 0, 0], [1, 1, 1, 1], [0, 0, 0, 0], [1, 1, 1, 1]);
            string file = Path.Combine(directory, "stored.xisf");
            Write(file, image);
            var loaded = Assert.Single(SkyImageLoader.Load(file));
            Assert.Contains("Stored display function", loaded.DisplayDescription);
            Assert.Contains("debayered GBRG", loaded.DisplayDescription);
            AssertColor(loaded, Transfer(204), Transfer(102), Transfer(51));

            static byte Transfer(byte value) => (byte)Math.Round(255 * XisfDisplayFunctions.MidtonesTransfer(value / 255d, 0.25));
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unsupported")]
    [InlineData("offset")]
    [InlineData("row-order")]
    [InlineData("native")]
    public void MissingOrInvalidMasksDoNotGuessColorsOrRunCleanup(string kind)
    {
        InDirectory(directory =>
        {
            var image = Mosaic("RGGB");
            if (kind != "missing")
            {
                image.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", kind == "unsupported" ? "RGBG" : "RGGB"));
            }
            if (kind == "offset")
            {
                image.FitsKeywords.Add(XisfFitsKeyword.FromValue("YBAYROFF", 0.5));
            }
            if (kind == "row-order")
            {
                image.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", "unknown"));
            }
            if (kind == "native")
            {
                image.ColorFilterArray = new("RGBG", 2, 2, "Bayer");
            }
            string file = Path.Combine(directory, "unknown.xisf");
            Write(file, image);
            var loaded = Assert.Single(SkyImageLoader.Load(file, kind == "missing" ? DisplayMode.Linear : DisplayMode.Finished));
            Assert.DoesNotContain("Automatically debayered", loaded.DisplayDescription);
            for (int i = 0; i < loaded.Preview.Bgra.Length; i += 4)
            {
                Assert.Equal(loaded.Preview.Bgra[i], loaded.Preview.Bgra[i + 1]);
                Assert.Equal(loaded.Preview.Bgra[i], loaded.Preview.Bgra[i + 2]);
            }
            if (kind != "missing")
            {
                Assert.Contains("CFA mosaic displayed as grayscale", loaded.Metadata);
            }
        });
    }

    [Fact]
    public void AlreadyRgbImagesIgnoreStaleBayerKeywords()
    {
        InDirectory(directory =>
        {
            var source = TestImages.Create();
            var expected = XisfDisplayRenderer.Render(source).Pixels;
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
            string file = Path.Combine(directory, "rgb.xisf");
            Write(file, source);
            var loaded = Assert.Single(SkyImageLoader.Load(file, DisplayMode.Linear));
            Assert.Equal(expected, loaded.Preview.Bgra);
            Assert.DoesNotContain("CFA mosaic", loaded.Metadata);
            Assert.DoesNotContain("debayered", loaded.DisplayDescription);
        });
    }

    [Fact]
    public void DebayeredPreviewsAndSourceMetadataSurviveCacheHitsAndCancellation()
    {
        InDirectory(directory =>
        {
            var image = Mosaic("RGGB");
            image.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            string file = Path.Combine(directory, "cached.xisf");
            Write(file, image);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache);
            var cold = Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default));
            var warm = Assert.Single(new CachedSkyImageLoader(cache, (_, _, _, _, _, _) =>
                throw new InvalidOperationException("Expected a cached RGB preview.")).Load(file, DisplayMode.Linear, SkyFinishSettings.Default));
            Assert.True(warm.IsFromCache);
            Assert.Equal(cold.Preview.Bgra, warm.Preview.Bgra);
            Assert.Equal(cold.Metadata, warm.Metadata);
            Assert.Equal(cold.Details.Entries, warm.Details.Entries);
            AssertColor(warm, 204, 102, 51);

            using var cancellation = new CancellationTokenSource();
            var progress = new CancelDebayer(cancellation);
            Assert.ThrowsAny<OperationCanceledException>(() => SkyImageLoader.Load(file, DisplayMode.Linear,
                SkyFinishSettings.Default, cancellation.Token, progress: progress));
            Assert.True(progress.ReachedDebayer);
        });
    }

    private sealed class CancelDebayer(CancellationTokenSource cancellation) : IProgress<ImageProcessingProgress>
    {
        public bool ReachedDebayer { get; private set; }

        public void Report(ImageProcessingProgress value)
        {
            if (value.Stage.StartsWith("Debayering", StringComparison.Ordinal))
            {
                ReachedDebayer = true;
                cancellation.Cancel();
            }
        }
    }

    private static XisfImageData Mosaic(string pattern, int width = 7, int height = 5)
    {
        byte[] samples = Enumerable.Range(0, width * height).Select(i =>
            pattern[((i / width) & 1) * 2 + ((i % width) & 1)] switch
            {
                'R' => (byte)204,
                'G' => (byte)102,
                _ => (byte)51
            }).ToArray();
        return XisfImageData.FromSamples(samples, [width, height], 1);
    }

    private static void AssertColor(LoadedSkyImage image, byte red, byte green, byte blue)
    {
        for (int i = 0; i < image.Preview.Bgra.Length; i += 4)
        {
            Assert.Equal(blue, image.Preview.Bgra[i]);
            Assert.Equal(green, image.Preview.Bgra[i + 1]);
            Assert.Equal(red, image.Preview.Bgra[i + 2]);
            Assert.Equal(255, image.Preview.Bgra[i + 3]);
        }
    }

    private static void Write(string file, XisfImageData image)
    {
        if (Path.GetExtension(file) == ".fits")
        {
            FitsWriter.Write(file, FitsConverter.FromXisf([image], ConversionLossPolicy.Allow));
        }
        else
        {
            XisfWriter.Write(file, [image]);
        }
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
