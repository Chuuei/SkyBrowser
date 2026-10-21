using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private void ApplySkyDefaults()
    {
        GridToggle.IsChecked = _options.ShowGrid;
        ConstellationToggle.IsChecked = _options.ShowConstellations;
        LabelToggle.IsChecked = _options.ShowLabels;
        MoonToggle.IsChecked = _options.ShowMoon;
        FootprintToggle.IsChecked = _options.ShowOutlines;
        BlendToggle.IsChecked = _options.BlendOverlaps;
        SkyOptionsChanged(this, new());
    }

    private void UpdateViewReadout()
    {
        var center = Sky.Camera.Center;
        if (Sky.Camera.Observer is { } observer)
        {
            GridToggle.Content = "Alt/Az grid";
            GridToggle.ToolTip = "Altitude/azimuth grid fixed to the local horizon. Meridians meet at the zenith (altitude 90°).";
            var direction = observer.ToHorizontal(center);
            ViewCoordinates.Text = $"Az {direction.Azimuth:000.0}°  Alt {direction.Altitude:+00.0;-00.0;00.0}°";
            ViewField.Text = $"FIELD OF VIEW  {Sky.Camera.FieldOfView:0.###}°  ·  LOCAL HORIZON";
            ViewObserverTime.Text = observer.TimeUtc.ToLocalTime()
                .ToString("dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
            ViewObserverTime.Visibility = Visibility.Visible;
            Timeline.UpdateTime(observer.TimeUtc, Sky.Observer!.UseSystemTime);
        }
        else
        {
            GridToggle.Content = "RA/Dec grid";
            GridToggle.ToolTip = "J2000 equatorial grid. Meridians meet at the north and south celestial poles.";
            ViewCoordinates.Text = $"{center.RaText}   {center.DecText}";
            ViewField.Text = $"FIELD OF VIEW  {Sky.Camera.FieldOfView:0.###}°  ·  J2000 / ICRS";
            ViewObserverTime.Visibility = Visibility.Collapsed;
        }
    }

    private void GoCoordinatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            double ra = ParseAngle(GoRa.Text), dec = ParseAngle(GoDec.Text);
            if (dec is < -90 or > 90)
            {
                throw new ArgumentException("Declination must be between −90° and +90°.");
            }

            var coordinate = new SkyCoordinate(SkyCoordinate.Normalize(ra), dec);
            Sky.ClearImageSelection();
            // Compare with the target coordinates as displayed to four decimal places.
            if (TargetChoice.SelectedItem is DeepSkyTarget target &&
                coordinate.DistanceTo(new(
                    SkyCoordinate.Normalize(Math.Round(target.Position.RightAscension, 4)),
                    Math.Round(target.Position.Declination, 4)
                )) < 1e-9)
            {
                Sky.AddTarget(target);
                Sky.CenterOn(target.Position, target.SuggestedFieldOfView);
                SetStatus($"{target.Name} · {target.TypeName} · Added to sky");
            }
            else
            {
                Sky.CenterOn(coordinate);
                SetStatus($"Centered on {coordinate} · J2000");
            }

            Sky.Focus();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filteringTargets || TargetChoice.SelectedItem is not DeepSkyTarget target || Sky is null)
        {
            return;
        }

        Sky.ClearImageSelection();
        Sky.AddTarget(target);
        Sky.CenterOn(target.Position, target.SuggestedFieldOfView);
        GoRa.Text = target.Position.RightAscension.ToString("0.####", CultureInfo.InvariantCulture);
        GoDec.Text = target.Position.Declination.ToString("0.####", CultureInfo.InvariantCulture);
        UpdateTargetSummary();
        SetStatus($"{target.Name} · {target.TypeName} · Added to sky");
    }

    private void TargetSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TargetChoice is not null)
        {
            RefreshTargetResults();
        }
    }

    private void TargetCatalogChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetChoice is not null)
        {
            RefreshTargetResults();
        }
    }

    private void RefreshTargetResults()
    {
        if (TargetCatalogChoice.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<TargetCatalog>(tag, out var catalog))
        {
            return;
        }

        var selected = TargetChoice.SelectedItem as DeepSkyTarget;
        var targets = DeepSkyCatalog.Search(TargetSearch.Text, catalog);
        _filteringTargets = true;
        try
        {
            TargetChoice.ItemsSource = targets;
            TargetChoice.SelectedItem = selected is not null && targets.Contains(selected) ? selected : null;
        }
        finally
        {
            _filteringTargets = false;
        }

        TargetSearchPlaceholder.Visibility = TargetSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTargetSummary();
    }

    private void UpdateTargetSummary()
    {
        TargetResults.Text = $"{TargetChoice.Items.Count:N0} result{(TargetChoice.Items.Count == 1 ? "" : "s")} · {Sky.AddedTargets.Count:N0} added";
        TargetChoice.IsEnabled = TargetChoice.Items.Count > 0;
        ClearTargetsButton.IsEnabled = Sky.AddedTargets.Count > 0;
    }

    private void ClearTargetsClicked(object sender, RoutedEventArgs e)
    {
        Sky.ClearAddedTargets();
        TargetChoice.SelectedIndex = -1;
        UpdateTargetSummary();
    }

    private void TargetSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TargetSearch.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && TargetChoice.Items.Count > 0)
        {
            var exact = DeepSkyCatalog.Find(TargetSearch.Text);
            if (exact is not null && TargetChoice.Items.Contains(exact))
            {
                TargetChoice.SelectedIndex = -1;
                TargetChoice.SelectedItem = exact;
            }
            else if (TargetChoice.Items.Count == 1)
            {
                TargetChoice.SelectedIndex = -1;
                TargetChoice.SelectedIndex = 0;
            }
            else
            {
                TargetChoice.IsDropDownOpen = true;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down && TargetChoice.IsEnabled)
        {
            TargetChoice.Focus();
            TargetChoice.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void SkyOptionsChanged(object sender, RoutedEventArgs e)
    {
        Sky.ShowGrid = GridToggle.IsChecked == true;
        Sky.ShowConstellations = ConstellationToggle.IsChecked == true;
        Sky.ShowLabels = LabelToggle.IsChecked == true;
        Sky.ShowMoon = MoonToggle.IsChecked == true;
        GoMoonButton.Visibility = Sky.ShowMoon ? Visibility.Visible : Visibility.Collapsed;
        UpdateSceneClock();
        Sky.ShowFootprints = FootprintToggle.IsChecked == true;
        Sky.BlendOverlaps = BlendToggle.IsChecked == true;
    }

    private void ZoomInClicked(object sender, RoutedEventArgs e) => Sky.Zoom(0.8);

    private void GoMoonClicked(object sender, RoutedEventArgs e)
    {
        Sky.ClearImageSelection();
        Sky.CenterOn(Sky.CurrentMoon.Position, Math.Min(12, Sky.Camera.FieldOfView));
    }

    private void UpdateSceneClock()
    {
        if (Sky.Observer is { UseSystemTime: true } || (Sky.Observer is null && Sky.ShowMoon))
        {
            _observerClock.Start();
        }
        else { _observerClock.Stop(); }
    }

    private void ZoomOutClicked(object sender, RoutedEventArgs e) => Sky.Zoom(1.25);

    private void OverviewClicked(object sender, RoutedEventArgs e) => Sky.CenterOn(Sky.Camera.Center, 90);

    public void ApplyObserver(ObserverSettings? observer, bool resetView = true)
    {
        Sky.SetObserver(observer, resetView);
        Timeline.Reset(observer, Sky.Camera.Observer?.TimeUtc);
        UpdateSceneClock();
    }

    private void SceneTimeSelected(object? sender, DateTimeOffset instant)
    {
        _observerClock.Stop();
        Sky.SetSceneTime(instant);
    }

    private void SceneLiveRequested(object? sender, EventArgs e)
    {
        if (Sky.Observer is null)
        {
            return;
        }

        Sky.SetSceneTime(DateTimeOffset.UtcNow, useSystemTime: true);
        _observerClock.Start();
    }
}
