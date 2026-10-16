using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class SolvedImageExportTests
{
    [Theory]
    [InlineData(".fits", ".fits")]
    [InlineData(".fits", ".xisf")]
    [InlineData(".xisf", ".fits")]
    [InlineData(".xisf", ".xisf")]
    [InlineData(".xish", ".fits")]
    [InlineData(".xish", ".xisf")]
    public async Task SolvedCopiesReopenWithoutLibraryWithFullScientificPixelsAndSelectedImageSolution(string inputExtension, string outputExtension)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            const int plane = 64 * 48;
            float[] samples = Enumerable.Range(0, plane * 4).Select(i => i % plane == 0 ? (i >= plane * 3 ? 1f : 0.1f) : (i % 1999) / 2000f).ToArray();
            var selected = XisfImageData.FromSamples(samples, [64, 48], 4);
            selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "Export fixture"));
            selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("A_ORDER", 2));
            selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("PV1_0", 0.2));
            selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("CTYPE1A", "RA---SIN"));
            selected.Properties.Set("Observation:Observer", "Test observer");
            selected.Orientation = "90;flip";
            XisfAstrometricSolution.CreateLinear("Gnomonic", [10, 20], [32, 24], new double[,] { { -0.01, 0 }, { 0, 0.01 } }).ApplyTo(selected);
            var other = XisfImageData.FromSamples(Enumerable.Repeat(0.05f, plane).ToArray(), [64, 48], 1);
            string source = Path.Combine(directory, "original" + inputExtension);
            string sidecar = Path.Combine(directory, "pixels.fits");
            if (inputExtension == ".fits")
            {
                FitsWriter.Write(source, FitsConverter.FromXisf([other, selected], ConversionLossPolicy.Allow));
            }
            else if (inputExtension == ".xish")
            {
                XisfWriter.WriteDistributed(source, sidecar, [other, selected]);
            }
            else
            {
                XisfWriter.Write(source, [other, selected]);
            }
            byte[] hash = SHA256.HashData(File.ReadAllBytes(source));
            byte[]? sidecarHash = File.Exists(sidecar) ? SHA256.HashData(File.ReadAllBytes(sidecar)) : null;
            var image = Assert.Single(SkyImageLoader.Load(source, DisplayMode.Linear, onlyImage: 1));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var result = await new AstapPlateSolver(library).SolveAsync(image, AstapTests.FakeOptions());
            string destination = Path.Combine(directory, "solved" + outputExtension);
            var exporter = new SolvedImageExporter(new ImageLibrary(library.DatabasePath));
            var export = exporter.Export(image, destination);
            Assert.Equal(destination, export.Path);
            // Load without the library: the file itself must carry the solution.
            var reopened = SkyImageLoader.Load(destination, DisplayMode.Linear);
            Assert.Equal(2, reopened.Count);
            Assert.Null(reopened[0].Placement);
            Assert.Null(reopened[1].PlacementWarning);
            Assert.Equal(64, reopened[1].Width);
            Assert.Equal(48, reopened[1].Height);
            Assert.Contains(reopened[1].Details.Entries, entry => entry.Name == "OBJECT" && entry.Value.Contains("Export fixture"));
            Assert.DoesNotContain(reopened[1].Details.Entries, entry => entry.Name is "A_ORDER" or "PV1_0" or "CTYPE1A");
            for (int y = 0; y <= 16; y++)
            {
                for (int x = 0; x <= 16; x++)
                {
                    Assert.InRange(reopened[1].Placement!.At(x / 16.0, y / 16.0).DistanceTo(result.Placement.At(x / 16.0, y / 16.0)), 0, 1e-8);
                }
            }
            if (outputExtension == ".fits")
            {
                var saved = FitsReader.Load(destination).Images.ElementAt(1);
                Assert.Equal(-32, saved.BitPix);
                Assert.Equal(samples.Select(value => (double)value), saved.GetPhysicalSamples());
            }
            else
            {
                var saved = XisfReader.Load(destination).Images[1];
                Assert.Equal(XisfSampleFormat.Float32, saved.SampleFormat);
                Assert.Equal(samples, saved.GetSamples<float>());
                Assert.Equal(4, saved.ChannelCount);
                Assert.Equal(1, saved.AstrometricSolution!.AvailableLayer);
                Assert.Contains(saved.FitsKeywords, keyword => keyword.Name == "CD1_1");
                if (inputExtension != ".fits")
                {
                    Assert.Equal("90;flip", saved.Orientation);
                    Assert.Equal("Test observer", saved.Properties["Observation:Observer"].Value);
                }
            }
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
            if (sidecarHash is not null)
            {
                Assert.Equal(sidecarHash, SHA256.HashData(File.ReadAllBytes(sidecar)));
                Assert.Throws<IOException>(() => exporter.Export(image, sidecar, overwrite: true));
            }
            Assert.Empty(Directory.GetFiles(directory, ".solved-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(".fits")]
    [InlineData(".xisf")]
    public async Task WideIntegerBayerSamplesRemainExactAndKeepTheirMosaicInterpretation(string extension)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            ulong[] samples = Enumerable.Range(0, 64 * 48).Select(i => i == 0 ? ulong.MaxValue / 10 : ulong.MaxValue - (ulong)i).ToArray();
            var source = XisfImageData.FromSamples(samples, [64, 48], 1);
            source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
            // The native tile takes precedence over these conflicting legacy cards.
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "BGGR"));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("XBAYROFF", 1));
            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", "BOTTOM-UP"));
            string file = Path.Combine(directory, "mosaic.xisf");
            XisfWriter.Write(file, [source]);
            var image = Assert.Single(SkyImageLoader.Load(file));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            await new AstapPlateSolver(library).SolveAsync(image, AstapTests.FakeOptions());
            string destination = Path.Combine(directory, "solved" + extension);
            new SolvedImageExporter(library).Export(image, destination);
            if (extension == ".xisf")
            {
                var saved = XisfReader.Load(destination).Images.Single();
                Assert.Equal(samples, saved.GetSamples<ulong>());
                Assert.Equal("RGGB", saved.ColorFilterArray!.Pattern);
                Assert.Equal(1, saved.ChannelCount);
            }
            else
            {
                var document = FitsReader.Load(destination);
                var saved = document.Images.Single();
                Assert.Equal(64, saved.BitPix);
                byte[] bytes = saved.ReadRawData();
                ulong[] restored = Enumerable.Range(0, samples.Length)
                    .Select(i => BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(i * 8, 8)) ^ (1UL << 63)).ToArray();
                Assert.Equal(samples, restored);
                Assert.Equal("RGGB", document.Hdus[0].Header.FindFirst("BAYERPAT")!.Value);
                Assert.Null(document.Hdus[0].Header.FindFirst("XBAYROFF"));
            }
            Assert.NotNull(Assert.Single(SkyImageLoader.Load(destination)).Placement);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndSourceChangesLeaveExistingDestinationUntouched(bool changeSource)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string source = Path.Combine(directory, "original.xisf");
            XisfWriter.Write(source, [XisfImageData.FromSamples(Enumerable.Repeat(0.1f, 64 * 48).ToArray(), [64, 48], 1)]);
            var image = Assert.Single(SkyImageLoader.Load(source));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            await new AstapPlateSolver(library).SolveAsync(image, AstapTests.FakeOptions());
            var exporter = new SolvedImageExporter(library);
            string destination = Path.Combine(directory, "existing.fits");
            File.WriteAllText(destination, "Existing file");
            Assert.Throws<IOException>(() => exporter.Export(image, source, overwrite: true));
            Assert.Throws<IOException>(() => exporter.Export(image, destination));
            using var cancellation = new CancellationTokenSource();
            var progress = new ExportProgress(() =>
            {
                if (changeSource)
                {
                    File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(3));
                }
                else
                {
                    cancellation.Cancel();
                }
            });
            if (changeSource)
            {
                Assert.Throws<IOException>(() => exporter.Export(image, destination, true, cancellation.Token, progress));
                Assert.Throws<InvalidOperationException>(() => exporter.Export(image, destination, true));
            }
            else
            {
                Assert.ThrowsAny<OperationCanceledException>(() => exporter.Export(image, destination, true, cancellation.Token, progress));
            }
            Assert.Equal("Existing file", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(directory, ".solved-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class ExportProgress(Action action) : IProgress<ImageProcessingProgress>
    {
        public void Report(ImageProcessingProgress value)
        {
            if (value.Stage == "Writing solved scientific copy")
            {
                action();
            }
        }
    }
}
