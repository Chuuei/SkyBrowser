using System.IO;
using System.Windows.Threading;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
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
