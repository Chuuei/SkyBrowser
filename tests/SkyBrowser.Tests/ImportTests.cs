using System.IO;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

internal static class TestImages
{
    public static XisfImageData Create(bool linear = false)
    {
        const int width = 64, height = 48;
        float[] samples = new float[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    samples[c * width * height + y * width + x] = linear
                        ? 0.002f + 0.00001f * ((x * 7 + y * 13 + c) % 100)
                        : (c == 0 ? (x < width / 2 ? 0.8f : 0.1f) : c == 1 ? (y < height / 2 ? 0.7f : 0.1f) : 0.2f);
                }
            }
        }

        var image = XisfImageData.FromSamples(samples, [width, height], 3);
        image.ColorSpace = "RGB";
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "Synthetic alignment fixture"));
        foreach (var (key, value) in new[]
        {
            ("CRPIX1", 32.5),
            ("CRPIX2", 24.5),
            ("CRVAL1", 83.822),
            ("CRVAL2", -5.391),
            ("CD1_1", -0.01),
            ("CD1_2", 0.0),
            ("CD2_1", 0.0),
            ("CD2_2", 0.01)
        }

        )
        {
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue(key, value));
        }

        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE1", "RA---TAN"));
        image.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE2", "DEC--TAN"));
        return image;
    }

    public static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "SkyBrowser-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class ImportTests
{
    [Fact]
    public void FitsAndXisfFilesProduceIdenticalTexturesAndFootprints()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var image = TestImages.Create();
            string xisf = Path.Combine(dir, "sample.xisf"), fits = Path.Combine(dir, "sample.fits");
            XisfWriter.Write(xisf, [image]);
            FitsWriter.Write(fits, FitsConverter.FromXisf([image], ConversionLossPolicy.Allow));
            var x = Assert.Single(SkyImageLoader.Load(xisf, DisplayMode.Linear));
            var f = Assert.Single(SkyImageLoader.Load(fits, DisplayMode.Linear));
            Assert.Equal(x.Preview.Bgra, f.Preview.Bgra);
            Assert.True(x.Placement!.At(0, 0).DistanceTo(f.Placement!.At(0, 0)) < 1e-10);
            Assert.Equal(64, x.Width);
            Assert.Equal(48, x.Height);
            Assert.Contains(
                x.Details.Entries,
                entry => entry.Source == "FITS" &&
                entry.Name == "OBJECT" &&
                entry.Value.Contains("Synthetic alignment fixture")
            );
            Assert.Contains(f.Details.Entries, entry => entry.Source == "FITS" && entry.Name == "BITPIX" && entry.Value == "-32");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DarkLinearFilesUseTheLibraryStretchAndCanBeOverridden()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string path = Path.Combine(dir, "linear.xisf");
            XisfWriter.Write(path, [TestImages.Create(true)]);
            var automatic = Assert.Single(SkyImageLoader.Load(path));
            var stretchOnly = SkyFinishSettings.Default with
            {
                BalanceBackground = false,
                ReduceGradients = false,
                ReduceNoise = false,
                EnhanceDetail = false
            };
            var forced = Assert.Single(SkyImageLoader.Load(path, DisplayMode.Finished, stretchOnly));
            var linear = Assert.Single(SkyImageLoader.Load(path, DisplayMode.Linear));
            Assert.Contains("Dark sky", automatic.DisplayDescription);
            Assert.Equal(forced.Preview.Bgra, automatic.Preview.Bgra);
            Assert.NotEqual(linear.Preview.Bgra, automatic.Preview.Bgra);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void XisfDisplayOrientationDoesNotRotatePixelsAwayFromTheirAstrometry()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var image = TestImages.Create();
            image.Orientation = "90;flip";
            string path = Path.Combine(dir, "oriented.xisf");
            XisfWriter.Write(path, [image]);
            var loaded = Assert.Single(SkyImageLoader.Load(path, DisplayMode.Linear));
            Assert.Equal(64, loaded.Preview.Width);
            Assert.Equal(48, loaded.Preview.Height);
            Assert.True(loaded.Preview.Bgra[2] > 190);
            Assert.True(loaded.Preview.Bgra[1] > 150);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MultipleXisfImagesAndPreCancelledImportsAreHandled()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string path = Path.Combine(dir, "multi.xisf");
            XisfWriter.Write(path, [TestImages.Create(), TestImages.Create()]);
            Assert.Equal(2, SkyImageLoader.Load(path).Count);
            Assert.Equal(1, Assert.Single(SkyImageLoader.Load(path, onlyImage: 1)).ImageIndex);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => SkyImageLoader.Load(path, cancellationToken: cancel.Token));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ImageMetadataRetainsCommentsAndPropertiesForEachImage()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var first = TestImages.Create();
            var second = TestImages.Create();
            first.Properties.Set("Observation:Observer", "Ada", comment: "Observer name");
            second.Properties.Set("Observation:Observer", "Grace");
            first.FitsKeywords.Add(XisfFitsKeyword.FromValue("HISTORY", "First processing step"));
            first.FitsKeywords.Add(XisfFitsKeyword.FromValue("HISTORY", "Second processing step"));
            first.Orientation = "90;flip";
            string path = Path.Combine(dir, "metadata.xisf");
            XisfWriter.Write(path, [first, second]);
            var images = SkyImageLoader.Load(path, DisplayMode.Linear);
            Assert.Contains(
                images[0].Details.Entries,
                entry => entry.Name == "Observation:Observer" && entry.Value == "Ada" && entry.Comment == "Observer name"
            );
            Assert.Contains(images[1].Details.Entries, entry => entry.Name == "Observation:Observer" && entry.Value == "Grace");
            Assert.Contains(images[0].Details.Entries, entry => entry.Name == "File orientation" && entry.Value == "90;flip");
            Assert.Equal(2, images[0].Details.Entries.Count(entry => entry.Name == "HISTORY"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NativeXisfSolutionTakesPriorityOverContradictoryFitsKeywords()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var image = TestImages.Create();
            XisfAstrometricSolution.CreateLinear("Gnomonic", [10.68, 41.27], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } })
                .ApplyTo(image);
            string path = Path.Combine(dir, "native.xisf");
            XisfWriter.Write(path, [image]);
            var loaded = Assert.Single(SkyImageLoader.Load(path));
            Assert.StartsWith("XISF", loaded.Placement!.Description);
            Assert.True(loaded.Placement.Center.DistanceTo(new(10.68, 41.27)) < 1e-10);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
