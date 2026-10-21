using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static async Task VerifySkyTools(MainWindow owner, string directory)
    {
        var sky = owner.SkyView;
        var oldOptions = owner.Options;
        var originalCenter = sky.Camera.Center;
        double originalField = sky.Camera.FieldOfView;
        bool grid = sky.ShowGrid, labels = sky.ShowLabels, footprints = sky.ShowFootprints;
        var request = new SurveyRequest(SurveyDefinition.Color.Id, new(359.95, 70), 4, 128);
        byte[] fits = SurveyTests.FitsFixture(directory, request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int handlerCalls = 0;
        var handler = new SurveyTests.Handler(async (_, token) =>
        {
            if (handlerCalls++ == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fits) };
            }
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Cancelled survey unexpectedly completed.");
        });
        using var client = new HttpClient(handler);
        var service = new SurveyImageService(Path.Combine(directory, "sky-tools-surveys"), client);
        var imageList = (ListBox)owner.FindName("LayerList");
        var image = SolidLayer("Measurement selection context", 2, 90, 100, 110);
        sky.Layers.Add(image);
        imageList.SelectedItem = image;
        var window = new SkyToolsWindow(sky, service) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show();
            await window.InitializeAsync();
            Assert.Equal(0, handler.Calls); // Opening a tool never downloads imagery.
            Assert.Same(image, imageList.SelectedItem); // Opening tools keeps image editing available.
            Assert.Equal(new[] { "Survey background", "Target visibility", "Measurements", "Framing & mosaics" },
                ((TabControl)window.FindName("ToolsTabs")).Items.Cast<TabItem>().Select(tab => tab.Header.ToString()).ToArray());
            ((TabControl)window.FindName("ToolsTabs")).SelectedIndex = 1;
            ((TextBox)window.FindName("PlanRa")).Text = "83.822";
            ((TextBox)window.FindName("PlanDec")).Text = "-5.391";
            ((TextBox)window.FindName("PlanLatitude")).Text = "0";
            ((TextBox)window.FindName("PlanLongitude")).Text = "0";
            ((DatePicker)window.FindName("PlanDate")).SelectedDate = new(2000, 1, 1);
            var report = await window.CalculateVisibilityAsync();
            Assert.NotNull(report);
            Assert.Equal(721, report.Samples.Length);
            Assert.Contains("UTC", ((TextBlock)window.FindName("VisibilitySummary")).Text);
            Assert.NotEmpty(report.Darkness);
            Assert.Equal(oldOptions, owner.Options);
            Capture(window, "sky-tools-visibility.png");
            await VerifyFraming(owner, window, directory);
            ((TabControl)window.FindName("ToolsTabs")).SelectedIndex = 2;
            foreach (string control in new[] { "ScaleBarChoice", "NorthEastChoice", "CatalogOverlayChoice" })
            {
                var choice = (CheckBox)window.FindName(control);
                choice.IsChecked = true;
                choice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            sky.BeginMeasurement();
            Assert.Empty(imageList.SelectedItems);
            Assert.Null(sky.SelectedLayer);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)owner.FindName("InspectorBorder")).Visibility);
            imageList.SelectedItem = image;
            Assert.False(sky.IsMeasuring); // Returning to image editing cancels unfinished sky-point picking.
            sky.BeginMeasurement();
            sky.PickMeasurementPoint(new(359, 0));
            Assert.True(sky.IsMeasuring);
            sky.PickMeasurementPoint(new(1, 0));
            Assert.False(sky.IsMeasuring);
            Assert.Equal(2, sky.Measurement!.Degrees, 9);
            Assert.Contains("2°", ((TextBlock)window.FindName("MeasurementSummary")).Text);
            var distanceA = (TextBox)window.FindName("MeasurementDistanceA");
            var distanceB = (TextBox)window.FindName("MeasurementDistanceB");
            imageList.SelectedItem = image;
            distanceA.Text = "1000";
            Assert.Empty(imageList.SelectedItems);
            Assert.Null(sky.Measurement.SeparationLightYears);
            distanceB.Text = "1000";
            Assert.InRange(sky.Measurement.SeparationLightYears!.Value, 34.90, 34.91);
            Assert.Contains("34.9048 light-years", ((TextBlock)window.FindName("PhysicalDistanceSummary")).Text);
            distanceB.Text = "NaN";
            Assert.Null(sky.Measurement.SeparationLightYears);
            Assert.Contains("finite", ((TextBlock)window.FindName("PhysicalDistanceSummary")).Text);
            distanceB.Text = "-1";
            Assert.Null(sky.Measurement.SeparationLightYears);
            Assert.Contains("positive", ((TextBlock)window.FindName("PhysicalDistanceSummary")).Text);
            distanceB.Clear();
            Assert.Null(sky.Measurement.SeparationLightYears);
            distanceB.Text = "1000";
            sky.CenterOn(new(0, 0), 6);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(window, "sky-tools-measurements.png");
            Capture(owner, "sky-tools-guides.png");
            VerifyKnownTargetDistances(owner, window, service);
            VerifyDistanceSpace(window);
            sky.Layers.Remove(image); // Keep the selection fixture out of the survey pixel comparisons.
            sky.ClearMeasurement();
            Assert.Empty(distanceA.Text);
            Assert.Empty(distanceB.Text);
            sky.ShowCatalogTargets = false;
            sky.ShowGrid = sky.ShowLabels = sky.ShowFootprints = false;
            sky.ShowNorthEast = sky.ShowScaleBar = false;
            Assert.True(await window.LoadSurveyAsync(request, false));
            Assert.Equal(1, handler.Calls);
            Assert.True(await window.LoadSurveyAsync(request, true));
            Assert.Equal(1, handler.Calls);
            sky.SurveyOpacity = 1;
            sky.CenterOn(request.Center, 4);
            var foreground = SolidLayer("Survey foreground", 1, 255, 0, 0);
            sky.Layers.Add(foreground);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var point = new Point(sky.ActualWidth / 2 + 15, sky.ActualHeight / 2 + 15);
            Assert.True(PixelAt(sky, point)[2] > 245);
            foreground.IsVisible = false;
            byte[] background = PixelAt(sky, point);
            Assert.InRange(background[2], 80, 160);
            var exported = sky.CaptureExport(new()
            {
                Width = 640,
                Height = 480,
                Labels = false,
                Outlines = false,
                Grid = false,
                Coordinates = false
            });
            byte[] color = new byte[4];
            exported.CopyPixels(new Int32Rect(335, 255, 1, 1), color, 4, 0);
            Assert.InRange(color[2], 80, 160);
            ((TabControl)window.FindName("ToolsTabs")).SelectedIndex = 0;
            Capture(window, "sky-tools-survey.png");
            var previousBackground = sky.SurveyBackground;
            var pending = window.LoadSurveyAsync(request with
            {
                FieldOfView = 5
            }, false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
            Assert.False(await pending);
            Assert.Same(previousBackground, sky.SurveyBackground);
            Assert.Single(service.GetCached());
        }
        finally
        {
            window.Close();
            sky.SetSurveyBackground(null);
            sky.ClearMeasurement();
            sky.ShowCatalogTargets = sky.ShowScaleBar = sky.ShowNorthEast = false;
            sky.SurveyOpacity = 0.75;
            sky.ShowGrid = grid;
            sky.ShowLabels = labels;
            sky.ShowFootprints = footprints;
            sky.Layers.Clear();
            sky.CenterOn(originalCenter, originalField);
        }
        var shared = owner.OpenSkyTools();
        await shared.InitializeAsync();
        Assert.Same(shared, owner.OpenSkyTools());
        shared.Close();
        VerifyOverlappingSurveyRendering(owner);
        await VerifyCachedSurveyDefaults(owner, directory);
    }

    private static void VerifyDistanceSpace(SkyToolsWindow tools)
    {
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Click((Button)tools.FindName("DistanceSpaceButton"));
        var window = Assert.Single(tools.OwnedWindows.OfType<DistanceSpaceWindow>());
        try
        {
            window.UpdateLayout();
            Assert.Equal(default, window.View.Camera.Center);
            Assert.True(window.View.VisibleObjectCount > 20);
            Assert.Equal(3283, ((ListBox)window.FindName("ObjectList")).Items.Count);
            Click((Button)tools.FindName("DistanceSpaceButton"));
            Assert.Single(tools.OwnedWindows.OfType<DistanceSpaceWindow>());
            ((TextBox)window.FindName("ObjectSearch")).Text = "M81";
            var list = (ListBox)window.FindName("ObjectList");
            list.SelectedItem = TargetDistances.Find("M81");
            Assert.Same(TargetDistances.Find("M81"), window.View.Selected);
            Assert.Equal(DistanceSpaceCamera.Position(TargetDistances.Find("M81")!), window.View.Camera.Center);
            Click((Button)window.FindName("FocusObjectButton"));
            Assert.Equal(DistanceSpaceCamera.Position(TargetDistances.Find("M81")!), window.View.Camera.Center);
            Capture(window, "distance-space-m81.png");
            Assert.Equal(window.View.Camera.Center, window.View.ReferenceOrigin);
            Assert.Contains(TargetDistances.Find("M81")!.Target.Name, ((TextBlock)window.FindName("ReferencePlaneDescription")).Text);
            byte[] ReadPlaneAxisPatch()
            {
                window.UpdateLayout();
                var axisPoint = window.View.ReferenceOrigin + new SkyVector(window.View.Camera.Distance * 0.15, 0, 0);
                var projected = window.View.Camera.Project(axisPoint, window.View.ActualWidth, window.View.ActualHeight)!.Value;
                var screen = window.View.TranslatePoint(new(projected.X, projected.Y), window);
                var rendered = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(window);
                byte[] patch = new byte[9 * 9 * 4];
                rendered.CopyPixels(new Int32Rect((int)screen.X - 4, (int)screen.Y - 4, 9, 9), patch, 9 * 4, 0);
                return patch;
            }
            var withPlane = ReadPlaneAxisPatch();
            window.View.ShowGuides = false;
            var withoutPlane = ReadPlaneAxisPatch();
            window.View.ShowGuides = true;
            Assert.False(withPlane.SequenceEqual(withoutPlane), "The reference-plane axis must render near the focused object.");
            var projection = window.View.Camera.Project(window.View.Camera.Center, window.View.ActualWidth, window.View.ActualHeight)!.Value;
            Assert.Same(TargetDistances.Find("M81"), window.View.HitTarget(new(projection.X, projection.Y)));
            Assert.Contains("messier-81", ((Button)window.FindName("ObjectSourceButton")).ToolTip.ToString());
            var firstCenter = window.View.Camera.Center;
            double firstZoom = window.View.Camera.Distance;
            var secondChoice = (CheckBox)window.FindName("SecondTargetChoice");
            secondChoice.IsChecked = true;
            secondChoice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            ((TextBox)window.FindName("ObjectSearch")).Text = "M82";
            list.SelectedItem = TargetDistances.Find("M82");
            Assert.Same(TargetDistances.Find("M81"), window.View.Selected);
            Assert.Same(TargetDistances.Find("M82"), window.View.ComparisonTarget);
            Assert.Equal(firstCenter, window.View.Camera.Center);
            Assert.Equal(firstZoom, window.View.Camera.Distance);
            Assert.Contains("≈420,000 light-years", ((TextBlock)window.FindName("PairResult")).Text);
            Assert.Contains("messier-82", ((TextBlock)window.FindName("PairResult")).ToolTip.ToString());
            window.View.SelectTarget(null, second: true);
            Assert.NotNull(window.View.PairMeasurement);
            Click((Button)window.FindName("FramePairButton"));
            Capture(window, "distance-space-pair.png");
            window.Width = 900;
            window.Height = 650;
            Capture(window, "distance-space-pair-minimum.png");
            Assert.True(window.View.ActualHeight > 180);
            window.Width = 1200;
            window.Height = 820;
            window.UpdateLayout();
            foreach (var target in new[] { window.View.Selected!, window.View.ComparisonTarget! })
            {
                var point = window.View.Camera.Project(DistanceSpaceCamera.Position(target), window.View.ActualWidth, window.View.ActualHeight)!.Value;
                Assert.InRange(point.X, 0, window.View.ActualWidth);
                Assert.InRange(point.Y, 0, window.View.ActualHeight);
            }
            Click((Button)window.FindName("ClearPairButton"));
            Assert.Null(window.View.PairMeasurement);
            Assert.Same(TargetDistances.Find("M81"), window.View.Selected);
            window.View.SelectTarget(TargetDistances.Find("M82"), second: true);
            window.View.SelectTarget(TargetDistances.Find("M81"), second: true);
            Assert.Null(window.View.PairMeasurement);
            window.View.SelectTarget(TargetDistances.Find("M82"), second: true);
            window.View.Selected = TargetDistances.Find("M31");
            Assert.Null(window.View.PairMeasurement);
            window.View.Selected = TargetDistances.Find("M81");
            secondChoice.IsChecked = false;
            secondChoice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            double distance = window.View.Camera.Distance;
            window.View.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            Assert.True(window.View.Camera.Distance > distance);
            Assert.Equal(DistanceSpaceCamera.Position(TargetDistances.Find("M81")!), window.View.Camera.Center);
            window.View.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.View), 0, Key.W) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.NotEqual(DistanceSpaceCamera.Position(TargetDistances.Find("M81")!), window.View.Camera.Center);
            ((ComboBox)window.FindName("CatalogChoice")).SelectedIndex = 4;
            Assert.Null(window.View.Selected);
            ((TextBox)window.FindName("ObjectSearch")).Text = "IC 1805";
            list.SelectedItem = TargetDistances.Find("IC1805");
            Assert.Contains("SIMBAD", ((TextBlock)window.FindName("SelectedDetails")).Text);
            ((ComboBox)window.FindName("ScaleChoice")).SelectedIndex = 4;
            Assert.Equal(DistanceSpaceCamera.Position(TargetDistances.Find("IC1805")!), window.View.Camera.Center);
            Assert.True(window.View.Camera.Distance > TargetDistances.All.Max(item => item.LightYears));
            Capture(window, "distance-space-all.png");
            ((ComboBox)window.FindName("CatalogChoice")).SelectedIndex = 0;
            ((TextBox)window.FindName("ObjectSearch")).Text = "no such target";
            Assert.Empty(list.Items.Cast<object>());
            ((TextBox)window.FindName("ObjectSearch")).Clear();
            Click((Button)window.FindName("SolarHomeButton"));
            Assert.Null(window.View.Selected);
            ((TextBox)window.FindName("ObjectSearch")).Text = "M31";
            list.SelectedItem = TargetDistances.Find("M31");
            var andromeda = DistanceSpaceCamera.Position(TargetDistances.Find("M31")!);
            Assert.Equal(andromeda, window.View.Camera.Center);
            Assert.Equal(andromeda, window.View.ReferenceOrigin);
            window.View.Camera.Orbit(35, 10);
            window.View.Camera.Zoom(2);
            Assert.Equal(andromeda, window.View.Camera.Center);
            window.View.Camera.Move(100, 0, 0);
            double andromedaZoom = window.View.Camera.Distance;
            Click((Button)window.FindName("FocusObjectButton"));
            Assert.Equal(andromeda, window.View.Camera.Center);
            Assert.Equal(andromedaZoom, window.View.Camera.Distance);
            Capture(window, "distance-space-andromeda.png");
            window.View.Camera.Frame(andromeda, double.MaxValue);
            window.View.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.View), 0, Key.OemMinus) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(DistanceSpaceCamera.MaximumDistance, window.View.Camera.Distance);
            Assert.Contains("Maximum zoom-out scale reached", ((TextBlock)window.FindName("SpaceStatus")).Text);
            window.View.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            Assert.InRange(window.View.Camera.ViewHeight, 13_800_000_000d - 0.001, 13_800_000_000d);
            window.View.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.View), 0, Key.OemPlus) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.True(window.View.Camera.Distance < DistanceSpaceCamera.MaximumDistance);
            Assert.DoesNotContain("Maximum zoom-out scale reached", ((TextBlock)window.FindName("SpaceStatus")).Text);
            window.View.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.View), 0, Key.Home) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Null(window.View.Selected);
            ((TextBox)window.FindName("ObjectSearch")).Clear();
            window.Width = 900;
            window.Height = 650;
            Capture(window, "distance-space-minimum.png");
            Assert.Equal(default, window.View.Camera.Center);
            Assert.True(window.View.ActualWidth > 500);
        }
        finally { window.Close(); }
        Click((Button)tools.FindName("DistanceSpaceButton"));
        Assert.Single(tools.OwnedWindows.OfType<DistanceSpaceWindow>()).Close();
    }

    private static void VerifyKnownTargetDistances(MainWindow owner, SkyToolsWindow window, SurveyImageService service)
    {
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var sky = owner.SkyView;
        var marked = sky.AddedTargets.Select(target => target.Id).ToArray();
        var options = owner.Options;
        var center = sky.Camera.Center;
        double field = sky.Camera.FieldOfView;
        var a = (ListBox)window.FindName("DistanceTargetsA");
        var b = (ListBox)window.FindName("DistanceTargetsB");
        try
        {
            ((TabControl)window.FindName("ToolsTabs")).SelectedIndex = 2;
            ((TextBox)window.FindName("DistanceSearchA")).Text = "M 81";
            ((TextBox)window.FindName("DistanceSearchB")).Text = "NGC 3034";
            var imageList = (ListBox)owner.FindName("LayerList");
            imageList.SelectAll();
            a.SelectedItem = TargetDistances.Find("M81");
            Assert.Empty(imageList.SelectedItems); // Even the first catalogue pick changes the editing context.
            imageList.SelectAll();
            b.SelectedItem = TargetDistances.Find("M82");
            Assert.Empty(imageList.SelectedItems);
            Assert.Equal(DeepSkyCatalog.Find("M81")!.Position, sky.Measurement!.Start);
            Assert.Equal(DeepSkyCatalog.Find("M82")!.Position, sky.Measurement.End);
            Assert.Contains("≈420,000 light-years", ((TextBlock)window.FindName("TargetDistanceResult")).Text);
            Assert.Equal("11600000", ((TextBox)window.FindName("MeasurementDistanceA")).Text);
            Assert.Equal("12000000", ((TextBox)window.FindName("MeasurementDistanceB")).Text);
            Assert.Contains("11.6 million light-years", ((TextBlock)window.FindName("DistanceSourceA")).Text);
            Assert.True(((Button)window.FindName("DistanceSourceButtonA")).IsEnabled);
            Assert.Contains("messier-81", ((Button)window.FindName("DistanceSourceButtonA")).ToolTip.ToString());
            Assert.Equal(center, sky.Camera.Center); // Selecting targets does not navigate until requested.
            Assert.Equal(options, owner.Options);
            imageList.SelectAll();
            Click((Button)window.FindName("DistanceFrameButton"));
            Assert.Empty(imageList.SelectedItems);
            Assert.InRange(sky.Camera.Center.DistanceTo(SkyGeometry.Interpolate(sky.Measurement.Start, sky.Measurement.End, 0.5)), 0, 1e-9);
            Capture(window, "sky-tools-target-distances.png");
            var reopened = new SkyToolsWindow(sky, service) { Owner = owner };
            try
            {
                Assert.Same(TargetDistances.Find("M81"), ((ListBox)reopened.FindName("DistanceTargetsA")).SelectedItem);
                Assert.Same(TargetDistances.Find("M82"), ((ListBox)reopened.FindName("DistanceTargetsB")).SelectedItem);
            }
            finally { reopened.Close(); }
            ((TextBox)window.FindName("MeasurementDistanceA")).Text = "11000000";
            Assert.Null(sky.Measurement.StartDistanceSource); // Manual overrides detach catalogue provenance.
            Assert.Null(a.SelectedItem);
            Assert.Null(b.SelectedItem);
            Assert.DoesNotContain("≈", sky.Measurement.DisplayLabel);
            sky.RestoreTargets(["M81", "M82"]);
            Click((Button)window.FindName("DistanceMarkedButton"));
            Assert.NotNull(sky.Measurement.StartDistanceSource);
            Assert.NotNull(sky.Measurement.EndDistanceSource);
            ((TextBox)window.FindName("DistanceSearchA")).Text = "Horsehead";
            Assert.Empty(a.Items.Cast<object>());
            Assert.Null(sky.Measurement); // An incomplete filtered pair cannot leave a stale catalogue result.
            Assert.Contains("Select target", ((TextBlock)window.FindName("TargetDistanceResult")).Text);
            Click((Button)window.FindName("DistanceMarkedButton"));
            Assert.NotNull(sky.Measurement);
            sky.BeginMeasurement();
            Assert.Null(a.SelectedItem);
            Assert.Null(b.SelectedItem);
            Assert.Empty(((TextBox)window.FindName("MeasurementDistanceA")).Text);
            sky.PickMeasurementPoint(DeepSkyCatalog.Find("M81")!.Position);
            sky.PickMeasurementPoint(DeepSkyCatalog.Find("M82")!.Position);
            Assert.Null(sky.Measurement!.SeparationLightYears); // Arbitrary sky clicks are never guessed object identities.
            Click((Button)window.FindName("DistanceMarkedButton"));
            Click((Button)window.FindName("DistanceClearButton"));
            Assert.Null(sky.Measurement);
            ((TextBox)window.FindName("DistanceSearchA")).Text = "Caldwell 102";
            ((TextBox)window.FindName("DistanceSearchB")).Text = "IC 1805";
            a.SelectedItem = TargetDistances.Find("IC2602");
            b.SelectedItem = TargetDistances.Find("IC1805");
            Assert.Same(TargetDistances.Find("C102"), sky.Measurement!.StartDistanceSource);
            Assert.Equal("SIMBAD / CDS", sky.Measurement.EndDistanceSource!.SourceName);
            Assert.Contains("Reported lower error", ((TextBlock)window.FindName("DistanceSourceB")).ToolTip.ToString());
            Assert.Contains("Literature reference", ((TextBlock)window.FindName("DistanceSourceB")).ToolTip.ToString());
            Assert.Contains("Caldwell 109/109", ((TextBlock)window.FindName("DistanceCoverage")).Text);
            Assert.Contains("NGC", ((TextBlock)window.FindName("DistanceCoverage")).Text);
            Capture(window, "sky-tools-multiple-catalogue-distances.png");
        }
        finally
        {
            sky.RestoreTargets(marked);
            sky.ClearMeasurement();
            sky.CenterOn(center, field);
        }
    }

    private static void VerifyOverlappingSurveyRendering(MainWindow owner)
    {
        var sky = owner.SkyView;
        var center = sky.Camera.Center;
        double field = sky.Camera.FieldOfView, opacity = sky.SurveyOpacity;
        bool grid = sky.ShowGrid, labels = sky.ShowLabels, outlines = sky.ShowFootprints, constellations = sky.ShowConstellations;
        SurveyFrame Frame(string name, double ra, double width, double rotation, byte red, byte green, byte blue)
        {
            var image = SolidLayer(name, width, red, green, blue).Image with
            {
                Placement = ImagePlacement.FromManual(new(ra, 70, width, rotation), 64, 64)
            };
            return new(new(SurveyDefinition.Color.Id, new(ra, 70), width, 128), image, true);
        }
        var newest = Frame("Newer survey", 359.95, 65, 27, 210, 30, 20);
        var older = Frame("Older survey", 2, 100, -14, 10, 40, 210);
        RenderTargetBitmap LiveFrame()
        {
            owner.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)owner.ActualWidth, (int)owner.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(owner);
            return bitmap;
        }
        void AssertOverlap(BitmapSource bitmap, double width, double height, Point offset, double alpha, BitmapSource? backdrop = null)
        {
            int checkedPixels = 0;
            double extent = Math.Min(0.25, sky.Camera.FieldOfView / 65 * 0.25);
            for (int y = -7; y <= 7; y++)
            {
                for (int x = -7; x <= 7; x++)
                {
                    var coordinate = newest.Image.Placement!.At(0.5 + x / 7d * extent, 0.5 + y / 7d * extent);
                    if (sky.Camera.Project(coordinate.Vector, width, height) is not { } point ||
                        point.X < 25 || point.X > width - 25 || point.Y < 25 || point.Y > height - 60) { continue; }
                    // The atlas center reticle is intentionally drawn above the imagery.
                    if (Math.Abs(point.X - width / 2) < 12 && Math.Abs(point.Y - height / 2) < 12) { continue; }
                    var rectangle = new Int32Rect((int)(offset.X + point.X), (int)(offset.Y + point.Y), 1, 1);
                    byte[] pixel = new byte[4], background = new byte[4];
                    bitmap.CopyPixels(rectangle, pixel, 4, 0);
                    backdrop?.CopyPixels(rectangle, background, 4, 0);
                    var front = new[] { 20d, 30d, 210d };
                    var back = new[] { 210d, 40d, 10d };
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double expected = front[channel] * alpha + back[channel] * alpha * (1 - alpha) + background[channel] * (1 - alpha) * (1 - alpha);
                        Assert.True(Math.Abs(pixel[channel] - expected) <= 3,
                            $"Overlapping surveys at {sky.Camera.FieldOfView}°, opacity {alpha}, sample ({x},{y}), channel {channel}: expected {expected}, got {pixel[channel]}.");
                    }
                    checkedPixels++;
                }
            }
            Assert.True(checkedPixels > 40);
        }
        try
        {
            sky.ShowGrid = sky.ShowLabels = sky.ShowFootprints = sky.ShowConstellations = false;
            foreach (double viewField in new[] { 80d, 25d, 8d })
            {
                // Differently sampled, rotated fields cross both the RA seam and the pole.
                sky.CenterOn(new(359.95 + viewField / 50, 70), viewField);
                sky.SetSurveyBackground(null);
                var backdrop = LiveFrame();
                foreach (double alpha in new[] { 1d, 0.5 })
                {
                    sky.SurveyOpacity = alpha;
                    sky.SetSurveyBackgrounds([newest, older]);
                    var rendered = LiveFrame();
                    AssertOverlap(rendered, sky.ActualWidth, sky.ActualHeight, sky.TranslatePoint(new(), owner), alpha, backdrop);
                    if (alpha == 1)
                    {
                        var export = sky.CaptureExport(new() { Width = 800, Height = 600, Labels = false, Outlines = false, Grid = false, Coordinates = false });
                        AssertOverlap(export, 800, 600, new(), 1);
                    }
                }
            }
            sky.SurveyOpacity = 1;
            sky.CenterOn(new(359.95, 70), 8);
            var foreground = SolidLayer("Survey overlap foreground", 3, 0, 255, 0);
            sky.Layers.Add(foreground);
            var photoPoint = new Point(sky.ActualWidth / 2 + 15, sky.ActualHeight / 2 + 15);
            Assert.InRange(PixelAt(sky, photoPoint)[1], 253, 255);
            sky.UpdateHover(photoPoint);
            Assert.Same(foreground, sky.HoveredLayer);
            sky.Layers.Remove(foreground);
            sky.UpdateHover(photoPoint);
            Assert.Null(sky.HoveredLayer);
            Capture(owner, "survey-overlap-regression.png");
        }
        finally
        {
            sky.Layers.Clear();
            sky.SetSurveyBackground(null);
            sky.SurveyOpacity = opacity;
            sky.ShowGrid = grid;
            sky.ShowLabels = labels;
            sky.ShowFootprints = outlines;
            sky.ShowConstellations = constellations;
            sky.CenterOn(center, field);
        }
    }

    private static async Task VerifyCachedSurveyDefaults(MainWindow owner, string directory)
    {
        var original = owner.Options;
        string settingsDirectory = Path.GetDirectoryName(owner.Library.DatabasePath)!;
        var first = new SurveyRequest(SurveyDefinition.Color.Id, new(0, 0), 4, 128);
        var second = first with { Center = new(90, 0) };
        var fixtures = new Dictionary<Uri, byte[]>
        {
            [first.DownloadUri] = SurveyTests.FitsFixture(directory, first),
            [second.DownloadUri] = SurveyTests.FitsFixture(directory, second)
        };
        var handler = new SurveyTests.Handler((message, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fixtures[message.RequestUri!])
        }));
        using var client = new HttpClient(handler);
        var service = new SurveyImageService(Path.Combine(settingsDirectory, "SurveyCache"), client);
        var frame = await service.LoadAsync(first, false);
        await service.LoadAsync(second, false);
        try
        {
            await owner.RestoreCachedSurveyBackgroundsAsync();
            Assert.Empty(owner.SkyView.SurveyBackgrounds);
            var camera = owner.SkyView.Camera.Center;
            await owner.ApplyOptionsAsync(original with { ShowCachedSurveyImages = true });
            Assert.Equal(2, owner.SkyView.SurveyBackgrounds.Count);
            Assert.All(owner.SkyView.SurveyBackgrounds, background => Assert.True(background.FromCache));
            Assert.Equal(camera, owner.SkyView.Camera.Center);
            Assert.Equal(2, handler.Calls);
            var store = new SkyBrowser.Core.Configuration.ApplicationOptionsStore(Path.Combine(settingsDirectory, "settings.json"));
            Assert.True(store.Read().ShowCachedSurveyImages);
            var restarted = new MainWindow(store) { ShowInTaskbar = false };
            try
            {
                restarted.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await restarted.RestoreCachedSurveyBackgroundsAsync();
                Assert.Equal(2, restarted.SkyView.SurveyBackgrounds.Count);
                Assert.Empty(restarted.SkyView.Layers);
            }
            finally { restarted.Close(); }
            owner.SkyView.SetSurveyBackground(frame);
            await owner.ApplyOptionsAsync(original);
            Assert.Same(frame, owner.SkyView.SurveyBackground); // A manual choice survives disabling the default.
            await owner.ApplyOptionsAsync(original with { ShowCachedSurveyImages = true });
            Assert.Equal(2, owner.SkyView.SurveyBackgrounds.Count);
            await owner.ApplyOptionsAsync(original);
            Assert.Empty(owner.SkyView.SurveyBackgrounds);
        }
        finally
        {
            await owner.ApplyOptionsAsync(original);
            owner.SkyView.SetSurveyBackground(null);
        }
    }
}
