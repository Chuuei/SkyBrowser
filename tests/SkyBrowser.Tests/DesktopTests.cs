using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Services;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed class DesktopTests
{
    [Fact]
    public async Task WindowImportsRendersAndRestoresSessions()
    {
        string directory = TestImages.NewDirectory();
        var cache = new ProcessedImageCache(Path.Combine(directory, "cache"));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // WPF allows one Application per process and requires an STA dispatcher. Keep the
        // desktop scenarios in this shared lifetime; shut it down only after all checks finish.
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                var app = new App();
                app.InitializeComponent();
                window = new MainWindow(cache)
                {
                    Width = 1440,
                    Height = 920,
                    ShowInTaskbar = false
                };
                window.Show();
                var currentWindow = window;
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.Same(currentWindow, Application.Current.MainWindow);
                        Assert.Single(Application.Current.Windows.OfType<MainWindow>());
                        Capture(currentWindow, "overview.png");
                        MeasureSkyPerformance(currentWindow);
                        await VerifyFirstRunSetup(currentWindow, directory);
                        await VerifyLoadProgress(currentWindow, cache, directory);
                        await VerifyObserver(currentWindow, directory);
                        await VerifyCollectionSearch(currentWindow, directory);
                        VerifyBlending(currentWindow);
                        await VerifyTargetPicker(currentWindow, directory);
                        string xisf = Path.Combine(directory, "synthetic.xisf"), fits = Path.Combine(directory, "synthetic.fits");
                        var image = TestImages.Create();
                        XisfWriter.Write(xisf, [image]);
                        FitsWriter.Write(fits, FitsConverter.FromXisf([image], ConversionLossPolicy.Allow));
                        await currentWindow.ImportAsync([xisf, fits]);
                        Assert.Equal(2, currentWindow.SkyView.Layers.Count);
                        await VerifyImageInfo(currentWindow, currentWindow.SkyView.Layers.Single(layer => layer.Image.SourcePath == fits));
                        Assert.True(currentWindow.SkyView.Camera.Center.DistanceTo(new(83.822, -5.391)) < 1e-8);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        // Opaque overlapping images must replace, not add their brightness.
                        var overlapping = SampleTexture(currentWindow);
                        currentWindow.SkyView.Layers[1].IsVisible = false;
                        var single = SampleTexture(currentWindow);
                        for (int c = 0; c < 3; c++)
                        {
                            Assert.InRange(Math.Abs(overlapping[c] - single[c]), 0, 2);
                        }

                        currentWindow.SkyView.Layers[1].IsVisible = true;
                        Capture(currentWindow, "aligned-images.png");
                        var layer = currentWindow.SkyView.Layers[0];
                        var customFinish = new SkyFinishSettings
                        {
                            TargetBackground = 0.07,
                            ReduceNoise = false,
                            ReduceGradients = false
                        };
                        await currentWindow.ApplyDisplayAsync(layer, DisplayMode.Finished, customFinish);
                        Assert.Equal(DisplayMode.Finished, layer.DisplayMode);
                        Assert.Equal(customFinish, layer.Image.FinishSettings);
                        layer.Opacity = 0.45;
                        layer.IsVisible = false;
                        layer.SetManual(new(359.95, 70, 3, 28, true));
                        SetBlending(currentWindow, false);
                        currentWindow.SkyView.CenterOn(new(359.95, 70), 6);
                        string session = Path.Combine(directory, "session.sky.json");
                        await currentWindow.SaveSessionAsync(session);
                        var saved = await SkySession.ReadAsync(session, CancellationToken.None);
                        Assert.False(Path.IsPathRooted(saved.Images[0].SourcePath));
                        Assert.Equal(28, saved.Images[0].Manual!.RotationDegrees);
                        Assert.Equal(customFinish, saved.Images[0].FinishSettings);
                        Assert.False(saved.BlendOverlaps);
                        SetBlending(currentWindow, true);
                        layer.Opacity = 1;
                        layer.IsVisible = true;
                        await currentWindow.OpenSessionAsync(session);
                        Assert.Equal(2, currentWindow.SkyView.Layers.Count);
                        Assert.Equal(0.45, currentWindow.SkyView.Layers[0].Opacity);
                        Assert.False(currentWindow.SkyView.Layers[0].IsVisible);
                        Assert.True(currentWindow.SkyView.Layers[0].Manual!.Mirror);
                        Assert.Equal(6, currentWindow.SkyView.Camera.FieldOfView);
                        Assert.Equal(customFinish, currentWindow.SkyView.Layers[0].Image.FinishSettings);
                        Assert.Equal(DisplayMode.Finished, currentWindow.SkyView.Layers[0].DisplayMode);
                        Assert.False(currentWindow.SkyView.BlendOverlaps);
                        Assert.False(((System.Windows.Controls.CheckBox)currentWindow.FindName("BlendToggle")).IsChecked);
                        SetBlending(currentWindow, true);
                        // A missing source must be reported while the other saved image remains usable.
                        File.Delete(fits);
                        await VerifyImageInfo(currentWindow, currentWindow.SkyView.Layers.Single(layer => layer.Image.SourcePath == fits), missing: true);
                        await currentWindow.OpenSessionAsync(session);
                        Assert.Single(currentWindow.SkyView.Layers);
                        Assert.Contains("1 unavailable", currentWindow.Status);
                        SetBlending(currentWindow, true);
                        string folderA = Path.Combine(directory, "browse-a"), folderB = Path.Combine(directory, "browse-b");
                        Directory.CreateDirectory(folderA);
                        Directory.CreateDirectory(folderB);
                        string browseA = Path.Combine(folderA, "a.xisf"), browseB = Path.Combine(folderB, "b.xisf");
                        XisfWriter.Write(browseA, [TestImages.Create(true)]);
                        XisfWriter.Write(browseB, [TestImages.Create(true)]);
                        await currentWindow.BrowseFolderAsync(folderA);
                        Assert.Single(currentWindow.SkyView.Layers);
                        await currentWindow.ApplyDisplayAsync(currentWindow.SkyView.Layers[0], DisplayMode.Finished, customFinish);
                        await currentWindow.BrowseFolderAsync(folderB);
                        await currentWindow.BrowseFolderAsync(folderA);
                        Assert.Equal(customFinish, currentWindow.SkyView.Layers[0].Image.FinishSettings);
                        Assert.Equal(DisplayMode.Finished, currentWindow.SkyView.Layers[0].DisplayMode);
                        Assert.True(currentWindow.SkyView.Layers[0].Image.IsFromCache);
                        var superseded = currentWindow.BrowseFolderAsync(folderA);
                        var latest = currentWindow.BrowseFolderAsync(folderB);
                        await Task.WhenAll(superseded, latest);
                        Assert.Equal(folderB, currentWindow.CurrentFolderPath);
                        Assert.Equal(browseB, Assert.Single(currentWindow.SkyView.Layers).Image.SourcePath);
                        Assert.Contains("1 cached", currentWindow.Status);
                        Assert.Contains(cache.GetRecentFolders(), folder => folder.Path == folderA);
                        Assert.Contains(cache.GetRecentFolders(), folder => folder.Path == folderB);
                        await VerifyOptions(currentWindow, directory, browseA, customFinish);
                        string? samples = Environment.GetEnvironmentVariable("SKYBROWSER_SAMPLE_DIRECTORY");
                        if (!string.IsNullOrEmpty(samples))
                        {
                            await currentWindow.ImportAsync([Path.Combine(samples, "IC_434_mosaic_wizardstack_992f.fits"), Path.Combine(samples, "slow.xisf")]);
                            Assert.Equal(3, currentWindow.SkyView.Layers.Count);
                            var imported = currentWindow.SkyView.Layers.Last();
                            if (imported.Placement is not null)
                            {
                                currentWindow.SkyView.Fit(imported);
                            }

                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Capture(currentWindow, "real-astronomy.png");
                        }

                        string? solved = Environment.GetEnvironmentVariable("SKYBROWSER_SOLVED_SAMPLES");
                        if (!string.IsNullOrEmpty(solved))
                        {
                            var paths = Directory.GetFiles(solved).Where(SkyImageLoader.IsSupported).ToArray();
                            await currentWindow.ImportAsync(paths);
                            var solvedLayers = currentWindow.SkyView.Layers.Where(l => paths.Contains(l.Image.SourcePath)).ToArray();
                            Assert.Equal(paths.Length, solvedLayers.Length);
                            Assert.All(solvedLayers, l => Assert.NotNull(l.Placement));
                            var m31 = solvedLayers.Single(l => Path.GetFileName(l.Image.SourcePath).StartsWith("m31_"));
                            Assert.True(m31.Placement!.Center.DistanceTo(new(10.692598, 41.264743)) < 0.001);
                            Assert.True(m31.Placement.AngularWidth is > 2 and < 3);
                            currentWindow.SkyView.Fit(m31);
                            ((System.Windows.Controls.ListBox)currentWindow.FindName("LayerList")).SelectedItem = m31;
                            ((System.Windows.Controls.ListBox)currentWindow.FindName("LayerList")).ScrollIntoView(m31);
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Capture(currentWindow, "m31-solved.png");
                            var horsehead = solvedLayers.Single(l => Path.GetFileName(l.Image.SourcePath).StartsWith("horsehead_"));
                            ((System.Windows.Controls.ListBox)currentWindow.FindName("LayerList")).SelectedItem = horsehead;
                            ((System.Windows.Controls.ListBox)currentWindow.FindName("LayerList")).ScrollIntoView(horsehead);
                            currentWindow.SkyView.Fit(horsehead);
                            var center = horsehead.Placement!.Center;
                            var corner = horsehead.Placement.At(0, 0);
                            await currentWindow.ApplyDisplayAsync(horsehead, DisplayMode.AutoStretch, SkyFinishSettings.Default);
                            double originalBackground = FinishingTests.MedianLight(horsehead.Image.Preview);
                            SavePreview(horsehead.Image.Preview, "horsehead-before.png");
                            await currentWindow.ApplyDisplayAsync(horsehead, DisplayMode.Finished, SkyFinishSettings.Default);
                            double darkBackground = FinishingTests.MedianLight(horsehead.Image.Preview);
                            Assert.True(
                                darkBackground < originalBackground * 0.65,
                                $"Background before {originalBackground:F1}, after {darkBackground:F1}"
                            );
                            Assert.True(horsehead.Placement.Center.DistanceTo(center) < 1e-10);
                            Assert.True(horsehead.Placement.At(0, 0).DistanceTo(corner) < 1e-10);
                            Assert.Equal(6365, horsehead.Image.Width);
                            Assert.Equal(4266, horsehead.Image.Height);
                            SavePreview(horsehead.Image.Preview, "horsehead-finished.png");
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Capture(currentWindow, "horsehead-sky.png");
                            var inset = solvedLayers.SingleOrDefault(l => Path.GetFileName(l.Image.SourcePath) == "IC434_20260122.xisf");
                            if (inset is not null)
                            {
                                var sky = currentWindow.SkyView;
                                Assert.True(sky.IsBlendedInset(inset));
                                Assert.False(sky.IsBlendedInset(horsehead));
                                var insetImage = inset.Image;
                                var position = sky.Camera.Project(inset.Placement!.Center.Vector, sky.ActualWidth, sky.ActualHeight)!.Value;
                                Point pointer = new(position.X, position.Y);
                                sky.UpdateHover(null);
                                var blended = PixelAt(sky, pointer);
                                Capture(currentWindow, "horsehead-blended.png");
                                Assert.Same(inset, sky.ImageAt(pointer));
                                sky.UpdateHover(pointer);
                                var solid = PixelAt(sky, pointer);
                                Capture(currentWindow, "horsehead-hover.png");
                                Assert.NotEqual(blended, solid);
                                horsehead.IsVisible = false;
                                var isolated = PixelAt(sky, pointer);
                                for (int c = 0; c < 3; c++)
                                {
                                    Assert.InRange(Math.Abs(solid[c] - isolated[c]), 0, 2);
                                }

                                horsehead.IsVisible = true;
                                sky.UpdateHover(null);
                                Assert.Equal(blended, PixelAt(sky, pointer));
                                Assert.Same(insetImage, inset.Image);
                                Assert.Equal(1, inset.Opacity);
                                await VerifyImageInfo(currentWindow, inset, screenshot: "image-details-xisf.png");
                            }
                        }

                        currentWindow.Width = 1050;
                        currentWindow.Height = 720;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.True(((FrameworkElement)currentWindow.FindName("InspectorScroller")).ActualHeight < 310);
                        Capture(currentWindow, "compact.png");
                        completion.SetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                    finally
                    {
                        currentWindow.Close();
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    }
                });
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromMinutes(3));
        }
        finally
        {
            if (thread.Join(TimeSpan.FromSeconds(10)))
            {
                cache.Dispose();
                Directory.Delete(directory, true);
            }
        }
    }

    private static byte[] SampleTexture(MainWindow window)
    {
        var sky = window.SkyView;
        sky.UpdateLayout();
        var point = sky.Camera.Project(sky.Layers[0].Placement!.At(0.25, 0.25).Vector, sky.ActualWidth, sky.ActualHeight)!.Value;
        var bitmap = new RenderTargetBitmap((int)sky.ActualWidth, (int)sky.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(sky);
        byte[] color = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), color, 4, 0);
        Assert.InRange(color[2], 195, 210); // 0.8 red, displayed without accidental lighting or additive boosts.
        return color;
    }

    private static void MeasureSkyPerformance(MainWindow window)
    {
        string? path = Environment.GetEnvironmentVariable("SKYBROWSER_PERFORMANCE_REPORT");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var sky = window.SkyView;
        var originalCenter = sky.Camera.Center;
        var drawings = sky.Children.OfType<FrameworkElement>()
            .Where(child => child.GetType()
            .Name == "SkyDrawing")
            .Select(child => (Element: child, Method: child.GetType()
            .GetMethod("OnRender", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!))
            .ToArray();
        List<object> results = [];
        var visual = new DrawingVisual();

        void Frames()
        {
            for (int frame = 0; frame < 40; frame++)
            {
                sky.Camera.Center = new(25 + frame * 0.02, 38);
                using var drawing = visual.RenderOpen();
                foreach (var item in drawings)
                {
                    item.Method.Invoke(item.Element, [drawing]);
                }
            }
        }

        Frames();
        List<double> times = [];
        List<long> allocations = [];
        for (int repeat = 0; repeat < 5; repeat++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Frames();
            timer.Stop();
            times.Add(timer.Elapsed.TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        times.Sort();
        allocations.Sort();
        results.Add(new
        {
            Name = "Sky drawing CPU (40 frames, atlas)",
            MedianMilliseconds = times[2],
            MedianAllocatedBytes = allocations[2]
        });
        var layers = Enumerable.Range(0, 100).Select(i => SolidLayer($"Benchmark {i}", 2, 100, 120, 150)).ToArray();
        var bulk = sky.GetType().GetMethod("DeferLayerUpdates");
        long initial = GC.GetAllocatedBytesForCurrentThread();
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();
        using (bulk?.Invoke(sky, null) as IDisposable)
        {
            foreach (var layer in layers)
            {
                sky.Layers.Add(layer);
            }
        }

        loadTimer.Stop();
        results.Add(new
        {
            Name = "Insert 100 sky layers",
            MedianMilliseconds = loadTimer.Elapsed.TotalMilliseconds,
            MedianAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - initial
        });
        sky.Layers.Clear();
        sky.Camera.Center = originalCenter;
        sky.Refresh();
        File.WriteAllText(
            path,
            System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static async Task VerifyFirstRunSetup(MainWindow window, string directory)
    {
        var original = window.Options;
        string settings = Path.Combine(original.CacheDirectory, "settings.json");
        string startupImage = Path.Combine(directory, "first-run-image.xisf");
        XisfWriter.Write(startupImage, [TestImages.Create()]);
        Assert.False(File.Exists(settings));
        Exception? failure = null;

        static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var welcome = Application.Current.Windows.OfType<WelcomeWindow>().Single();
            try
            {
                Assert.Empty(window.SkyView.Layers);
                Assert.False(File.Exists(settings));
                var stretch = (System.Windows.Controls.ComboBox)welcome.FindName("StretchChoice");
                Assert.Equal((int)DisplayMode.Automatic, stretch.SelectedIndex);
                Assert.Contains("No location", ((System.Windows.Controls.TextBlock)welcome.FindName("LocationSummary")).Text);
                Capture(welcome, "first-run-setup.png");
                stretch.SelectedIndex = (int)DisplayMode.Linear;
                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    var map = Application.Current.Windows.OfType<ObserverWindow>().Single();
                    try
                    {
                        Assert.True(((System.Windows.Controls.CheckBox)map.FindName("UseSystemTime")).IsChecked);
                        ((System.Windows.Controls.TextBox)map.FindName("LatitudeInput")).Text = "52.52";
                        ((System.Windows.Controls.TextBox)map.FindName("LongitudeInput")).Text = "13.405";
                        Click((System.Windows.Controls.Button)map.FindName("ApplyButton"));
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        map.Close();
                    }
                    }, DispatcherPriority.ApplicationIdle);
                Click((System.Windows.Controls.Button)welcome.FindName("ChooseLocationButton"));
                if (failure is not null)
                {
                    throw failure;
                }

                Assert.Null(window.Options.Observer);
                Assert.Empty(window.SkyView.Layers);
                Assert.Contains("52", ((System.Windows.Controls.TextBlock)welcome.FindName("LocationSummary")).Text);
                Capture(welcome, "first-run-location.png");
                Click((System.Windows.Controls.Button)welcome.FindName("StartButton"));
            }
            catch (Exception exception)
            {
                failure = exception;
                welcome.Close();
            }
            }, DispatcherPriority.ApplicationIdle);
        await window.OpenStartupAsync([startupImage]);
        if (failure is not null)
        {
            throw failure;
        }

        Assert.True(File.Exists(settings));
        Assert.Equal(DisplayMode.Linear, window.Options.DefaultDisplayMode);
        Assert.Equal(new(52.52, 13.405), window.SkyView.Observer!.Location);
        Assert.True(window.SkyView.Observer.UseSystemTime);
        Assert.Equal(DisplayMode.Linear, Assert.Single(window.SkyView.Layers).DisplayMode); // Startup paths load after the chosen default is applied.
        Assert.Equal(window.Options, new ApplicationOptionsStore(settings, original).Read());
        var restarted = new MainWindow(new ApplicationOptionsStore(settings, original))
        {
            ShowInTaskbar = false
        };
        try
        {
            restarted.Show();
            await restarted.OpenStartupAsync([]);
            Assert.Empty(Application.Current.Windows.OfType<WelcomeWindow>());
            Assert.Equal(window.Options, restarted.Options);
        }
        finally
        {
            restarted.Close();
        }

        async Task RunWelcome(MainWindow fresh, Action<WelcomeWindow> interact)
        {
            _ = fresh.Dispatcher.BeginInvoke(() =>
            {
                var welcome = Application.Current.Windows.OfType<WelcomeWindow>().Single();
                try
                {
                    interact(welcome);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    welcome.Close();
                }
                }, DispatcherPriority.ApplicationIdle);
            await fresh.OpenStartupAsync([]);
            if (failure is not null)
            {
                throw failure;
            }
        }

        var skippedStore = new ApplicationOptionsStore(
            Path.Combine(directory, "new-user", "settings.json"),
            new() { CacheDirectory = Path.Combine(directory, "new-user", "cache") }
        );
        var skipped = new MainWindow(skippedStore)
        {
            ShowInTaskbar = false
        };
        try
        {
            skipped.Show();
            await RunWelcome(skipped, welcome => Click((System.Windows.Controls.Button)welcome.FindName("NotNowButton")));
            Assert.False(File.Exists(skippedStore.FilePath));
            Assert.Null(skipped.SkyView.Observer);
            await skipped.OpenStartupAsync([]); // Deferring setup must not reopen it during the same launch.
        }
        finally
        {
            skipped.Close();
        }

        var defaults = new MainWindow(skippedStore)
        {
            ShowInTaskbar = false
        };
        try
        {
            defaults.Show();
            await RunWelcome(defaults, welcome => Click((System.Windows.Controls.Button)welcome.FindName("DefaultsButton")));
            Assert.True(File.Exists(skippedStore.FilePath));
            Assert.Equal(skippedStore.Defaults, skippedStore.Read());
            Assert.Null(defaults.SkyView.Observer);
            Assert.Equal(10, defaults.Options.CacheSizeGb);
        }
        finally
        {
            defaults.Close();
        }

        // A failed first save leaves setup open, with a way to continue without changing the user's choices.
        string blocked = Path.Combine(directory, "blocked-settings-parent");
        File.WriteAllText(blocked, "occupied");
        var failedStore = new ApplicationOptionsStore(
            Path.Combine(blocked, "settings.json"),
            new() { CacheDirectory = Path.Combine(directory, "failed-setup-cache") }
        );
        var failed = new MainWindow(failedStore)
        {
            ShowInTaskbar = false
        };
        try
        {
            failed.Show();
            _ = failed.Dispatcher.BeginInvoke(async () =>
            {
                var welcome = Application.Current.Windows.OfType<WelcomeWindow>().Single();
                try
                {
                    var start = (System.Windows.Controls.Button)welcome.FindName("StartButton");
                    ((System.Windows.Controls.ComboBox)welcome.FindName("StretchChoice")).SelectedIndex = (int)DisplayMode.Finished;
                    Click(start);
                    await WaitUntil(() => start.IsEnabled);
                    Assert.True(welcome.IsVisible);
                    Assert.Contains("Could not save", ((System.Windows.Controls.TextBlock)welcome.FindName("ValidationMessage")).Text);
                    Assert.Equal(failedStore.Defaults, failed.Options);
                    Click((System.Windows.Controls.Button)welcome.FindName("NotNowButton"));
                }
                catch (Exception exception)
                {
                    failure = exception;
                    welcome.Close();
                }
                }, DispatcherPriority.ApplicationIdle);
            await failed.OpenStartupAsync([]);
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            failed.Close();
        }

        await window.ApplyOptionsAsync(original);
        window.SkyView.Layers.Clear();
    }

    private static async Task VerifyLoadProgress(MainWindow window, ProcessedImageCache cache, string directory)
    {
        string folder = Path.Combine(directory, "load-progress");
        Directory.CreateDirectory(folder);
        string existing = Path.Combine(folder, "a-existing.xisf"), cached = Path.Combine(folder, "b-cached.xisf"), broken = Path.Combine(folder, "c-broken.xisf"), slow = Path.Combine(folder, "d-horsehead.xisf"), remaining = Path.Combine(folder, "e-remaining.xisf");
        foreach (string path in new[]
        {
            existing,
            cached,
            slow,
            remaining
        }

        )
        {
            XisfWriter.Write(path, [TestImages.Create()]);
        }

        File.WriteAllText(broken, "Not an XISF document");
        await window.ImportAsync([existing]);
        await Task.Run(() => new CachedSkyImageLoader(cache).Load(cached, DisplayMode.Finished, SkyFinishSettings.Default));
        // A blocked native-style step makes cancellation timing deterministic, without a large test image.
        var loaderField = typeof(MainWindow).GetField("_imageLoader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var originalLoader = loaderField.GetValue(window);
        using var release = new ManualResetEventSlim();
        bool holdAll = false;
        var attempted = new System.Collections.Concurrent.ConcurrentQueue<string>();
        IProgress<ImageProcessingProgress>? lastReporter = null;
        var controlled = new CachedSkyImageLoader(cache, (path, mode, finish, token, index, progress) =>
        {
            attempted.Enqueue(path);
            if (path == slow || Volatile.Read(ref holdAll))
            {
                lastReporter = progress;
                progress?.Report(new("Smoothing background and color noise", 37));
                release.Wait(); // Simulate a decoder/filter that cannot stop in the middle of a call.
                token.ThrowIfCancellationRequested();
            }

            return SkyImageLoader.Load(path, mode, finish, token, index, progress);
        })
        {
            DefaultDisplayMode = DisplayMode.Finished
        };
        loaderField.SetValue(window, controlled);
        Task? pending = null;

        static ImageLoadProgressWindow? VisibleProgress() => Application.Current.Windows.OfType<ImageLoadProgressWindow>().SingleOrDefault(dialog => dialog.IsVisible);

        static string Text(ImageLoadProgressWindow dialog, string name) => ((System.Windows.Controls.TextBlock)dialog.FindName(name)).Text;

        static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        async Task<ImageLoadProgressWindow> WaitForProgress()
        {
            await WaitUntil(() => VisibleProgress() is { } dialog &&
                Text(dialog, "StageText") == "Smoothing background and color noise");
            return VisibleProgress()!;
        }

        try
        {
            pending = window.ImportAsync([existing, cached, broken, slow, remaining]);
            var dialog = await WaitForProgress();
            Assert.Equal("3 of 5 files finished · 2 remaining", Text(dialog, "CountText"));
            Assert.Equal("File 4 of 5", Text(dialog, "CurrentItemText"));
            Assert.Equal("d-horsehead.xisf", Text(dialog, "FileNameText"));
            Assert.Equal("1 image ready · 1 cached · 1 failed · 1 already loaded", Text(dialog, "ResultsText"));
            var overall = (System.Windows.Controls.ProgressBar)dialog.FindName("OverallProgress");
            Assert.False(overall.IsIndeterminate);
            Assert.Equal(5, overall.Maximum);
            Assert.Equal(3, overall.Value);
            var step = (System.Windows.Controls.ProgressBar)dialog.FindName("StageProgress");
            Assert.False(step.IsIndeterminate);
            Assert.Equal(37, step.Value);
            Assert.True(window.IsEnabled);
            Capture(dialog, "image-loading-progress.png");
            var cancel = (System.Windows.Controls.Button)dialog.FindName("CancelLoadButton");
            Click(cancel);
            Assert.False(cancel.IsEnabled);
            Assert.Equal("Stopping…", Text(dialog, "Heading"));
            Assert.False(pending.IsCompleted); // The popup stays responsive while the filter finishes.
            Capture(dialog, "image-loading-cancelling.png");
            release.Set();
            await pending;
            Assert.Null(VisibleProgress());
            Assert.Contains("Import cancelled · 1 image(s) added", window.Status);
            Assert.Equal(2, window.SkyView.Layers.Count);
            Assert.DoesNotContain(remaining, attempted);
            string completedStatus = window.Status;
            lastReporter!.Report(new("Late progress from cancelled work", 99));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(completedStatus, window.Status);
            var layer = window.SkyView.Layers[0];
            var originalImage = layer.Image;
            var originalMode = layer.DisplayMode;
            release.Reset();
            Volatile.Write(ref holdAll, true);
            pending = window.ApplyDisplayAsync(layer, DisplayMode.Finished, new() { TargetBackground = 0.08 });
            dialog = await WaitForProgress();
            Assert.Equal("0 of 1 image finished · 1 remaining", Text(dialog, "CountText"));
            dialog.Close(); // Closing the progress window requests cancellation, rather than hiding the work.
            Assert.True(dialog.IsVisible);
            Assert.Equal("Stopping…", Text(dialog, "Heading"));
            release.Set();
            await pending;
            Assert.Same(originalImage, layer.Image);
            Assert.Equal(originalMode, layer.DisplayMode);
            Assert.Null(VisibleProgress());
            Assert.Equal("Display change cancelled", window.Status);
            release.Reset();
            var originalLayers = window.SkyView.Layers.ToArray();
            var center = window.SkyView.Camera.Center;
            string sessionPath = Path.Combine(directory, "cancel-loading.sky.json");
            var session = new SkySession(
                1,
                new(10, 20),
                12,
                true,
                true,
                true,
                true,
                [new(existing, 0, DisplayMode.Linear, true, 1, null), new(slow, 0, DisplayMode.Linear, true, 1, null)]
            );
            await session.WriteAsync(sessionPath, CancellationToken.None);
            pending = window.OpenSessionAsync(sessionPath);
            dialog = await WaitForProgress();
            Assert.Equal("0 of 2 images finished · 2 remaining", Text(dialog, "CountText"));
            dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(dialog),
                0,
                System.Windows.Input.Key.Escape
            )
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Assert.Equal("Stopping…", Text(dialog, "Heading"));
            release.Set();
            await pending;
            Assert.Equal(originalLayers, window.SkyView.Layers.ToArray());
            Assert.Equal(center, window.SkyView.Camera.Center);
            Assert.Null(VisibleProgress());
            Assert.Equal("Session open cancelled", window.Status);
            Volatile.Write(ref holdAll, false);
            await window.ImportAsync([remaining]);
            Assert.Contains(window.SkyView.Layers, image => image.Image.SourcePath == remaining);
            Assert.True(((UIElement)window.FindName("FileActions")).IsEnabled);
            Assert.Empty(Application.Current.Windows.OfType<ImageLoadProgressWindow>());
        }
        finally
        {
            Click((System.Windows.Controls.Button)window.FindName("CancelButton"));
            release.Set();
            if (pending is not null)
            {
                await pending;
            }

            loaderField.SetValue(window, originalLoader);
            window.SkyView.Layers.Clear();
        }
    }

    private static async Task VerifyObserver(MainWindow window, string directory)
    {
        Assert.Null(window.SkyView.Observer);
        Assert.Null(window.SkyView.Camera.Observer);
        Assert.Equal(Visibility.Collapsed, ((UIElement)window.FindName("Timeline")).Visibility);
        var button = (System.Windows.Controls.Button)window.FindName("OptionsButton");
        Exception? failure = null;

        void OpenObserver(Action<ObserverWindow> interact, bool save = true)
        {
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                var options = Application.Current.Windows.OfType<OptionsWindow>().Single();
                try
                {
                    ((System.Windows.Controls.TabControl)options.FindName("SettingsTabs")).SelectedIndex = 1;
                    options.UpdateLayout();
                    _ = window.Dispatcher.BeginInvoke(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<ObserverWindow>().Single();
                        try
                        {
                            interact(dialog);
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                            dialog.Close();
                        }
                        }, DispatcherPriority.ApplicationIdle);
                    FindButtons(options)
                        .Single(b => Equals(b.Content, "Choose on map…"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (save && failure is null)
                    {
                        ((System.Windows.Controls.Button)options.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else
                    {
                        options.Close();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                    options.Close();
                }
                }, DispatcherPriority.ApplicationIdle);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (failure is not null)
            {
                throw failure;
            }
        }

        OpenObserver(dialog =>
        {
            Assert.True(((System.Windows.Controls.CheckBox)dialog.FindName("UseSystemTime")).IsChecked);
            var apply = (System.Windows.Controls.Button)dialog.FindName("ApplyButton");
            Assert.False(apply.IsEnabled);
            var map = (SkyBrowser.Desktop.Controls.ObserverMap)dialog.FindName("Map");
            Assert.True(map.ActualWidth > 300);
            Assert.Null(map.Selection);
            map.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(map),
                0,
                System.Windows.Input.Key.Right
            )
            { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Assert.Equal(new ObserverLocation(0, 1), map.Selection);
            ((System.Windows.Controls.TextBox)dialog.FindName("LatitudeInput")).Text = "52.52";
            ((System.Windows.Controls.TextBox)dialog.FindName("LongitudeInput")).Text = "13.405";
            map.CenterOnSelection();
            Assert.Equal(new ObserverLocation(52.52, 13.405), map.Selection);
            var restored = map.LocationAt(map.Project(map.Selection!))!;
            Assert.Equal(52.52, restored.Latitude, 8);
            Assert.Equal(13.405, restored.Longitude, 8);
            Capture(dialog, "observer-location.png");
            ((System.Windows.Controls.CheckBox)dialog.FindName("UseSystemTime")).IsChecked = false;
            ((System.Windows.Controls.ComboBox)dialog.FindName("TimeZoneInput")).SelectedIndex = 1;
            ((System.Windows.Controls.DatePicker)dialog.FindName("DateInput")).SelectedDate = new(2026, 10, 15);
            ((System.Windows.Controls.TextBox)dialog.FindName("TimeInput")).Text = "18:00:00";
            ((System.Windows.Controls.TextBox)dialog.FindName("LatitudeInput")).Text = "91";
            Assert.False(apply.IsEnabled);
            ((System.Windows.Controls.TextBox)dialog.FindName("LatitudeInput")).Text = "52.52";
            Assert.True(apply.IsEnabled);
            Capture(dialog, "observer-custom-time.png");
            apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        });
        await WaitUntil(() => window.SkyView.Observer is not null && ((UIElement)window.FindName("FileActions")).IsEnabled);
        var observer = Assert.IsType<ObserverSettings>(window.SkyView.Observer);
        Assert.False(observer.UseSystemTime);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 18, 0, 0, TimeSpan.Zero), observer.TimeUtc);
        var frame = window.SkyView.Camera.Observer!;
        var direction = frame.ToHorizontal(window.SkyView.Camera.Center);
        Assert.Equal(180, direction.Azimuth, 7);
        Assert.Equal(20, direction.Altitude, 7);
        Capture(window, "observer-south.png");
        var center = window.SkyView.Camera.Center;
        string observerImage = Path.Combine(directory, "observer-test.xisf");
        XisfWriter.Write(observerImage, [TestImages.Create()]);
        await window.ImportAsync([observerImage]);
        Assert.Equal(center, window.SkyView.Camera.Center); // Imports must preserve the selected compass view.
        window.SkyView.AdvanceObserverTime(observer.TimeUtc.AddHours(4));
        Assert.Equal(center, window.SkyView.Camera.Center);
        string sessionPath = Path.Combine(directory, "observer.sky.json");
        await window.SaveSessionAsync(sessionPath);
        Assert.Equal(observer, (await SkySession.ReadAsync(sessionPath, CancellationToken.None)).Observer);
        window.ApplyObserver(null);
        await window.OpenSessionAsync(sessionPath);
        Assert.Equal(observer, window.SkyView.Observer);
        // Closing/cancelling the popup must preserve the current observer and view.
        OpenObserver(dialog => dialog.Close(), save: false);
        Assert.Equal(observer, window.SkyView.Observer);
        Assert.Equal(center, window.SkyView.Camera.Center);
        var live = observer with
        {
            UseSystemTime = true
        };
        window.ApplyObserver(live);
        var oldCenter = window.SkyView.Camera.Center;
        window.SkyView.AdvanceObserverTime(window.SkyView.Camera.Observer!.TimeUtc.AddHours(4));
        Assert.True(oldCenter.DistanceTo(window.SkyView.Camera.Center) > 30);
        direction = window.SkyView.Camera.Observer!.ToHorizontal(window.SkyView.Camera.Center);
        Assert.Equal(180, direction.Azimuth, 7);
        Assert.Equal(20, direction.Altitude, 7);
        await VerifySceneTimeline(window, directory);
        OpenObserver(dialog =>
        {
            var clear = FindButtons(dialog).Single(b => Equals(b.Content, "Use atlas view"));
            clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        });
        await WaitUntil(() => window.SkyView.Observer is null && ((UIElement)window.FindName("FileActions")).IsEnabled);
        Assert.Null(window.SkyView.Observer);
        Assert.Null(window.SkyView.Camera.Observer);
        Assert.Equal(Visibility.Collapsed, ((UIElement)window.FindName("Timeline")).Visibility);
        Assert.Equal(new SkyCoordinate(25, 38), window.SkyView.Camera.Center);
        Assert.Equal(75, window.SkyView.Camera.FieldOfView);
        window.SkyView.Layers.Clear();
    }

    private static async Task VerifySceneTimeline(MainWindow window, string directory)
    {
        static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        var timeline = (SkyBrowser.Desktop.Controls.SceneTimeline)window.FindName("Timeline");
        var slider = (System.Windows.Controls.Slider)timeline.FindName("TimeSlider");
        var mode = (System.Windows.Controls.TextBlock)timeline.FindName("TimeMode");
        var nextDay = (System.Windows.Controls.Button)timeline.FindName("NextDayButton");
        var previousDay = (System.Windows.Controls.Button)timeline.FindName("PreviousDayButton");
        var settings = window.SkyView.Observer! with
        {
            UseSystemTime = false,
            TimeUtc = new(2000, 1, 1, 23, 0, 0, TimeSpan.Zero)
        };
        var defaults = window.Options;
        window.ApplyObserver(settings);
        window.SkyView.CenterOn(window.SkyView.Camera.Observer!.FromHorizontal(137, 43), 17);
        var image = Assert.Single(window.SkyView.Layers).Image;
        Assert.True(timeline.IsVisible);
        slider.Value = 180;
        Assert.Equal(settings.TimeUtc.AddHours(3), window.SkyView.Observer!.TimeUtc);
        Assert.Equal("PAUSED", mode.Text);
        Assert.False(window.SkyView.Observer.UseSystemTime);
        var direction = window.SkyView.Camera.Observer!.ToHorizontal(window.SkyView.Camera.Center);
        Assert.Equal(137, direction.Azimuth, 7);
        Assert.Equal(43, direction.Altitude, 7);
        Assert.Equal(17, window.SkyView.Camera.FieldOfView);
        Assert.Same(image, Assert.Single(window.SkyView.Layers).Image);
        slider.Value = -360;
        Assert.Equal(settings.TimeUtc.AddHours(-6), window.SkyView.Observer.TimeUtc); // Same anchor for the whole drag.
        var pausedCenter = window.SkyView.Camera.Center;
        window.SkyView.AdvanceObserverTime(DateTimeOffset.UtcNow);
        Assert.Equal(pausedCenter, window.SkyView.Camera.Center);
        Click(nextDay);
        Assert.Equal(settings.TimeUtc.AddHours(18), window.SkyView.Observer.TimeUtc);
        Assert.Equal(0, slider.Value);
        Click(previousDay);
        Assert.Equal(settings.TimeUtc.AddHours(-6), window.SkyView.Observer.TimeUtc);
        slider.Focus();
        slider.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider), 0, System.Windows.Input.Key.Right)
        { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        Assert.Equal(settings.TimeUtc.AddHours(-6).AddMinutes(1), window.SkyView.Observer.TimeUtc);
        string session = Path.Combine(directory, "timeline.sky.json");
        await window.SaveSessionAsync(session);
        var paused = window.SkyView.Observer;
        window.ApplyObserver(null);
        await window.OpenSessionAsync(session);
        Assert.Equal(paused, window.SkyView.Observer);
        Assert.True(timeline.IsVisible);
        Assert.Equal("PAUSED", mode.Text);
        Assert.Equal(defaults, window.Options); // Scene edits never replace saved startup preferences.
        Capture(window, "scene-timeline.png");
        await VerifyNorthFacingSkyRotation(window);

        var earliest = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        window.ApplyObserver(settings with { TimeUtc = earliest.AddMinutes(30) });
        Assert.Equal(-30, slider.Minimum);
        slider.Value = slider.Minimum;
        Assert.Equal(earliest, window.SkyView.Observer!.TimeUtc);
        Assert.False(previousDay.IsEnabled);
        var latest = new DateTimeOffset(2101, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);
        window.ApplyObserver(settings with { TimeUtc = latest.AddMinutes(-30) });
        slider.Value = slider.Maximum;
        Assert.Equal(latest, window.SkyView.Observer.TimeUtc);
        Assert.False(nextDay.IsEnabled);
        var before = DateTimeOffset.UtcNow;
        Click((System.Windows.Controls.Button)timeline.FindName("LiveButton"));
        Assert.True(window.SkyView.Observer.UseSystemTime);
        Assert.InRange(window.SkyView.Camera.Observer!.TimeUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal("LIVE", mode.Text);
        Assert.Equal(0, slider.Value);
        Assert.Equal(defaults, window.Options);
        slider.Value = 60;
        Assert.False(window.SkyView.Observer.UseSystemTime); // Scrubbing a live clock pauses it immediately.
        Assert.Equal("PAUSED", mode.Text);
        Click((System.Windows.Controls.Button)timeline.FindName("LiveButton"));
    }

    private static async Task VerifyNorthFacingSkyRotation(MainWindow window)
    {
        var start = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        window.ApplyObserver(new(new(50, 0), false, start));
        var camera = window.SkyView.Camera;
        window.SkyView.CenterOn(camera.Observer!.FromHorizontal(0, 50), 100);
        var star = new SkyCoordinate(0, 65);
        double width = window.SkyView.ActualWidth;
        double height = window.SkyView.ActualHeight;
        var before = camera.Project(star.Vector, width, height)!.Value;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Capture(window, "observer-north-0h.png");
        var timeline = (SkyBrowser.Desktop.Controls.SceneTimeline)window.FindName("Timeline");
        ((System.Windows.Controls.Slider)timeline.FindName("TimeSlider")).Value = 360;
        Assert.Equal(start.AddHours(6), camera.Observer!.TimeUtc);
        var direction = camera.Observer.ToHorizontal(camera.Center);
        Assert.InRange(Math.Min(direction.Azimuth, 360 - direction.Azimuth), 0, 1e-7);
        Assert.Equal(50, direction.Altitude, 7);
        Assert.Equal(100, camera.FieldOfView);
        var after = camera.Project(star.Vector, width, height)!.Value;
        var a = new Vector(before.X - width / 2, before.Y - height / 2);
        var b = new Vector(after.X - width / 2, after.Y - height / 2);
        Assert.InRange(b.Length, a.Length - 0.05, a.Length + 0.05);
        Assert.InRange(Math.Atan2(Vector.CrossProduct(a, b), Vector.Multiply(a, b)) / SkyCoordinate.Radians, -91, -89);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Capture(window, "observer-north-6h.png");
        window.SkyView.CenterOn(camera.Observer.FromHorizontal(0, 70), 100);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Capture(window, "observer-pole-and-zenith.png");
    }

    private static async Task VerifyTargetPicker(MainWindow window, string directory)
    {
        var finder = (System.Windows.Controls.Expander)window.FindName("TargetFinder");
        finder.IsExpanded = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var targets = (System.Windows.Controls.ComboBox)window.FindName("TargetChoice");
        var search = (System.Windows.Controls.TextBox)window.FindName("TargetSearch");
        var catalog = (System.Windows.Controls.ComboBox)window.FindName("TargetCatalogChoice");
        Assert.Equal(DeepSkyCatalog.Targets.Count, targets.Items.Count);
        Assert.Empty(window.SkyView.AddedTargets);
        targets.IsDropDownOpen = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(targets.IsDropDownOpen);
        int realized = Enumerable.Range(0, targets.Items.Count)
            .Count(index => targets.ItemContainerGenerator.ContainerFromIndex(index) is not null);
        Assert.InRange(realized, 1, 100); // The full catalogue must not construct thousands of dropdown rows.
        var popup = (System.Windows.Controls.Primitives.Popup)targets.Template.FindName("PART_Popup", targets);
        CaptureElement((FrameworkElement)popup.Child, "target-picker-dropdown.png");
        targets.IsDropDownOpen = false;
        catalog.SelectedIndex = 1;
        Assert.Equal(110, targets.Items.Count);
        catalog.SelectedIndex = 2;
        Assert.Equal(109, targets.Items.Count);
        catalog.SelectedIndex = 3;
        Assert.True(targets.Items.Count > 7800);
        catalog.SelectedIndex = 4;
        Assert.True(targets.Items.Count > 5000);
        catalog.SelectedIndex = 0;

        async Task Choose(string query)
        {
            search.Text = query;
            var target = (DeepSkyTarget)targets.Items[0];
            targets.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var item = Assert.IsType<System.Windows.Controls.ComboBoxItem>(targets.ItemContainerGenerator.ContainerFromItem(target));
            item.SetCurrentValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty, true);
            targets.IsDropDownOpen = false;
            Assert.Same(target, targets.SelectedItem);
            Assert.True(window.SkyView.Camera.Center.DistanceTo(target.Position) < 1e-10);
            Assert.Equal(target.SuggestedFieldOfView, window.SkyView.Camera.FieldOfView);
            Assert.Contains(window.SkyView.AddedTargets, added => added.Id == target.Id);
            Assert.Equal(
                target.Position.RightAscension.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                ((System.Windows.Controls.TextBox)window.FindName("GoRa")).Text
            );
        }

        var obscure = DeepSkyCatalog.Find("IC5386")!;
        window.SkyView.CenterOn(obscure.Position, obscure.SuggestedFieldOfView);
        Point marker = new(window.SkyView.ActualWidth / 2 + 5, window.SkyView.ActualHeight / 2);
        byte[] before = PixelAt(window.SkyView, marker);
        await Choose("IC 5386");
        Assert.NotEqual(before, PixelAt(window.SkyView, marker));
        await Choose("Caldwell 109");
        await Choose("M110");
        await Choose("Caldwell 65");
        int count = window.SkyView.AddedTargets.Count;
        await Choose("NGC 253");
        Assert.Equal(count, window.SkyView.AddedTargets.Count);
        window.SkyView.CenterOn(new(0, 0), 90);
        var go = (System.Windows.Controls.Button)window.FindName("TargetGoButton");
        var ra = (System.Windows.Controls.TextBox)window.FindName("GoRa");
        var dec = (System.Windows.Controls.TextBox)window.FindName("GoDec");

        void Go() => go.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Go();
        Assert.True(window.SkyView.Camera.Center.DistanceTo(DeepSkyCatalog.Find("C65")!.Position) < 1e-10);
        Assert.Equal(DeepSkyCatalog.Find("C65")!.SuggestedFieldOfView, window.SkyView.Camera.FieldOfView);
        Capture(window, "target-picker-selected.png");
        string targetRa = ra.Text, targetDec = dec.Text;
        double fieldOfView = window.SkyView.Camera.FieldOfView;
        ra.Text = "383.5";
        dec.Text = "-12.25";
        Go();
        Assert.Equal(new SkyCoordinate(23.5, -12.25), window.SkyView.Camera.Center);
        Assert.Equal(fieldOfView, window.SkyView.Camera.FieldOfView);
        Assert.Equal(count, window.SkyView.AddedTargets.Count);
        dec.Text = "91";
        Go();
        Assert.Contains("Declination must be", window.Status);
        Assert.Equal(new SkyCoordinate(23.5, -12.25), window.SkyView.Camera.Center);
        ra.Text = targetRa;
        dec.Text = targetDec;
        Go();
        Assert.True(window.SkyView.Camera.Center.DistanceTo(DeepSkyCatalog.Find("C65")!.Position) < 1e-10);
        search.Text = "no-matching-target-987654";
        Assert.Empty(targets.Items);
        Assert.False(targets.IsEnabled);
        Assert.True(go.IsEnabled);
        ra.Text = "-0.25";
        dec.Text = "9.25";
        Go();
        Assert.Equal(new SkyCoordinate(359.75, 9.25), window.SkyView.Camera.Center);
        Assert.Equal(count, window.SkyView.AddedTargets.Count);
        search.Clear();
        Assert.Equal(DeepSkyCatalog.Targets.Count, targets.Items.Count);
        string path = Path.Combine(directory, "catalog-targets.sky.json");
        await window.SaveSessionAsync(path);
        Assert.Equal(count, (await SkySession.ReadAsync(path, CancellationToken.None)).AddedTargets.Length);
        ((System.Windows.Controls.Button)window.FindName("ClearTargetsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Empty(window.SkyView.AddedTargets);
        await window.OpenSessionAsync(path);
        Assert.Equal(count, window.SkyView.AddedTargets.Count);
        window.SkyView.ClearAddedTargets();
        targets.SelectedIndex = -1;
        finder.IsExpanded = false;
    }

    private static void CaptureElement(FrameworkElement element, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SKYBROWSER_SCREENSHOTS");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name));
        encoder.Save(file);
    }

    private static async Task VerifyCollectionSearch(MainWindow window, string directory)
    {
        string folder = Path.Combine(directory, "search-fixtures"), other = Path.Combine(directory, "other-target");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(other);
        var field = TestImages.Create();
        field.FitsKeywords[0] = XisfFitsKeyword.FromValue("OBJECT", "Target field");
        field.Properties.Set("Observation:CatalogIdentifier", "IC 434");
        field.FitsKeywords.Add(XisfFitsKeyword.FromValue("TELESCOP", "Ritchey–Chrétien 8"));
        field.FitsKeywords.Add(XisfFitsKeyword.FromValue("FILTER", "H-alpha", "Hydrogen narrowband"));
        var detail = TestImages.Create();
        detail.FitsKeywords[0] = XisfFitsKeyword.FromValue("OBJECT", "Detail field");
        var m31 = TestImages.Create();
        m31.FitsKeywords[0] = XisfFitsKeyword.FromValue("OBJECT", "M 31");
        string wide = Path.Combine(folder, "wide_field.xisf"), narrow = Path.Combine(folder, "IC434_detail.xisf"), andromeda = Path.Combine(other, "andromeda.xisf");
        XisfWriter.Write(wide, [field]);
        XisfWriter.Write(narrow, [detail]);
        XisfWriter.Write(andromeda, [m31]);
        await window.ImportAsync([wide, narrow, andromeda]);
        var list = (System.Windows.Controls.ListBox)window.FindName("LayerList");
        var search = (System.Windows.Controls.TextBox)window.FindName("CollectionSearch");
        var count = (System.Windows.Controls.TextBlock)window.FindName("LayerCount");
        var original = window.SkyView.Layers.Select(layer => layer.Image).ToArray();
        var center = window.SkyView.Camera.Center;
        search.Text = "ic434";
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("2 / 3", count.Text);
        search.Text = "IC 434";
        Assert.Equal(2, list.Items.Count);
        Capture(window, "collection-search.png");
        search.Text = "ic434 chretien h alpha";
        Assert.Same(
            window.SkyView.Layers.Single(layer => layer.Image.SourcePath == wide),
            Assert.Single(list.Items.Cast<object>())
        );
        search.Text = "no-such-target-987654";
        Assert.Empty(list.Items);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("NoSearchResults")).Visibility);
        Assert.Equal("0 / 3", count.Text);
        Capture(window, "collection-no-results.png");
        Assert.Equal(center, window.SkyView.Camera.Center);
        Assert.Equal(original, window.SkyView.Layers.Select(layer => layer.Image));
        Assert.All(window.SkyView.Layers, layer => Assert.True(layer.IsVisible));
        search.Text = "IC 434";
        string extra = Path.Combine(folder, "ic_434_extra.xisf");
        XisfWriter.Write(extra, [detail]);
        await window.ImportAsync([extra]);
        Assert.Equal(3, list.Items.Count);
        Assert.Equal("3 / 4", count.Text);
        list.SelectedIndex = 0;
        ((System.Windows.Controls.Button)window.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(3, window.SkyView.Layers.Count);
        Assert.NotNull(list.SelectedItem);
        await window.BrowseFolderAsync(other);
        Assert.Empty(list.Items);
        Assert.Single(window.SkyView.Layers);
        Assert.Equal("IC 434", search.Text);
        search.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(search),
            0,
            System.Windows.Input.Key.Escape
        )
        { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        Assert.Equal("", search.Text);
        Assert.Single(list.Items);
        search.Text = "m31";
        Assert.Single(list.Items);
        ((System.Windows.Controls.Button)window.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal("", search.Text);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("NoSearchResults")).Visibility);
        window.SkyView.Layers.Clear();
    }

    private static async Task VerifyImageInfo(
        MainWindow window,
        SkyBrowser.Desktop.Models.SkyImageLayer layer,
        bool missing = false,
        string screenshot = "image-details.png"
    )
    {
        var list = (System.Windows.Controls.ListBox)window.FindName("LayerList");
        list.ScrollIntoView(layer);
        window.UpdateLayout();
        var button = FindButtons(list).Single(button => button.Name == "ImageInfoButton" && ReferenceEquals(button.DataContext, layer));
        var center = window.SkyView.Camera.Center;
        var originalImage = layer.Image;
        Exception? failure = null;
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            var dialog = Application.Current.Windows.OfType<ImageDetailsWindow>().Single();
            try
            {
                Assert.Equal(layer.Image.SourcePath, ((System.Windows.Controls.TextBox)dialog.FindName("FilePathText")).Text);
                Assert.Equal(layer.Name, ((System.Windows.Controls.TextBlock)dialog.FindName("ImageName")).Text);
                var fileFacts = (System.Windows.Controls.TextBox)dialog.FindName("FileFacts");
                await WaitUntil(() => !fileFacts.Text.StartsWith("Reading"));
                Assert.Contains(missing ? "unavailable" : "bytes", fileFacts.Text);
                var grid = (System.Windows.Controls.DataGrid)dialog.FindName("MetadataGrid");
                Assert.Equal(layer.Image.Details.Entries.Length, grid.Items.Count);
                var search = (System.Windows.Controls.TextBox)dialog.FindName("MetadataSearch");
                search.Text = "diMensions";
                Assert.True(grid.Items.Count > 0);
                grid.SelectedIndex = 0;
                Assert.Contains("Dimensions", ((System.Windows.Controls.TextBox)dialog.FindName("SelectedMetadata")).Text);
                search.Text = "no-matching-metadata-123456";
                Assert.Empty(grid.Items);
                search.Text = "";
                grid.SelectedItem = layer.Image.Details.Entries.FirstOrDefault(entry => entry.Source == "XISF") ?? layer.Image.Details.Entries.First();
                grid.ScrollIntoView(grid.SelectedItem);
                Capture(dialog, missing ? "image-details-missing.png" : screenshot);
                dialog.Width = 700;
                dialog.Height = 640;
                dialog.UpdateLayout();
                Assert.True(grid.ActualHeight >= 100);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
            }
            }, DispatcherPriority.ApplicationIdle);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (failure is not null)
        {
            throw failure;
        }

        Assert.Equal(center, window.SkyView.Camera.Center);
        Assert.Same(originalImage, layer.Image);
    }

    private static async Task VerifyOptions(MainWindow window, string directory, string preferredImage, SkyFinishSettings finish)
    {
        var original = window.Options;
        var optionsButton = (System.Windows.Controls.Button)window.FindName("OptionsButton");
        Exception? failure = null;

        static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        void ChooseLocation(OptionsWindow options)
        {
            options.UpdateLayout();
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                var mapDialog = Application.Current.Windows.OfType<ObserverWindow>().Single();
                ((System.Windows.Controls.TextBox)mapDialog.FindName("LatitudeInput")).Text = "52.52";
                ((System.Windows.Controls.TextBox)mapDialog.FindName("LongitudeInput")).Text = "13.405";
                Click((System.Windows.Controls.Button)mapDialog.FindName("ApplyButton"));
                }, DispatcherPriority.ApplicationIdle);
            Click(FindButtons(options).Single(b => Equals(b.Content, "Choose on map…")));
        }

        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<OptionsWindow>().Single();
            try
            {
                Assert.Equal("10", ((System.Windows.Controls.TextBox)dialog.FindName("CacheSizeInput")).Text);
                var tabs = (System.Windows.Controls.TabControl)dialog.FindName("SettingsTabs");
                tabs.SelectedIndex = 1;
                ChooseLocation(dialog);
                Assert.Null(window.Options.Observer);
                Assert.Null(window.SkyView.Observer);
                dialog.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
                dialog.Close();
            }
            }, DispatcherPriority.ApplicationIdle);
        Click(optionsButton);
        if (failure is not null)
        {
            throw failure;
        }

        Assert.Equal(original, window.Options);
        string newCache = Path.Combine(directory, "relocated-cache"), blocked = Path.Combine(directory, "blocked-cache");
        File.WriteAllText(blocked, "occupied");
        string? optionsPath = null;
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            var dialog = Application.Current.Windows.OfType<OptionsWindow>().Single();
            try
            {
                var tabs = (System.Windows.Controls.TabControl)dialog.FindName("SettingsTabs");
                var size = (System.Windows.Controls.TextBox)dialog.FindName("CacheSizeInput");
                var save = (System.Windows.Controls.Button)dialog.FindName("SaveButton");
                size.Text = "0";
                Assert.False(save.IsEnabled);
                size.Text = "1.5";
                Assert.True(save.IsEnabled);
                ((System.Windows.Controls.ComboBox)dialog.FindName("StretchChoice")).SelectedIndex = (int)DisplayMode.Linear;
                ((System.Windows.Controls.TextBox)dialog.FindName("BackgroundInput")).Text = "7";
                ((System.Windows.Controls.CheckBox)dialog.FindName("ReduceNoise")).IsChecked = false;
                Capture(dialog, "options-images.png");
                tabs.SelectedIndex = 1;
                ChooseLocation(dialog);
                Capture(dialog, "options-observer.png");
                tabs.SelectedIndex = 3;
                ((System.Windows.Controls.CheckBox)dialog.FindName("ShowGrid")).IsChecked = false;
                ((System.Windows.Controls.CheckBox)dialog.FindName("BlendOverlaps")).IsChecked = false;
                Capture(dialog, "options-sky.png");
                tabs.SelectedIndex = 2;
                var folder = (System.Windows.Controls.TextBox)dialog.FindName("CacheDirectoryInput");
                folder.Text = blocked;
                optionsPath = ((System.Windows.Controls.TextBox)dialog.FindName("OptionsFilePath")).Text;
                Click(save);
                await WaitUntil(() => save.IsEnabled);
                Assert.Contains("Could not save", ((System.Windows.Controls.TextBlock)dialog.FindName("ValidationMessage")).Text);
                Assert.Equal(original, window.Options);
                folder.Text = newCache;
                var usage = (System.Windows.Controls.TextBlock)dialog.FindName("CacheUsageText");
                await WaitUntil(() => !usage.Text.Contains("loading"));
                Assert.True(usage.Text.Contains("previews"), $"{usage.Text}: {usage.ToolTip}");
                Assert.Contains("Used:", usage.Text);
                Capture(dialog, "options-cache.png");
                Click(save);
            }
            catch (Exception exception)
            {
                failure = exception;
                dialog.Close();
            }
            }, DispatcherPriority.ApplicationIdle);
        Click(optionsButton);
        if (failure is not null)
        {
            throw failure;
        }

        Assert.Equal(1.5, window.Options.CacheSizeGb);
        Assert.Equal(newCache, window.Options.CacheDirectory);
        Assert.Equal(DisplayMode.Linear, window.Options.DefaultDisplayMode);
        Assert.Equal(0.07, window.Options.DefaultFinishSettings.TargetBackground);
        Assert.False(window.SkyView.BlendOverlaps);
        Assert.Equal(new(52.52, 13.405), window.Options.Observer!.Location);
        Assert.Equal(original.CacheDirectory, Path.GetDirectoryName(optionsPath));
        var restarted = new MainWindow(new ApplicationOptionsStore(optionsPath!, original))
        {
            ShowInTaskbar = false
        };
        try
        {
            restarted.Show();
            Assert.Equal(window.Options, restarted.Options);
            Assert.Equal(window.Options.Observer, restarted.SkyView.Observer);
            Assert.False(((System.Windows.Controls.CheckBox)restarted.FindName("GridToggle")).IsChecked);
            Assert.False(restarted.SkyView.BlendOverlaps);
            await restarted.ImportAsync([preferredImage]);
            var preferred = Assert.Single(restarted.SkyView.Layers);
            Assert.True(preferred.Image.IsFromCache);
            Assert.Equal(DisplayMode.Finished, preferred.DisplayMode);
            Assert.Equal(finish, preferred.Image.FinishSettings);
            string fresh = Path.Combine(directory, "default-stretch.xisf");
            XisfWriter.Write(fresh, [TestImages.Create(true)]);
            await restarted.ImportAsync([fresh]);
            var added = restarted.SkyView.Layers.Last();
            Assert.Equal(DisplayMode.Linear, added.DisplayMode);
            Assert.Equal(window.Options.DefaultFinishSettings, added.Image.FinishSettings);
        }
        finally
        {
            restarted.Close();
        }

        var loadedImages = window.SkyView.Layers.Select(layer => layer.Image).ToArray();
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            var dialog = Application.Current.Windows.OfType<OptionsWindow>().Single();
            try
            {
                ((System.Windows.Controls.TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 2;
                var usage = (System.Windows.Controls.TextBlock)dialog.FindName("CacheUsageText");
                await WaitUntil(() => !usage.Text.Contains("loading"));
                Assert.DoesNotContain("· 0 previews", usage.Text);
                var clear = (System.Windows.Controls.Button)dialog.FindName("ClearPreviewsButton");
                Click(clear);
                await WaitUntil(() => clear.IsEnabled);
                Assert.Contains("Used: 0 bytes · 0 previews", usage.Text);
                Assert.Contains("Saved previews cleared", ((System.Windows.Controls.TextBlock)dialog.FindName("ValidationMessage")).Text);
                Assert.Equal(loadedImages, window.SkyView.Layers.Select(layer => layer.Image).ToArray());
                ((System.Windows.Controls.TextBox)dialog.FindName("CacheSizeInput")).Text = "0";
                Click(clear);
                await WaitUntil(() => clear.IsEnabled);
                Assert.False(((System.Windows.Controls.Button)dialog.FindName("SaveButton")).IsEnabled);
                Capture(dialog, "options-cache-cleared.png");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
            }
            }, DispatcherPriority.ApplicationIdle);
        Click(optionsButton);
        if (failure is not null)
        {
            throw failure;
        }

        await window.ApplyOptionsAsync(original);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static IEnumerable<System.Windows.Controls.Button> FindButtons(DependencyObject root)
    {
        if (root is System.Windows.Controls.Button button)
        {
            yield return button;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }

    private static void VerifyBlending(MainWindow window)
    {
        var sky = window.SkyView;
        var staged = SolidLayer("Staged", 2, 100, 120, 150);
        var mesh = SkyBrowser.Desktop.Controls.SkyViewport.CreateMesh(staged.Placement!);
        var secondMesh = SkyBrowser.Desktop.Controls.SkyViewport.CreateMesh(staged.Placement!);
        Assert.True(mesh.IsFrozen);
        Assert.True(mesh.TextureCoordinates.IsFrozen);
        Assert.True(mesh.TriangleIndices.IsFrozen);
        Assert.Same(mesh.TextureCoordinates, secondMesh.TextureCoordinates);
        Assert.Same(mesh.TriangleIndices, secondMesh.TriangleIndices);
        Assert.Equal(33 * 33, mesh.Positions.Count);
        Assert.Equal(32 * 32 * 6, mesh.TriangleIndices.Count);
        int changes = 0;

        void ViewChanged(object? sender, EventArgs e) => changes++;
        sky.ViewChanged += ViewChanged;
        using (sky.DeferLayerUpdates())
        {
            using (sky.DeferLayerUpdates())
            {
                sky.Layers.Add(staged);
            }

            staged.IsVisible = false;
            staged.IsVisible = true;
            Assert.Equal(0, changes);
        }

        Assert.Equal(1, changes);
        sky.Layers.Clear();
        int afterReset = changes;
        staged.IsVisible = false;
        Assert.Equal(afterReset, changes); // A cleared layer must no longer rebuild its old viewport.
        sky.ViewChanged -= ViewChanged;
        var big = SolidLayer("Wide field", 4, 0, 0, 255);
        var small = SolidLayer("Detail", 1, 255, 0, 0);
        small.SetManual(new(359.95, 70, 1, 37));
        // Deliberately load the small image first: sky area must determine compositing order.
        sky.Layers.Add(small);
        sky.Layers.Add(big);
        sky.CenterOn(new(359.95, 70), 6);
        sky.UpdateLayout();
        var center = sky.Camera.Project(small.Placement!.Center.Vector, sky.ActualWidth, sky.ActualHeight)!.Value;
        Point pointer = new(center.X, center.Y);
        Assert.True(sky.IsBlendedInset(small));
        Assert.False(sky.IsBlendedInset(big));
        Assert.Same(small, sky.ImageAt(pointer));
        // Earlier desktop scenarios can leave a pointer in the viewport. The baseline
        // samples the unhovered blend, independently of the runner's cursor position.
        sky.UpdateHover(null);
        Assert.Null(sky.HoveredLayer);
        var blended = PixelAt(sky, pointer);
        Assert.InRange(blended[2], 150, 156);
        Assert.InRange(blended[0], 99, 105);
        sky.UpdateHover(pointer);
        var solid = PixelAt(sky, pointer);
        Assert.InRange(solid[2], 253, 255);
        Assert.InRange(solid[0], 0, 2);
        sky.UpdateHover(null);
        Assert.Equal(blended, PixelAt(sky, pointer));
        // Hovering outside the rotated inset must hit the background rather than a rectangular bound.
        var outside = sky.Camera.Project(small.Placement.At(-0.05, 0.5).Vector, sky.ActualWidth, sky.ActualHeight)!.Value;
        sky.UpdateHover(new Point(outside.X, outside.Y));
        Assert.Same(big, sky.HoveredLayer);
        Assert.Equal(blended, PixelAt(sky, pointer));
        big.IsVisible = false;
        Assert.False(sky.IsBlendedInset(small));
        Assert.Equal(solid, PixelAt(sky, pointer));
        big.IsVisible = true;
        sky.UpdateHover(null);
        small.Opacity = 0.5;
        sky.UpdateHover(pointer);
        var manual = PixelAt(sky, pointer);
        Assert.Equal(solid, manual);
        Assert.Equal(0.5, small.Opacity);
        sky.UpdateHover(null);
        var manualBlend = PixelAt(sky, pointer);
        Assert.InRange(manualBlend[2], 74, 79);
        Assert.InRange(manualBlend[0], 175, 181);
        small.Opacity = 1;
        sky.UpdateHover(null);
        sky.BlendOverlaps = false;
        var background = PixelAt(sky, pointer);
        Assert.InRange(background[2], 0, 2);
        Assert.InRange(background[0], 253, 255);
        sky.BlendOverlaps = true;
        Assert.Equal(blended, PixelAt(sky, pointer));
        var detail = SolidLayer("Nested detail", 0.4, 0, 255, 0);
        sky.Layers.Add(detail);
        Assert.True(sky.IsBlendedInset(detail));
        sky.UpdateHover(pointer);
        Assert.Same(detail, sky.HoveredLayer);
        var nested = PixelAt(sky, pointer);
        Capture(window, "blending-nested-hover.png");
        big.IsVisible = false;
        small.IsVisible = false;
        Assert.Equal(PixelAt(sky, pointer), nested);
        Assert.True(nested[1] > nested[2] + 100 && nested[1] > nested[0] + 100);
        sky.CenterOn(new(180, 0), 6);
        Assert.Null(sky.HoveredLayer);
        sky.UpdateHover(null);
        sky.Layers.Clear();
        sky.CenterOn(new(25, 38), 75);
    }

    private static void SetBlending(MainWindow window, bool enabled)
    {
        var toggle = (System.Windows.Controls.CheckBox)window.FindName("BlendToggle");
        toggle.IsChecked = enabled;
        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(enabled, window.SkyView.BlendOverlaps);
    }

    private static SkyBrowser.Desktop.Models.SkyImageLayer SolidLayer(string name, double width, byte red, byte green, byte blue)
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 255;
        }

        var placement = ImagePlacement.FromManual(new(359.95, 70, width, 0), 64, 64);
        return new(new LoadedSkyImage(name, 0, name, 64, 64, new(64, 64, pixels), "Linear", placement, null, ""));
    }

    private static byte[] PixelAt(SkyBrowser.Desktop.Controls.SkyViewport sky, Point point)
    {
        var window = Window.GetWindow(sky);
        window.UpdateLayout();
        // Render the root so layout offsets in the nested sky grid are accounted for.
        var screen = sky.TranslatePoint(point, window);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        byte[] color = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)screen.X, (int)screen.Y, 1, 1), color, 4, 0);
        return color;
    }

    private static void Capture(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SKYBROWSER_SCREENSHOTS");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name));
        encoder.Save(file);
    }

    private static void SavePreview(DisplayPixels preview, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SKYBROWSER_SCREENSHOTS");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        var bitmap = BitmapSource.Create(preview.Width, preview.Height, 96, 96, PixelFormats.Bgra32, null, preview.Bgra, preview.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name));
        encoder.Save(file);
    }

    [Fact]
    public void InvalidSessionSettingsAreRejected()
    {
        var session = new SkySession(1, new(0, 91), 60, true, true, true, true, []);
        Assert.Throws<InvalidDataException>(session.Validate);
        Assert.Throws<InvalidDataException>(() => (session with { Version = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (session with { Center = new(0, 0), FieldOfView = double.NaN }).Validate());
    }
}
