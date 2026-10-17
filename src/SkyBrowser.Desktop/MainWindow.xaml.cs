using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Models;

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
    private readonly ImageLibrary _library;
    private LibraryWindow? _libraryWindow;
    private bool _startupFoldersLoaded;

    public ImageLibrary Library => _library;
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
        _library = new(Path.Combine(Path.GetDirectoryName(_optionsStore.FilePath)!, "library-v1.sqlite3"));
        _imageLoader = CreateLoader(_cache, _options);
        InitializeComponent();
        _collectionView = new(Sky.Layers)
        {
            Filter = item => item is SkyImageLayer layer && _collectionSearch.Matches(layer.Image) && _collectionFilter.Matches(layer)
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
                ClearCollectionFiltersClicked(this, new());
            }

            LayerList.SelectedItem = layer;
            LayerList.ScrollIntoView(layer);
        };
        Sky.OpenSourceRequested += OpenSourceRequested;
        Sky.InspectImageRequested += (_, layer) => OpenInspection(layer);
        _updating = false;
        _observerClock.Tick += (_, _) => Sky.AdvanceObserverTime(DateTimeOffset.UtcNow);
        ApplySkyDefaults();
        ApplyObserver(_options.Observer);
        Loaded += async (_, _) =>
        {
            Sky.Refresh();
            UpdateCollection();
            var surveyRestore = RestoreCachedSurveyBackgroundsAsync();
            try
            {
                await Task.Run(_cache.ApplySizeLimit);
                long bookmarkRevision = Sky.BookmarkRevision;
                var bookmarks = await Task.Run(_library.GetBookmarks);
                if (!_closing && Sky.BookmarkRevision == bookmarkRevision) { Sky.SetBookmarks(bookmarks); }
            }
            catch (Exception exception)
            {
                if (!_closing)
                {
                    SetStatus($"Cache unavailable: {exception.Message}");
                }
            }

            await RefreshRecentFoldersAsync();
            await surveyRestore;
            if (!_closing && _optionsStore.LastError is { } error)
            {
                _details.AppendLine($"Options: {error}");
            }
        };
        Closed += (_, _) =>
        {
            _libraryWindow?.Close();
            if (_ownsCache)
            {
                _cache.Dispose();
            }
        };
    }

    private void SetBusy(bool busy)
    {
        LibraryButton.IsEnabled = !busy;
        CollectionActions.IsEnabled = !busy;
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

    private void UpdateCollection()
    {
        LayerCount.Text = _collectionSearch.IsActive || _collectionFilter.IsActive
            ? $"{_collectionView.Count:N0} / {Sky.Layers.Count:N0}"
            : $"{Sky.Layers.Count} image{(Sky.Layers.Count == 1 ? "" : "s")}";
        LayerCount.ToolTip = _collectionSearch.IsActive || _collectionFilter.IsActive
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
        _surveyRestoreOperation?.Cancel();
        _operation?.Cancel();
        _loadProgress?.Finish();
        _observerClock.Stop();
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
            bool cachedSurveysChanged = requested.ShowCachedSurveyImages != _options.ShowCachedSurveyImages;
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
            if (cachedSurveysChanged)
            {
                await RestoreCachedSurveyBackgroundsAsync();
            }
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
