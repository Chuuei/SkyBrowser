using System.Windows;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private CancellationTokenSource? _surveyOperation;
    private int _cacheRefreshVersion;
    public SurveyImageService SurveyService => _surveys;

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
            _sky.ClearImageSelection();
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
}
