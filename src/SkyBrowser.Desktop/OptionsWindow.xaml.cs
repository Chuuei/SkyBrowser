using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop;

public partial class OptionsWindow : Window
{
    private bool _initializing = true, _saving;
    private ObserverSettings? _observer;
    private readonly ApplicationOptions _defaults;

    public Func<ApplicationOptions, Task>? SaveOptionsAsync { get; init; }

    public Func<ImageCacheStatistics>? ReadCacheStatistics { get; init; }

    public Func<Task>? ClearCachePreviewsAsync { get; init; }

    public ApplicationOptions? Result { get; private set; }

    public OptionsWindow(ApplicationOptions current, string optionsPath, ApplicationOptions? defaults = null)
    {
        _defaults = defaults ?? new();
        InitializeComponent();
        OptionsFilePath.Text = optionsPath;
        Populate(current);
        Closing += (_, e) =>
        {
            if (_saving)
            {
                e.Cancel = true;
            }
        };
        Loaded += async (_, _) =>
        {
            ClearPreviewsButton.IsEnabled = ClearCachePreviewsAsync is not null;
            await LoadCacheUsageAsync();
        };
    }

    private async Task LoadCacheUsageAsync()
    {
        try
        {
            if (ReadCacheStatistics is not { } read)
            {
                CacheUsageText.Text = "Used: unavailable";
                return;
            }

            var statistics = await Task.Run(read);
            if (!IsVisible)
            {
                return;
            }

            if (!statistics.IsAvailable)
            {
                CacheUsageText.Text = "Used: unavailable";
                CacheUsageText.ToolTip = statistics.LastError;
                return;
            }

            long bytes = statistics.StoredBytes;
            string size = bytes >= 1073741824
                ? $"{bytes / 1073741824d:N2} GB"
                : bytes >= 1048576
                ? $"{bytes / 1048576d:N1} MB"
                : bytes >= 1024 ? $"{bytes / 1024d:N1} KB" : $"{bytes:N0} bytes";
            CacheUsageText.Text = $"Used: {size} · {statistics.PreviewCount:N0} previews";
            CacheUsageText.ToolTip = $"Active cache: {statistics.DatabasePath}\nStored preview data; SQLite pages and journals add some disk overhead.\nChanging the folder takes effect when you save options.";
        }
        catch (Exception exception)
        {
            if (IsVisible)
            {
                CacheUsageText.Text = "Used: unavailable";
                CacheUsageText.ToolTip = exception.Message;
            }
        }
    }

    private void Populate(ApplicationOptions options)
    {
        _initializing = true;
        try
        {
            StretchChoice.SelectedIndex = (int)options.DefaultDisplayMode;
            BackgroundInput.Text = (options.DefaultFinishSettings.TargetBackground * 100).ToString("0.##", CultureInfo.InvariantCulture);
            BalanceBackground.IsChecked = options.DefaultFinishSettings.BalanceBackground;
            ReduceGradients.IsChecked = options.DefaultFinishSettings.ReduceGradients;
            ReduceNoise.IsChecked = options.DefaultFinishSettings.ReduceNoise;
            EnhanceDetail.IsChecked = options.DefaultFinishSettings.EnhanceDetail;
            CacheDirectoryInput.Text = options.CacheDirectory;
            CacheSizeInput.Text = options.CacheSizeGb.ToString("0.##", CultureInfo.InvariantCulture);
            _observer = options.Observer;
            EnableObserver.IsChecked = _observer is not null;
            ShowGrid.IsChecked = options.ShowGrid;
            ShowConstellations.IsChecked = options.ShowConstellations;
            ShowLabels.IsChecked = options.ShowLabels;
            ShowOutlines.IsChecked = options.ShowOutlines;
            BlendOverlaps.IsChecked = options.BlendOverlaps;
            AstapPathInput.Text = options.AstapExecutablePath;
            AstapDatabaseInput.Text = options.AstapDatabaseDirectory;
            AstapTimeoutInput.Text = options.AstapTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            AstapFieldInput.Text = options.AstapFieldHeightDegrees.ToString("0.####", CultureInfo.InvariantCulture);
        }
        finally
        {
            _initializing = false;
        }

        ValidateInputs();
    }

    private ApplicationOptions ReadSelection()
    {
        if (!Number(BackgroundInput.Text, out double background))
        {
            throw new ArgumentException("Enter a sky background percentage between 2 and 25.");
        }

        if (!Number(CacheSizeInput.Text, out double size))
        {
            throw new ArgumentException("Enter the maximum cache size in GB.");
        }

        if (EnableObserver.IsChecked == true && _observer is null)
        {
            throw new ArgumentException("Choose an observer location on the map, or disable the startup observer.");
        }

        if (!int.TryParse(AstapTimeoutInput.Text, out int timeout) || !Number(AstapFieldInput.Text, out double field))
        {
            throw new ArgumentException("Enter an ASTAP time limit and field height (0 for automatic).");
        }

        return new ApplicationOptions
        {
            CacheDirectory = CacheDirectoryInput.Text.Trim(),
            CacheSizeGb = size,
            DefaultDisplayMode = (DisplayMode)StretchChoice.SelectedIndex,
            DefaultFinishSettings = new()
            {
                TargetBackground = background / 100,
                BalanceBackground = BalanceBackground.IsChecked == true,
                ReduceGradients = ReduceGradients.IsChecked == true,
                ReduceNoise = ReduceNoise.IsChecked == true,
                EnhanceDetail = EnhanceDetail.IsChecked == true
            },
            Observer = EnableObserver.IsChecked == true ? _observer : null,
            ShowGrid = ShowGrid.IsChecked == true,
            ShowConstellations = ShowConstellations.IsChecked == true,
            ShowLabels = ShowLabels.IsChecked == true,
            ShowOutlines = ShowOutlines.IsChecked == true,
            BlendOverlaps = BlendOverlaps.IsChecked == true,
            AstapExecutablePath = AstapPathInput.Text.Trim().Trim('"'),
            AstapDatabaseDirectory = AstapDatabaseInput.Text.Trim().Trim('"'),
            AstapTimeoutSeconds = timeout,
            AstapFieldHeightDegrees = field
        }.Normalize();
    }

    private void ValidateInputs()
    {
        if (_initializing || _saving)
        {
            return;
        }

        ObserverSummary.Text = _observer is null
            ? "No observer location selected."
            : $"Latitude {_observer.Location.Latitude:0.######}° · Longitude {_observer.Location.Longitude:0.######}°\n" + (_observer.UseSystemTime
            ? "Current system time · Face south on startup"
            : $"Fixed time: {_observer.TimeUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        try
        {
            _ = ReadSelection();
            SaveButton.IsEnabled = true;
            ValidationMessage.Text = "Options are saved for your Windows user.";
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            SaveButton.IsEnabled = false;
            ValidationMessage.Text = exception.Message;
        }
    }

    private static bool Number(string text, out double value) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void InputsChanged(object sender, TextChangedEventArgs e) => ValidateInputs();

    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateInputs();

    private void ObserverEnabledChanged(object sender, RoutedEventArgs e) => ValidateInputs();

    private void BrowseCacheClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the processed-image cache folder",
            FolderName = Directory.Exists(CacheDirectoryInput.Text)
                ? CacheDirectoryInput.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dialog.ShowDialog(this) == true)
        {
            CacheDirectoryInput.Text = dialog.FolderName;
        }
    }

    private void BrowseAstapClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose ASTAP or ASTAP CLI", Filter = "ASTAP executable|*.exe|All files|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            AstapPathInput.Text = dialog.FileName;
        }
    }

    private void BrowseAstapDatabaseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the ASTAP star database folder" };
        if (dialog.ShowDialog(this) == true)
        {
            AstapDatabaseInput.Text = dialog.FolderName;
        }
    }

    private void ChooseObserverClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ObserverWindow(_observer)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _observer = dialog.Result;
        EnableObserver.IsChecked = _observer is not null;
        ValidateInputs();
    }

    private void ClearObserverClicked(object sender, RoutedEventArgs e)
    {
        _observer = null;
        EnableObserver.IsChecked = false;
        ValidateInputs();
    }

    private void RestoreDefaultsClicked(object sender, RoutedEventArgs e) => Populate(_defaults);

    private async void ClearPreviewsClicked(object sender, RoutedEventArgs e)
    {
        if (_saving || ClearCachePreviewsAsync is not { } clear)
        {
            return;
        }

        SetSaving(true);
        ValidationMessage.Text = "Clearing saved previews…";
        try
        {
            await clear();
            await LoadCacheUsageAsync();
            SetSaving(false);
            ValidationMessage.Text = "Saved previews cleared. Image preferences, recent folders and loaded images are retained.";
        }
        catch (Exception exception)
        {
            SetSaving(false);
            ValidationMessage.Text = $"Could not clear previews: {exception.Message}";
        }
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = ReadSelection();
            SetSaving(true);
            if (SaveOptionsAsync is { } save)
            {
                await save(options);
            }

            Result = options;
            _saving = false;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            SetSaving(false);
            ValidationMessage.Text = $"Could not save options: {exception.Message}";
        }
    }

    private void SetSaving(bool saving)
    {
        _saving = saving;
        SettingsTabs.IsEnabled = !saving;
        SaveButton.IsEnabled = !saving;
        ClearPreviewsButton.IsEnabled = !saving && ClearCachePreviewsAsync is not null;
        CancelButton.IsEnabled = !saving;
        DefaultsButton.IsEnabled = !saving;
        SaveProgress.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
        if (saving)
        {
            ValidationMessage.Text = "Saving options and updating the cache…";
        }
        else
        {
            ValidateInputs();
        }
    }
}
