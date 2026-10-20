using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Controls;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow : Window
{
    private readonly SkyViewport _sky;
    private readonly SurveyImageService _surveys;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _initialization;
    private bool _ready, _closed;

    public SkyToolsWindow(SkyViewport sky, SurveyImageService surveys)
    {
        _sky = sky;
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
        PlanDate.SelectedDate = (sky.Camera.Observer?.TimeUtc ?? DateTimeOffset.UtcNow).UtcDateTime.Date;
        UsePlanningCenterClicked(this, new());
        UseObserverClicked(this, new());
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

    private void OverlayChoicesChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }
        _sky.ShowCatalogTargets = CatalogOverlayChoice.IsChecked == true;
        _sky.ShowScaleBar = ScaleBarChoice.IsChecked == true;
        _sky.ShowNorthEast = NorthEastChoice.IsChecked == true;
    }


}
