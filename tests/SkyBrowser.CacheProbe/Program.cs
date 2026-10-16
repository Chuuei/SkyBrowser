using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Core.Astrometry;

if (args.Length > 0 && args[0] == "-f")
{
    return await FakeAstap.RunAsync(args);
}

if (args is ["--astap-solve", var solveSource, var astapExecutable, var solveDirectory])
{
    Directory.CreateDirectory(solveDirectory);
    byte[] before = SHA256.HashData(File.ReadAllBytes(solveSource));
    var library = new ImageLibrary(Path.Combine(solveDirectory, "library.sqlite3"));
    var image = SkyImageLoader.Load(solveSource, DisplayMode.Linear)[0];
    var timer = Stopwatch.StartNew();
    var result = await new AstapPlateSolver(library).SolveAsync(image, new()
    {
        AstapExecutablePath = astapExecutable,
        AstapTimeoutSeconds = 300
    }, progress: new SolverProgress());
    var scale = result.Placement.PixelScale(image.Width, image.Height);
    var report = new
    {
        image.Width,
        image.Height,
        Milliseconds = timer.ElapsedMilliseconds,
        Center = new { result.Placement.Center.RightAscension, result.Placement.Center.Declination },
        result.Placement.AngularWidth,
        result.Placement.AngularHeight,
        PixelScaleXArcseconds = scale.X,
        PixelScaleYArcseconds = scale.Y,
        result.Warning,
        ReferenceErrorDegrees = image.Placement?.Center.DistanceTo(result.Placement.Center),
        SourceUnchanged = before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(solveSource)))
    };
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(solveDirectory, "result.json"), json);
    Console.WriteLine(json);
    return report.SourceUnchanged ? 0 : 1;
}

if (args is ["--export-solved", var exportSource, var exportLibrary, var exportDestination])
{
    using var sourceStream = File.OpenRead(exportSource);
    byte[] originalHash = SHA256.HashData(sourceStream);
    sourceStream.Close();
    var image = SkyImageLoader.Load(exportSource, DisplayMode.Linear)[0];
    var result = new SolvedImageExporter(new ImageLibrary(exportLibrary)).Export(image, exportDestination, progress: new SolverProgress());
    var reopened = SkyImageLoader.Load(result.Path, DisplayMode.Linear)[0];
    using var finalSource = File.OpenRead(exportSource);
    bool unchanged = originalHash.SequenceEqual(SHA256.HashData(finalSource));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        reopened.Width,
        reopened.Height,
        reopened.Placement?.AngularWidth,
        reopened.Placement?.AngularHeight,
        PixelScale = reopened.Placement?.PixelScale(reopened.Width, reopened.Height),
        reopened.PlacementWarning,
        result.Warning,
        SourceUnchanged = unchanged
    }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    return unchanged && reopened.Placement is not null && reopened.PlacementWarning is null ? 0 : 1;
}

if (args is ["--performance", var performanceSource, var performanceReport])
{
    return PerformanceReview.Run(performanceSource, performanceReport);
}

if (args is ["--options", var settingsPath])
{
    Console.WriteLine(JsonSerializer.Serialize(new ApplicationOptionsStore(settingsPath).Read()));
    return 0;
}

if (args is ["--library", var libraryPath, var query])
{
    Console.WriteLine(JsonSerializer.Serialize(new ImageLibrary(libraryPath).Search(query)));
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
