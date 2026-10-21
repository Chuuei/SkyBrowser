using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static async Task VerifyFraming(MainWindow owner, SkyToolsWindow tools, string directory)
    {
        var sky = owner.SkyView;
        var originalCenter = sky.Camera.Center;
        double originalFov = sky.Camera.FieldOfView;
        var tabs = (TabControl)tools.FindName("ToolsTabs");
        var choice = (ComboBox)tools.FindName("EquipmentChoice");
        TextBox Box(string name) => (TextBox)tools.FindName(name);
        void Click(string name) => ((Button)tools.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        try
        {
            tabs.SelectedItem = tools.FindName("FramingTab");
            Assert.Equal(29, choice.Items.Count);
            choice.SelectedItem = SmartTelescopeProfiles.All.Single(p => p.Name == "DWARFLAB DWARF 3 · telephoto");
            Assert.Contains("3.38", ((TextBlock)tools.FindName("EquipmentSource")).Text);
            Box("FramingRa").Text = "359.9";
            Box("FramingDec").Text = "80";
            Box("MosaicRows").Text = "2";
            Box("MosaicColumns").Text = "3";
            Box("FramingRotation").Text = "37";
            var plan = tools.ApplyFramingPlan();
            Assert.NotNull(plan);
            Assert.Equal(6, plan.Panels.Count);
            Assert.Same(plan, sky.FramingPlan);
            foreach (var corner in plan.Panels.SelectMany(p => p.Corners))
            {
                var point = sky.Camera.Project(corner.Vector, sky.ActualWidth, sky.ActualHeight);
                Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, sky.ActualWidth);
                Assert.InRange(point.Value.Y, 0, sky.ActualHeight);
            }
            Assert.True(((FrameworkElement)tools.FindName("FramingActions")).IsEnabled);
            var list = (ListBox)tools.FindName("MosaicPanels");
            list.SelectedIndex = 4;
            Assert.Equal(5, sky.SelectedFramingPanel);
            Click("PanelVisibilityButton");
            Assert.True(((TabItem)tools.FindName("VisibilityTab")).IsSelected);
            Assert.Null(tools.VisibilityReport);
            Assert.Equal(plan.Panels[4].Center.RightAscension, double.Parse(Box("PlanRa").Text, CultureInfo.InvariantCulture));
            Assert.Equal(plan.Panels[4].Center.Declination, double.Parse(Box("PlanDec").Text, CultureInfo.InvariantCulture));
            Assert.NotNull(await tools.CalculateVisibilityAsync());
            Assert.Equal(plan.Panels[4].Center, tools.VisibilityReport!.Target);
            var outdated = tools.CalculateVisibilityAsync();
            Box("PlanMinAltitude").Text = "31";
            await outdated;
            Assert.Null(tools.VisibilityReport);
            string export = Path.Combine(directory, "mosaic-coordinates.csv");
            tools.ExportFramingCoordinates(export);
            Assert.Equal(7, File.ReadAllLines(export).Length);
            Assert.Throws<IOException>(() => tools.ExportFramingCoordinates(export));
            Assert.Throws<ArgumentException>(() => tools.ExportFramingCoordinates(Path.Combine(directory, "original.fits")));
            tabs.SelectedItem = tools.FindName("FramingTab");
            tools.UpdateLayout();
            Capture(tools, "sky-tools-framing.png");
            var withOverlay = sky.CaptureExport(new() { Width = 800, Height = 600, Coordinates = false, Grid = false });
            sky.FramingPlan = null;
            var withoutOverlay = sky.CaptureExport(new() { Width = 800, Height = 600, Coordinates = false, Grid = false });
            byte[] first = new byte[800 * 600 * 4], second = new byte[first.Length];
            withOverlay.CopyPixels(first, 800 * 4, 0);
            withoutOverlay.CopyPixels(second, 800 * 4, 0);
            Assert.False(first.SequenceEqual(second));
            sky.FramingPlan = plan;
            Box("MosaicRows").Text = "invalid";
            Assert.Null(sky.FramingPlan);
            Assert.False(((FrameworkElement)tools.FindName("FramingActions")).IsEnabled);
            Assert.Null(tools.ApplyFramingPlan());
            Assert.Contains("whole numbers", ((TextBlock)tools.FindName("FramingSummary")).Text);
            Assert.Throws<InvalidOperationException>(() => tools.ExportFramingCoordinates(export, true));
            Box("MosaicRows").Text = "1";
            Box("EquipmentName").Text = "My saved camera";
            Box("FieldWidth").Text = "2.5";
            Box("FieldHeight").Text = "1.5";
            Click("SaveEquipmentButton");
            Assert.Equal(30, choice.Items.Count);
            Assert.Equal("My saved camera", Assert.IsType<EquipmentProfile>(choice.SelectedItem).Name);
            Click("DeleteEquipmentButton");
            Assert.Equal(29, choice.Items.Count);
        }
        finally
        {
            sky.FramingPlan = null;
            sky.CenterOn(originalCenter, originalFov);
        }
    }
}
