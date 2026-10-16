using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Tests;

public sealed class OptionsTests
{
    [Fact]
    public async Task CompleteOptionsSurviveANewProgramProcess()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var store = new ApplicationOptionsStore(Path.Combine(dir, "settings.json"));
            Assert.True(store.NeedsFirstRunSetup);
            Assert.Equal(10, store.Read().CacheSizeGb);
            Assert.True(store.NeedsFirstRunSetup); // Reading defaults does not complete setup.
            Assert.Equal(10L * 1024 * 1024 * 1024, ProcessedImageCache.DefaultLimitBytes);
            var options = new ApplicationOptions
            {
                CacheDirectory = Path.Combine(dir, "previews"),
                CacheSizeGb = 1.5,
                DefaultDisplayMode = DisplayMode.Finished,
                DefaultFinishSettings = new()
                {
                    TargetBackground = 0.07,
                    BalanceBackground = false,
                    ReduceNoise = false
                },
                Observer = new(new(52.52, 13.405), false, new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero)),
                ShowGrid = false,
                ShowLabels = false,
                ShowConstellations = false,
                ShowOutlines = false,
                BlendOverlaps = false
            };
            store.Write(options);
            Assert.False(new ApplicationOptionsStore(store.FilePath).NeedsFirstRunSetup);
            Assert.Equal(options, new ApplicationOptionsStore(store.FilePath).Read());
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
            start.ArgumentList.Add("--options");
            start.ArgumentList.Add(store.FilePath);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await errors);
            Assert.Equal(options, JsonSerializer.Deserialize<ApplicationOptions>(await output));
            Assert.DoesNotContain("CacheLimitBytes", File.ReadAllText(store.FilePath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CorruptOptionsRecoverTheLastValidBackup()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var store = new ApplicationOptionsStore(Path.Combine(dir, "settings.json"));
            var first = new ApplicationOptions
            {
                CacheDirectory = Path.Combine(dir, "cache"),
                CacheSizeGb = 3
            };
            store.Write(first);
            store.Write(first with { CacheSizeGb = 4 });
            File.Delete(store.FilePath);
            Assert.False(store.NeedsFirstRunSetup); // An existing user's backup must not trigger onboarding.
            Assert.Equal(first, store.Read());
            File.WriteAllText(store.FilePath, "{broken");
            Assert.Equal(first, store.Read());
            Assert.Contains("Recovered", store.LastError);
            store.Write(first with { CacheSizeGb = 5 });
            File.WriteAllText(store.FilePath, "{broken again");
            Assert.Equal(first, store.Read()); // Saving after corruption must preserve the good backup.
            File.WriteAllText(store.FilePath + ".bak", "null");
            Assert.Equal(store.Defaults, store.Read());
            Assert.NotNull(store.LastError);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LegacyLocationMigratesAndClearingItDoesNotResurrectIt()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var defaults = new ApplicationOptions
            {
                CacheDirectory = Path.Combine(dir, "cache")
            };
            Directory.CreateDirectory(defaults.CacheDirectory);
            string legacy = Path.Combine(defaults.CacheDirectory, "observer-location.json");
            File.WriteAllText(legacy, JsonSerializer.Serialize(new ObserverLocation(-33.9, 151.2)));
            var store = new ApplicationOptionsStore(Path.Combine(dir, "settings.json"), defaults);
            var migrated = store.Read();
            Assert.Equal(new(-33.9, 151.2), migrated.Observer!.Location);
            Assert.True(migrated.Observer.UseSystemTime);
            store.Write(migrated with { Observer = null, CacheDirectory = Path.Combine(dir, "new-cache") });
            var restarted = new ApplicationOptionsStore(store.FilePath, defaults).Read();
            Assert.Null(restarted.Observer);
            Assert.Equal(Path.Combine(dir, "new-cache"), restarted.CacheDirectory);
            Assert.True(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void InvalidOptionsAreRejectedWithoutReplacingSavedSettings()
    {
        string dir = TestImages.NewDirectory();
        try
        {
            var store = new ApplicationOptionsStore(Path.Combine(dir, "settings.json"));
            var saved = new ApplicationOptions
            {
                CacheDirectory = dir
            };
            store.Write(saved);
            ApplicationOptions[] invalid = [saved with
            {
                CacheSizeGb = 0
            }, saved with
            {
                CacheSizeGb = double.NaN
            }, saved with
            {
                CacheSizeGb = 10001
            }, saved with
            {
                CacheDirectory = "relative"
            }, saved with
            {
                Version = 2
            }, saved with
            {
                DefaultDisplayMode = (DisplayMode)99
            }, saved with
            {
                DefaultFinishSettings = new()
                {
                    TargetBackground = 0.99
                }
            }, saved with
            {
                Observer = new(new(91, 0), true, DateTimeOffset.UtcNow)
            }

            ];
            foreach (var options in invalid)
            {
                Assert.ThrowsAny<ArgumentException>(() => store.Write(options));
                Assert.Equal(saved, store.Read());
            }

            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
