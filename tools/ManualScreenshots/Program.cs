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
using SkyBrowser.Desktop.Services;
using XISFLib;

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
        bool inspectionOnly = arguments.FirstOrDefault() == "--inspection-only";
        bool collectionOnly = arguments.FirstOrDefault() == "--collection-only";
        bool skyToolsOnly = arguments.FirstOrDefault() == "--sky-tools-only";
        bool selectedImageOnly = arguments.FirstOrDefault() == "--selected-image-only";
        if (inspectionOnly || collectionOnly || skyToolsOnly || selectedImageOnly)
        {
            arguments = arguments[1..];
        }
        if (arguments.Length > 1 || (inspectionOnly && arguments.Length != 1))
        {
            Console.Error.WriteLine("Usage: SkyBrowser.ManualScreenshots [sample-directory] | --inspection-only source-image | --collection-only sample-directory | --selected-image-only sample-directory | --sky-tools-only");
            return 2;
        }
        string sampleDirectory = Path.GetFullPath(arguments.FirstOrDefault() ??
            Environment.GetEnvironmentVariable("SKYBROWSER_SOLVED_SAMPLES") ?? Path.Combine(root, "examples"));
        string[] files = Directory.Exists(sampleDirectory) ? Directory.GetFiles(sampleDirectory) : [];
        string? widePath = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".fits", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).Contains("horsehead", StringComparison.OrdinalIgnoreCase));
        string? detailPath = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".xisf", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).Contains("434", StringComparison.OrdinalIgnoreCase));
        if (inspectionOnly && File.Exists(sampleDirectory) && SkyImageLoader.IsSupported(sampleDirectory))
        {
            widePath = detailPath = sampleDirectory;
        }
        if (!skyToolsOnly && (widePath is null || detailPath is null))
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
                if (skyToolsOnly)
                {
                    await CaptureSkyTools(window, root, output);
                    WriteManifest(output, merge: true);
                    Console.WriteLine("Captured anonymous survey, planning, annotation and target-distance demonstrations.");
                    return;
                }
                var loader = new CachedSkyImageLoader(cache);
                if (inspectionOnly)
                {
                    var source = await Task.Run(() => loader.LoadForBrowsing(detailPath!)[0]);
                    await CaptureInspection(new SkyImageLayer(DemoImage(source, true)), window, root, output);
                    WriteManifest(output, merge: true);
                    Console.WriteLine("Captured anonymous image details and inspection demonstrations.");
                    return;
                }
                // Scientific pixels and sky geometry are useful illustrations. All identifying
                // fields are replaced before any image enters a window or screenshot.
                var originals = await Task.Run(() => new[]
                {
                    loader.LoadForBrowsing(widePath!)[0],
                    loader.LoadForBrowsing(detailPath!)[0]
                });
                if (collectionOnly)
                {
                    var source = DemoImage(originals[0], false);
                    source = source with
                    {
                        Placement = source.Placement ?? ImagePlacement.FromManual(new(85, -2, 5, 0), source.Width, source.Height)
                    };
                    // Compare two display versions of the same anonymous image. Their
                    // geometry is identical even when optional source astrometry is absent.
                    byte[] pixels = source.Preview.Bgra.ToArray();
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            pixels[i + channel] = (byte)Math.Min(255, pixels[i + channel] * 1.4);
                        }
                    }
                    var version = source with
                    {
                        SourcePath = DemoRoot + @"\Images\horsehead-version.xisf", Name = "Processing version",
                        Preview = new(source.Preview.Width, source.Preview.Height, pixels)
                    };
                    await CaptureCollection(window, new(source), new(version), output);
                    WriteManifest(output, merge: true);
                    Console.WriteLine("Captured anonymous collection and comparison demonstrations.");
                    return;
                }
                if (originals.Any(image => image.Placement is null))
                {
                    throw new InvalidOperationException("Full manual capture requires supported sky placements for both overlapping images. Use --inspection-only for an unsolved source. No screenshots were changed.");
                }
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
                Capture(window, "selected-image-collapsed.png", output);
                ((Expander)window.FindName("InspectorExpander")).IsExpanded = true;
                await Ready();
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

                if (selectedImageOnly)
                {
                    WriteManifest(output, merge: true);
                    Console.WriteLine("Captured anonymous collapsed and expanded selected-image demonstrations.");
                    return;
                }

                await CaptureCollection(window, wide, detail, output);

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

                await CaptureInspection(detail, window, root, output);

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

                await CaptureMoon(window, output);
                WriteManifest(output, merge: false);
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

    private static async Task CaptureInspection(SkyImageLayer detail, MainWindow window, string root, string output)
    {
        var information = new ImageDetailsWindow(detail) { Owner = window };
        information.Show();
        await Task.Delay(150);
        // The path is deliberately an example, not a real source file on this machine.
        ((TextBox)information.FindName("FileFacts")).Text =
            "Example source file: 22.0 MB\nExample modified time: 01 Jan 2000 12:00 UTC";
        await Ready();
        Capture(information, "image-details.png", output);
        information.Close();

        // Build a new, anonymous scientific demonstration from approved display
        // pixels. Never expose an original photograph's samples or metadata here.
        var preview = detail.Image.Preview;
        byte[] demoSamples = new byte[preview.Width * preview.Height * 3];
        for (int p = 0; p < preview.Width * preview.Height; p++)
        {
            demoSamples[p * 3] = preview.Bgra[p * 4 + 2];
            demoSamples[p * 3 + 1] = preview.Bgra[p * 4 + 1];
            demoSamples[p * 3 + 2] = preview.Bgra[p * 4];
        }
        string demoSource = Path.Combine(root, "artifacts", "inspection-demo.xisf");
        var demoScientific = XisfImageData.FromSamples(demoSamples, [preview.Width, preview.Height], 3, XisfPixelLayout.Interleaved);
        XisfWriter.Write(demoSource, [demoScientific]);
        var demoLayer = new SkyImageLayer(SkyImageLoader.Load(demoSource, DisplayMode.Linear)[0] with { Name = "Image inspection demonstration" }, DisplayMode.Linear);
        demoLayer.SetManual(new(85, -2, 2, 0));
        var inspection = new ImageInspectionWindow(demoLayer) { Owner = window };
        inspection.Show();
        await inspection.InitializeAsync();
        if (((Image)inspection.FindName("FullImage")).Source is null)
        {
            throw new InvalidOperationException("The anonymous inspection demonstration did not load.");
        }
        inspection.ReadPixel(preview.Width / 2, preview.Height / 2);
        await Ready();
        Capture(inspection, "image-inspection.png", output);
        inspection.Close();

        var sky = new SkyViewport();
        var skyWindow = new Window { Owner = window, Content = sky, Width = 940, Height = 650, ShowInTaskbar = false };
        // Reuse only the anonymous demonstration image, never an original source path.
        var skyLayer = new SkyImageLayer(DemoImage(demoLayer.Image, true), DisplayMode.Linear);
        skyLayer.SetManual(new(85, -2, 2, 0));
        sky.Layers.Add(skyLayer);
        sky.SelectedLayer = skyLayer;
        skyWindow.Show();
        sky.Fit(skyLayer);
        await Ready();
        sky.UpdateHover(null);
        Capture(skyWindow, "sky-image-actions.png", output);
        var exportSettings = new ExportWindow(1600, 1000, preview.Width, preview.Height) { Owner = window };
        exportSettings.Show();
        await Ready();
        Capture(exportSettings, "image-export.png", output);
        exportSettings.Close();
        string exportedPath = Path.Combine(output, "sky-export.png");
        ImageExport.Save(sky.CaptureExport(new() { Width = 1280, Height = 800, Coordinates = true, Grid = true }),
            exportedPath, [demoSource], overwrite: true);
        Screenshots.Add("sky-export.png", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exportedPath))));
        skyWindow.Close();
    }

    private static async Task CaptureMoon(MainWindow window, string output)
    {
        window.Width = 1440;
        window.Height = 920;
        window.ApplyObserver(new(new(0, 0), false, DemoTime), resetView: false);
        var toggle = (CheckBox)window.FindName("MoonToggle");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.SkyView.CenterOn(window.SkyView.CurrentMoon.Position, 12);
        await Ready();
        string clock = DemoTime.ToString("dd MMM yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture);
        ((TextBlock)window.FindName("ViewObserverTime")).Text = clock;
        ((TextBlock)((SceneTimeline)window.FindName("Timeline")).FindName("TimeReadout")).Text = clock;
        Capture(window, "moon-overlay.png", output);
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ApplyObserver(null);
    }

    private static async Task CaptureSkyTools(MainWindow window, string root, string output)
    {
        var service = new SurveyImageService(Path.Combine(root, "artifacts", "manual-sky-surveys"));
        window.SkyView.SetObserver(null);
        window.SkyView.CenterOn(new(10.6847, 41.269), 4);
        var tools = new SkyToolsWindow(window.SkyView, service) { Owner = window, ShowInTaskbar = false };
        tools.Show();
        try
        {
            await tools.InitializeAsync();
            ((DatePicker)tools.FindName("PlanDate")).SelectedDate = DemoTime.UtcDateTime.Date;
            ((TextBox)tools.FindName("PlanLatitude")).Text = "0";
            ((TextBox)tools.FindName("PlanLongitude")).Text = "0";
            ((TextBox)tools.FindName("PlanRa")).Text = "10.6847";
            ((TextBox)tools.FindName("PlanDec")).Text = "41.269";
            if (!await tools.LoadSurveyAsync(new(SurveyDefinition.Color.Id, new(10.6847, 41.269), 4, 512), false))
            {
                throw new InvalidOperationException("The public DSS demonstration could not load. No screenshots were changed.");
            }
            await Ready();
            Capture(tools, "sky-tools-survey.png", output);
            ((TabControl)tools.FindName("ToolsTabs")).SelectedIndex = 1;
            tools.Height = 1050;
            if (await tools.CalculateVisibilityAsync() is null) { throw new InvalidOperationException("Demonstration planning failed."); }
            await Ready();
            Capture(tools, "sky-tools-visibility.png", output);
            tools.Height = 850;
            ((TabItem)tools.FindName("FramingTab")).IsSelected = true;
            ((ComboBox)tools.FindName("EquipmentChoice")).SelectedItem = SmartTelescopeProfiles.All.Single(profile => profile.Name == "ZWO Seestar S50");
            ((TextBox)tools.FindName("FramingRa")).Text = "10.6847";
            ((TextBox)tools.FindName("FramingDec")).Text = "41.269";
            ((TextBox)tools.FindName("FramingRotation")).Text = "25";
            ((TextBox)tools.FindName("MosaicRows")).Text = "2";
            ((TextBox)tools.FindName("MosaicColumns")).Text = "3";
            if (tools.ApplyFramingPlan() is null) { throw new InvalidOperationException("Demonstration mosaic failed."); }
            await Ready();
            Capture(tools, "sky-tools-framing.png", output);
            Capture(window, "sky-tools-mosaic.png", output);
            window.SkyView.FramingPlan = null;
            ((TextBlock)tools.FindName("SkyToolsStatus")).Text = "Ready";
            window.SkyView.CenterOn(new(10.6847, 41.269), 4);
            ((TabControl)tools.FindName("ToolsTabs")).SelectedIndex = 2;
            foreach (string name in new[] { "ScaleBarChoice", "NorthEastChoice" })
            {
                var choice = (CheckBox)tools.FindName(name);
                choice.IsChecked = true;
                choice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            window.SkyView.BeginMeasurement();
            window.SkyView.PickMeasurementPoint(new(10.1847, 41.269));
            window.SkyView.PickMeasurementPoint(new(11.1847, 41.269));
            await Ready();
            Capture(tools, "sky-tools-measurements.png", output);
            Capture(window, "sky-tools-guides.png", output);
            tools.Width = 800;
            tools.Height = 660;
            ((TextBox)tools.FindName("DistanceSearchA")).Text = "M 81";
            ((TextBox)tools.FindName("DistanceSearchB")).Text = "M 82";
            ((ListBox)tools.FindName("DistanceTargetsA")).SelectedItem = TargetDistances.Find("M81");
            ((ListBox)tools.FindName("DistanceTargetsB")).SelectedItem = TargetDistances.Find("M82");
            await Ready();
            ((ScrollViewer)tools.FindName("MeasurementsScroll")).ScrollToBottom();
            await Ready();
            Capture(tools, "sky-tools-distances.png", output);
            var space = new DistanceSpaceWindow { Owner = tools, ShowInTaskbar = false };
            try
            {
                space.Show();
                await Ready();
                Capture(space, "distance-space.png", output);
                space.View.Selected = TargetDistances.Find("M81");
                space.View.SelectTarget(TargetDistances.Find("M82"), second: true);
                space.View.FramePair();
                await Ready();
                Capture(space, "distance-space-pair.png", output);
            }
            finally { space.Close(); }
        }
        finally
        {
            tools.Close();
            window.SkyView.SetSurveyBackground(null);
            window.SkyView.ClearMeasurement();
        }
    }

    private static async Task CaptureCollection(MainWindow window, SkyImageLayer wide, SkyImageLayer detail, string output)
    {
        var comparison = new ComparisonWindow([wide, detail]) { Owner = window, ShowInTaskbar = false };
        comparison.Show();
        ((ComboBox)comparison.FindName("ModeChoice")).SelectedIndex = (int)ComparisonMode.SideBySide;
        await Ready();
        Capture(comparison, "comparison-linked.png", output);
        ((ComboBox)comparison.FindName("ModeChoice")).SelectedIndex = (int)ComparisonMode.Swipe;
        await Ready();
        Capture(comparison, "comparison-swipe.png", output);
        comparison.Close();
        wide.SetOrganization(new(true, "Broadband, Winter", "Horsehead"));
        detail.SetOrganization(new(true, "Detail, Winter", "Horsehead"));
        var list = (ListBox)window.FindName("LayerList");
        list.SelectAll();
        var batch = new CollectionBatchWindow([wide, detail]) { Owner = window, ShowInTaskbar = false };
        batch.Show();
        ((ComboBox)batch.FindName("FavoriteChoice")).SelectedIndex = 1;
        ((CheckBox)batch.FindName("SetGroup")).IsChecked = true;
        await Ready();
        Capture(batch, "collection-batch.png", output);
        batch.Close();
        list.SelectedItem = wide;
        wide.SetOrganization(new());
        detail.SetOrganization(new());
    }

    private static void WriteManifest(string output, bool merge)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (merge)
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            foreach (var file in existing.RootElement.GetProperty("Files").EnumerateObject())
            {
                files.Add(file.Name, file.Value.GetString()!);
            }
        }
        foreach (var file in Screenshots)
        {
            files[file.Key] = file.Value;
        }
        var manifest = new
        {
            Version = 1,
            Dataset = "anonymous-demonstration",
            ExampleRoot = DemoRoot,
            Observer = new { Latitude = 0, Longitude = 0, TimeUtc = DemoTime },
            Files = files
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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
