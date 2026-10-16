using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Desktop.Models;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Desktop;

public partial class MainWindow : Window
{
    // Inspector controls raise change events when populated from a selected image.
    // Suppress those events until all values reflect that image.
    private bool _updating = true;
    private bool _closing;
    private bool _refreshingFolders;
    private bool _firstRunHandled;
    private GridLength _collectionHeight = new(3, GridUnitType.Star);
    private GridLength _inspectorHeight = new(2, GridUnitType.Star);
    private ProcessedImageCache _cache;
    private CachedSkyImageLoader _imageLoader;
    private readonly ApplicationOptionsStore _optionsStore;
    private ApplicationOptions _options;
    private bool _ownsCache, _applyingOptions;
    private readonly DispatcherTimer _observerClock = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private readonly ImageCollectionSearch _collectionSearch = new();
    private readonly ListCollectionView _collectionView;
    private bool _filteringTargets;
    private CancellationTokenSource? _operation;
    private ImageLoadProgressWindow? _loadProgress;
    private readonly StringBuilder _details = new(
        "SkyBrowser uses J2000/ICRS equatorial coordinates.\n" +
        "Drag to pan; scroll to zoom; arrow keys to navigate; Home for overview; F to fit the selected image.\n\n" +
        "Automatic display detection is a heuristic. Select Linear or Library auto stretch to override it.\n" +
        "Textures are limited to 2048 pixels on their longest side; scientific source files are never modified.\n" +
        "Cancellation is checked between decoding/rendering stages.\n\n" +
        "Sky background: d3-celestial by Olaf Frohn (BSD-3-Clause), Hipparcos J2000 star positions. " +
        "See THIRD-PARTY-NOTICES.md.\n\n");

    private SkyImageLayer? Selected => LayerList?.SelectedItem as SkyImageLayer;

    public Controls.SkyViewport SkyView => Sky;

    public string Status => StatusText.Text;

    public string? CurrentFolderPath { get; private set; }

    public ApplicationOptions Options => _options;

    private sealed record Startup(ProcessedImageCache Cache, ApplicationOptionsStore Store, ApplicationOptions Options, bool OwnsCache);

    public MainWindow() : this(ApplicationOptionsStore.Default)
    {
    }

    public MainWindow(ApplicationOptionsStore store) : this(CreateStartup(store))
    {
    }

    public MainWindow(ProcessedImageCache cache) : this(CreateStartup(cache))
    {
    }

    private static Startup CreateStartup(ApplicationOptionsStore store)
    {
        var options = store.Read();
        return new(new(options.CacheDirectory, options.CacheLimitBytes), store, options, true);
    }

    private static Startup CreateStartup(ProcessedImageCache cache)
    {
        var defaults = new ApplicationOptions
        {
            CacheDirectory = Path.GetDirectoryName(cache.DatabasePath)!,
            CacheSizeGb = cache.LimitBytes / 1073741824d
        };
        var store = new ApplicationOptionsStore(Path.Combine(defaults.CacheDirectory, "settings.json"), defaults);
        return new(
            cache,
            store,
            store.Read() with { CacheDirectory = defaults.CacheDirectory, CacheSizeGb = defaults.CacheSizeGb },
            false
        );
    }

    private MainWindow(Startup startup)
    {
        _cache = startup.Cache;
        _optionsStore = startup.Store;
        _options = startup.Options;
        _ownsCache = startup.OwnsCache;
        _imageLoader = CreateLoader(_cache, _options);
        InitializeComponent();
        _collectionView = new(Sky.Layers)
        {
            Filter = item => item is SkyImageLayer layer && _collectionSearch.Matches(layer.Image)
        };
        LayerList.ItemsSource = _collectionView;
        ((INotifyCollectionChanged)_collectionView).CollectionChanged += (_, _) => UpdateCollection();
        RefreshTargetResults();
        Sky.TargetsChanged += (_, _) => UpdateTargetSummary();
        Sky.ViewChanged += (_, _) => UpdateViewReadout();
        Sky.LayerSelected += (_, layer) =>
        {
            if (!_collectionView.Contains(layer))
            {
                CollectionSearch.Clear();
            }

            LayerList.SelectedItem = layer;
            LayerList.ScrollIntoView(layer);
        };
        Sky.OpenSourceRequested += OpenSourceRequested;
        _updating = false;
        _observerClock.Tick += (_, _) => Sky.AdvanceObserverTime(DateTimeOffset.UtcNow);
        ApplySkyDefaults();
        ApplyObserver(_options.Observer);
        Loaded += async (_, _) =>
        {
            Sky.Refresh();
            UpdateCollection();
            try
            {
                await Task.Run(_cache.ApplySizeLimit);
            }
            catch (Exception exception)
            {
                if (!_closing)
                {
                    SetStatus($"Cache unavailable: {exception.Message}");
                }
            }

            await RefreshRecentFoldersAsync();
            if (!_closing && _optionsStore.LastError is { } error)
            {
                _details.AppendLine($"Options: {error}");
            }
        };
        Closed += (_, _) =>
        {
            if (_ownsCache)
            {
                _cache.Dispose();
            }
        };
    }

    private static CachedSkyImageLoader CreateLoader(ProcessedImageCache cache, ApplicationOptions options) => new(cache)
    {
        DefaultDisplayMode = options.DefaultDisplayMode,
        DefaultFinishSettings = options.DefaultFinishSettings
    };

    public async Task OpenStartupAsync(string[] paths)
    {
        if (_closing)
        {
            return;
        }

        if (!_firstRunHandled)
        {
            _firstRunHandled = true;
            if (_optionsStore.NeedsFirstRunSetup)
            {
                var welcome = new WelcomeWindow(_options, _optionsStore.Defaults)
                {
                    Owner = this,
                    SaveOptionsAsync = ApplyOptionsAsync
                };
                welcome.ShowDialog();
            }
        }

        if (_closing || paths.Length == 0)
        {
            return;
        }

        if (paths.Length == 1 && paths[0].EndsWith(".sky.json", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSessionAsync(paths[0]);
        }
        else
        {
            await ImportAsync(paths);
        }
    }

    private void ApplySkyDefaults()
    {
        GridToggle.IsChecked = _options.ShowGrid;
        ConstellationToggle.IsChecked = _options.ShowConstellations;
        LabelToggle.IsChecked = _options.ShowLabels;
        FootprintToggle.IsChecked = _options.ShowOutlines;
        BlendToggle.IsChecked = _options.BlendOverlaps;
        SkyOptionsChanged(this, new());
    }

    private void UpdateViewReadout()
    {
        var center = Sky.Camera.Center;
        if (Sky.Camera.Observer is { } observer)
        {
            GridToggle.Content = "Alt/Az grid";
            GridToggle.ToolTip = "Altitude/azimuth grid fixed to the local horizon. Meridians meet at the zenith (altitude 90°).";
            var direction = observer.ToHorizontal(center);
            ViewCoordinates.Text = $"Az {direction.Azimuth:000.0}°  Alt {direction.Altitude:+00.0;-00.0;00.0}°";
            ViewField.Text = $"FIELD OF VIEW  {Sky.Camera.FieldOfView:0.###}°  ·  LOCAL HORIZON";
            ViewObserverTime.Text = observer.TimeUtc.ToLocalTime()
                .ToString("dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
            ViewObserverTime.Visibility = Visibility.Visible;
            Timeline.UpdateTime(observer.TimeUtc, Sky.Observer!.UseSystemTime);
        }
        else
        {
            GridToggle.Content = "RA/Dec grid";
            GridToggle.ToolTip = "J2000 equatorial grid. Meridians meet at the north and south celestial poles.";
            ViewCoordinates.Text = $"{center.RaText}   {center.DecText}";
            ViewField.Text = $"FIELD OF VIEW  {Sky.Camera.FieldOfView:0.###}°  ·  J2000 / ICRS";
            ViewObserverTime.Visibility = Visibility.Collapsed;
        }
    }

    private void SetBusy(bool busy)
    {
        FileActions.IsEnabled = !busy;
        AddFolderButton.IsEnabled = !busy;
        FolderButton.IsEnabled = !_applyingOptions;
        RecentFolders.IsEnabled = !_applyingOptions;
        Inspector.IsEnabled = !busy && Selected is not null;
        CancelButton.Visibility = busy && !_applyingOptions ? Visibility.Visible : Visibility.Collapsed;
        ProcessingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ProcessingProgress.IsIndeterminate = true;
        ProcessingProgress.Value = 0;
        CancelButton.IsEnabled = true;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        _details.AppendLine(message);
    }

    private ImageLoadProgressWindow BeginLoadProgress(CancellationTokenSource operation, string heading, string unit, string cancellationNote)
    {
        _loadProgress?.Finish();
        var window = new ImageLoadProgressWindow(heading, unit, cancellationNote, () =>
        {
            if (ReferenceEquals(_operation, operation))
            {
                CancelClicked(this, new());
            }
        })
        {
            Owner = this
        };
        _loadProgress = window;
        return window;
    }

    private void EndLoadProgress(ImageLoadProgressWindow window)
    {
        window.Finish();
        if (ReferenceEquals(_loadProgress, window))
        {
            _loadProgress = null;
        }
    }

    private IProgress<ImageProcessingProgress> ProcessingReporter(CancellationTokenSource operation)
    {
        var window = _loadProgress;
        int itemVersion = window?.ItemVersion ?? 0;
        return new Progress<ImageProcessingProgress>(update =>
        {
            // Progress<T> posts to the UI thread. A queued callback can arrive after the user
            // changes folders or after the next file starts, so check both identities.
            if (_closing ||
                !ReferenceEquals(_operation, operation) ||
                operation.IsCancellationRequested ||
                (window is not null &&
                window.ItemVersion != itemVersion))
            {
                return;
            }

            StatusText.Text = window is null ? update.Stage : $"{window.StatusPrefix} · {update.Stage}";
            ProcessingProgress.IsIndeterminate = update.Percentage is null;
            ProcessingProgress.Value = update.Percentage ?? 0;
            window?.Report(update, itemVersion);
        });
    }

    private void UpdateCollection()
    {
        LayerCount.Text = _collectionSearch.IsActive
            ? $"{_collectionView.Count:N0} / {Sky.Layers.Count:N0}"
            : $"{Sky.Layers.Count} image{(Sky.Layers.Count == 1 ? "" : "s")}";
        LayerCount.ToolTip = _collectionSearch.IsActive
            ? $"{_collectionView.Count:N0} matching images of {Sky.Layers.Count:N0} loaded"
            : "Loaded images";
        EmptyCollection.Visibility = Sky.Layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoSearchResults.Visibility = Sky.Layers.Count > 0 && _collectionView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = CollectionSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.IsEnabled = CollectionSearch.Text.Length > 0;
    }

    private void CollectionSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_collectionView is null)
        {
            return;
        }

        _collectionSearch.SetQuery(CollectionSearch.Text);
        _collectionView.Refresh();
        UpdateCollection();
    }

    private void ClearSearchClicked(object sender, RoutedEventArgs e)
    {
        CollectionSearch.Clear();
        CollectionSearch.Focus();
    }

    private void CollectionSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CollectionSearch.Clear();
            e.Handled = true;
        }
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CollectionSearch.Focus();
            CollectionSearch.SelectAll();
            e.Handled = true;
        }
    }

    private IReadOnlyList<LoadedSkyImage> PrepareForCollection(IReadOnlyList<LoadedSkyImage> images)
    {
        foreach (var image in images)
        {
            _collectionSearch.Index(image);
        }

        return images;
    }

    public async Task ImportAsync(IEnumerable<string> paths) => await LoadImagesAsync(paths, null);

    public async Task BrowseFolderAsync(string path)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus($"Invalid folder: {exception.Message}");
            return;
        }

        if (!Directory.Exists(path))
        {
            SetStatus($"Folder is unavailable: {path}");
            return;
        }

        await LoadImagesAsync([path], path);
    }

    private async Task LoadImagesAsync(IEnumerable<string> paths, string? browseFolder)
    {
        if (_closing || _applyingOptions || (_operation is not null && browseFolder is null))
        {
            return;
        }

        if (browseFolder is not null)
        {
            _operation?.Cancel();
        }

        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Loading images", "file", "Cancel keeps the images already added to your sky.");
        int added = 0, failures = 0, skipped = 0, reused = 0;
        SkyImageLayer? first = null;
        try
        {
            if (browseFolder is not null)
            {
                CurrentFolderPath = browseFolder;
                FolderLocation.Text = Path.GetFileName(browseFolder);
                FolderLocation.ToolTip = browseFolder;
                Sky.Layers.Clear();
                UpdateCollection();
            }
            else
            {
                CurrentFolderPath = null;
                FolderLocation.Text = "All added images";
                FolderLocation.ToolTip = null;
            }

            var files = await Task.Run(() =>
            {
                var cached = _cache.GetCachedSourcePaths(operation.Token);
                return paths
                    .SelectMany(path => Directory.Exists(path)
                        ? Directory.EnumerateFiles(path).Where(SkyImageLoader.IsSupported)
                        : [path])
                    .Select(path =>
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        return Path.GetFullPath(path);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(path => cached.Contains(ProcessedImageCache.SourceKey(path)))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                }, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            loadProgress.SetTotal(files.Length);
            if (browseFolder is not null)
            {
                await Task.Run(() => _cache.RememberFolder(browseFolder), operation.Token);
                await RefreshRecentFoldersAsync();
            }

            foreach (string path in files)
            {
                operation.Token.ThrowIfCancellationRequested();
                loadProgress.BeginItem(path);
                if (Sky.Layers.Any(l => string.Equals(l.Image.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    loadProgress.FinishItem(skipped: true);
                    continue;
                }

                SetStatus($"{loadProgress.StatusPrefix} · Loading {Path.GetFileName(path)}…");
                try
                {
                    var progress = ProcessingReporter(operation);
                    var loaded = await Task.Run(() => PrepareForCollection(_imageLoader.LoadForBrowsing(path, operation.Token, progress)), operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    using (Sky.DeferLayerUpdates())
                    {
                        foreach (var image in loaded)
                        {
                            var layer = new SkyImageLayer(image, image.RenderMode);
                            Sky.Layers.Add(layer);
                            added++;
                            if (image.IsFromCache)
                            {
                                reused++;
                            }

                            if (first is null)
                            {
                                first = layer;
                                if (_collectionView.Contains(layer))
                                {
                                    LayerList.SelectedItem = layer;
                                }

                                if (layer.Placement is not null && Sky.Observer is null)
                                {
                                    Sky.Fit(layer);
                                }
                            }

                            if (Selected is null && _collectionView.Contains(layer))
                            {
                                LayerList.SelectedItem = layer;
                            }

                            if (image.PlacementWarning is { } warning)
                            {
                                _details.AppendLine($"{image.Name}: {warning}");
                            }
                        }
                    }

                    UpdateCollection();
                    loadProgress.FinishItem(loaded.Count, loaded.Count(image => image.IsFromCache));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    loadProgress.FinishItem(failed: true);
                    _details.AppendLine($"{path}: {exception.Message}");
                }
            }

            int pending = Sky.Layers.Count(l => l.Placement is null);
            SetStatus($"{(browseFolder is null ? "Added" : "Opened")} {added} image(s) · {reused} cached{(pending > 0 ? $" · {pending} need manual placement" : "")}{(skipped > 0 ? $" · {skipped} already loaded" : "")}{(failures > 0 ? $" · {failures} failed (see Details)" : "")}{(files.Length == 0 ? " · No supported images in the selected folder" : "")}");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Import cancelled · {added} image(s) added");
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Import failed: {exception.Message}");
            }
        }
        finally
        {
            EndLoadProgress(loadProgress);
            // A newer folder request may already own the UI while this operation unwinds.
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_closing)
                {
                    SetBusy(false);
                    UpdateCollection();
                    UpdateInspector();
                }
            }
        }
    }

    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add astronomical images",
            Multiselect = true,
            Filter = "Astronomical images|*.xisf;*.xish;*.fits;*.fit;*.fts|XISF images|*.xisf;*.xish|FITS images|*.fits;*.fit;*.fts"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ImportAsync(dialog.FileNames);
        }
    }

    private async void FolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Browse astronomical images in a folder"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await BrowseFolderAsync(dialog.FolderName);
        }
    }

    private async void AddFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add images from another folder"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ImportAsync([dialog.FolderName]);
        }
    }

    private async Task RefreshRecentFoldersAsync()
    {
        var cache = _cache;
        var folders = await Task.Run(cache.GetRecentFolders);
        if (_closing || cache != _cache)
        {
            return;
        }

        _refreshingFolders = true;
        try
        {
            RecentFolders.ItemsSource = folders;
            RecentFolders.SelectedIndex = -1;
        }
        finally
        {
            _refreshingFolders = false;
        }
    }

    private async void RecentFolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingFolders && RecentFolders.SelectedItem is RecentImageFolder folder)
        {
            await BrowseFolderAsync(folder.Path);
        }
    }

    private void FilesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _operation is null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || _operation is not null)
        {
            return;
        }

        if (files.Length == 1 && files[0].EndsWith(".sky.json", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSessionAsync(files[0]);
        }
        else
        {
            await ImportAsync(files);
        }
    }

    private void LayerSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateInspector();

    private void ImageInfoClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: SkyImageLayer layer })
        {
            new ImageDetailsWindow(layer)
            {
                Owner = this
            }.ShowDialog();
        }
    }

    private void UpdateInspector()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            var selected = Selected;
            Sky.SelectedLayer = selected;
            if (selected is not null && InspectorBorder.Visibility == Visibility.Collapsed)
            {
                CollectionRow.Height = _collectionHeight;
                InspectorRow.Height = _inspectorHeight;
                InspectorRow.MinHeight = 140;
            }
            else if (selected is null && InspectorBorder.Visibility == Visibility.Visible)
            {
                _collectionHeight = CollectionRow.Height;
                _inspectorHeight = InspectorRow.Height;
                InspectorRow.MinHeight = 0;
                InspectorRow.Height = new(0);
            }

            InspectorBorder.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
            CollectionDivider.Visibility = InspectorBorder.Visibility;
            Inspector.IsEnabled = selected is not null && _operation is null;
            ImageTitle.Text = selected?.Name ?? "Select an image to inspect it";
            ImageMetadata.Text = selected?.Image.Metadata ?? "";
            var placement = selected?.Placement;
            ImagePosition.Text = placement is null
                ? ""
                : $"{placement.Center.RaText}  {placement.Center.DecText}\n{placement.AngularWidth:0.###}° × {placement.AngularHeight:0.###}° · {placement.Description}";
            PlacementWarning.Text = selected is { Manual: null }
                ? selected.Image.PlacementWarning ?? ""
                : "Manual placement overrides the file's solution.";
            FitButton.IsEnabled = placement is not null;
            UpButton.IsEnabled = selected is not null && Sky.Layers.IndexOf(selected) > 0;
            DownButton.IsEnabled = selected is not null && Sky.Layers.IndexOf(selected) < Sky.Layers.Count - 1;
            DisplayChoice.SelectedIndex = (int)(selected?.DisplayMode ?? DisplayMode.Automatic);
            DisplayDescription.Text = selected?.Image.DisplayDescription ?? "";
            var finish = selected?.Image.FinishSettings ?? SkyFinishSettings.Default;
            FinishBackground.Value = finish.TargetBackground;
            FinishBackgroundValue.Text = $"{finish.TargetBackground:P0}";
            FinishBalance.IsChecked = finish.BalanceBackground;
            FinishGradient.IsChecked = finish.ReduceGradients;
            FinishNoise.IsChecked = finish.ReduceNoise;
            FinishDetail.IsChecked = finish.EnhanceDetail;
            OpacitySlider.Value = selected?.Opacity ?? 1;
            OpacityLabel.Text = $"{OpacitySlider.Value:P0}";
            var manual = selected?.Manual ?? new ManualPlacement(
                placement?.Center.RightAscension ?? Sky.Camera.Center.RightAscension,
                placement?.Center.Declination ?? Sky.Camera.Center.Declination,
                placement?.AngularWidth ?? 2,
                0
            );
            ManualRa.Text = manual.RightAscension.ToString("0.######", CultureInfo.InvariantCulture);
            ManualDec.Text = manual.Declination.ToString("0.######", CultureInfo.InvariantCulture);
            ManualWidth.Text = manual.WidthDegrees.ToString("0.######", CultureInfo.InvariantCulture);
            ManualRotation.Text = manual.RotationDegrees.ToString("0.###", CultureInfo.InvariantCulture);
            ManualMirror.IsChecked = manual.Mirror;
            ManualExpander.IsExpanded = selected is { Placement: null } || selected?.Manual is not null;
        }
        finally
        {
            _updating = false;
        }
    }

    private async void DisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating ||
            _operation is not null ||
            Selected is not { } selected ||
            DisplayChoice.SelectedIndex < 0)
        {
            return;
        }

        var mode = (DisplayMode)DisplayChoice.SelectedIndex;
        await ApplyDisplayAsync(selected, mode, selected.Image.FinishSettings);
    }

    public async Task ApplyDisplayAsync(SkyImageLayer selected, DisplayMode mode, SkyFinishSettings finish)
    {
        if (_operation is not null || _closing || _applyingOptions)
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Updating image preview", "image", "Cancel keeps the existing preview and display settings.");
        loadProgress.SetTotal(1);
        loadProgress.BeginItem(selected.Image.SourcePath);
        try
        {
            SetStatus($"Rendering {selected.Name}…");
            var progress = ProcessingReporter(operation);
            var loaded = await Task.Run(
                () => PrepareForCollection(_imageLoader.Load(selected.Image.SourcePath, mode, finish, operation.Token, selected.Image.ImageIndex, progress)),
                operation.Token
            );
            operation.Token.ThrowIfCancellationRequested();
            await Task.Run(
                () => _cache.SavePreference(selected.Image.SourcePath, selected.Image.ImageIndex, mode, finish),
                operation.Token
            );
            operation.Token.ThrowIfCancellationRequested();
            selected.ReplaceDisplay(loaded[0], mode);
            _collectionView.Refresh();
            loadProgress.FinishItem(1, loaded[0].IsFromCache ? 1 : 0);
            SetStatus($"{selected.Name} · {loaded[0].DisplayDescription}");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus("Display change cancelled");
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Display failed: {exception.Message}");
            }
        }
        finally
        {
            EndLoadProgress(loadProgress);
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_closing)
                {
                    SetBusy(false);
                    UpdateInspector();
                }
            }
        }
    }

    private void FinishBackgroundChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updating)
        {
            FinishBackgroundValue.Text = $"{e.NewValue:P0}";
        }
    }

    private async void ApplyFinishClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        await ApplyDisplayAsync(
            selected,
            DisplayMode.Finished,
            new()
            {
                TargetBackground = FinishBackground.Value,
                BalanceBackground = FinishBalance.IsChecked == true,
                ReduceGradients = FinishGradient.IsChecked == true,
                ReduceNoise = FinishNoise.IsChecked == true,
                EnhanceDetail = FinishDetail.IsChecked == true
            }
        );
    }

    private async void ResetFinishClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is { } selected)
        {
            await ApplyDisplayAsync(selected, _options.DefaultDisplayMode, _options.DefaultFinishSettings);
        }
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || Selected is not { } selected)
        {
            return;
        }

        selected.Opacity = e.NewValue;
        OpacityLabel.Text = $"{e.NewValue:P0}";
    }

    private void FitClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is { } layer)
        {
            Sky.Fit(layer);
            Sky.Focus();
        }
    }

    private void LayerDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != LayerList; source = source is System.Windows.Media.Visual
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return;
            }
        }

        if (Selected is { } layer)
        {
            Sky.Fit(layer);
        }
    }

    private async void OpenSourceRequested(object? sender, SkyImageLayer layer)
    {
        string path = layer.Image.SourcePath;
        SetStatus($"Opening {Path.GetFileName(path)} in its default application…");
        try
        {
            await Task.Run(() =>
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("The source image is no longer available.", path);
                }

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            });
            if (!_closing)
            {
                SetStatus($"Opened {Path.GetFileName(path)} in its default application");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1155)
        {
            if (!_closing)
            {
                SetStatus($"No default application for {Path.GetExtension(path)} files. Choose one in Windows Settings → Apps → Default apps.");
            }
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                SetStatus($"Cannot open {Path.GetFileName(path)}: {exception.Message}");
            }
        }
    }

    private void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } layer)
        {
            return;
        }

        int index = LayerList.SelectedIndex;
        Sky.Layers.Remove(layer);
        if (LayerList.Items.Count > 0)
        {
            LayerList.SelectedIndex = Math.Min(index, LayerList.Items.Count - 1);
        }

        UpdateCollection();
        UpdateInspector();
        SetStatus($"Removed {layer.Name} from this session");
    }

    private void MoveUpClicked(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDownClicked(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        int from = Sky.Layers.IndexOf(selected), to = from + delta;
        if (to < 0 || to >= Sky.Layers.Count)
        {
            return;
        }

        Sky.Layers.Move(from, to);
        LayerList.SelectedItem = selected;
        UpdateInspector();
    }

    private static double ParseAngle(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle) &&
            double.IsFinite(angle))
        {
            return angle;
        }

        throw new ArgumentException("Enter angles as decimal degrees using a dot as the decimal separator.");
    }

    private void ApplyManualClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        try
        {
            selected.SetManual(new(
                ParseAngle(ManualRa.Text),
                ParseAngle(ManualDec.Text),
                ParseAngle(ManualWidth.Text),
                ParseAngle(ManualRotation.Text),
                ManualMirror.IsChecked == true
            ));
            UpdateInspector();
            Sky.Fit(selected);
            SetStatus($"Placed {selected.Name} manually");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void ResetPlacementClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        try
        {
            selected.SetManual(null);
            UpdateInspector();
            SetStatus($"{selected.Name} · {selected.PlacementLabel}");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void GoCoordinatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            double ra = ParseAngle(GoRa.Text), dec = ParseAngle(GoDec.Text);
            if (dec is < -90 or > 90)
            {
                throw new ArgumentException("Declination must be between −90° and +90°.");
            }

            var coordinate = new SkyCoordinate(SkyCoordinate.Normalize(ra), dec);
            // Compare with the target coordinates as displayed to four decimal places.
            if (TargetChoice.SelectedItem is DeepSkyTarget target &&
                coordinate.DistanceTo(new(
                    SkyCoordinate.Normalize(Math.Round(target.Position.RightAscension, 4)),
                    Math.Round(target.Position.Declination, 4)
                )) < 1e-9)
            {
                Sky.AddTarget(target);
                Sky.CenterOn(target.Position, target.SuggestedFieldOfView);
                SetStatus($"{target.Name} · {target.TypeName} · Added to sky");
            }
            else
            {
                Sky.CenterOn(coordinate);
                SetStatus($"Centered on {coordinate} · J2000");
            }

            Sky.Focus();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filteringTargets || TargetChoice.SelectedItem is not DeepSkyTarget target || Sky is null)
        {
            return;
        }

        Sky.AddTarget(target);
        Sky.CenterOn(target.Position, target.SuggestedFieldOfView);
        GoRa.Text = target.Position.RightAscension.ToString("0.####", CultureInfo.InvariantCulture);
        GoDec.Text = target.Position.Declination.ToString("0.####", CultureInfo.InvariantCulture);
        UpdateTargetSummary();
        SetStatus($"{target.Name} · {target.TypeName} · Added to sky");
    }

    private void TargetSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TargetChoice is not null)
        {
            RefreshTargetResults();
        }
    }

    private void TargetCatalogChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetChoice is not null)
        {
            RefreshTargetResults();
        }
    }

    private void RefreshTargetResults()
    {
        if (TargetCatalogChoice.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<TargetCatalog>(tag, out var catalog))
        {
            return;
        }

        var selected = TargetChoice.SelectedItem as DeepSkyTarget;
        var targets = DeepSkyCatalog.Search(TargetSearch.Text, catalog);
        _filteringTargets = true;
        try
        {
            TargetChoice.ItemsSource = targets;
            TargetChoice.SelectedItem = selected is not null && targets.Contains(selected) ? selected : null;
        }
        finally
        {
            _filteringTargets = false;
        }

        TargetSearchPlaceholder.Visibility = TargetSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTargetSummary();
    }

    private void UpdateTargetSummary()
    {
        TargetResults.Text = $"{TargetChoice.Items.Count:N0} result{(TargetChoice.Items.Count == 1 ? "" : "s")} · {Sky.AddedTargets.Count:N0} added";
        TargetChoice.IsEnabled = TargetChoice.Items.Count > 0;
        ClearTargetsButton.IsEnabled = Sky.AddedTargets.Count > 0;
    }

    private void ClearTargetsClicked(object sender, RoutedEventArgs e)
    {
        Sky.ClearAddedTargets();
        TargetChoice.SelectedIndex = -1;
        UpdateTargetSummary();
    }

    private void TargetSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TargetSearch.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && TargetChoice.Items.Count > 0)
        {
            var exact = DeepSkyCatalog.Find(TargetSearch.Text);
            if (exact is not null && TargetChoice.Items.Contains(exact))
            {
                TargetChoice.SelectedIndex = -1;
                TargetChoice.SelectedItem = exact;
            }
            else if (TargetChoice.Items.Count == 1)
            {
                TargetChoice.SelectedIndex = -1;
                TargetChoice.SelectedIndex = 0;
            }
            else
            {
                TargetChoice.IsDropDownOpen = true;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down && TargetChoice.IsEnabled)
        {
            TargetChoice.Focus();
            TargetChoice.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void SkyOptionsChanged(object sender, RoutedEventArgs e)
    {
        Sky.ShowGrid = GridToggle.IsChecked == true;
        Sky.ShowConstellations = ConstellationToggle.IsChecked == true;
        Sky.ShowLabels = LabelToggle.IsChecked == true;
        Sky.ShowFootprints = FootprintToggle.IsChecked == true;
        Sky.BlendOverlaps = BlendToggle.IsChecked == true;
    }

    private void ZoomInClicked(object sender, RoutedEventArgs e) => Sky.Zoom(0.8);

    private void ZoomOutClicked(object sender, RoutedEventArgs e) => Sky.Zoom(1.25);

    private void OverviewClicked(object sender, RoutedEventArgs e) => Sky.CenterOn(Sky.Camera.Center, 90);

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        if (_operation is null || _operation.IsCancellationRequested)
        {
            return;
        }

        _operation.Cancel();
        _loadProgress?.ShowCancelling();
        CancelButton.IsEnabled = false;
        SetStatus("Cancelling after the current processing step…");
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_applyingOptions)
        {
            e.Cancel = true;
            return;
        }

        _closing = true;
        _operation?.Cancel();
        _loadProgress?.Finish();
        _observerClock.Stop();
    }

    public void ApplyObserver(ObserverSettings? observer, bool resetView = true)
    {
        Sky.SetObserver(observer, resetView);
        Timeline.Reset(observer, Sky.Camera.Observer?.TimeUtc);
        if (observer is { UseSystemTime: true })
        {
            _observerClock.Start();
        }
        else
        {
            _observerClock.Stop();
        }
    }

    private void SceneTimeSelected(object? sender, DateTimeOffset instant)
    {
        _observerClock.Stop();
        Sky.SetSceneTime(instant);
    }

    private void SceneLiveRequested(object? sender, EventArgs e)
    {
        if (Sky.Observer is null)
        {
            return;
        }

        Sky.SetSceneTime(DateTimeOffset.UtcNow, useSystemTime: true);
        _observerClock.Start();
    }

    private void OptionsClicked(object sender, RoutedEventArgs e)
    {
        var cache = _cache;
        var dialog = new OptionsWindow(_options, _optionsStore.FilePath, _optionsStore.Defaults)
        {
            Owner = this,
            SaveOptionsAsync = ApplyOptionsAsync,
            ReadCacheStatistics = cache.GetStatistics,
            ClearCachePreviewsAsync = () => Task.Run(cache.ClearPreviews)
        };
        dialog.ShowDialog();
    }

    public async Task ApplyOptionsAsync(ApplicationOptions requested)
    {
        if (_closing || _operation is not null || _applyingOptions)
        {
            throw new InvalidOperationException("Wait for the current operation to finish before saving options.");
        }

        requested = requested.Normalize();
        bool pathChanged = ProcessedImageCache.SourceKey(requested.CacheDirectory) != ProcessedImageCache.SourceKey(_options.CacheDirectory);
        bool cacheChanged = pathChanged || requested.CacheLimitBytes != _cache.LimitBytes;
        var previous = _cache;
        var next = cacheChanged ? new ProcessedImageCache(requested.CacheDirectory, requested.CacheLimitBytes) : previous;
        bool copied = true;
        _applyingOptions = true;
        SetBusy(true);
        SetStatus(pathChanged ? "Copying saved previews to the new cache…" : "Saving options…");
        try
        {
            await Task.Run(() =>
            {
                if (pathChanged)
                {
                    copied = previous.CopyTo(next);
                }
                else if (cacheChanged)
                {
                    next.ApplySizeLimit();
                }

                _optionsStore.Write(requested);
            });
            bool observerChanged = requested.Observer != _options.Observer;
            _options = requested;
            if (cacheChanged)
            {
                _cache = next;
                _imageLoader = CreateLoader(next, requested);
                if (_ownsCache)
                {
                    previous.Dispose();
                }

                _ownsCache = true;
            }
            else
            {
                _imageLoader.DefaultDisplayMode = requested.DefaultDisplayMode;
                _imageLoader.DefaultFinishSettings = requested.DefaultFinishSettings;
            }

            ApplySkyDefaults();
            if (observerChanged)
            {
                ApplyObserver(requested.Observer);
            }

            await RefreshRecentFoldersAsync();
            SetStatus(copied
                ? "Options saved · Defaults and cache settings are active"
                : "Options saved · Previous cache unavailable; previews will be regenerated");
        }
        catch
        {
            if (next != previous && _cache != next)
            {
                next.Dispose();
            }

            throw;
        }
        finally
        {
            _applyingOptions = false;
            if (!_closing)
            {
                SetBusy(false);
                UpdateInspector();
            }
        }
    }

    private async void OpenSessionClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open sky session",
            Filter = "SkyBrowser session|*.sky.json"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenSessionAsync(dialog.FileName);
        }
    }

    public async Task OpenSessionAsync(string path)
    {
        if (_operation is not null || _closing || _applyingOptions)
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Opening session", "image", "Cancel keeps your current sky and session unchanged.");
        try
        {
            var session = await SkySession.ReadAsync(path, operation.Token);
            loadProgress.SetTotal(session.Images.Length);
            List<SkyImageLayer> layers = [];
            int failures = 0;
            foreach (var saved in session.Images)
            {
                operation.Token.ThrowIfCancellationRequested();
                string sourcePath = Path.GetFullPath(saved.SourcePath, Path.GetDirectoryName(Path.GetFullPath(path))!);
                loadProgress.BeginItem(sourcePath);
                SetStatus($"{loadProgress.StatusPrefix} · Restoring {Path.GetFileName(sourcePath)}…");
                try
                {
                    var progress = ProcessingReporter(operation);
                    var loaded = await Task.Run(
                        () => PrepareForCollection(_imageLoader.Load(
                            sourcePath,
                            saved.DisplayMode,
                            saved.FinishSettings ?? SkyFinishSettings.Default,
                            operation.Token,
                            saved.ImageIndex,
                            progress
                        )),
                        operation.Token
                    );
                    operation.Token.ThrowIfCancellationRequested();
                    var layer = new SkyImageLayer(loaded[0], saved.DisplayMode)
                    {
                        IsVisible = saved.Visible,
                        Opacity = saved.Opacity
                    };
                    layer.SetManual(saved.Manual);
                    layers.Add(layer);
                    loadProgress.FinishItem(1, loaded[0].IsFromCache ? 1 : 0);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    loadProgress.FinishItem(failed: true);
                    _details.AppendLine($"{sourcePath}: {exception.Message}");
                }
            }

            operation.Token.ThrowIfCancellationRequested();
            // Commit only after the whole session has been read, so cancellation preserves the current sky.
            using (Sky.DeferLayerUpdates())
            {
                Sky.Layers.Clear();
                foreach (var layer in layers)
                {
                    Sky.Layers.Add(layer);
                }
            }

            GridToggle.IsChecked = session.Grid;
            ConstellationToggle.IsChecked = session.Constellations;
            LabelToggle.IsChecked = session.Labels;
            FootprintToggle.IsChecked = session.Outlines;
            BlendToggle.IsChecked = session.BlendOverlaps;
            ApplyObserver(session.Observer, resetView: false);
            SkyOptionsChanged(this, new());
            Sky.RestoreTargets(session.AddedTargets);
            foreach (string identifier in session.AddedTargets.Where(id => DeepSkyCatalog.Find(id) is null))
            {
                _details.AppendLine($"Catalogue target {identifier} is unavailable in this catalogue version.");
            }

            Sky.CenterOn(session.Center, session.FieldOfView);
            LayerList.SelectedItem = layers.FirstOrDefault(layer => _collectionView.Contains(layer));
            CurrentFolderPath = null;
            FolderLocation.Text = Path.GetFileName(path);
            FolderLocation.ToolTip = path;
            UpdateCollection();
            SetStatus($"Opened {Path.GetFileName(path)} · {layers.Count} image(s){(failures > 0 ? $" · {failures} unavailable (see Details)" : "")}");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus("Session open cancelled");
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Cannot open session: {exception.Message}");
            }
        }
        finally
        {
            EndLoadProgress(loadProgress);
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_closing)
                {
                    SetBusy(false);
                    UpdateInspector();
                }
            }
        }
    }

    public async Task SaveSessionAsync(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var session = new SkySession(
            1,
            Sky.Camera.Center,
            Sky.Camera.FieldOfView,
            Sky.ShowGrid,
            Sky.ShowConstellations,
            Sky.ShowLabels,
            Sky.ShowFootprints,
            Sky.Layers
                .Select(l => new SessionLayer(
                    Path.GetRelativePath(directory, l.Image.SourcePath),
                    l.Image.ImageIndex,
                    l.DisplayMode,
                    l.IsVisible,
                    l.Opacity,
                    l.Manual)
                {
                    FinishSettings = l.Image.FinishSettings
                })
                .ToArray())
        {
            BlendOverlaps = Sky.BlendOverlaps,
            Observer = Sky.Observer,
            AddedTargets = Sky.AddedTargets.Select(target => target.Id)
                .ToArray()
        };
        await session.WriteAsync(path, CancellationToken.None);
    }

    private async void SaveSessionClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save sky session",
            Filter = "SkyBrowser session|*.sky.json",
            DefaultExt = ".sky.json",
            FileName = "My sky.sky.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (SkyImageLoader.IsSupported(dialog.FileName))
            {
                throw new InvalidOperationException("Use the .sky.json extension for sessions.");
            }

            await SaveSessionAsync(dialog.FileName);
            SetStatus($"Saved {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            SetStatus($"Cannot save session: {exception.Message}");
        }
    }

    private void DetailsClicked(object sender, RoutedEventArgs e)
    {
        var text = new TextBox
        {
            Text = _details.ToString(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new(14),
            FontFamily = new("Consolas"),
            FontSize = 12
        };
        new Window
        {
            Owner = this,
            Title = "SkyBrowser · Details",
            Width = 800,
            Height = 520,
            Content = text,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        }.Show();
    }
}
