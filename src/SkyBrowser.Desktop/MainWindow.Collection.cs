using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private readonly CollectionFilter _collectionFilter = new();
    private readonly HashSet<SkyImageLayer> _comparisonSelection = [];

    private void UpdateComparisonSelection()
    {
        foreach (var layer in _comparisonSelection) { layer.PropertyChanged -= ComparisonPlacementChanged; }
        _comparisonSelection.Clear();
        foreach (var layer in LayerList.SelectedItems.Cast<SkyImageLayer>())
        {
            _comparisonSelection.Add(layer);
            layer.PropertyChanged += ComparisonPlacementChanged;
        }
        UpdateComparisonButton();
    }

    private void ComparisonPlacementChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SkyImageLayer.Placement) or null or "") { UpdateComparisonButton(); }
    }

    private void UpdateComparisonButton()
    {
        var reason = ComparisonBlockReason(LayerList.SelectedItems.Cast<SkyImageLayer>().ToArray());
        CompareButton.IsEnabled = reason is null;
        CompareButton.ToolTip = reason ?? "Compare selected images in Swipe divider mode.";
    }

    private static string? ComparisonBlockReason(SkyImageLayer[] selected)
    {
        if (selected.Length < 2) { return "Select at least two images using Ctrl or Shift to compare."; }
        if (selected.Any(layer => layer.Placement is null)) { return "Place or solve all selected images before comparing."; }
        return ImageComparisonGeometry.CanCompare(selected.Select(layer => layer.Placement!).ToArray()) ? null :
            "These images cover different sky regions. Select images whose sky areas overlap, then choose Compare.";
    }

    private void CollectionFiltersChanged(object sender, RoutedEventArgs e)
    {
        if (_collectionView is null || MetadataQuery is null || TagQuery is null || GroupQuery is null || FavoriteFilter is null)
        {
            return;
        }
        _collectionFilter.Field = (MetadataField)Math.Max(0, MetadataChoice.SelectedIndex);
        _collectionFilter.MetadataValue = MetadataQuery.Text.Trim();
        _collectionFilter.Tag = TagQuery.Text.Trim();
        _collectionFilter.Group = GroupQuery.Text.Trim();
        _collectionFilter.FavoritesOnly = FavoriteFilter.IsChecked == true;
        var sort = (CollectionSort)Math.Max(0, CollectionSortChoice.SelectedIndex);
        _collectionView.CustomSort = sort == CollectionSort.LayerOrder ? null : new LayerComparer(sort);
        _collectionView.Refresh();
        UpdateCollection();
        UpdateInspector();
    }

    private sealed class LayerComparer(CollectionSort sort) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not SkyImageLayer a || y is not SkyImageLayer b)
            {
                return 0;
            }
            int result = Comparer.DefaultInvariant.Compare(CollectionFilter.SortKey(a, sort), CollectionFilter.SortKey(b, sort));
            if (result != 0)
            {
                return result;
            }
            result = StringComparer.OrdinalIgnoreCase.Compare(a.Image.SourcePath, b.Image.SourcePath);
            return result != 0 ? result : a.Image.ImageIndex.CompareTo(b.Image.ImageIndex);
        }
    }

    private void ClearCollectionFiltersClicked(object sender, RoutedEventArgs e)
    {
        CollectionSearch.Clear();
        MetadataQuery.Clear();
        TagQuery.Clear();
        GroupQuery.Clear();
        FavoriteFilter.IsChecked = false;
        CollectionFiltersChanged(sender, e);
    }

    private void SelectMatchingClicked(object sender, RoutedEventArgs e) => LayerList.SelectAll();

    private void CompareClicked(object sender, RoutedEventArgs e)
    {
        var selected = LayerList.SelectedItems.Cast<SkyImageLayer>().ToArray();
        if (ComparisonBlockReason(selected) is { } reason)
        {
            SetStatus(reason);
            return;
        }
        new ComparisonWindow(selected) { Owner = this }.Show();
    }

    private async void OrganizeClicked(object sender, RoutedEventArgs e)
    {
        var selected = LayerList.SelectedItems.Cast<SkyImageLayer>().ToArray();
        if (selected.Length == 0)
        {
            SetStatus("Select one or more images using Ctrl or Shift, then choose Organize.");
            return;
        }
        var dialog = new CollectionBatchWindow(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Change is { } change)
        {
            await ApplyCollectionBatchAsync(selected, change);
        }
    }

    public async Task ApplyCollectionBatchAsync(IEnumerable<SkyImageLayer> selection, CollectionBatchChange change)
    {
        if (_operation is not null || _closing || _applyingOptions)
        {
            return;
        }
        var layers = selection.Where(Sky.Layers.Contains).Distinct().ToArray();
        if (layers.Length == 0)
        {
            return;
        }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var progressWindow = BeginLoadProgress(operation, "Updating selected images", "image", "Saved organization and completed images are retained when cancelled.");
        progressWindow.SetTotal(layers.Length);
        int completed = 0, failed = 0;
        try
        {
            var organizations = await Task.Run(() =>
            {
                _library.UpdateOrganization(layers.Select(layer => new LibraryImageId(layer.Image.SourcePath, layer.Image.ImageIndex)), change.Organization, operation.Token);
                return layers.Select(layer => _library.GetOrganization(layer.Image.SourcePath, layer.Image.ImageIndex)).ToArray();
            }, operation.Token);
            if (_closing || !ReferenceEquals(_operation, operation))
            {
                return;
            }
            using (Sky.DeferLayerUpdates())
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    if (!Sky.Layers.Contains(layers[i]))
                    {
                        continue;
                    }
                    layers[i].SetOrganization(organizations[i]);
                    if (change.IsVisible is { } visible)
                    {
                        layers[i].IsVisible = visible;
                    }
                    if (change.Opacity is { } opacity)
                    {
                        layers[i].Opacity = opacity;
                    }
                }
            }
            foreach (var layer in layers)
            {
                operation.Token.ThrowIfCancellationRequested();
                progressWindow.BeginItem(layer.Image.SourcePath);
                try
                {
                    if (change.DisplayMode is { } mode && Sky.Layers.Contains(layer))
                    {
                        var finish = layer.Image.FinishSettings;
                        var progress = ProcessingReporter(operation);
                        var loaded = await Task.Run(() => PrepareForCollection(_imageLoader.Load(layer.Image.SourcePath, mode, finish,
                            operation.Token, layer.Image.ImageIndex, progress)), operation.Token);
                        operation.Token.ThrowIfCancellationRequested();
                        await Task.Run(() => _cache.SavePreference(layer.Image.SourcePath, layer.Image.ImageIndex, mode, finish), operation.Token);
                        operation.Token.ThrowIfCancellationRequested();
                        if (_closing || !ReferenceEquals(_operation, operation))
                        {
                            return;
                        }
                        if (Sky.Layers.Contains(layer))
                        {
                            layer.ReplaceDisplay(loaded[0], mode);
                        }
                    }
                    completed++;
                    progressWindow.FinishItem(1);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    failed++;
                    progressWindow.FinishItem(failed: true);
                    _details.AppendLine($"{layer.Name}: {exception.Message}");
                }
            }
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus($"Updated {completed:N0} selected images · {failed:N0} failed");
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Batch cancelled · {completed:N0} completed · saved organization retained");
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operation, operation))
            {
                SetStatus($"Collection update failed: {exception.Message}");
            }
        }
        finally
        {
            EndLoadProgress(progressWindow);
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_closing)
                {
                    _collectionView.Refresh();
                    UpdateCollection();
                    SetBusy(false);
                    UpdateInspector();
                }
            }
        }
    }
}
