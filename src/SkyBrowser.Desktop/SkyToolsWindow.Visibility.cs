using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop;

public partial class SkyToolsWindow
{
    private CancellationTokenSource? _planningOperation;
    public VisibilityReport? VisibilityReport => AltitudeChart.Report;

    private void VisibilityInputChanged(object sender, TextChangedEventArgs e)
    {
        // DatePicker also contains a TextBox and may format it when focus moves.
        // Only our actual numeric/time inputs invalidate a running calculation.
        if (e.OriginalSource is TextBox box && new[] { PlanRa, PlanDec, PlanLatitude, PlanLongitude, PlanTime,
                PlanMinAltitude, PlanMaxSun, PlanMinMoon }.Contains(box))
        {
            InvalidateVisibility();
        }
    }
    private void VisibilityDateChanged(object sender, SelectionChangedEventArgs e) => InvalidateVisibility();
    private void VisibilityCriteriaChanged(object sender, RoutedEventArgs e) => InvalidateVisibility();

    private void InvalidateVisibility()
    {
        if (!_ready || _closed)
        { return; }
        _planningOperation?.Cancel();
        AltitudeChart.Report = null;
        VisibilitySummary.Text = "Planning settings changed. Calculate visibility to update the chart and times.";
    }

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
            operation.Token.ThrowIfCancellationRequested();
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
                SkyToolsStatus.Text = "Visibility calculation cancelled";
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
}
