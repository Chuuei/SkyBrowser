using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Desktop;

public partial class ObserverWindow : Window
{
    private bool _initializing = true;
    private bool _updatingLocation;
    private readonly DispatcherTimer _clock = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public ObserverSettings? Result { get; private set; }

    public ObserverWindow(ObserverSettings? current = null)
    {
        InitializeComponent();
        bool fixedTime = current is { UseSystemTime: false };
        var time = fixedTime ? current!.TimeUtc.ToUniversalTime() : DateTimeOffset.Now;
        DateInput.SelectedDate = time.Date;
        TimeInput.Text = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        UseSystemTime.IsChecked = current?.UseSystemTime ?? true;
        TimeZoneInput.SelectedIndex = fixedTime ? 1 : 0;
        if (current?.Location is { } location)
        {
            SetLocation(location);
        }

        _initializing = false;
        CustomTimePanel.IsEnabled = UseSystemTime.IsChecked != true;
        CustomTimePanel.Visibility = CustomTimePanel.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        SystemTimeReadout.Visibility = CustomTimePanel.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        UpdateZoneDescription();
        UpdateClock();
        ValidateInputs();
        _clock.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) =>
        {
            _clock.Start();
            if (Map.Selection is not null)
            {
                Map.CenterOnSelection();
            }
        };
        Closed += (_, _) => _clock.Stop();
    }

    private void UpdateClock() => SystemTimeReadout.Text = DateTimeOffset.Now.ToString("dd MMM yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private void SetLocation(ObserverLocation location)
    {
        _updatingLocation = true;
        try
        {
            LatitudeInput.Text = location.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
            LongitudeInput.Text = location.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
            Map.Selection = location;
        }
        finally
        {
            _updatingLocation = false;
        }

        if (!_initializing)
        {
            ValidateInputs();
        }
    }

    private void MapLocationSelected(object? sender, ObserverLocation location) => SetLocation(location);

    private void InputsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing && !_updatingLocation)
        {
            ValidateInputs();
        }
    }

    private void DateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
        {
            ValidateInputs();
        }
    }

    private void ZoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
        {
            UpdateZoneDescription();
            ValidateInputs();
        }
    }

    private void UpdateZoneDescription() => TimeZoneDescription.Text = TimeZoneInput.SelectedIndex == 1
        ? "UTC is independent of the map location and daylight saving time."
        : $"Computer zone: {TimeZoneInfo.Local.StandardName}. This is not inferred from the map location.";

    private void TimeModeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        CustomTimePanel.IsEnabled = UseSystemTime.IsChecked != true;
        CustomTimePanel.Visibility = CustomTimePanel.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        SystemTimeReadout.Visibility = CustomTimePanel.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        ValidateInputs();
    }

    private ObserverSettings ReadSelection()
    {
        if (!TryNumber(LatitudeInput.Text, out double latitude) ||
            !TryNumber(LongitudeInput.Text, out double longitude))
        {
            throw new ArgumentException("Choose your location on the map, or enter latitude and longitude in degrees.");
        }

        var location = new ObserverLocation(latitude, longitude);
        location.Validate();
        bool live = UseSystemTime.IsChecked == true;
        DateTimeOffset instant = DateTimeOffset.UtcNow;
        if (!live)
        {
            if (!DateTime.TryParse(DateInput.Text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                throw new ArgumentException("Choose a valid observing date.");
            }

            if (!TimeSpan.TryParseExact(TimeInput.Text.Trim(), [@"hh\:mm\:ss", @"hh\:mm"], CultureInfo.InvariantCulture, out var time))
            {
                throw new ArgumentException("Enter the time as HH:mm or HH:mm:ss, using the 24-hour clock.");
            }

            instant = ObserverTimeInput.Resolve(date, time, TimeZoneInput.SelectedIndex == 1 ? TimeZoneInfo.Utc : TimeZoneInfo.Local);
        }

        var settings = new ObserverSettings(location, live, instant);
        settings.Validate();
        return settings;
    }

    private void ValidateInputs()
    {
        try
        {
            var settings = ReadSelection();
            Map.Selection = settings.Location;
            ApplyButton.IsEnabled = true;
            ValidationMessage.Text = "Apply to use this location and observing time. Cancel keeps the current selection.";
            ValidationMessage.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (ArgumentException exception)
        {
            ApplyButton.IsEnabled = false;
            ValidationMessage.Text = exception.Message;
            ValidationMessage.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 186, 123));
        }
    }

    private static bool TryNumber(string text, out double value) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void ApplyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = ReadSelection();
            DialogResult = true;
        }
        catch (ArgumentException)
        {
            ValidateInputs();
        }
    }

    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }

    private void ZoomInClicked(object sender, RoutedEventArgs e) => Map.Zoom(1.7);

    private void ZoomOutClicked(object sender, RoutedEventArgs e) => Map.Zoom(1 / 1.7);

    private void WorldClicked(object sender, RoutedEventArgs e) => Map.ResetView();

    private void CenterMarkerClicked(object sender, RoutedEventArgs e) => Map.CenterOnSelection();
}
