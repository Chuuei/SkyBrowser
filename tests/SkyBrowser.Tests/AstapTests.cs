using System.IO;
using System.Security.Cryptography;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class AstapTests
{
    internal static ApplicationOptions FakeOptions()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "SkyBrowser.CacheProbe", configuration, "SkyBrowser.CacheProbe.exe"));
        return new() { AstapExecutablePath = executable, AstapTimeoutSeconds = 10 };
    }

    [Fact]
    public async Task BlindSolvingUsesTemporaryScientificDataAndPersistsAcrossCacheClears()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            string file = Path.Combine(directory, "source with spaces.xisf");
            var source = XisfImageData.FromSamples(Enumerable.Repeat(0.1f, 64 * 48).ToArray(), [64, 48], 1);
            source.Orientation = "90;flip";
            XisfWriter.Write(file, [source]);
            byte[] before = SHA256.HashData(File.ReadAllBytes(file));
            var original = Assert.Single(loader.LoadForBrowsing(file));
            Assert.Null(original.Placement);
            var result = await new AstapPlateSolver(library).SolveAsync(original, FakeOptions());
            Assert.True(result.Placement.Center.DistanceTo(new(83.822, -5.391)) < 1e-8);
            Assert.Contains("Fixture solution", result.Warning);
            Assert.DoesNotContain("inexact scale", result.Warning);
            Assert.InRange(result.Placement.PixelScale(original.Width, original.Height).X, 35.999, 36.001);
            cache.ClearPreviews();
            var reopened = new ImageLibrary(library.DatabasePath);
            var reloaded = Assert.Single(new CachedSkyImageLoader(cache) { Library = reopened }.LoadForBrowsing(file.ToUpperInvariant()));
            Assert.Contains("ASTAP", reloaded.Placement!.Description);
            Assert.True(reloaded.Placement.Center.DistanceTo(result.Placement.Center) < 1e-8);
            Assert.Equal(result.Placement.AngularWidth, reloaded.Placement.AngularWidth);
            Assert.Equal(result.Placement.AngularHeight, reloaded.Placement.AngularHeight);
            Assert.Equal(result.Placement.PixelScale(original.Width, original.Height), reloaded.Placement.PixelScale(reloaded.Width, reloaded.Height));
            Assert.True(Assert.Single(reopened.Search()).HasPlacement);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(file)));
            Assert.False(File.Exists(Path.ChangeExtension(file, ".ini")));
            Assert.False(File.Exists(Path.ChangeExtension(file, ".wcs")));

            source.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "Source changed"));
            XisfWriter.Write(file, [source]);
            Assert.Null(Assert.Single(loader.LoadForBrowsing(file)).Placement);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("Warning inexact scale! Set FOV=1,84d or scale=1,5\"/pix", null)]
    [InlineData("Warning inexact scale! Set FOV=1.84d or scale=1.5\"/pix or FL=517mm ", null)]
    [InlineData("Warning inexact scale! Set FOV=1,84d or scale=1,5\"/pix Warning: too few stars.", "Warning: too few stars.")]
    [InlineData("Warning: too few stars.", "Warning: too few stars.")]
    [InlineData("Warning inexact scale! Unrecognized diagnostic.", "Warning inexact scale! Unrecognized diagnostic.")]
    public void SolvedMatrixSuppliesFullFrameSizingAndOnlyInitialScaleAdvisoryIsDismissed(string warning, string? expected)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            // Non-square source pixels and a rotated field: rounded warning values,
            // preview dimensions and individual matrix entries cannot define the size.
            const int width = 6461, height = 4320;
            const double sx = 1.53 / 3600, sy = 1.54 / 3600;
            double angle = 30 * SkyCoordinate.Radians;
            string ini = Path.Combine(directory, "solution.ini");
            File.WriteAllText(ini, FormattableString.Invariant($"""
                PLTSOLVD=T
                CRPIX1={(width + 1) / 2.0}
                CRPIX2={(height + 1) / 2.0}
                CRVAL1=359.9
                CRVAL2=80
                CD1_1={-sx * Math.Cos(angle):G17}
                CD1_2={sy * Math.Sin(angle):G17}
                CD2_1={sx * Math.Sin(angle):G17}
                CD2_2={sy * Math.Cos(angle):G17}
                WARNING={warning}
                """));
            var result = AstapPlateSolver.ReadSolution(ini, width, height);
            Assert.Equal(expected, result.Warning);
            Assert.InRange(result.Placement.Center.DistanceTo(new(359.9, 80)), 0, 1e-10);
            double expectedWidth = 2 * Math.Atan(width * sx * SkyCoordinate.Radians / 2) / SkyCoordinate.Radians;
            double expectedHeight = 2 * Math.Atan(height * sy * SkyCoordinate.Radians / 2) / SkyCoordinate.Radians;
            Assert.Equal(expectedWidth, result.Placement.AngularWidth, 8);
            Assert.Equal(expectedHeight, result.Placement.AngularHeight, 8);
            var scale = result.Placement.PixelScale(width, height);
            Assert.InRange(scale.X, 1.53 - 1e-5, 1.53 + 1e-5);
            Assert.InRange(scale.Y, 1.54 - 1e-5, 1.54 + 1e-5);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0.2f, false)]
    [InlineData(0.3f, true)]
    public async Task FailureAndCancellationDoNotPublishPlacements(float sample, bool cancel)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            string file = Path.Combine(directory, "failure.xisf");
            XisfWriter.Write(file, [XisfImageData.FromSamples(Enumerable.Repeat(sample, 64 * 48).ToArray(), [64, 48], 1)]);
            var image = Assert.Single(loader.LoadForBrowsing(file));
            using var cancellation = new CancellationTokenSource();
            if (cancel)
            { cancellation.CancelAfter(TimeSpan.FromSeconds(1)); }
            var solve = new AstapPlateSolver(library).SolveAsync(image, FakeOptions(), cancellation.Token);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solve.WaitAsync(TimeSpan.FromSeconds(15)));
            }
            else
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => solve);
                Assert.Contains("star database", error.Message);
            }
            Assert.Null(Assert.Single(loader.LoadForBrowsing(file)).Placement);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SolverTimeoutTerminatesTheProcessAndRetainsThePreviousPlacement()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, "timeout.xisf");
            XisfWriter.Write(file, [XisfImageData.FromSamples(Enumerable.Repeat(0.3f, 64 * 48).ToArray(), [64, 48], 1)]);
            var image = Assert.Single(SkyImageLoader.Load(file));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var error = await Assert.ThrowsAsync<TimeoutException>(() => new AstapPlateSolver(library)
                .SolveAsync(image, FakeOptions()).WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Contains("10-second", error.Message);
            Assert.Empty(library.Search());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcesOrSidecarsChangedDuringSolvingCannotAcquireStaleSolutions(bool distributed)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, distributed ? "source.xish" : "source.xisf");
            string data = Path.Combine(directory, "source.xisb");
            var source = XisfImageData.FromSamples(Enumerable.Repeat(0.1f, 64 * 48).ToArray(), [64, 48], 1);
            if (distributed)
            { XisfWriter.WriteDistributed(file, data, [source]); }
            else
            { XisfWriter.Write(file, [source]); }
            var image = Assert.Single(SkyImageLoader.Load(file));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var progress = new SourceChangeProgress(() =>
            {
                string changed = distributed ? data : file;
                File.SetLastWriteTimeUtc(changed, File.GetLastWriteTimeUtc(changed).AddSeconds(3));
            });
            var error = await Assert.ThrowsAsync<IOException>(() => new AstapPlateSolver(library).SolveAsync(image,
                FakeOptions(), progress: progress));
            Assert.Contains("source changed", error.Message, StringComparison.OrdinalIgnoreCase);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            Assert.Null(Assert.Single(new CachedSkyImageLoader(cache) { Library = library }.LoadForBrowsing(file)).Placement);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class SourceChangeProgress(Action change) : IProgress<ImageProcessingProgress>
    {
        public void Report(ImageProcessingProgress update)
        {
            if (update.Stage.StartsWith("ASTAP blind solving", StringComparison.Ordinal))
            { change(); }
        }
    }

    [Fact]
    public void CommandLineIsBlindInvariantAndDoesNotEnableSourceUpdates()
    {
        var options = FakeOptions() with { AstapFieldHeightDegrees = 1.25, AstapDatabaseDirectory = "C:\\Test database" };
        var start = AstapPlateSolver.CreateStartInfo(options, "C:\\Test inputs\\image.fits", "C:\\Test outputs\\solution");
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Contains("180", start.ArgumentList);
        Assert.Contains("1.25", start.ArgumentList);
        Assert.Contains("C:\\Test database", start.ArgumentList);
        Assert.DoesNotContain("-update", start.ArgumentList);
        Assert.Contains("-sip", start.ArgumentList);
    }

    [Theory]
    [InlineData("PLTSOLVD=F")]
    [InlineData("PLTSOLVD=T\nCRPIX1=NaN")]
    [InlineData("PLTSOLVD=T\nCRPIX1=32.5\nCRPIX2=24.5\nCRVAL1=80\nCRVAL2=-5\nCD1_1=0\nCD1_2=0\nCD2_1=0\nCD2_2=0")]
    public void InvalidSolverReportsAreRejected(string report)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string ini = Path.Combine(directory, "solution.ini");
            File.WriteAllText(ini, report);
            Assert.Throws<InvalidDataException>(() => AstapPlateSolver.ReadSolution(ini, 64, 48));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
