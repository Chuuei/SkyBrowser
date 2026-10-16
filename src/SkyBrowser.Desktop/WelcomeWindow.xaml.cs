using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop;

public partial class WelcomeWindow : Window
{
    private readonly ApplicationOptions _current, _defaults;
    private ObserverSettings? _observer;
    private bool _initializing = true, _saving;

    public required Func<ApplicationOptions, Task> SaveOptionsAsync { get; init; }

    public WelcomeWindow(ApplicationOptions current, ApplicationOptions defaults)
    {
        _current = current;
        _defaults = defaults;
        _observer = current.Observer;
        InitializeComponent();
        StretchChoice.SelectedIndex = (int)current.DefaultDisplayMode;
        _initializing = false;
        UpdateChoices();
        Closing += (_, e) =>
        {
            if (_saving)
            {
                e.Cancel = true;
            }
        };
    }

    private void UpdateChoices()
    {
        if (_initializing)
        {
            return;
        }

        StretchDescription.Text = (DisplayMode)StretchChoice.SelectedIndex switch
        {
            DisplayMode.Automatic => "Fast default: stretches linear images onto a dark background without cleanup. Already stretched images keep their exposure.",
            DisplayMode.Linear => "For images that are already stretched. Shows their existing exposure without extra stretching or cleanup.",
            DisplayMode.AutoStretch => "Uses the library's brighter automatic stretch with a 25% sky background.",
            DisplayMode.StoredDisplay => "Uses the display function saved in each file. Files without one are shown linearly.",
            DisplayMode.Finished => "Applies a dark sky stretch and cosmetic cleanup. The first load can take longer; later loads use saved previews.",
            _ => "Choose a default stretch for new images."
        };
        LocationSummary.Text = _observer is null
            ? "No location selected · Explore the celestial atlas."
            : $"Latitude {_observer.Location.Latitude:0.######}° · Longitude {_observer.Location.Longitude:0.######}°\n" + (_observer.UseSystemTime
            ? "Current system time · Face south"
            : $"Fixed time: {_observer.TimeUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        ClearLocationButton.IsEnabled = !_saving && _observer is not null;
    }

    private void StretchChanged(object sender, SelectionChangedEventArgs e) => UpdateChoices();

    private void ChooseLocationClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ObserverWindow(_observer)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _observer = dialog.Result;
            UpdateChoices();
        }
    }

    private void ClearLocationClicked(object sender, RoutedEventArgs e)
    {
        _observer = null;
        UpdateChoices();
    }

    private void NotNowClicked(object sender, RoutedEventArgs e)
    {
        if (!_saving)
        {
            DialogResult = false;
        }
    }

    private async void UseDefaultsClicked(object sender, RoutedEventArgs e) => await SaveAsync(_defaults);

    private async void StartClicked(object sender, RoutedEventArgs e) =>
        await SaveAsync(_current with
        {
            DefaultDisplayMode = (DisplayMode)StretchChoice.SelectedIndex,
            Observer = _observer
        });

    private async Task SaveAsync(ApplicationOptions choices)
    {
        if (_saving)
        {
            return;
        }

        SetSaving(true);
        try
        {
            await SaveOptionsAsync(choices);
            _saving = false;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            SetSaving(false);
            ValidationMessage.Text = $"Could not save your choices: {exception.Message}";
        }
    }

    private void SetSaving(bool saving)
    {
        _saving = saving;
        StretchChoice.IsEnabled = !saving;
        ChooseLocationButton.IsEnabled = !saving;
        ClearLocationButton.IsEnabled = !saving && _observer is not null;
        NotNowButton.IsEnabled = !saving;
        DefaultsButton.IsEnabled = !saving;
        StartButton.IsEnabled = !saving;
        SaveProgress.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
        ValidationMessage.Text = saving ? "Saving your choices…" : "";
    }
}
