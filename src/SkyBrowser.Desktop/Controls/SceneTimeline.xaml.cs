using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

/// <summary>Scrubs a bounded UTC interval while displaying computer-local dates and offsets.</summary>
public partial class SceneTimeline : UserControl
{
    private static readonly DateTimeOffset Earliest = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Latest = new DateTimeOffset(2101, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);
    private DateTimeOffset _anchor;
    private DateTimeOffset _displayTime;
    private bool _updating = true;

    public event EventHandler<DateTimeOffset>? TimeSelected;

    public event EventHandler? LiveRequested;

    public SceneTimeline()
    {
        InitializeComponent();
        _updating = false;
    }

    public void Reset(ObserverSettings? observer, DateTimeOffset? instant)
    {
        Visibility = observer is null ? Visibility.Collapsed : Visibility.Visible;
        if (observer is null || instant is null)
        {
            return;
        }

        Recenter(instant.Value);
        UpdateTime(instant.Value, observer.UseSystemTime);
    }

    public void UpdateTime(DateTimeOffset instant, bool live)
    {
        _displayTime = instant.ToUniversalTime();
        TimeReadout.Text = instant.ToLocalTime().ToString("dd MMM yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture);
        TimeMode.Text = live ? "LIVE" : "PAUSED";
        PreviousDayButton.IsEnabled = _displayTime > Earliest;
        NextDayButton.IsEnabled = _displayTime < Latest;
        if (live)
        {
            // Keep zero at the displayed live instant, even after the app has been open
            // for hours. Once paused, the anchor stays fixed throughout a slider drag.
            Recenter(instant);
        }
    }

    private void Recenter(DateTimeOffset instant)
    {
        _anchor = instant.ToUniversalTime();
        _updating = true;
        try
        {
            TimeSlider.Minimum = Math.Max(-720, (Earliest - _anchor).TotalMinutes);
            TimeSlider.Maximum = Math.Min(720, (Latest - _anchor).TotalMinutes);
            TimeSlider.Value = 0;
            RangeStart.Text = $"{TimeSlider.Minimum / 60:0.#} h";
            RangeEnd.Text = $"+{TimeSlider.Maximum / 60:0.#} h";
            RangeStart.ToolTip = AtOffset(TimeSlider.Minimum).ToLocalTime().ToString("dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
            RangeEnd.ToolTip = AtOffset(TimeSlider.Maximum).ToLocalTime().ToString("dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    private DateTimeOffset AtOffset(double minutes)
    {
        // Clamp ticks before constructing a date: floating-point slider endpoints can
        // round just beyond the supported year boundary. UTC arithmetic also skips DST gaps.
        long ticks = _anchor.UtcTicks + (long)Math.Round(minutes * TimeSpan.TicksPerMinute);
        return new(Math.Clamp(ticks, Earliest.UtcTicks, Latest.UtcTicks), TimeSpan.Zero);
    }

    private void TimeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updating && IsVisible)
        {
            TimeSelected?.Invoke(this, AtOffset(e.NewValue));
        }
    }

    private void StepDay(int days)
    {
        var target = new DateTimeOffset(
            Math.Clamp(_displayTime.UtcTicks + days * TimeSpan.TicksPerDay, Earliest.UtcTicks, Latest.UtcTicks),
            TimeSpan.Zero);
        Recenter(target);
        TimeSelected?.Invoke(this, target);
    }

    private void PreviousDayClicked(object sender, RoutedEventArgs e) => StepDay(-1);

    private void NextDayClicked(object sender, RoutedEventArgs e) => StepDay(1);

    private void LiveClicked(object sender, RoutedEventArgs e) => LiveRequested?.Invoke(this, EventArgs.Empty);
}
