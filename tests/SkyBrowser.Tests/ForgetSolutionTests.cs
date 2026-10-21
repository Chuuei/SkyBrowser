using System.IO;
using System.Security.Cryptography;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class ForgetSolutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgettingOneImagePersistsWithoutRemovingFileAstrometryPreviewsOrOrganization(bool hasFilePlacement)
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, "multi.xisf");
            var first = Source();
            if (hasFilePlacement)
            {
                XisfAstrometricSolution.CreateLinear("Gnomonic", [10, 20], [32, 24],
                    new double[,] { { -.01, 0 }, { 0, .01 } }).ApplyTo(first);
            }
            XisfWriter.Write(file, [first, Source()]);
            byte[] before = SHA256.HashData(File.ReadAllBytes(file));
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.AddFolder(directory);
            library.UpdateOrganization([new(file, 0)], new(true, "Favorite target", "Test group"));
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library, DefaultDisplayMode = DisplayMode.Linear };
            var original = loader.LoadForBrowsing(file);
            var solver = new AstapPlateSolver(library);
            foreach (var image in original)
            {
                await solver.SolveAsync(image, AstapTests.FakeOptions());
            }
            cache.SavePreference(file, 0, DisplayMode.Linear, SkyFinishSettings.Default);
            var solved = loader.LoadForBrowsing(file);
            Assert.All(solved, image => Assert.True(image.HasAstapSolution));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => library.ForgetSolution(solved[0], cancelled.Token));
            Assert.True(loader.LoadForBrowsing(file)[0].HasAstapSolution);

            int previews = cache.GetStatistics().PreviewCount;
            var restored = library.ForgetSolution(solved[0]);
            Assert.False(restored.HasAstapSolution);
            Assert.Same(solved[0].FilePlacement, restored.Placement);
            Assert.Same(solved[0].Preview, restored.Preview);
            Assert.Equal(hasFilePlacement, restored.Placement is not null);
            Assert.Equal(previews, cache.GetStatistics().PreviewCount);
            Assert.Single(cache.GetPreferences(file));
            Assert.Single(library.GetFolders());
            Assert.Equal(new ImageOrganization(true, "Favorite target", "Test group"), library.GetOrganization(file, 0));
            Assert.Equal(hasFilePlacement, library.Search().Single(image => image.ImageIndex == 0).HasPlacement);

            // Reopening the library must not reapply the forgotten solve from a warm
            // preview. A stale in-memory ASTAP image cannot export a forgotten solution.
            var reopened = new ImageLibrary(library.DatabasePath);
            var warmOnly = new CachedSkyImageLoader(cache, (_, _, _, _, _, _) =>
                throw new InvalidOperationException("Forgetting a solve must preserve usable previews."))
            { Library = reopened, DefaultDisplayMode = DisplayMode.Linear };
            var reloaded = warmOnly.LoadForBrowsing(file);
            Assert.All(reloaded, image => Assert.True(image.IsFromCache));
            Assert.False(reloaded[0].HasAstapSolution);
            Assert.Equal(hasFilePlacement, reloaded[0].Placement is not null);
            Assert.True(reloaded[1].HasAstapSolution);
            Assert.Throws<InvalidOperationException>(() => new SolvedImageExporter(reopened)
                .Export(solved[0], Path.Combine(directory, "forgotten.xisf")));
            if (hasFilePlacement)
            {
                Assert.InRange(original[0].Placement!.At(.23, .61).DistanceTo(reloaded[0].Placement!.At(.23, .61)), 0, 1e-4);
            }
            cache.ClearPreviews();
            var cold = new CachedSkyImageLoader(cache) { Library = reopened }.LoadForBrowsing(file);
            Assert.False(cold[0].HasAstapSolution);
            Assert.Equal(hasFilePlacement, cold[0].Placement is not null);
            Assert.True(cold[1].HasAstapSolution);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(file)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task UnavailableSourceCanBeForgottenWithoutReopeningIt()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(directory, "missing.xisf");
            XisfWriter.Write(file, [Source()]);
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            var original = Assert.Single(loader.LoadForBrowsing(file));
            await new AstapPlateSolver(library).SolveAsync(original, AstapTests.FakeOptions());
            var solved = Assert.Single(loader.LoadForBrowsing(file));
            Assert.True(solved.HasAstapSolution);
            File.Delete(file);
            var restored = library.ForgetSolution(solved);
            Assert.False(restored.HasAstapSolution);
            Assert.Null(restored.Placement);
            Assert.Same(solved.Preview, restored.Preview);
            Assert.False(Assert.Single(library.Search()).HasPlacement);
            // Repeated deletion remains harmless, including with a stale scene snapshot.
            Assert.False(library.ForgetSolution(solved).HasAstapSolution);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RepeatedSolvingRetainsTheOriginalFilePlacementAndWarning()
    {
        var filePlacement = ImagePlacement.FromManual(new(10, 20, 2, 0), 2, 2);
        var astap = ImagePlacement.FromManual(new(30, 40, 2, 0), 2, 2);
        var image = new LoadedSkyImage("source.fits", 0, "Source", 2, 2, new(2, 2, new byte[16]),
            "Linear", filePlacement, "File warning", "Metadata");
        var solved = image.WithAstapSolution(astap, "Solver warning").WithAstapSolution(astap);
        Assert.True(solved.HasAstapSolution);
        Assert.Same(filePlacement, solved.FilePlacement);
        var restored = solved.WithoutAstapSolution();
        Assert.False(restored.HasAstapSolution);
        Assert.Same(filePlacement, restored.Placement);
        Assert.Equal("File warning", restored.PlacementWarning);
        Assert.Same(image.Preview, restored.Preview);
        Assert.Same(restored, restored.WithoutAstapSolution());
    }

    private static XisfImageData Source() => XisfImageData.FromSamples(Enumerable.Repeat(.1f, 64 * 48).ToArray(), [64, 48], 1);
}
