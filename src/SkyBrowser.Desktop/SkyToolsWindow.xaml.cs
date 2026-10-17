using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Controls;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow : Window
{
    private readonly SkyViewport _sky;
    private readonly ImageLibrary _library;
    private readonly SurveyImageService _surveys;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _surveyOperation, _planningOperation;
    private Task? _initialization;
    private bool _ready, _closed;
    private string? _bookmarkId;
    private int _cacheRefreshVersion;
    public VisibilityReport? VisibilityReport => AltitudeChart.Report;
    public SurveyImageService SurveyService => _surveys;

    public SkyToolsWindow(SkyViewport sky, ImageLibrary library, SurveyImageService surveys)
    {
        _sky = sky;
        _library = library;
        _surveys = surveys;
        InitializeComponent();
        InitializeTargetDistances();
        SurveyChoice.ItemsSource = SurveyDefinition.All;
        SurveyChoice.SelectedIndex = 0;
        SurveyAttribution.Text = SurveyDefinition.Color.Attribution + "\nhttps://alasky.cds.unistra.fr/hips-image-services/hips2fits";
        SurveyOpacityChoice.Value = sky.SurveyOpacity;
        CatalogOverlayChoice.IsChecked = sky.ShowCatalogTargets;
        ScaleBarChoice.IsChecked = sky.ShowScaleBar;
        NorthEastChoice.IsChecked = sky.ShowNorthEast;
        BookmarkOverlayChoice.IsChecked = sky.ShowBookmarks;
        PlanDate.SelectedDate = (sky.Camera.Observer?.TimeUtc ?? DateTimeOffset.UtcNow).UtcDateTime.Date;
        UsePlanningCenterClicked(this, new());
        UseObserverClicked(this, new());
        NewBookmarkClicked(this, new());
        _sky.MeasurementChanged += MeasurementChanged;
        MeasurementDistanceA.Text = sky.Measurement?.StartDistanceLightYears?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        MeasurementDistanceB.Text = sky.Measurement?.EndDistanceLightYears?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        _ready = true;
        MeasurementChanged(this, EventArgs.Empty);
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _surveyOperation?.Cancel();
            _planningOperation?.Cancel();
            _sky.MeasurementChanged -= MeasurementChanged;
            if (_sky.IsMeasuring)
            {
                _sky.ClearMeasurement();
            }
        };
    }

    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try
        {
            var bookmarks = await Task.Run(_library.GetBookmarks, _lifetime.Token);
            if (_closed)
            {
                return;
            }
            BookmarkList.ItemsSource = bookmarks;
            _sky.SetBookmarks(bookmarks);
            await RefreshSurveyCacheAsync();
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                SkyToolsStatus.Text = "Sky tools could not read saved data: " + exception.Message;
            }
        }
    }

    private static double Number(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
        ? value : throw new ArgumentException("Enter finite numbers using a decimal point.");

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private void UsePlanningCenterClicked(object sender, RoutedEventArgs e)
    {
        PlanRa.Text = Number(_sky.Camera.Center.RightAscension);
        PlanDec.Text = Number(_sky.Camera.Center.Declination);
    }

    private void UseObserverClicked(object sender, RoutedEventArgs e)
    {
        if (_sky.Observer is { } observer)
        {
            PlanLatitude.Text = Number(observer.Location.Latitude);
            PlanLongitude.Text = Number(observer.Location.Longitude);
        }
        else
        {
            SkyToolsStatus.Text = "Enter a planning latitude and longitude, or select an observer location in Options.";
        }
    }

    private async void LoadSurveyClicked(object sender, RoutedEventArgs e)
    {
        if (SurveyChoice.SelectedItem is not SurveyDefinition survey)
        {
            return;
        }
        double tangent = Math.Tan(_sky.Camera.FieldOfView * SkyCoordinate.Radians / 2);
        double extent = 2 * Math.Atan(tangent * Math.Max(1, _sky.ActualHeight / Math.Max(1, _sky.ActualWidth))) / SkyCoordinate.Radians;
        await LoadSurveyAsync(new(survey.Id, _sky.Camera.Center, Math.Clamp(extent, 0.01, 160)), SurveyOffline.IsChecked == true);
    }

    public async Task<bool> LoadSurveyAsync(SurveyRequest request, bool offlineOnly)
    {
        if (_closed)
        {
            return false;
        }
        _surveyOperation?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _surveyOperation = operation;
        SurveyLoadControls.IsEnabled = SurveyCacheControls.IsEnabled = false;
        CancelSurveyButton.Visibility = Visibility.Visible;
        SkyToolsStatus.Text = offlineOnly ? "Loading saved survey region…" : "Loading survey region from cache or CDS…";
        try
        {
            var frame = await _surveys.LoadAsync(request, offlineOnly, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_closed || !ReferenceEquals(_surveyOperation, operation))
            {
                return false;
            }
            _sky.AddSurveyBackground(frame);
            SurveyAttribution.Text = request.Survey.Attribution + "\nhttps://alasky.cds.unistra.fr/hips-image-services/hips2fits";
            SkyToolsStatus.Text = frame.FromCache ? "Survey background loaded from the offline cache" : "Survey background loaded and saved for offline use";
            await RefreshSurveyCacheAsync();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_surveyOperation, operation))
            {
                SkyToolsStatus.Text = "Survey load cancelled; previous background retained";
            }
        }
        catch (Exception exception)
        {
            if (!_closed && ReferenceEquals(_surveyOperation, operation))
            {
                SkyToolsStatus.Text = "Survey unavailable: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_surveyOperation, operation))
            {
                _surveyOperation = null;
                if (!_closed)
                {
                    SurveyLoadControls.IsEnabled = SurveyCacheControls.IsEnabled = true;
                    CancelSurveyButton.Visibility = Visibility.Collapsed;
                }
            }
        }
        return false;
    }

    private async Task RefreshSurveyCacheAsync()
    {
        int version = ++_cacheRefreshVersion;
        var entries = await Task.Run(_surveys.GetCached, _lifetime.Token);
        if (_closed || version != _cacheRefreshVersion)
        {
            return;
        }
        CachedSurveyList.ItemsSource = entries.Select(e => e.Request).ToArray();
        SurveyCacheSummary.Text = $"{entries.Length:N0} regions · {entries.Sum(e => e.Bytes) / 1048576d:0.0} MB of 512 MB · separate from image previews";
    }

    private async void ShowCachedSurveyClicked(object sender, RoutedEventArgs e)
    {
        if (CachedSurveyList.SelectedItem is SurveyRequest request && await LoadSurveyAsync(request, true))
        {
            _sky.CenterOn(request.Center, Math.Min(120, request.FieldOfView));
        }
    }

    private void CancelSurveyClicked(object sender, RoutedEventArgs e) => _surveyOperation?.Cancel();

    private void RemoveSurveyClicked(object sender, RoutedEventArgs e)
    {
        _surveyOperation?.Cancel();
        _sky.SetSurveyBackground(null);
        SkyToolsStatus.Text = "Survey background removed; saved regions retained";
    }

    private async void ClearSurveyCacheClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(_surveys.ClearCache, _lifetime.Token);
            await RefreshSurveyCacheAsync();
            if (!_closed)
            {
                SkyToolsStatus.Text = "Survey cache cleared; the displayed background is retained";
            }
        }
        catch (Exception exception) { if (!_closed) { SkyToolsStatus.Text = exception.Message; } }
    }

    private void SurveyOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
        {
            _sky.SurveyOpacity = e.NewValue;
        }
    }

    private void OverlayChoicesChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }
        _sky.ShowCatalogTargets = CatalogOverlayChoice.IsChecked == true;
        _sky.ShowScaleBar = ScaleBarChoice.IsChecked == true;
        _sky.ShowNorthEast = NorthEastChoice.IsChecked == true;
        _sky.ShowBookmarks = BookmarkOverlayChoice.IsChecked == true;
    }

    private async void CalculateVisibilityClicked(object sender, RoutedEventArgs e) => await CalculateVisibilityAsync();

    public async Task<VisibilityReport?> CalculateVisibilityAsync()
    {
        if (_closed)
        {
            return null;
        }
        _planningOperation?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _planningOperation = operation;
        CalculateVisibilityButton.IsEnabled = false;
        CancelPlanningButton.Visibility = Visibility.Visible;
        try
        {
            var target = new SkyCoordinate(Number(PlanRa), Number(PlanDec));
            var location = new ObserverLocation(Number(PlanLatitude), Number(PlanLongitude));
            if (PlanDate.SelectedDate is not { } date || !TimeSpan.TryParseExact(PlanTime.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var time) || time.TotalHours >= 24)
            {
                throw new ArgumentException("Choose a UTC start date and a time in HH:mm format.");
            }
            var start = new DateTimeOffset(DateTime.SpecifyKind(date.Date + time, DateTimeKind.Utc));
            var criteria = new VisibilityCriteria(Number(PlanMinAltitude), Number(PlanMaxSun), Number(PlanMinMoon), PlanIgnoreLowMoon.IsChecked == true);
            SkyToolsStatus.Text = "Calculating target, Sun and Moon visibility…";
            var report = await Task.Run(() => TargetVisibility.Calculate(target, location, start, criteria, operation.Token), operation.Token);
            if (_closed || !ReferenceEquals(_planningOperation, operation))
            {
                return null;
            }
            AltitudeChart.Report = report;
            VisibilitySummary.Text = FormatReport(report);
            SkyToolsStatus.Text = "Visibility calculated · all times are UTC";
            return report;
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_planningOperation, operation))
            {
                SkyToolsStatus.Text = "Visibility calculation cancelled; previous chart retained";
            }
        }
        catch (Exception exception)
        {
            if (!_closed && ReferenceEquals(_planningOperation, operation))
            {
                SkyToolsStatus.Text = "Visibility unavailable: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_planningOperation, operation))
            {
                _planningOperation = null;
                if (!_closed)
                {
                    CalculateVisibilityButton.IsEnabled = true;
                    CancelPlanningButton.Visibility = Visibility.Collapsed;
                }
            }
        }
        return null;
    }

    public static string FormatReport(VisibilityReport report)
    {
        var text = new StringBuilder($"{report.StartUtc:yyyy-MM-dd HH:mm} – {report.EndUtc:yyyy-MM-dd HH:mm} UTC\n" +
            $"J2000 {report.Target} · latitude {report.Location.Latitude:0.####}°, longitude {report.Location.Longitude:0.####}°\n" +
            $"{report.HorizonStatus}. Maximum altitude {report.MaximumAltitude:0.0}°.\n");
        foreach (var crossing in report.HorizonEvents)
        {
            text.AppendLine($"{(crossing.Rising ? "Rise" : "Set")} (geometric horizon): {crossing.TimeUtc:MM-dd HH:mm} UTC");
        }
        text.AppendLine(report.TransitUtc is { } transit ? $"Upper transit: {transit:MM-dd HH:mm} UTC" : "No distinct upper transit in this period.");
        var middle = report.Samples[report.Samples.Length / 2];
        text.AppendLine($"Moon at period midpoint: {report.MoonPhase} · {middle.MoonIllumination:P0} illuminated · separation {middle.MoonSeparation:0.0}° · altitude {middle.MoonAltitude:0.0}°");
        void Intervals(string label, VisibilityInterval[] intervals)
        {
            text.AppendLine(label + (intervals.Length == 0 ? ": none in this period" : ":"));
            foreach (var interval in intervals)
            {
                text.AppendLine($"  {interval.StartUtc:MM-dd HH:mm} – {interval.EndUtc:MM-dd HH:mm} UTC ({interval.Duration.TotalHours:0.##} h)");
            }
        }
        Intervals($"Darkness (Sun ≤ {report.Criteria.MaximumSunAltitude:0.#}°)", report.Darkness);
        Intervals("Imaging windows (altitude, darkness and Moon criteria)", report.ImagingWindows);
        return text.ToString().TrimEnd();
    }

    private void CancelPlanningClicked(object sender, RoutedEventArgs e) => _planningOperation?.Cancel();

    private void NewBookmarkClicked(object sender, RoutedEventArgs e)
    {
        BookmarkList.SelectedItem = null;
        _bookmarkId = null;
        BookmarkName.Text = "";
        BookmarkNotes.Text = "";
        BookmarkRa.Text = Number(_sky.Camera.Center.RightAscension);
        BookmarkDec.Text = Number(_sky.Camera.Center.Declination);
        BookmarkField.Text = Number(_sky.Camera.FieldOfView);
    }

    private void BookmarkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not SkyBookmark bookmark)
        {
            return;
        }
        _bookmarkId = bookmark.Id;
        BookmarkName.Text = bookmark.Name;
        BookmarkNotes.Text = bookmark.Notes;
        BookmarkRa.Text = Number(bookmark.Position.RightAscension);
        BookmarkDec.Text = Number(bookmark.Position.Declination);
        BookmarkField.Text = Number(bookmark.FieldOfView);
    }

    private async void SaveBookmarkClicked(object sender, RoutedEventArgs e) => await SaveBookmarkAsync();

    public async Task<bool> SaveBookmarkAsync()
    {
        BookmarkActions.IsEnabled = false;
        try
        {
            var bookmark = new SkyBookmark(_bookmarkId ?? Guid.NewGuid().ToString("N"), BookmarkName.Text.Trim(),
                new(Number(BookmarkRa), Number(BookmarkDec)), Number(BookmarkField), BookmarkNotes.Text);
            bookmark.Validate();
            var bookmarks = await Task.Run(() =>
            {
                _library.SaveBookmark(bookmark, _lifetime.Token);
                return _library.GetBookmarks();
            }, _lifetime.Token);
            if (_closed)
            {
                return false;
            }
            _sky.SetBookmarks(bookmarks);
            BookmarkList.ItemsSource = bookmarks;
            BookmarkList.SelectedItem = bookmarks.Single(b => b.Id == bookmark.Id);
            SkyToolsStatus.Text = "Bookmark and notes saved";
            return true;
        }
        catch (Exception exception) { if (!_closed) { SkyToolsStatus.Text = "Bookmark not saved: " + exception.Message; } }
        finally { if (!_closed) { BookmarkActions.IsEnabled = true; } }
        return false;
    }

    private void GoBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is SkyBookmark bookmark)
        {
            _sky.CenterOn(bookmark.Position, bookmark.FieldOfView);
        }
    }

    private async void DeleteBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not SkyBookmark bookmark)
        {
            return;
        }
        BookmarkActions.IsEnabled = false;
        try
        {
            var bookmarks = await Task.Run(() => { _library.DeleteBookmark(bookmark.Id); return _library.GetBookmarks(); }, _lifetime.Token);
            if (_closed)
            {
                return;
            }
            BookmarkList.ItemsSource = bookmarks;
            _sky.SetBookmarks(bookmarks);
            NewBookmarkClicked(this, new());
            SkyToolsStatus.Text = "Bookmark deleted";
        }
        catch (Exception exception) { if (!_closed) { SkyToolsStatus.Text = "Bookmark not deleted: " + exception.Message; } }
        finally { if (!_closed) { BookmarkActions.IsEnabled = true; } }
    }

    private void BeginMeasurementClicked(object sender, RoutedEventArgs e)
    {
        _sky.BeginMeasurement();
        Owner?.Activate();
        _sky.Focus();
    }

    private void MeasureCenterClicked(object sender, RoutedEventArgs e) => _sky.PickMeasurementPoint(_sky.Camera.Center);
    private void ClearMeasurementClicked(object sender, RoutedEventArgs e) => _sky.ClearMeasurement();

    private void MeasurementChanged(object? sender, EventArgs e)
    {
        MeasurementDistanceInputs.IsEnabled = _sky.Measurement is not null && !_sky.IsMeasuring;
        if (_sky.Measurement is null || _updatingDistanceTargets ||
            _sky.Measurement.StartDistanceSource is not null || _sky.Measurement.EndDistanceSource is not null)
        {
            _ready = false;
            MeasurementDistanceA.Text = _sky.Measurement?.StartDistanceLightYears?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            MeasurementDistanceB.Text = _sky.Measurement?.EndDistanceLightYears?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            _ready = true;
        }
        MeasurementSummary.Text = _sky.Measurement is { } measurement
            ? $"Angular distance {measurement.Label} ({measurement.Degrees:0.######}°)\nA: {measurement.Start} · B: {measurement.End}" +
                (_sky.IsMeasuring ? "\nChoose the second point in the atlas, or use its center." : "")
            : _sky.IsMeasuring ? "Choose the first point in the atlas, or use its center. Escape cancels." : "Measurement cleared.";
        UpdatePhysicalDistanceSummary();
        SynchronizeDistanceTargets();
    }

    private void MeasurementDistancesChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _sky.Measurement is null || _sky.IsMeasuring) { return; }
        try
        {
            double? a = string.IsNullOrWhiteSpace(MeasurementDistanceA.Text) ? null : Number(MeasurementDistanceA);
            double? b = string.IsNullOrWhiteSpace(MeasurementDistanceB.Text) ? null : Number(MeasurementDistanceB);
            _sky.SetMeasurementDistances(a, b);
            UpdatePhysicalDistanceSummary();
        }
        catch (ArgumentException exception)
        {
            _sky.SetMeasurementDistances(null, null);
            PhysicalDistanceSummary.Text = exception.Message;
        }
    }

    private void UpdatePhysicalDistanceSummary()
    {
        PhysicalDistanceSummary.Text = _sky.Measurement?.SeparationLightYears is { } distance
            ? "Straight-line 3D separation: " + (_sky.Measurement.StartDistanceSource is not null || _sky.Measurement.EndDistanceSource is not null
                ? TargetDistances.FormatApproximate(distance) : distance.ToString("G6", CultureInfo.InvariantCulture)) + " light-years (Euclidean estimate from the supplied distances)."
            : "Supply both known object distances, or select a pair in Target distances for an offline published estimate.";
    }
}
