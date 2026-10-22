using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private void MeasureExactClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            double raA = Number(MeasureRaA), decA = Number(MeasureDecA);
            double raB = Number(MeasureRaB), decB = Number(MeasureDecB);
            if (decA is < -90 or > 90 || decB is < -90 or > 90)
            {
                throw new ArgumentException("Declinations must be between -90 and 90 degrees.");
            }
            _sky.BeginMeasurement();
            _sky.PickMeasurementPoint(new(SkyCoordinate.Normalize(raA), decA));
            _sky.PickMeasurementPoint(new(SkyCoordinate.Normalize(raB), decB));
            SkyToolsStatus.Text = "Measured the supplied coordinates. Results are below.";
        }
        catch (ArgumentException exception)
        {
            SkyToolsStatus.Text = exception.Message;
        }
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
            : "Supply both known object distances, or choose catalogue targets below for an offline published estimate.";
    }
}
