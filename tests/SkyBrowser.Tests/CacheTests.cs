using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class CacheTests
{
    [Fact]
    public void LargeMetadataIsBoundedAndRetainedWithCachedPreviews()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var source = TestImages.Create();
            source.Properties.Set("Processing:History", new string('x', 100000));
            source.Properties.Set("Processing:Samples", Enumerable.Range(0, 100).Select(i => (double)i).ToArray());
            string path = Path.Combine(dir, "metadata.xisf");
            XisfWriter.Write(path, [source]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            var image = Assert.Single(new CachedSkyImageLoader(cache).LoadForBrowsing(path));
            Assert.True(image.Details.IsTruncated);
            Assert.Contains(image.Details.Entries, entry => entry.Name == "Processing:History" && entry.Value.Length < 2100);
            Assert.Contains(image.Details.Entries, entry => entry.Name == "Processing:Samples" && entry.Value.Contains("100 elements"));
            var hit = Assert.Single(new CachedSkyImageLoader(cache, (_, _, _, _, _, _) => throw new InvalidOperationException("Metadata was not cached.")).LoadForBrowsing(path));
            Assert.True(hit.IsFromCache);
            Assert.True(hit.Details.IsTruncated);
            Assert.Equal(image.Details.Entries, hit.Details.Entries);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ChangingCacheFoldersMergesPreviewsPreferencesAndHistory()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string first = Path.Combine(dir, "first.xisf"), second = Path.Combine(dir, "second.xisf");
            foreach (string file in new[]
            {
                first,
                second
            }

            )
            {
                XisfWriter.Write(file, [TestImages.Create()]);
            }

            using var source = new ProcessedImageCache(Path.Combine(dir, "source"));
            using var target = new ProcessedImageCache(Path.Combine(dir, "target"));
            var original = Assert.Single(new CachedSkyImageLoader(source).LoadForBrowsing(first));
            new CachedSkyImageLoader(target).LoadForBrowsing(second);
            source.SavePreference(first, 0, DisplayMode.Linear, SkyFinishSettings.Default);
            target.SavePreference(first, 0, DisplayMode.AutoStretch, SkyFinishSettings.Default);
            target.SavePreference(second, 0, DisplayMode.Finished, SkyFinishSettings.Default);
            source.RememberFolder(Path.Combine(dir, "a"));
            target.RememberFolder(Path.Combine(dir, "b"));
            Assert.True(source.CopyTo(target));
            Assert.Equal(2, target.GetStatistics().PreviewCount);
            Assert.Equal(1, source.GetStatistics().PreviewCount);
            Assert.Equal(DisplayMode.Linear, target.GetPreferences(first)[0].Mode);
            Assert.Equal(DisplayMode.Finished, target.GetPreferences(second)[0].Mode);
            Assert.Equal(2, target.GetRecentFolders().Length);
            var hits = new CachedSkyImageLoader(target, (_, _, _, _, _, _) => throw new InvalidOperationException("Cache migration lost a preview."));
            var copied = Assert.Single(hits.Load(first, DisplayMode.Automatic, SkyFinishSettings.Default));
            Assert.True(copied.IsFromCache);
            Assert.Equal(original.Preview.Bgra, copied.Preview.Bgra);
            Assert.True(Assert.Single(hits.Load(second, DisplayMode.Automatic, SkyFinishSettings.Default)).IsFromCache);
            Assert.True(source.CopyTo(target));
            Assert.Equal(2, target.GetStatistics().PreviewCount);
            using var smaller = new ProcessedImageCache(Path.Combine(dir, "target"), 1);
            smaller.ApplySizeLimit();
            Assert.Equal(0, smaller.GetStatistics().PreviewCount);
            Assert.Single(smaller.GetPreferences(first));
            Assert.Equal(2, smaller.GetRecentFolders().Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AnUnavailableSourceCacheCanBeReplacedButAnInvalidDestinationFails()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string blocked = Path.Combine(dir, "blocked");
            File.WriteAllText(blocked, "occupied");
            using var unavailable = new ProcessedImageCache(blocked);
            using var target = new ProcessedImageCache(Path.Combine(dir, "target"));
            Assert.False(unavailable.CopyTo(target));
            Assert.NotNull(unavailable.LastError);
            Assert.True(File.Exists(target.DatabasePath));
            Assert.ThrowsAny<IOException>(() => target.CopyTo(unavailable));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BrowsingUsesGlobalDefaultsUntilAnImageHasItsOwnPreference()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "multi.xisf");
            XisfWriter.Write(file, [TestImages.Create(true), TestImages.Create()]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            var finish = SkyFinishSettings.Default with
            {
                TargetBackground = 0.06
            };
            var loader = new CachedSkyImageLoader(cache)
            {
                DefaultDisplayMode = DisplayMode.Linear,
                DefaultFinishSettings = finish
            };
            Assert.All(loader.LoadForBrowsing(file), image =>
            {
                Assert.Equal(DisplayMode.Linear, image.RenderMode);
                Assert.Equal(finish, image.FinishSettings);
            });
            cache.SavePreference(file, 1, DisplayMode.StoredDisplay, SkyFinishSettings.Default);
            loader.DefaultDisplayMode = DisplayMode.Finished;
            var browsing = loader.LoadForBrowsing(file);
            Assert.Equal(DisplayMode.Finished, browsing[0].RenderMode);
            Assert.Equal(finish, browsing[0].FinishSettings);
            Assert.Equal(DisplayMode.StoredDisplay, browsing[1].RenderMode);
            Assert.Equal(SkyFinishSettings.Default, browsing[1].FinishSettings);
            Assert.All(loader.LoadForBrowsing(file), image => Assert.True(image.IsFromCache));
            Assert.All(loader.Load(file, DisplayMode.Linear, finish), image => Assert.True(image.IsFromCache));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task NewProgramProcessReusesFinishedPixelsAndPreferencesWithoutProcessing()
    {
        string dir = TestImages.NewDirectory(), file = Path.Combine(dir, "sample.xisf"), cachePath = Path.Combine(dir, "cache");
        try
        {
            XisfWriter.Write(file, [TestImages.Create(true)]);
            string hash;
            using (var firstRun = new ProcessedImageCache(cachePath))
            {
                var settings = new SkyFinishSettings
                {
                    TargetBackground = 0.07,
                    ReduceNoise = false
                };
                var loader = new CachedSkyImageLoader(firstRun);
                var image = Assert.Single(loader.Load(file, DisplayMode.Finished, settings));
                firstRun.SavePreference(file, 0, DisplayMode.Finished, settings);
                firstRun.RememberFolder(dir);
                Assert.False(image.IsFromCache);
                hash = Convert.ToHexString(SHA256.HashData(image.Preview.Bgra));
            }

            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
            string probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "SkyBrowser.CacheProbe", configuration, "SkyBrowser.CacheProbe.dll"));
            Assert.True(File.Exists(probe), probe);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(probe);
            start.ArgumentList.Add(cachePath);
            start.ArgumentList.Add(file);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await errors);
            using var report = JsonDocument.Parse(await output);
            var reopened = Assert.Single(report.RootElement.EnumerateArray());
            Assert.True(reopened.GetProperty("IsFromCache").GetBoolean());
            Assert.Equal(hash, reopened.GetProperty("Hash").GetString());
            Assert.Equal("Finished", reopened.GetProperty("Mode").GetString());
            Assert.Equal(0.07, reopened.GetProperty("TargetBackground").GetDouble());
            Assert.Equal(83.822, reopened.GetProperty("Ra").GetDouble(), 7);
            Assert.Contains(
                reopened.GetProperty("Details")
                .GetProperty("Entries")
                .EnumerateArray(),
                entry => entry.GetProperty("Name")
                .GetString() == "OBJECT" &&
                entry.GetProperty("Value")
                .GetString()!.Contains("Synthetic alignment fixture")
            );
            using var nextRun = new ProcessedImageCache(cachePath);
            Assert.Equal(dir, Assert.Single(nextRun.GetRecentFolders()).Path);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileAndSettingsChangesInvalidateOnlyTheAppropriatePreview()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "sample.xisf");
            XisfWriter.Write(file, [TestImages.Create(true)]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            int calls = 0;
            var loader = CountingLoader(cache, () => calls++);
            var original = Assert.Single(loader.Load(file, DisplayMode.Automatic, SkyFinishSettings.Default));
            var warm = Assert.Single(loader.Load(file, DisplayMode.Automatic, SkyFinishSettings.Default));
            Assert.True(warm.IsFromCache);
            Assert.Equal(1, calls);
            Assert.Equal(original.Preview.Bgra, warm.Preview.Bgra);
            Assert.Same(original.FinishSettings, SkyFinishSettings.Default);
            for (int y = 0; y <= 32; y++)
            {
                for (int x = 0; x <= 32; x++)
                {
                    Assert.True(original.Placement!.At(x / 32d, y / 32d).DistanceTo(warm.Placement!.At(x / 32d, y / 32d)) < 1e-10);
                }
            }

            loader.Load(file, DisplayMode.Automatic, SkyFinishSettings.Default with { TargetBackground = 0.06 });
            Assert.Equal(2, calls);
            Assert.True(Assert.Single(loader.Load(file, DisplayMode.Automatic, SkyFinishSettings.Default)).IsFromCache);
            XisfWriter.Write(file, [TestImages.Create()]);
            var changed = Assert.Single(loader.Load(file, DisplayMode.Automatic, SkyFinishSettings.Default));
            Assert.False(changed.IsFromCache);
            Assert.Equal(3, calls);
            Assert.NotEqual(original.Preview.Bgra, changed.Preview.Bgra);
            Assert.Equal(1, cache.GetStatistics().PreviewCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SameLengthAndTimestampEditsAreDetectedByTheContentSample()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "sample.xisf");
            XisfWriter.Write(file, [TestImages.Create()], new()
            {
                Compression = XisfCompression.None,
                Checksum = XisfChecksum.None
            });
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            int calls = 0;
            var loader = CountingLoader(cache, () => calls++);
            loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default);
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            byte[] bytes = File.ReadAllBytes(file);
            bytes[^1] ^= 1;
            File.WriteAllBytes(file, bytes);
            File.SetLastWriteTimeUtc(file, stamp);
            Assert.False(Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
            Assert.Equal(2, calls);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DistributedXisfSidecarChangesInvalidateThePreview()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string header = Path.Combine(dir, "sample.xish"), data = Path.Combine(dir, "sample.xisb");
            XisfWriter.WriteDistributed(header, data, [TestImages.Create()]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            int calls = 0;
            var loader = CountingLoader(cache, () => calls++);
            loader.Load(header, DisplayMode.Linear, SkyFinishSettings.Default);
            Assert.True(Assert.Single(loader.Load(header, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
            DateTime headerTime = File.GetLastWriteTimeUtc(header);
            File.SetLastWriteTimeUtc(data, File.GetLastWriteTimeUtc(data).AddSeconds(3));
            Assert.False(Assert.Single(loader.Load(header, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
            Assert.Equal(headerTime, File.GetLastWriteTimeUtc(header));
            Assert.Equal(2, calls);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MultipleImageVariantsDoNotMakeAPartialFileLookComplete()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "multi.xisf");
            XisfWriter.Write(file, [TestImages.Create(true), TestImages.Create()]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            int calls = 0;
            var loader = CountingLoader(cache, () => calls++);
            Assert.Equal(1, Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default, onlyImage: 1))
                .ImageIndex);
            Assert.Equal(2, loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default).Count);
            Assert.Equal(2, calls);
            Assert.All(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default), image => Assert.True(image.IsFromCache));
            cache.SavePreference(file, 1, DisplayMode.Linear, SkyFinishSettings.Default);
            var browsing = loader.LoadForBrowsing(file);
            Assert.Equal(2, browsing.Count);
            Assert.Equal(DisplayMode.Linear, browsing[1].RenderMode);
            Assert.Equal(3, calls); // Only the missing automatic variant for image zero was processed.
            Assert.All(loader.LoadForBrowsing(file), image => Assert.True(image.IsFromCache));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CorruptPreviewAndUnavailableCacheFallBackToProcessing()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "sample.xisf");
            XisfWriter.Write(file, [TestImages.Create()]);
            using (var cache = new ProcessedImageCache(Path.Combine(dir, "cache")))
            {
                int calls = 0;
                var loader = CountingLoader(cache, () => calls++);
                loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default);
                using (var connection = new SqliteConnection($"Data Source={cache.DatabasePath};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE previews SET pixels=X'0001'";
                    command.ExecuteNonQuery();
                }

                Assert.False(Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
                Assert.Equal(2, calls);
                Assert.True(Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
                using (var connection = new SqliteConnection($"Data Source={cache.DatabasePath};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE previews SET source_json='{\"Files\":null}'";
                    command.ExecuteNonQuery();
                }

                Assert.False(Assert.Single(loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default)).IsFromCache);
                Assert.Equal(3, calls);
            }

            string unavailable = Path.Combine(dir, "not-a-directory");
            File.WriteAllText(unavailable, "occupied");
            using var failed = new ProcessedImageCache(unavailable);
            Assert.False(Assert.Single(new CachedSkyImageLoader(failed).LoadForBrowsing(file)).IsFromCache);
            Assert.NotNull(failed.LastError);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EvictionKeepsRecentPreviewsAndClearKeepsPreferences()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string first = Path.Combine(dir, "one.xisf"), second = Path.Combine(dir, "two.xisf"), third = Path.Combine(dir, "three.xisf");
            foreach (string file in new[]
            {
                first,
                second,
                third
            }

            )
            {
                XisfWriter.Write(file, [TestImages.Create()]);
            }

            long size;
            using (var measure = new ProcessedImageCache(Path.Combine(dir, "measure")))
            {
                new CachedSkyImageLoader(measure).LoadForBrowsing(first);
                size = measure.GetStatistics().StoredBytes;
            }

            using var cache = new ProcessedImageCache(Path.Combine(dir, "small"), size * 2 + 500);
            var loader = new CachedSkyImageLoader(cache);
            loader.LoadForBrowsing(first);
            loader.LoadForBrowsing(second);
            loader.LoadForBrowsing(first);
            loader.LoadForBrowsing(third);
            Assert.True(cache.HasPreview(first));
            Assert.False(cache.HasPreview(second));
            Assert.True(cache.HasPreview(third));
            Assert.True(cache.GetStatistics().StoredBytes <= cache.LimitBytes);
            cache.SavePreference(first, 0, DisplayMode.Linear, SkyFinishSettings.Default);
            cache.RememberFolder(dir);
            cache.ClearPreviews();
            Assert.Equal(0, cache.GetStatistics().PreviewCount);
            Assert.Single(cache.GetPreferences(first));
            Assert.Single(cache.GetRecentFolders());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CancelledProcessingDoesNotPublishAPartialCacheEntry()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "sample.xisf");
            XisfWriter.Write(file, [TestImages.Create()]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            using var cancellation = new CancellationTokenSource();
            var loader = new CachedSkyImageLoader(cache, (path, mode, settings, token, index, progress) =>
            {
                var images = SkyImageLoader.Load(path, mode, settings, token, index);
                cancellation.Cancel();
                return images;
            });
            Assert.Throws<OperationCanceledException>(() => loader.Load(file, DisplayMode.Linear, SkyFinishSettings.Default, cancellation.Token));
            Assert.Equal(0, cache.GetStatistics().PreviewCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static CachedSkyImageLoader CountingLoader(ProcessedImageCache cache, Action processed) => new(cache, (path, mode, settings, token, index, progress) =>
    {
        processed();
        return SkyImageLoader.Load(path, mode, settings, token, index, progress);
    });

    [Fact]
    public async Task CachedFolderLoadsWhileAnUnrelatedColdDecodeIsStillRunning()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string warmFile = Path.Combine(dir, "warm.xisf"), coldFile = Path.Combine(dir, "cold.xisf");
            foreach (string path in new[]
            {
                warmFile,
                coldFile
            }

            )
            {
                XisfWriter.Write(path, [TestImages.Create()]);
            }

            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            new CachedSkyImageLoader(cache).LoadForBrowsing(warmFile);
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var loader = new CachedSkyImageLoader(cache, (path, mode, settings, token, index, progress) =>
            {
                started.Set();
                release.Wait(token);
                return SkyImageLoader.Load(path, mode, settings, token, index, progress);
            });
            var cold = Task.Run(() => loader.LoadForBrowsing(coldFile));
            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                var warm = await Task.Run(() => loader.LoadForBrowsing(warmFile)).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(Assert.Single(warm).IsFromCache);
                Assert.False(cold.IsCompleted);
            }
            finally
            {
                release.Set();
                await cold;
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SourceChangesDuringProcessingAreNotCached()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            string file = Path.Combine(dir, "sample.xisf");
            XisfWriter.Write(file, [TestImages.Create()]);
            using var cache = new ProcessedImageCache(Path.Combine(dir, "cache"));
            var loader = new CachedSkyImageLoader(cache, (path, mode, settings, token, index, progress) =>
            {
                var images = SkyImageLoader.Load(path, mode, settings, token, index, progress);
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(10));
                return images;
            });
            loader.LoadForBrowsing(file);
            Assert.Equal(0, cache.GetStatistics().PreviewCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
