using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;

if (args is ["--performance", var performanceSource, var performanceReport])
{
    return PerformanceReview.Run(performanceSource, performanceReport);
}

if (args is ["--options", var settingsPath])
{
    Console.WriteLine(JsonSerializer.Serialize(new ApplicationOptionsStore(settingsPath).Read()));
    return 0;
}

if (args is ["--compare-stretches", var sourcePath, var comparisonPath])
{
    List<object> measurements = [];
    foreach (var mode in new[]
    {
        DisplayMode.Automatic,
        DisplayMode.Finished
    }

    )
    {
        var timer = Stopwatch.StartNew();
        var comparisonImages = SkyImageLoader.Load(sourcePath, mode);
        timer.Stop();
        measurements.Add(new
        {
            Mode = mode.ToString(),
            Milliseconds = timer.ElapsedMilliseconds,
            Images = comparisonImages.Select(image => new
            {
                image.ImageIndex,
                image.DisplayDescription,
                image.Preview.Width,
                image.Preview.Height,
                Hash = Convert.ToHexString(SHA256.HashData(image.Preview.Bgra))
            })
            .ToArray()
        });
    }

    string json = JsonSerializer.Serialize(
        new { File = Path.GetFullPath(sourcePath), Measurements = measurements },
        new JsonSerializerOptions { WriteIndented = true }
    );
    File.WriteAllText(comparisonPath, json);
    Console.WriteLine(json);
    return 0;
}

// Each phase is run in its own process. The warm phase fails if scientific processing runs.
if (args is ["--measure", var phase, var cacheDirectory, var sourceDirectory, var reportPath] &&
    phase is "cold" or "warm")
{
    using var measuredCache = new ProcessedImageCache(cacheDirectory);
    var measuredLoader = phase == "cold"
        ? new CachedSkyImageLoader(measuredCache)
        : new CachedSkyImageLoader(
            measuredCache,
            (_, _, _, _, _, _) => throw new InvalidOperationException("Scientific processing ran during the warm phase.")
        );
    List<object> files = [];
    var total = Stopwatch.StartNew();
    foreach (string path in Directory.EnumerateFiles(sourceDirectory).Where(SkyImageLoader.IsSupported).Order())
    {
        var timer = Stopwatch.StartNew();
        var loaded = measuredLoader.LoadForBrowsing(path);
        long milliseconds = timer.ElapsedMilliseconds;
        if (loaded.Any(image => image.IsFromCache != (phase == "warm")))
        {
            throw new InvalidOperationException("Unexpected cache state; use a fresh directory for the cold phase.");
        }

        files.Add(new
        {
            File = Path.GetFileName(path),
            Milliseconds = milliseconds,
            Images = loaded.Select(image => new
            {
                image.IsFromCache,
                image.ImageIndex,
                image.Preview.Width,
                image.Preview.Height,
                Hash = Convert.ToHexString(SHA256.HashData(image.Preview.Bgra))
            })
            .ToArray()
        });
    }

    var result = new
    {
        Phase = phase,
        Milliseconds = total.ElapsedMilliseconds,
        Files = files,
        Statistics = measuredCache.GetStatistics()
    };
    string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(reportPath, json);
    Console.WriteLine(json);
    return 0;
}

if (args.Length != 2)
{
    return 2;
}

using var cache = new ProcessedImageCache(args[0]);
var loader = new CachedSkyImageLoader(cache, (_, _, _, _, _, _) => throw new InvalidOperationException("Scientific processing ran after restart."));
var images = loader.LoadForBrowsing(args[1]);
Console.WriteLine(JsonSerializer.Serialize(images.Select(image => new
{
    image.IsFromCache,
    image.ImageIndex,
    image.SourceImageCount,
    Mode = image.RenderMode.ToString(),
    image.FinishSettings.TargetBackground,
    Hash = Convert.ToHexString(SHA256.HashData(image.Preview.Bgra)),
    Ra = image.Placement?.Center.RightAscension,
    Dec = image.Placement?.Center.Declination,
    image.Details
})));
return images.All(image => image.IsFromCache) ? 0 : 1;
