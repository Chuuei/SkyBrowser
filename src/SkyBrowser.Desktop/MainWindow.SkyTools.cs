using System.IO;
using System.Windows;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private SkyToolsWindow? _skyToolsWindow;
    private SurveyImageService? _surveyService;
    private CancellationTokenSource? _surveyRestoreOperation;
    private long? _defaultSurveyRevision;

    private SurveyImageService SurveyService => _surveyService ??= new(Path.Combine(Path.GetDirectoryName(_optionsStore.FilePath)!, "SurveyCache"));

    public async Task RestoreCachedSurveyBackgroundsAsync()
    {
        _surveyRestoreOperation?.Cancel();
        if (_closing) { return; }
        if (!_options.ShowCachedSurveyImages)
        {
            if (_defaultSurveyRevision == Sky.SurveyRevision) { Sky.SetSurveyBackground(null); }
            _defaultSurveyRevision = null;
            return;
        }
        using var operation = new CancellationTokenSource();
        _surveyRestoreOperation = operation;
        long revision = Sky.SurveyRevision;
        try
        {
            var frames = await SurveyService.LoadCachedAsync(operation.Token);
            if (!_closing && !operation.IsCancellationRequested && Sky.SurveyRevision == revision)
            {
                Sky.SetSurveyBackgrounds(frames);
                _defaultSurveyRevision = Sky.SurveyRevision;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_closing && !operation.IsCancellationRequested)
            {
                SetStatus("Cached DSS2 images unavailable: " + exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_surveyRestoreOperation, operation)) { _surveyRestoreOperation = null; }
        }
    }

    private void SkyToolsClicked(object sender, RoutedEventArgs e) => OpenSkyTools();

    public SkyToolsWindow OpenSkyTools()
    {
        if (_skyToolsWindow is not null)
        {
            _skyToolsWindow.Activate();
            return _skyToolsWindow;
        }
        var window = new SkyToolsWindow(Sky, SurveyService) { Owner = this };
        _skyToolsWindow = window;
        window.Closed += (_, _) => _skyToolsWindow = null;
        window.Show();
        return window;
    }
}
