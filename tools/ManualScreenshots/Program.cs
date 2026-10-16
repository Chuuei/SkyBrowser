using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Controls;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.ManualScreenshots;

/// <summary>
/// Captures real application windows using an explicit, anonymous demonstration model.
/// Never copies a user's options, observer location, filenames or image metadata into a figure.
/// </summary>
internal static class Program
{
    private const string DemoRoot = @"C:\SkyBrowserDemo";
    private static readonly DateTimeOffset DemoTime = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> Screenshots = new(StringComparer.Ordinal);

    [STAThread]
    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        string root = Path.GetFullPath(".");
        string[] arguments = Environment.GetCommandLineArgs()[1..];
        if (arguments.Length > 1)
        {
            Console.Error.WriteLine("Usage: SkyBrowser.ManualScreenshots [sample-directory]");
            return 2;
        }
        string sampleDirectory = Path.GetFullPath(arguments.FirstOrDefault() ??
            Environment.GetEnvironmentVariable("SKYBROWSER_SOLVED_SAMPLES") ?? Path.Combine(root, "examples"));
        string[] files = Directory.Exists(sampleDirectory) ? Directory.GetFiles(sampleDirectory) : [];
        string? widePath = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".fits", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).Contains("horsehead", StringComparison.OrdinalIgnoreCase));
        string? detailPath = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".xisf", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).Contains("434", StringComparison.OrdinalIgnoreCase));
        if (widePath is null || detailPath is null)
        {
            Console.Error.WriteLine("Optional manual samples are missing. Supply a sample directory containing a plate-solved Horsehead .fits and a smaller overlapping IC434 .xisf. See docs/manual/README.md. No screenshots were changed.");
            return 2;
        }
        string output = Path.Combine(root, "docs", "manual", "screenshots");
        Directory.CreateDirectory(output);

        using var cache = new ProcessedImageCache(Path.Combine(root, "artifacts", "manual-demo-cache"));
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow(cache) { Width = 1440, Height = 920, ShowInTaskbar = false };
        window.Show();
        int result = 0;

        window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var loader = new CachedSkyImageLoader(cache);
                // Scientific pixels and sky geometry are useful illustrations. All identifying
                // fields are replaced before any image enters a window or screenshot.
                var originals = await Task.Run(() => new[]
                {
                    loader.LoadForBrowsing(widePath)[0],
                    loader.LoadForBrowsing(detailPath)[0]
                });
                var wide = new SkyImageLayer(DemoImage(originals[0], false));
                var detail = new SkyImageLayer(DemoImage(originals[1], true));
                using (window.SkyView.DeferLayerUpdates())
                {
                    window.SkyView.Layers.Add(wide);
                    window.SkyView.Layers.Add(detail);
                }
                ((ListBox)window.FindName("LayerList")).SelectedItem = wide;
                ((TextBlock)window.FindName("FolderLocation")).Text = "Example image collection";
                window.SkyView.Fit(wide);
                await Ready();
                Hover(window.SkyView, null);
                Capture(window, "main-window.png", output);
                Capture(window, "horsehead-blended.png", output);

                var inset = window.SkyView.Camera.Project(
                    detail.Placement!.Center.Vector,
                    window.SkyView.ActualWidth,
                    window.SkyView.ActualHeight)!.Value;
                await Ready();
                // Set the demonstration hover after yielding so native pointer events cannot replace it.
                Hover(window.SkyView, new Point(inset.X, inset.Y));
                Capture(window, "horsehead-hover.png", output);
                if (Screenshots["horsehead-hover.png"] == Screenshots["horsehead-blended.png"])
                {
                    throw new InvalidOperationException("The hover illustration must show the inset at full selected opacity.");
                }
                Hover(window.SkyView, null);

                var defaults = new ApplicationOptions { CacheDirectory = DemoRoot + @"\Cache" };
                var welcome = new WelcomeWindow(defaults, defaults)
                {
                    Owner = window,
                    SaveOptionsAsync = _ => Task.CompletedTask
                };
                welcome.Show();
                await Ready();
                Capture(welcome, "first-run-setup.png", output);
                welcome.Close();

                var options = new OptionsWindow(defaults, DemoRoot + @"\Settings\settings.json", defaults)
                {
                    Owner = window,
                    ReadCacheStatistics = () => new(
                        2, 20 * 1024 * 1024, defaults.CacheLimitBytes,
                        0, 0, DemoRoot + @"\Cache\previews-v1.sqlite3", null),
                    ClearCachePreviewsAsync = () => Task.CompletedTask
                };
                options.Show();
                await Task.Delay(150);
                var tabs = (TabControl)options.FindName("SettingsTabs");
                tabs.SelectedIndex = 0;
                await Ready();
                Capture(options, "options-images.png", output);
                tabs.SelectedIndex = 2;
                await Ready();
                Capture(options, "options-cache.png", output);
                tabs.SelectedIndex = 3;
                ((TextBox)options.FindName("AstapPathInput")).Text = @"C:\Program Files\ASTAP\astap.exe";
                await Ready();
                Capture(options, "options-plate-solving.png", output);
                options.Close();

                var demoLibrary = new ImageLibrary(Path.Combine(root, "artifacts", "manual-library", "library.sqlite3"));
                var libraryWindow = new LibraryWindow(demoLibrary) { Owner = window, ApplyFoldersAsync = _ => Task.FromResult(true) };
                libraryWindow.Show();
                await libraryWindow.InitializeAsync();
                await Ready();
                ((ListBox)libraryWindow.FindName("FolderList")).ItemsSource = new LibraryFolder[]
                {
                    new(DemoRoot + @"\Images\Wide fields", true),
                    new(DemoRoot + @"\Images\Deep sky", true),
                    new(DemoRoot + @"\Archive\External drive", false)
                };
                ((TextBlock)libraryWindow.FindName("EmptyLibrary")).Visibility = Visibility.Collapsed;
                ((ListBox)libraryWindow.FindName("FolderList")).SelectedIndex = 2;
                ((Button)libraryWindow.FindName("ClearFoldersButton")).IsEnabled = true;
                ((TextBlock)libraryWindow.FindName("LibraryStatus")).Text = "3 folders · 1 unavailable";
                await Dispatcher.Yield(DispatcherPriority.Render);
                Capture(libraryWindow, "image-library.png", output);
                libraryWindow.Close();

                var information = new ImageDetailsWindow(detail) { Owner = window };
                information.Show();
                await Task.Delay(150);
                // The path is deliberately an example, not a real source file on this machine.
                ((TextBox)information.FindName("FileFacts")).Text =
                    "Example source file: 22.0 MB\nExample modified time: 01 Jan 2000 12:00 UTC";
                await Ready();
                Capture(information, "image-details.png", output);
                information.Close();

                var observer = new ObserverWindow(new(new(0, 0), false, DemoTime)) { Owner = window };
                observer.Show();
                await Ready();
                ((ObserverMap)observer.FindName("Map")).ResetView();
                await Ready();
                Capture(observer, "observer-location.png", output);
                observer.Close();

                ((Expander)window.FindName("TargetFinder")).IsExpanded = true;
                ((TextBox)window.FindName("TargetSearch")).Text = "NGC 253";
                ((ComboBox)window.FindName("TargetChoice")).SelectedItem = DeepSkyCatalog.Find("NGC253");
                await Ready();
                Capture(window, "target-picker-selected.png", output);

                ((Expander)window.FindName("TargetFinder")).IsExpanded = false;
                window.Width = window.MinWidth;
                window.ApplyObserver(new(new(0, 0), false, DemoTime));
                var timeline = (SceneTimeline)window.FindName("Timeline");
                ((Slider)timeline.FindName("TimeSlider")).Value = 180;
                await Ready();
                // Show the neutral demonstration clock in UTC, independent of the
                // authoring computer's local zone. Sanitize both visible readouts.
                string demoClock = DemoTime.AddHours(3).ToString("dd MMM yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture);
                ((TextBlock)timeline.FindName("TimeReadout")).Text = demoClock;
                ((TextBlock)window.FindName("ViewObserverTime")).Text = demoClock;
                Capture(window, "scene-timeline.png", output);
                window.ApplyObserver(null);

                var loading = new ImageLoadProgressWindow(
                    "Loading images", "file", "Cancel keeps the images already added to your sky.", () => { })
                {
                    Owner = window
                };
                loading.SetTotal(2);
                loading.BeginItem("horsehead-detail.xisf");
                loading.FinishItem(1, 1);
                loading.BeginItem("horsehead-wide.fits");
                loading.Report(new("Stretching onto a dark sky", 58), loading.ItemVersion);
                loading.Show();
                await Ready();
                Capture(loading, "loading-progress.png", output);
                loading.Finish();

                // Reviewed hashes prevent a future PDF rebuild from silently picking up raw
                // test screenshots whose hidden/cropped areas might contain identifying data.
                var manifest = new
                {
                    Version = 1,
                    Dataset = "anonymous-demonstration",
                    ExampleRoot = DemoRoot,
                    Observer = new { Latitude = 0, Longitude = 0, TimeUtc = DemoTime },
                    Files = Screenshots
                };
                File.WriteAllText(Path.Combine(output, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Captured {Screenshots.Count} anonymous demonstration screenshots.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                result = 1;
            }
            finally
            {
                window.Close();
                app.Shutdown();
            }
        });

        app.Run();
        return result;
    }

    private static LoadedSkyImage DemoImage(LoadedSkyImage source, bool detail)
    {
        string name = detail ? "Horsehead detail" : "IC 434";
        string filename = detail ? "horsehead-detail.xisf" : "horsehead-wide.fits";
        return source with
        {
            SourcePath = DemoRoot + @"\Images\" + filename,
            Name = name,
            ImageIndex = 0,
            SourceImageCount = 1,
            Metadata = $"{source.Width:N0} × {source.Height:N0} · Example colour image",
            PlacementWarning = null,
            DisplayDescription = "Dark sky · background 10% · stretch only",
            FinishSettings = SkyFinishSettings.Default,
            IsFromCache = false,
            Details = new([
                new("Image", "Dimensions", $"{source.Width} × {source.Height}"),
                new("Image", "Channels", "3"),
                new("Image", "Color space", "RGB"),
                new("FITS", "OBJECT", "IC 434", "Example target"),
                new("FITS", "FILTER", "Example filter", "Demonstration metadata"),
                new("FITS", "TIMESYS", "UTC", "Demonstration time system")
            ])
        };
    }

    private static void Hover(SkyViewport sky, Point? point)
    {
        sky.UpdateHover(point);
    }

    private static async Task Ready()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }

    private static void Capture(Window window, string filename, string output)
    {
        window.UpdateLayout();
        ValidateVisibleText(window, filename);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(output, filename);
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }
        Screenshots.Add(filename, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    private static void ValidateVisibleText(DependencyObject visual, string filename)
    {
        string? text = visual switch
        {
            TextBlock block => block.Text,
            TextBox box => box.Text,
            ContentControl { Content: string content } => content,
            _ => null
        };
        if (text is not null)
        {
            string account = Environment.UserName;
            bool ordinaryWord = account.ToLowerInvariant() is
                "user" or "admin" or "administrator" or "system" or "root" or "public" or "default" or "demo" or "skybrowser";
            bool containsAccount = account.Length >= 3 && !ordinaryWord &&
                Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(account)}(?!\w)", RegexOptions.IgnoreCase);
            if (containsAccount || Regex.IsMatch(text, @"\b[A-Z]:[\\/]+Users[\\/]+", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException($"Personal account information would appear in {filename}; capture blocked.");
            }
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        {
            ValidateVisibleText(VisualTreeHelper.GetChild(visual, index), filename);
        }
    }
}
