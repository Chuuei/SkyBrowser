using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static async Task VerifyForgettingAstapSolution(MainWindow owner, string directory)
    {
        string root = Path.Combine(directory, "forget-solution");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "multi.xisf");
        var plain = XisfImageData.FromSamples(Enumerable.Repeat(.1f, 64 * 48).ToArray(), [64, 48], 1);
        var native = XisfImageData.FromSamples(Enumerable.Repeat(.1f, 64 * 48).ToArray(), [64, 48], 1);
        XisfAstrometricSolution.CreateLinear("Gnomonic", [10, 20], [32, 24], new double[,] { { -.01, 0 }, { 0, .01 } }).ApplyTo(native);
        XisfWriter.Write(file, [plain, native]);
        byte[] before = SHA256.HashData(File.ReadAllBytes(file));
        var options = AstapTests.FakeOptions() with { CacheDirectory = Path.Combine(root, "cache") };
        var store = new ApplicationOptionsStore(Path.Combine(root, "settings.json"), options);
        store.Write(options);
        var window = new MainWindow(store) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show();
            await window.ImportAsync([file]);
            var sky = window.SkyView;
            var first = sky.Layers[0];
            var second = sky.Layers[1];
            var filePlacement = second.Placement;
            var select = (ListBox)window.FindName("LayerList");
            var forget = (Button)window.FindName("ForgetSolutionButton");
            select.SelectedItem = first;
            Assert.False(forget.IsEnabled);
            await window.SolveImageAsync(first);
            await window.SolveImageAsync(second);
            select.SelectedItem = first;
            Assert.True(forget.IsEnabled);
            Assert.True(((Button)window.FindName("SaveSolvedImageButton")).IsEnabled);
            ((Expander)window.FindName("InspectorExpander")).IsExpanded = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(forget.IsVisible);
            forget.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(window, "forget-astap-action.png");
            first.Opacity = .37;
            first.IsVisible = false;
            var bitmap = first.Bitmap;
            var preview = first.Image.Preview;
            var center = sky.Camera.Center;
            double fov = sky.Camera.FieldOfView;
            forget.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitUntil(() => !first.Image.HasAstapSolution && !forget.IsEnabled);
            Assert.Null(first.Placement);
            Assert.Same(bitmap, first.Bitmap);
            Assert.Same(preview, first.Image.Preview);
            Assert.Equal(.37, first.Opacity);
            Assert.False(first.IsVisible);
            Assert.True(second.Image.HasAstapSolution);
            Assert.False(((Button)window.FindName("SaveSolvedImageButton")).IsEnabled);
            Assert.False(((Button)window.FindName("FitButton")).IsEnabled);
            Assert.Equal(center, sky.Camera.Center);
            Assert.Equal(fov, sky.Camera.FieldOfView);
            Assert.Contains("needs placement", window.Status);
            Assert.False(await window.ForgetSolutionAsync(first));

            var manual = new ManualPlacement(80, -5, 3, 20);
            second.SetManual(manual);
            select.SelectedItem = second;
            Assert.True(forget.IsEnabled);
            Assert.True(await window.ForgetSolutionAsync(second));
            Assert.Equal(manual, second.Manual);
            Assert.Contains("manual placement retained", window.Status);
            Assert.Same(filePlacement, second.Image.Placement);
            second.SetManual(null);
            Assert.Same(filePlacement, second.Placement);
            select.SelectedItem = null;
            select.SelectedItem = second;
            Assert.False(forget.IsEnabled);
            Assert.True(((Button)window.FindName("FitButton")).IsEnabled);
            Assert.False(((Button)window.FindName("SaveSolvedImageButton")).IsEnabled);
            Capture(window, "forgot-astap-solution.png");
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(file)));
        }
        finally
        {
            window.Close();
        }
        using var reopenedCache = new SkyBrowser.Core.Caching.ProcessedImageCache(options.CacheDirectory);
        var reopenedLibrary = new SkyBrowser.Core.Library.ImageLibrary(Path.Combine(root, "library-v1.sqlite3"));
        var restarted = new SkyBrowser.Core.Caching.CachedSkyImageLoader(reopenedCache)
        { Library = reopenedLibrary, DefaultDisplayMode = options.DefaultDisplayMode }.LoadForBrowsing(file);
        Assert.All(restarted, image => Assert.False(image.HasAstapSolution));
        Assert.Null(restarted[0].Placement);
        Assert.NotNull(restarted[1].Placement);
    }

    private static async Task VerifyCameraSipPlacement(MainWindow window, string directory)
    {
        string file = Path.Combine(directory, "camera-sip.fits");
        FitsWriter.Write(file, FitsConverter.FromXisf([TestImages.CameraSip()], ConversionLossPolicy.Allow));
        var sky = window.SkyView;
        var previousLayers = sky.Layers.ToArray();
        var previousCenter = sky.Camera.Center;
        double previousField = sky.Camera.FieldOfView;
        bool grid = sky.ShowGrid, labels = sky.ShowLabels, footprints = sky.ShowFootprints;
        try
        {
            sky.Layers.Clear();
            sky.ShowGrid = sky.ShowLabels = sky.ShowFootprints = false;
            await window.ImportAsync([file]);
            var layer = Assert.Single(sky.Layers);
            Assert.Contains("TAN-SIP", layer.Placement!.Description);
            Assert.Null(layer.Image.PlacementWarning);
            await window.ApplyDisplayAsync(layer, DisplayMode.Linear, SkyFinishSettings.Default);
            sky.Fit(layer);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var point = sky.Camera.Project(layer.Placement.At(0.25, 0.25).Vector, sky.ActualWidth, sky.ActualHeight)!.Value;
            var pixel = PixelAt(sky, new(point.X, point.Y));
            Assert.InRange(pixel[0], 195, 210);
            Assert.InRange(pixel[1], 195, 210);
            Assert.InRange(pixel[2], 195, 210);
            Capture(window, "camera-sip-sky.png");
        }
        finally
        {
            sky.Layers.Clear();
            foreach (var layer in previousLayers)
            {
                sky.Layers.Add(layer);
            }
            sky.Camera.Center = previousCenter;
            sky.Camera.FieldOfView = previousField;
            sky.ShowGrid = grid;
            sky.ShowLabels = labels;
            sky.ShowFootprints = footprints;
        }
    }
}
