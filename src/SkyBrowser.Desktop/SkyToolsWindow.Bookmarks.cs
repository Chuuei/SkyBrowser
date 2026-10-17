using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Library;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private string? _bookmarkId;

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
