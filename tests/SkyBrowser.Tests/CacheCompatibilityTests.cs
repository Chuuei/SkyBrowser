using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class CacheCompatibilityTests
{
    [Fact]
    public async Task DifferentCoreBuildReusesPreviewsWithoutScientificProcessing()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string source = Path.Combine(directory, "sample.xisf"), cachePath = Path.Combine(directory, "cache");
            XisfWriter.Write(source, [TestImages.Create(true)]);
            string hash;
            using (var cache = new ProcessedImageCache(cachePath))
            {
                var image = Assert.Single(new CachedSkyImageLoader(cache).LoadForBrowsing(source));
                Assert.False(image.IsFromCache);
                hash = Convert.ToHexString(SHA256.HashData(image.Preview.Bgra));
            }

            string root = FindRepository();
            string artifacts = Path.Combine(directory, "alternate-build");
            string configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
            // Compile the real probe/Core in another configuration and output path.
            // A normal process restart alone cannot catch compilation-specific cache keys.
            await RunAsync(root, "build", "tests/SkyBrowser.CacheProbe/SkyBrowser.CacheProbe.csproj",
                "-c", configuration, "--artifacts-path", artifacts, "--nologo", "--verbosity", "quiet",
                "--disable-build-servers", "-p:UseSharedCompilation=false");
            string probe = Path.Combine(artifacts, "bin", "SkyBrowser.CacheProbe", configuration.ToLowerInvariant(), "SkyBrowser.CacheProbe.dll");
            string output = await RunAsync(root, probe, cachePath, source);
            using var report = JsonDocument.Parse(output);
            var reopened = Assert.Single(report.RootElement.EnumerateArray());
            Assert.True(reopened.GetProperty("IsFromCache").GetBoolean());
            Assert.Equal(hash, reopened.GetProperty("Hash").GetString());
            Assert.NotEqual(typeof(SkyImageLoader).Assembly.ManifestModule.ModuleVersionId.ToString(),
                reopened.GetProperty("ProcessingBuildId").GetString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string FindRepository()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "SkyBrowser.slnx")))
                {
                    return current.FullName;
                }
            }
        }
        throw new DirectoryNotFoundException("The cache compatibility test needs the source checkout.");
    }

    private static async Task<string> RunAsync(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        // Reusable build workers can inherit redirected output handles and keep
        // ReadToEnd waiting after the build itself exits. Keep this child isolated.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            throw new TimeoutException("The isolated cache compatibility process did not finish within three minutes.");
        }
        string text = await output;
        Assert.True(process.ExitCode == 0, text + "\n" + await errors);
        return text;
    }
}
