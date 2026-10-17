using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private bool _updatingDistanceTargets;
    private DistanceSpaceWindow? _distanceSpaceWindow;

    private void OpenDistanceSpaceClicked(object sender, RoutedEventArgs e)
    {
        if (_distanceSpaceWindow is not null)
        {
            _distanceSpaceWindow.Activate();
            return;
        }
        _distanceSpaceWindow = new DistanceSpaceWindow { Owner = this };
        _distanceSpaceWindow.Closed += (_, _) => _distanceSpaceWindow = null;
        _distanceSpaceWindow.Show();
    }

    private void InitializeTargetDistances()
    {
        DistanceTargetsA.ItemsSource = TargetDistances.Search("");
        DistanceTargetsB.ItemsSource = TargetDistances.Search("");
        string Coverage(string label, TargetCatalog catalog)
        {
            var count = TargetDistances.Coverage(catalog);
            return $"{label} {count.Available:N0}/{count.Total:N0}";
        }
        DistanceCoverage.Text = $"{TargetDistances.All.Count:N0} offline objects. " +
            string.Join(" · ", Coverage("Messier", TargetCatalog.Messier), Coverage("Caldwell", TargetCatalog.Caldwell),
                Coverage("NGC", TargetCatalog.Ngc), Coverage("IC", TargetCatalog.Ic)) +
            ". Search by name or ID; select A and B. Hover sources for method, reference and errors. Missing estimates remain unavailable.";
    }

    private void DistanceSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingDistanceTargets) { return; }
        var list = ReferenceEquals(sender, DistanceSearchA) ? DistanceTargetsA : DistanceTargetsB;
        var selected = list.SelectedItem;
        _updatingDistanceTargets = true;
        try
        {
            list.ItemsSource = TargetDistances.Search(((TextBox)sender).Text);
            if (selected is not null && list.Items.Contains(selected)) { list.SelectedItem = selected; }
        }
        finally { _updatingDistanceTargets = false; }
        UpdateSelectedDistanceTargets();
    }

    private void DistanceTargetChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectedDistanceTargets();

    private void UpdateSelectedDistanceTargets()
    {
        if (!_ready || _updatingDistanceTargets) { return; }
        _updatingDistanceTargets = true;
        try
        {
            if (DistanceTargetsA.SelectedItem is TargetDistanceEstimate a && DistanceTargetsB.SelectedItem is TargetDistanceEstimate b)
            {
                _sky.MeasureTargets(a.Target.Id, b.Target.Id);
                SkyToolsStatus.Text = "Target measurement updated from the offline distance catalogue";
            }
            else if (_sky.Measurement?.StartDistanceSource is not null || _sky.Measurement?.EndDistanceSource is not null)
            {
                _sky.ClearMeasurement();
            }
            UpdateDistancePairPresentation();
        }
        finally { _updatingDistanceTargets = false; }
    }

    private void SynchronizeDistanceTargets()
    {
        if (_updatingDistanceTargets) { return; }
        _updatingDistanceTargets = true;
        try
        {
            DistanceTargetsA.SelectedItem = _sky.Measurement?.StartDistanceSource;
            DistanceTargetsB.SelectedItem = _sky.Measurement?.EndDistanceSource;
            UpdateDistancePairPresentation();
        }
        finally { _updatingDistanceTargets = false; }
    }

    private void UpdateDistancePairPresentation()
    {
        var a = DistanceTargetsA.SelectedItem as TargetDistanceEstimate;
        var b = DistanceTargetsB.SelectedItem as TargetDistanceEstimate;
        string SourceText(TargetDistanceEstimate? estimate) => estimate is null ? "No target selected." :
            $"{estimate.Target.AliasText}\nDistance from us: {estimate.SourceValue}\n{estimate.SourceName} · {estimate.AsOf}";
        DistanceSourceA.Text = SourceText(a);
        DistanceSourceB.Text = SourceText(b);
        DistanceSourceA.ToolTip = a?.SourceDetails;
        DistanceSourceB.ToolTip = b?.SourceDetails;
        DistanceSourceButtonA.IsEnabled = a is not null;
        DistanceSourceButtonB.IsEnabled = b is not null;
        DistanceSourceButtonA.DataContext = a;
        DistanceSourceButtonB.DataContext = b;
        DistanceSourceButtonA.ToolTip = a?.SourceDetails;
        DistanceSourceButtonB.ToolTip = b?.SourceDetails;
        if (a is not null && b is not null)
        {
            var measurement = TargetDistances.Measure(a.Target.Id, b.Target.Id);
            TargetDistanceResult.Text = "Estimated 3D separation: " + TargetDistances.FormatApproximate(measurement.SeparationLightYears!.Value) + " light-years";
            TargetDistanceAngle.Text = $"{a.Target.Name} → {b.Target.Name}\nAngular separation: {measurement.Label}. Object-center coordinates and distances are supplied by the offline catalogues.";
        }
        else
        {
            TargetDistanceResult.Text = "Select target A and target B to measure their separation.";
            TargetDistanceAngle.Text = "";
        }
    }

    private void FrameDistancePairClicked(object sender, RoutedEventArgs e)
    {
        if (DistanceTargetsA.SelectedItem is not TargetDistanceEstimate a || DistanceTargetsB.SelectedItem is not TargetDistanceEstimate b) { return; }
        double padding = Math.Max(2, 2 * _sky.ActualWidth / Math.Max(1, _sky.ActualHeight));
        _sky.CenterOn(SkyGeometry.Interpolate(a.Target.Position, b.Target.Position, 0.5),
            Math.Clamp(a.Target.Position.DistanceTo(b.Target.Position) * padding, 1.5, 120));
    }

    private void UseMarkedDistanceTargetsClicked(object sender, RoutedEventArgs e)
    {
        var pair = _sky.AddedTargets.Select(target => TargetDistances.Find(target.Id)).OfType<TargetDistanceEstimate>().ToArray();
        if (pair.Length != 2)
        {
            SkyToolsStatus.Text = "Add exactly two targets with bundled distances using Find in the sky, or choose them in the lists.";
            return;
        }
        _updatingDistanceTargets = true;
        try
        {
            DistanceSearchA.Clear();
            DistanceSearchB.Clear();
            DistanceTargetsA.ItemsSource = TargetDistances.Search("");
            DistanceTargetsB.ItemsSource = TargetDistances.Search("");
            DistanceTargetsA.SelectedItem = pair[0];
            DistanceTargetsB.SelectedItem = pair[1];
        }
        finally { _updatingDistanceTargets = false; }
        UpdateSelectedDistanceTargets();
    }

    private void ClearDistancePairClicked(object sender, RoutedEventArgs e)
    {
        _updatingDistanceTargets = true;
        try
        {
            DistanceTargetsA.SelectedItem = DistanceTargetsB.SelectedItem = null;
            if (_sky.Measurement?.StartDistanceSource is not null || _sky.Measurement?.EndDistanceSource is not null) { _sky.ClearMeasurement(); }
            UpdateDistancePairPresentation();
            SkyToolsStatus.Text = "Target pair cleared";
        }
        finally { _updatingDistanceTargets = false; }
    }

    private void OpenDistanceSourceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TargetDistanceEstimate estimate }) { return; }
        try { Process.Start(new ProcessStartInfo(estimate.SourceUrl) { UseShellExecute = true }); }
        catch (Exception exception) { SkyToolsStatus.Text = "Could not open source: " + exception.Message; }
    }
}
