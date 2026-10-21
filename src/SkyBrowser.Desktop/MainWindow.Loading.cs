using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private CachedSkyImageLoader CreateLoader(ProcessedImageCache cache, ApplicationOptions options) => new(cache)
    {
        Library = _library,
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

        if (_closing)
        {
            return;
        }

        if (!_startupFoldersLoaded)
        {
            _startupFoldersLoaded = true;
            await RestoreSavedFoldersAsync();
        }
        if (!_closing && paths.Length > 0)
        {
            await ImportAsync(paths);
        }
    }

    public async Task<bool> RestoreSavedFoldersAsync()
    {
        if (_closing || _operation is not null || _applyingOptions)
        {
            return false;
        }
        try
        {
            var folders = await Task.Run(_library.GetFolders);
            if (_closing)
            {
                return false;
            }
            return folders.Length == 0 || await LoadImagesAsync(folders, null, folders, saveLibraryFolders: false);
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                SetStatus($"Could not load saved folders: {exception.Message}");
            }
            return false;
        }
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

    private IReadOnlyList<LoadedSkyImage> PrepareForCollection(IReadOnlyList<LoadedSkyImage> images)
    {
        foreach (var image in images)
        {
            _collectionSearch.Index(image);
        }

        return images;
    }

    public async Task ImportAsync(IEnumerable<string> paths)
    {
        await LoadImagesAsync(paths, null);
    }

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

    private async Task<bool> LoadImagesAsync(IEnumerable<string> paths, string? browseFolder, IReadOnlyList<string>? libraryFolders = null,
        bool saveLibraryFolders = true)
    {
        if (_closing || _applyingOptions || (_operation is not null && browseFolder is null))
        {
            return false;
        }

        if (browseFolder is not null)
        {
            _operation?.Cancel();
        }

        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Loading images", "file", "Cancel keeps the images already added to your sky.");
        int added = 0, failures = 0, skipped = 0, reused = 0, refreshed = 0;
        string[] unavailableFolders = [];
        SkyImageLayer? first = null;
        try
        {
            if (libraryFolders is not null && saveLibraryFolders)
            {
                var previousFolders = await Task.Run(_library.GetFolders, operation.Token);
                await Task.Run(() => _library.SetFolders(libraryFolders, operation.Token), operation.Token);
                // Once the folder transaction commits, synchronize removals before
                // honoring cancellation of image loading, so a retry keeps the same scope.
                using (Sky.DeferLayerUpdates())
                {
                    foreach (var layer in Sky.Layers.Where(layer => previousFolders.Any(folder => ImageLibrary.ContainsSource(folder, layer.Image.SourcePath)) &&
                        !libraryFolders.Any(folder => ImageLibrary.ContainsSource(folder, layer.Image.SourcePath))).ToArray())
                    {
                        Sky.Layers.Remove(layer);
                    }
                }
                UpdateCollection();
                operation.Token.ThrowIfCancellationRequested();
            }
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
                if (libraryFolders is not null)
                {
                    unavailableFolders = libraryFolders.Where(folder => !Directory.Exists(folder)).ToArray();
                }
                IEnumerable<string> candidates = libraryFolders is null
                    ? paths.SelectMany(path => Directory.Exists(path)
                        ? Directory.EnumerateFiles(path).Where(SkyImageLoader.IsSupported)
                        : [path])
                    : ImageLibrary.FindFolderImages(libraryFolders, operation.Token);
                return candidates
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
            foreach (string folder in unavailableFolders)
            {
                _details.AppendLine($"Folder unavailable; retained in library: {folder}");
            }
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
                if (libraryFolders is null && Sky.Layers.Any(l => string.Equals(l.Image.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
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
                    var organizations = await Task.Run(() => loaded.ToDictionary(image => image.ImageIndex,
                        image => _library.GetOrganization(image.SourcePath, image.ImageIndex)), operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    using (Sky.DeferLayerUpdates())
                    {
                        foreach (var image in loaded)
                        {
                            if (image.IsFromCache)
                            {
                                reused++;
                            }
                            var existing = Sky.Layers.FirstOrDefault(layer => layer.Image.ImageIndex == image.ImageIndex &&
                                string.Equals(layer.Image.SourcePath, image.SourcePath, StringComparison.OrdinalIgnoreCase));
                            if (existing is not null)
                            {
                                existing.ReplaceDisplay(image, image.RenderMode);
                                existing.SetOrganization(organizations[image.ImageIndex]);
                                refreshed++;
                                continue;
                            }
                            var layer = new SkyImageLayer(image, image.RenderMode);
                            layer.SetOrganization(organizations[image.ImageIndex]);
                            Sky.Layers.Add(layer);
                            added++;

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

                    if (libraryFolders is not null)
                    {
                        _collectionView.Refresh();
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
            SetStatus($"{(browseFolder is null ? "Added" : "Opened")} {added} image(s) · {reused} cached{(refreshed > 0 ? $" · {refreshed} refreshed" : "")}{(pending > 0 ? $" · {pending} need placement" : "")}{(skipped > 0 ? $" · {skipped} already loaded" : "")}{(failures > 0 ? $" · {failures} failed (see Details)" : "")}{(unavailableFolders.Length > 0 ? $" · {unavailableFolders.Length} folders unavailable (see Details)" : "")}{(files.Length == 0 ? " · No supported images in the listed folders" : "")}");
            return true;
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
        return false;
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

        await ImportAsync(files);
    }

    public Task<bool> ApplyLibraryFoldersAsync(IReadOnlyList<string> folders)
    {
        if (_closing || _operation is not null || _applyingOptions)
        {
            throw new InvalidOperationException("Wait for the current operation to finish before loading library folders.");
        }
        string[] paths = folders.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return LoadImagesAsync(paths, null, paths);
    }

    private void LibraryClicked(object sender, RoutedEventArgs e)
    {
        if (_libraryWindow is { IsVisible: true })
        {
            _libraryWindow.Activate();
            return;
        }
        _libraryWindow = new(_library)
        {
            Owner = this,
            ApplyFoldersAsync = ApplyLibraryFoldersAsync
        };
        _libraryWindow.Closed += (_, _) => _libraryWindow = null;
        _libraryWindow.Show();
    }
}
