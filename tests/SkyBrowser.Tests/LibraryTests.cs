using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class LibraryTests
{
    [Fact]
    public void FolderListsCommitAtomicallyAndDiscoveryDoesNotDecodeOrDuplicateImages()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string root = Path.Combine(directory, "archive"), nested = Path.Combine(root, "nested");
            string sibling = Path.Combine(directory, "archive-other");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(sibling);
            string file = Path.Combine(nested, "not-yet-readable.fits");
            File.WriteAllText(file, "Discovery must not decode this file.");
            File.WriteAllText(Path.Combine(nested, "ignored.txt"), "Not supported");
            string missing = Path.Combine(directory, "offline");
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.SetFolders([root, nested, root.ToUpperInvariant() + Path.DirectorySeparatorChar, missing]);
            Assert.Equal(3, new ImageLibrary(library.DatabasePath).GetFolders().Length);
            Assert.Equal(file, Assert.Single(ImageLibrary.FindFolderImages(library.GetFolders())), ignoreCase: true);
            Assert.Empty(library.Search());
            Assert.True(ImageLibrary.ContainsSource(root, file));
            Assert.False(ImageLibrary.ContainsSource(root, Path.Combine(sibling, "image.fits")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => library.SetFolders([], cancellation.Token));
            Assert.Equal(3, library.GetFolders().Length);
            Assert.ThrowsAny<OperationCanceledException>(() => ImageLibrary.FindFolderImages([root], cancellation.Token));
            library.SetFolders([]);
            Assert.Empty(new ImageLibrary(library.DatabasePath).GetFolders());
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ArchivesScanRecursivelyPersistAndSearchWithoutDecodingAgain()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string root = Path.Combine(directory, "archive"), nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            var image = TestImages.Create();
            image.Properties.Set("Instrument:Telescope:Name", "Téléscope Alpha");
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("FILTER", "H-alpha"));
            string file = Path.Combine(nested, "IC_434.xisf");
            XisfWriter.Write(file, [image, image]);
            File.WriteAllText(Path.Combine(root, "ignored.txt"), "Not an image");
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.AddFolder(root);
            library.AddFolder(nested); // Overlapping roots must not duplicate records.
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            await library.ScanAsync(loader);
            Assert.Equal(2, library.Search().Count);
            Assert.Equal(2, library.Search("ic434 telescope h-alpha").Count);
            Assert.Empty(library.Search("not-present"));
            Assert.All(library.Search(), entry => Assert.True(entry.HasPlacement));
            cache.ClearPreviews();
            var reopened = new ImageLibrary(library.DatabasePath);
            var noDecode = new CachedSkyImageLoader(cache, (_, _, _, _, _, _) =>
                throw new InvalidOperationException("Unchanged library entries must not decode."));
            await reopened.ScanAsync(noDecode);
            Assert.Equal(2, reopened.Search("IC 434").Count);
            Assert.Equal(2, reopened.GetFolders().Length);

            var start = Probe("--library", library.DatabasePath, "ic434 h-alpha");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await errors);
            Assert.Equal(2, JsonSerializer.Deserialize<LibraryImage[]>(await output)!.Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RescansDetectNewChangedMissingAndRestoredFilesWithoutLosingMetadata()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string root = Path.Combine(directory, "archive");
            Directory.CreateDirectory(root);
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.AddFolder(root);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            await library.ScanAsync(loader);
            Assert.Empty(library.Search());
            string file = Path.Combine(root, "image.xisf");
            var image = TestImages.Create();
            XisfWriter.Write(file, [image, image]);
            await library.ScanAsync(loader);
            Assert.Equal(2, library.Search().Count);
            image.FitsKeywords.Clear();
            image.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "Updated target"));
            XisfWriter.Write(file, [image]);
            await library.ScanAsync(loader);
            Assert.Equal("Updated target", Assert.Single(library.Search()).Name);
            Assert.False(Assert.Single(library.Search()).HasPlacement);
            File.Delete(file);
            await library.ScanAsync(loader);
            var missing = Assert.Single(library.Search("Updated target"));
            Assert.True(missing.Missing);
            Assert.Equal(64, missing.Width);
            XisfWriter.Write(file, [image]);
            await library.ScanAsync(loader);
            Assert.False(Assert.Single(library.Search()).Missing);
            Assert.Null(Assert.Single(library.Search()).Error);
            library.RemoveFolder(root);
            Assert.Empty(library.GetFolders());
            Assert.Single(library.Search());
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BackgroundMonitorIndexesRequestedChangesAndStopsCleanly()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string root = Path.Combine(directory, "archive");
            Directory.CreateDirectory(root);
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.AddFolder(root);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            using var monitor = new ImageLibraryMonitor(library, () => loader);
            try
            {
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                monitor.Changed += (_, _) =>
                {
                    if (library.Search().Count == 1)
                    { changed.TrySetResult(); }
                };
                XisfWriter.Write(Path.Combine(root, "new.xisf"), [TestImages.Create()]);
                monitor.RequestScan();
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Null(monitor.LastError);
            }
            finally
            {
                monitor.Dispose();
                await monitor.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BadImagesAreReportedAndCancellationRetainsCompletedEntries()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string root = Path.Combine(directory, "archive");
            Directory.CreateDirectory(root);
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            library.AddFolder(root);
            using var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
            var loader = new CachedSkyImageLoader(cache) { Library = library };
            XisfWriter.Write(Path.Combine(root, "a.xisf"), [TestImages.Create()]);
            File.WriteAllText(Path.Combine(root, "b.xisf"), "broken");
            await library.ScanAsync(loader);
            Assert.Equal(2, library.Search().Count);
            Assert.Single(library.Search(), entry => entry.Error is not null);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ScanAsync(loader, token: cancellation.Token));
            Assert.Equal(2, library.Search().Count);
            XisfWriter.Write(Path.Combine(root, "b.xisf"), [TestImages.Create()]);
            await library.ScanAsync(loader);
            Assert.All(library.Search(), entry => Assert.Null(entry.Error));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    internal static ProcessStartInfo Probe(params string[] arguments)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "SkyBrowser.CacheProbe", configuration, "SkyBrowser.CacheProbe.dll"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(probe);
        foreach (string argument in arguments)
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
