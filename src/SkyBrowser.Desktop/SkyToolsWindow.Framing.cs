using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Configuration;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private readonly EquipmentProfileStore _equipmentStore;
    private EquipmentProfile[] _customEquipment = [];
    private bool _updatingFraming;
    public MosaicPlan? FramingPlan => _sky.FramingPlan;

    private void InitializeFraming()
    {
        _customEquipment = _equipmentStore.Read();
        RefreshEquipmentChoices();
        EquipmentChoice.SelectedItem = SmartTelescopeProfiles.All.First(profile => profile.Name == "ZWO Seestar S50");
        UseFramingCenterClicked(this, new());
        if (_sky.FramingPlan is { } plan)
        {
            EquipmentChoice.SelectedItem = EquipmentChoice.Items.Cast<EquipmentProfile>().FirstOrDefault(p => p == plan.Equipment);
            EquipmentSource.Text = plan.Equipment.Notes + (string.IsNullOrEmpty(plan.Equipment.Source) ? "" : "\n" + plan.Equipment.Source);
            EquipmentName.Text = plan.Equipment.Name;
            FieldWidth.Text = Number(plan.Equipment.WidthDegrees);
            FieldHeight.Text = Number(plan.Equipment.HeightDegrees);
            FramingRa.Text = Number(plan.Center.RightAscension);
            FramingDec.Text = Number(plan.Center.Declination);
            FramingRotation.Text = Number(plan.RotationDegrees);
            MosaicRows.Text = plan.Rows.ToString(CultureInfo.InvariantCulture);
            MosaicColumns.Text = plan.Columns.ToString(CultureInfo.InvariantCulture);
            MosaicOverlap.Text = Number(plan.OverlapPercent);
            DisplayFramingPlan(plan);
        }
        if (_equipmentStore.LastError is { } error)
        { SkyToolsStatus.Text = error; }
    }

    private void RefreshEquipmentChoices() => EquipmentChoice.ItemsSource =
        _customEquipment.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).Concat(SmartTelescopeProfiles.All).ToArray();

    private void EquipmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EquipmentChoice.SelectedItem is not EquipmentProfile equipment)
        { return; }
        _updatingFraming = true;
        try
        {
            EquipmentName.Text = equipment.Name;
            FieldWidth.Text = Number(equipment.WidthDegrees);
            FieldHeight.Text = Number(equipment.HeightDegrees);
            EquipmentSource.Text = string.IsNullOrEmpty(equipment.Source) ? "Your saved equipment profile." : equipment.Notes + "\n" + equipment.Source;
            DeleteEquipmentButton.IsEnabled = _customEquipment.Contains(equipment);
        }
        finally { _updatingFraming = false; }
        InvalidateFraming();
    }

    private void FramingInputChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_updatingFraming && (ReferenceEquals(sender, EquipmentName) || ReferenceEquals(sender, FieldWidth) || ReferenceEquals(sender, FieldHeight)))
        {
            EquipmentSource.Text = "Custom configuration. Save it with your own name to use it next time.";
        }
        InvalidateFraming();
    }

    private void InvalidateFraming()
    {
        if (!_ready || _updatingFraming)
        { return; }
        _sky.FramingPlan = null;
        _sky.SelectedFramingPanel = 0;
        MosaicPanels.ItemsSource = null;
        FramingActions.IsEnabled = false;
        FramingSummary.Text = "Settings changed. Choose Show plan on atlas to apply them.";
        SkyToolsStatus.Text = "Framing settings changed; show the plan to apply them.";
    }

    private EquipmentProfile CurrentEquipment()
    {
        var selected = EquipmentChoice.SelectedItem as EquipmentProfile;
        string name = EquipmentName.Text.Trim();
        double width = Number(FieldWidth), height = Number(FieldHeight);
        // A modified factory field is a custom configuration; don't attach misleading provenance.
        var result = selected is not null && name == selected.Name && Math.Abs(width - selected.WidthDegrees) < 1e-7 && Math.Abs(height - selected.HeightDegrees) < 1e-7
            ? selected : new EquipmentProfile(name, width, height);
        result.Validate();
        return result;
    }

    private void SaveEquipmentClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = CurrentEquipment() with { Source = "", Notes = "" };
            if (SmartTelescopeProfiles.All.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Give your custom configuration a different name from the factory preset.");
            }
            var updated = _customEquipment.Where(p => !p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).Append(profile).ToArray();
            _equipmentStore.Write(updated);
            _customEquipment = updated;
            RefreshEquipmentChoices();
            EquipmentChoice.SelectedItem = profile;
            SkyToolsStatus.Text = "Custom equipment saved; it will be available next time.";
        }
        catch (Exception exception) { SkyToolsStatus.Text = "Equipment was not saved: " + exception.Message; }
    }

    private void DeleteEquipmentClicked(object sender, RoutedEventArgs e)
    {
        if (EquipmentChoice.SelectedItem is not EquipmentProfile profile || !_customEquipment.Contains(profile))
        { return; }
        try
        {
            var updated = _customEquipment.Where(p => p != profile).ToArray();
            _equipmentStore.Write(updated);
            _customEquipment = updated;
            RefreshEquipmentChoices();
            EquipmentChoice.SelectedIndex = 0;
            SkyToolsStatus.Text = "Custom equipment removed.";
        }
        catch (Exception exception) { SkyToolsStatus.Text = "Equipment was not removed: " + exception.Message; }
    }

    private void CalculateFieldClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = EquipmentProfile.FromSensor("My camera", Number(SensorWidth), Number(SensorHeight), Number(FramingFocalLength));
            EquipmentName.Text = profile.Name;
            FieldWidth.Text = Number(profile.WidthDegrees);
            FieldHeight.Text = Number(profile.HeightDegrees);
            EquipmentSource.Text = "Calculated rectilinear field from sensor dimensions and effective focal length, including any reducer or Barlow.";
        }
        catch (Exception exception) { SkyToolsStatus.Text = "Field unavailable: " + exception.Message; }
    }

    private void UseFramingCenterClicked(object sender, RoutedEventArgs e)
    {
        FramingRa.Text = Number(_sky.Camera.Center.RightAscension);
        FramingDec.Text = Number(_sky.Camera.Center.Declination);
    }

    private void RotateFramingClicked(object sender, RoutedEventArgs e)
    {
        bool shown = FramingPlan is not null;
        try
        {
            FramingRotation.Text = Number(SkyCoordinate.Normalize(Number(FramingRotation) + 90));
            if (shown)
            { ApplyFramingPlan(); }
        }
        catch (Exception exception) { SkyToolsStatus.Text = exception.Message; }
    }

    private void ShowFramingClicked(object sender, RoutedEventArgs e) => ApplyFramingPlan();

    public MosaicPlan? ApplyFramingPlan()
    {
        if (_closed)
        { return null; }
        try
        {
            static int Count(TextBox box) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                ? count : throw new ArgumentException("Rows and columns must be whole numbers from 1 to 10.");
            var plan = MosaicPlan.Create(CurrentEquipment(), new(Number(FramingRa), Number(FramingDec)), Number(FramingRotation),
                Count(MosaicRows), Count(MosaicColumns), Number(MosaicOverlap));
            _sky.ClearImageSelection();
            _sky.FramingPlan = plan;
            DisplayFramingPlan(plan);
            FitFramingClicked(this, new());
            return plan;
        }
        catch (Exception exception)
        {
            _sky.FramingPlan = null;
            MosaicPanels.ItemsSource = null;
            FramingActions.IsEnabled = false;
            FramingSummary.Text = "Plan unavailable: " + exception.Message;
            SkyToolsStatus.Text = FramingSummary.Text;
            return null;
        }
    }

    private void DisplayFramingPlan(MosaicPlan plan)
    {
        MosaicPanels.ItemsSource = plan.Panels;
        MosaicPanels.SelectedIndex = 0;
        _sky.SelectedFramingPanel = 1;
        FramingActions.IsEnabled = true;
        FramingSummary.Text = string.Create(CultureInfo.InvariantCulture,
            $"{plan.Panels.Count} panel(s) · each {plan.Equipment.WidthDegrees:0.####}° × {plan.Equipment.HeightDegrees:0.####}° · {plan.OverlapPercent:0.##}% requested overlap · rotation {plan.RotationDegrees:0.##}° CCW");
    }

    private void HideFramingClicked(object sender, RoutedEventArgs e)
    {
        InvalidateFraming();
        FramingSummary.Text = "Plan hidden. Choose Show plan on atlas to display it again.";
        SkyToolsStatus.Text = "Framing plan hidden.";
    }

    private void MosaicPanelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
        { _sky.SelectedFramingPanel = (MosaicPanels.SelectedItem as MosaicPanel)?.Number ?? 0; }
    }

    private void FitFramingClicked(object sender, RoutedEventArgs e)
    {
        if (FramingPlan is not { } plan)
        { return; }
        FitFramingPanels(plan.Panels, plan.Center);
    }

    private void FitFramingPanels(IEnumerable<MosaicPanel> panels, SkyCoordinate center)
    {
        _sky.ClearImageSelection();
        _sky.CenterOn(center);
        double tangent = 0;
        foreach (var corner in panels.SelectMany(panel => panel.Corners))
        {
            double z = SkyVector.Dot(corner.Vector, _sky.Camera.Forward);
            tangent = Math.Max(tangent, Math.Max(Math.Abs(SkyVector.Dot(corner.Vector, _sky.Camera.Right) / z),
                Math.Abs(SkyVector.Dot(corner.Vector, _sky.Camera.Up) / z) * Math.Max(1, _sky.ActualWidth) / Math.Max(1, _sky.ActualHeight)));
        }
        double field = 2 * Math.Atan(tangent * 1.15) / SkyCoordinate.Radians;
        _sky.CenterOn(center, field);
        SkyToolsStatus.Text = field > 120
            ? "Plan shown, but this tall/wide field exceeds the atlas zoom limit. Resize the atlas, rotate the plan or reduce its size to see every corner."
            : "Plan shown on the atlas · J2000 coordinates · select a panel to check visibility or export all panels.";
    }

    private void CenterPanelClicked(object sender, RoutedEventArgs e)
    {
        if (FramingPlan is not null && MosaicPanels.SelectedItem is MosaicPanel panel)
        {
            FitFramingPanels([panel], panel.Center);
        }
    }

    private void PanelVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (FramingPlan is null || MosaicPanels.SelectedItem is not MosaicPanel panel)
        { return; }
        PlanRa.Text = panel.Center.RightAscension.ToString("R", CultureInfo.InvariantCulture);
        PlanDec.Text = panel.Center.Declination.ToString("R", CultureInfo.InvariantCulture);
        VisibilityTab.IsSelected = true;
        SkyToolsStatus.Text = $"Panel {panel.Number} copied to Target visibility. Set your location and UTC date, then calculate.";
    }

    private void ExportFramingClicked(object sender, RoutedEventArgs e)
    {
        if (FramingPlan is not { } plan)
        { return; }
        var dialog = new SaveFileDialog { Title = "Export mosaic coordinates", Filter = "CSV coordinates (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, FileName = "SkyBrowser-mosaic.csv" };
        if (dialog.ShowDialog(this) != true)
        { return; }
        try
        {
            ExportFramingCoordinates(dialog.FileName, overwrite: true);
            SkyToolsStatus.Text = $"Exported {plan.Panels.Count} J2000 panel coordinates, field sizes and rotations.";
        }
        catch (Exception exception) { SkyToolsStatus.Text = "Coordinates were not exported: " + exception.Message; }
    }

    public void ExportFramingCoordinates(string destination, bool overwrite = false)
    {
        if (FramingPlan is not { } plan)
        { throw new InvalidOperationException("Show a valid plan before exporting."); }
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        { throw new ArgumentException("Export coordinates as CSV."); }
        var sources = _sky.Layers.Select(layer => layer.Image.SourcePath).ToArray();
        SkyBrowser.Core.Imaging.ImageExportDestination.Validate(destination, sources);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, plan.ToCsv(), new UTF8Encoding(true));
            SkyBrowser.Core.Imaging.ImageExportDestination.Validate(destination, sources);
            File.Move(temporary, destination, overwrite);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
