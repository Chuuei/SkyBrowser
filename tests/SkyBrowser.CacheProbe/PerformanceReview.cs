using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using XISFLib;

internal static class PerformanceReview
{
    public static int Run(string source, string reportPath)
    {
        List<object> results = [];
        var stars = new SkyCatalog().Stars.Select(star => star.Position.Vector).ToArray();
        foreach (bool observer in new[]
        {
            false,
            true
        }

        )
        {
            var camera = new SkyCamera
            {
                Observer = observer ? new(new(52.52, 13.405), new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero)) : null
            };
            Measure(observer ? "Observer projections (100 frames)" : "Atlas projections (100 frames)", () =>
            {
                double checksum = 0;
                for (int frame = 0; frame < 100; frame++)
                {
                    camera.Center = new(25 + frame * 0.01, 38);
                    foreach (var star in stars)
                    {
                        if (camera.Project(star, 1200, 800) is { } point)
                        {
                            checksum += point.X + point.Y;
                        }
                    }
                }

                return checksum.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            });
        }

        var solution = XisfAstrometricSolution.CreateLinear("Gnomonic", [83.822, -5.391], [3200, 2100], new double[,] { { -0.0005, 0 }, { 0, 0.0005 } });
        var placement = ImagePlacement.FromNative(solution, 6400, 4200);
        Measure("Capture native sky grid (100 captures)", () =>
        {
            double sum = 0;
            for (int i = 0; i < 100; i++)
            {
                sum += placement.CaptureSampleGrid()[0].RightAscension;
            }

            return sum.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        });
        foreach (var mode in new[]
        {
            DisplayMode.Automatic,
            DisplayMode.Finished
        }

        )
        {
            Measure($"{mode} image processing", () => Hash(SkyImageLoader.Load(source, mode)[0]));
        }

        using var cache = new ProcessedImageCache(Path.GetFullPath(Path.ChangeExtension(reportPath, null) + "-cache"));
        var loader = new CachedSkyImageLoader(cache);
        loader.Load(source, DisplayMode.Automatic, SkyFinishSettings.Default);
        Measure(
            "Persistent preview cache hit",
            () => Hash(loader.Load(source, DisplayMode.Automatic, SkyFinishSettings.Default)[0])
        );
        string json = JsonSerializer.Serialize(
            new
            {
                Source = Path.GetFullPath(source),
                Runtime = Environment.Version.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                Results = results
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(reportPath, json);
        Console.WriteLine(json);
        return 0;

        void Measure(string name, Func<string> action)
        {
            action(); // JIT and filesystem warm-up; this is not a cold-disk benchmark.
            List<double> milliseconds = [];
            List<long> allocated = [];
            string checksum = "";
            for (int i = 0; i < 5; i++)
            {
                long before = GC.GetTotalAllocatedBytes(precise: true);
                var timer = Stopwatch.StartNew();
                checksum = action();
                timer.Stop();
                milliseconds.Add(timer.Elapsed.TotalMilliseconds);
                allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
            }

            milliseconds.Sort();
            allocated.Sort();
            results.Add(new
            {
                Name = name,
                MedianMilliseconds = milliseconds[2],
                MedianAllocatedBytes = allocated[2],
                Checksum = checksum
            });
        }
    }

    private static string Hash(LoadedSkyImage image) => Convert.ToHexString(SHA256.HashData(image.Preview.Bgra));
}
