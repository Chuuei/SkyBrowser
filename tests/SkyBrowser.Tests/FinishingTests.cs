using System.IO;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class FinishingTests
{
    internal static XisfImageData LinearField(int width = 160, int height = 120, bool alpha = false, double pedestal = 0)
    {
        int count = width * height, channels = alpha ? 4 : 3;
        float[] pixels = new float[count * channels];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                double noise = 0.0001 * Math.Sin(x * 7.1 + y * 2.3), gradient = 0.0005 * x / width;
                double nebula = 0.016 * Math.Exp(-(Math.Pow((x - width * 0.5) / (width * 0.1), 2) + Math.Pow((y - height * 0.5) / (height * 0.1), 2)));
                for (int c = 0; c < 3; c++)
                {
                    pixels[c * count + p] = (float)(pedestal + 0.003 + c * 0.0007 + gradient + noise + nebula * (c == 0 ? 1 : 0.4));
                }

                if (alpha)
                {
                    pixels[3 * count + p] = x < 4 ? 0 : x < 8 ? 0.5f : 1;
                }
            }
        }

        var image = XisfImageData.FromSamples(pixels, [width, height], channels);
        image.ColorSpace = "RGB";
        return image;
    }

    [Fact]
    public void DarkSkyRetainsEmissionWhileReducingTheBackgroundAndPreservingSource()
    {
        var source = LinearField();
        byte[] original = source.RawData.ToArray();
        var finished = SkyImageFinisher.Render(source, SkyFinishSettings.Default);
        Assert.Equal(160, finished.Pixels.Width);
        Assert.Equal(120, finished.Pixels.Height);
        Assert.Equal(original, source.RawData.ToArray());
        Assert.InRange(MedianLight(finished.Pixels), 10, 45);
        int background = 5 * 160 + 80, core = 60 * 160 + 80;
        Assert.True(finished.Pixels.Bgra[core * 4 + 2] > finished.Pixels.Bgra[background * 4 + 2] + 50);
        Assert.Contains("noise cleanup", finished.Description);
        Assert.Contains("background color balance", finished.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.62)]
    public void AutomaticStretchesWithoutCleanupEvenWhenAllCleanupOptionsAreEnabled(double pedestal)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = LinearField(alpha: true, pedestal: pedestal);
            string path = Path.Combine(directory, "linear.xisf");
            XisfWriter.Write(path, [source]);
            byte[] original = File.ReadAllBytes(path);
            var settings = SkyFinishSettings.Default;
            var progress = new ProgressRecorder();
            var automatic = Assert.Single(SkyImageLoader.Load(path, DisplayMode.Automatic, settings, progress: progress));
            var expected = SkyImageFinisher.Render(
                source,
                settings with { BalanceBackground = false, ReduceGradients = false, ReduceNoise = false, EnhanceDetail = false }
            );
            Assert.Equal(expected.Pixels.Bgra, automatic.Preview.Bgra);
            Assert.InRange(MedianLight(automatic.Preview), 10, 45);
            Assert.Contains("stretch only", automatic.DisplayDescription);
            Assert.Equal(settings, automatic.FinishSettings); // Keep the user's cleanup choices for explicit finishing.
            Assert.DoesNotContain(
                progress.Updates,
                update => update.Stage is "Reducing broad sky gradients" or "Smoothing background and color noise" or "Enhancing detail and color"
            );
            Assert.Equal(0, automatic.Preview.Bgra[3]);
            Assert.InRange(automatic.Preview.Bgra[5 * 4 + 3], 127, 128);
            var fullProgress = new ProgressRecorder();
            var finished = Assert.Single(SkyImageLoader.Load(path, DisplayMode.Finished, settings, progress: fullProgress));
            Assert.Contains(fullProgress.Updates, update => update.Stage == "Reducing broad sky gradients");
            Assert.Contains(fullProgress.Updates, update => update.Stage == "Smoothing background and color noise");
            Assert.Contains(fullProgress.Updates, update => update.Stage == "Enhancing detail and color");
            Assert.NotEqual(automatic.Preview.Bgra, finished.Preview.Bgra);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class ProgressRecorder : IProgress<ImageProcessingProgress>
    {
        public List<ImageProcessingProgress> Updates { get; } = [];

        public Action<ImageProcessingProgress>? OnReport { get; init; }

        public void Report(ImageProcessingProgress value)
        {
            Updates.Add(value);
            OnReport?.Invoke(value);
        }
    }

    [Fact]
    public async Task ParallelPreviewMatchesSingleProcessorPixelsAndCancelsDuringRows()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var source = LinearField(768, 512, alpha: true, pedestal: 0.62);
            string path = Path.Combine(directory, "parallel.xisf"), report = Path.Combine(directory, "serial.json");
            XisfWriter.Write(path, [source]);
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
            string probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "SkyBrowser.CacheProbe", configuration, "SkyBrowser.CacheProbe.dll"));
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
            start.ArgumentList.Add(probe);
            start.ArgumentList.Add("--compare-stretches");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(report);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await errors);
            using var serial = System.Text.Json.JsonDocument.Parse(await output);
            foreach (var result in serial.RootElement.GetProperty("Measurements").EnumerateArray())
            {
                var mode = Enum.Parse<DisplayMode>(result.GetProperty("Mode").GetString()!);
                var actual = Assert.Single(SkyImageLoader.Load(path, mode));
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual.Preview.Bgra));
                Assert.Equal(result.GetProperty("Images")[0].GetProperty("Hash").GetString(), hash);
            }

            using var cancellation = new CancellationTokenSource();
            var progress = new ProgressRecorder
            {
                OnReport = update =>
                {
                    if (update.Stage == "Preparing full-frame preview" && update.Percentage > 0)
                    {
                        cancellation.Cancel();
                    }
                }
            };
            Assert.ThrowsAny<OperationCanceledException>(() => SkyImageFinisher.Render(source, SkyFinishSettings.Default, cancellation.Token, progress));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SkyBackgroundAdjustmentIsMonotonicAndOptionalCleanupCanBeDisabled()
    {
        var source = LinearField();
        var settings = new SkyFinishSettings
        {
            TargetBackground = 0.06,
            ReduceGradients = false,
            ReduceNoise = false,
            EnhanceDetail = false
        };
        var dark = SkyImageFinisher.Render(source, settings);
        var bright = SkyImageFinisher.Render(source, settings with { TargetBackground = 0.18 });
        Assert.True(MedianLight(dark.Pixels) + 15 < MedianLight(bright.Pixels));
        Assert.DoesNotContain("cleanup", dark.Description);
        Assert.DoesNotContain("enhancement", dark.Description);
    }

    [Fact]
    public void TransparentCoverageAndBlankEdgesAreNotCroppedOrRotated()
    {
        var source = LinearField(alpha: true);
        source.Orientation = "90;flip";
        var result = SkyImageFinisher.Render(source, SkyFinishSettings.Default).Pixels;
        Assert.Equal(160, result.Width);
        Assert.Equal(120, result.Height);
        Assert.Equal(0, result.Bgra[3]);
        Assert.InRange(result.Bgra[5 * 4 + 3], 127, 128);
        Assert.Equal(255, result.Bgra[20 * 4 + 3]);
        Assert.Equal("90;flip", source.Orientation);
    }

    [Fact]
    public void HighPedestalLinearDataIsRecenteredInsteadOfInverted()
    {
        var source = LinearField(pedestal: 0.62);
        Assert.True(SkyImageFinisher.LooksLinear(source));
        var result = SkyImageFinisher.Render(source, SkyFinishSettings.Default).Pixels;
        Assert.InRange(MedianLight(result), 10, 45);
        Assert.True(result.Bgra[(60 * 160 + 80) * 4 + 2] > result.Bgra[3 * 4 + 2]);
    }

    [Fact]
    public void ConstantImagesRetainExposureAndCancellationInterruptsProcessing()
    {
        var source = XisfImageData.FromSamples(Enumerable.Repeat(0.5f, 64 * 48).ToArray(), [64, 48], 1);
        var result = SkyImageFinisher.Render(source, SkyFinishSettings.Default);
        Assert.All(result.Pixels.Bgra.Where((_, i) => i % 4 != 3), value => Assert.InRange(value, 127, 128));
        Assert.Equal("Existing exposure retained", result.Description);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SkyImageFinisher.Render(source, SkyFinishSettings.Default, cancellation.Token));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(0.5)]
    public void InvalidBackgroundSettingsAreRejected(double background) => Assert.Throws<ArgumentOutOfRangeException>(() => new SkyFinishSettings { TargetBackground = background }.Validate());

    internal static double MedianLight(DisplayPixels image)
    {
        var values = Enumerable.Range(0, image.Width * image.Height)
            .Where(p => image.Bgra[p * 4 + 3] > 0)
            .Select(p => (image.Bgra[p * 4] + image.Bgra[p * 4 + 1] + image.Bgra[p * 4 + 2]) / 3.0)
            .Order()
            .ToArray();
        return values[values.Length / 2];
    }
}
