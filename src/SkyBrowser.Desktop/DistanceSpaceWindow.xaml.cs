using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Controls;

namespace SkyBrowser.Desktop;

public partial class DistanceSpaceWindow : Window
{
    private bool _ready, _synchronizing;
    public DistanceSpaceView View => SpaceView;

    public DistanceSpaceWindow()
    {
        InitializeComponent();
        SpaceView.SelectionChanged += SelectionChanged;
        SpaceView.ComparisonChanged += (_, _) => UpdatePair();
        SpaceView.ViewChanged += (_, _) =>
        {
            SecondTargetChoice.IsChecked = SpaceView.SelectSecondTarget;
            ScaleChoice.SelectedIndex = -1;
            UpdateStatus();
        };
        _ready = true;
        UpdateResults();
        UpdateStatus();
        Loaded += (_, _) => SpaceView.Focus();
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) { UpdateResults(); }
    }

    private void CatalogChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) { return; }
        SpaceView.Catalog = Enum.Parse<TargetCatalog>((string)((ComboBoxItem)CatalogChoice.SelectedItem).Tag);
        SpaceView.Selected = null;
        UpdateResults();
    }

    private void UpdateResults()
    {
        _synchronizing = true;
        try
        {
            ObjectList.ItemsSource = TargetDistances.Search(ObjectSearch.Text, SpaceView.Catalog);
            ObjectList.SelectedItem = SpaceView.Selected;
        }
        finally { _synchronizing = false; }
        UpdateStatus();
    }

    private void ObjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _synchronizing) { return; }
        bool second = SpaceView.SelectSecondTarget || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        SpaceView.SelectTarget(ObjectList.SelectedItem as TargetDistanceEstimate, second);
        if (second)
        {
            _synchronizing = true;
            try { ObjectList.SelectedItem = SpaceView.Selected; }
            finally { _synchronizing = false; }
        }
    }

    private void SelectionChanged(object? sender, EventArgs e)
    {
        var selected = SpaceView.Selected;
        _synchronizing = true;
        try
        {
            ObjectList.SelectedItem = selected;
            if (selected is not null) { ObjectList.ScrollIntoView(selected); }
        }
        finally { _synchronizing = false; }
        SelectedName.Text = selected?.Target.Name ?? "Select a point or search result";
        SelectedDetails.Text = selected is null ? "Click the map to give it keyboard focus." :
            $"{selected.Target.AliasText}\n{selected.Target.TypeName}\nFrom Solar System: {selected.SourceValue}\n{selected.SourceName}\nMethod: {selected.Method}";
        SelectedDetails.ToolTip = selected?.SourceDetails;
        ObjectSourceButton.ToolTip = selected?.SourceDetails;
        FocusObjectButton.IsEnabled = ObjectSourceButton.IsEnabled = selected is not null;
        UpdatePair();
        UpdateStatus();
    }

    private void SecondTargetModeChanged(object sender, RoutedEventArgs e) => SpaceView.SelectSecondTarget = SecondTargetChoice.IsChecked == true;

    private void UpdatePair()
    {
        var measurement = SpaceView.PairMeasurement;
        PairPanel.Visibility = measurement is null ? Visibility.Collapsed : Visibility.Visible;
        PairResult.Text = measurement?.SeparationLightYears is { } distance
            ? "Estimated 3D separation: " + TargetDistances.FormatApproximate(distance) + " light-years" : "";
        PairNames.Text = measurement is null ? "" : $"A: {SpaceView.Selected!.Target.Name} ↔ B: {SpaceView.ComparisonTarget!.Target.Name}";
        PairResult.ToolTip = measurement is null ? null :
            $"A: {SpaceView.Selected!.SourceDetails}\n\nB: {SpaceView.ComparisonTarget!.SourceDetails}\n\nStraight-line estimate; distance errors are not propagated.";
    }

    private void FramePairClicked(object sender, RoutedEventArgs e)
    {
        SpaceView.FramePair();
        SpaceView.Focus();
    }

    private void ClearPairClicked(object sender, RoutedEventArgs e) => SpaceView.ClearComparison();

    private void FocusObjectClicked(object sender, RoutedEventArgs e)
    {
        SpaceView.FocusSelected(keepScale: true);
        SpaceView.Focus();
    }

    private void HomeClicked(object sender, RoutedEventArgs e)
    {
        SpaceView.ResetToSun();
        SpaceView.Focus();
    }

    private void ScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ScaleChoice.SelectedItem is not ComboBoxItem item) { return; }
        string scale = (string)item.Tag;
        double distance = scale == "all" ? TargetDistances.All.Max(estimate => estimate.LightYears) * 3 :
            double.Parse(scale, CultureInfo.InvariantCulture);
        SpaceView.Camera.Frame(SpaceView.Camera.Center, distance);
        SpaceView.RefreshCamera();
        SpaceView.Focus();
    }

    private void DisplayChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) { return; }
        SpaceView.ShowGuides = GuidesChoice.IsChecked == true;
        SpaceView.ShowLabels = LabelsChoice.IsChecked == true;
    }

    private void UpdateStatus()
    {
        ReferencePlaneDescription.Text = $"J2000 equatorial reference plane through {SpaceView.ReferenceOriginName}. Rings measure light-years from this origin. " +
            "Symbols represent object centres, not their size. Published distances are approximate; errors and cosmological effects are not modelled. Labels are reduced to avoid overlap.";
        string zoomHint = SpaceView.Camera.Distance >= DistanceSpaceCamera.MaximumDistance
            ? "Maximum zoom-out scale reached."
            : "Zoom out to reveal more distant objects.";
        string center = SpaceView.Camera.Center.Length < 1e-6 ? "Sun / Solar System" :
            SpaceView.Selected is { } selected &&
            (SpaceView.Camera.Center - DistanceSpaceCamera.Position(selected)).Length < Math.Max(1e-6, selected.LightYears * 1e-12)
                ? selected.Target.Name : "Free space";
        SpaceStatus.Text = $"{TargetDistances.Coverage(SpaceView.Catalog).Available:N0} offline objects · " +
            $"Navigation centre: {center} · " +
            $"Camera {DistanceSpaceView.FormatScale(SpaceView.Camera.Eye.Length)} ly from Solar System · " +
            $"Focus span {DistanceSpaceView.FormatScale(SpaceView.Camera.ViewHeight)} ly. {zoomHint}";
    }

    private void SourceClicked(object sender, RoutedEventArgs e)
    {
        if (SpaceView.Selected is not { } selected) { return; }
        try { Process.Start(new ProcessStartInfo(selected.SourceUrl) { UseShellExecute = true }); }
        catch (Exception exception) { SpaceStatus.Text = "Could not open source: " + exception.Message; }
    }
}
