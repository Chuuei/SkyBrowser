using System.Windows;
using Microsoft.Win32;
using SkyBrowser.Core.Astrometry;
using SkyBrowser.Desktop.Models;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private async void SolveClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected)
        { return; }
        if (string.IsNullOrEmpty(_options.AstapExecutablePath))
        {
            OptionsClicked(this, new());
            if (string.IsNullOrEmpty(_options.AstapExecutablePath))
            { return; }
        }
        await SolveImageAsync(selected);
    }

    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        var preview = Selected?.Image.Preview;
        var options = new ExportWindow((int)Math.Max(128, Sky.ActualWidth), (int)Math.Max(128, Sky.ActualHeight), preview?.Width, preview?.Height)
        {
            Owner = this
        };
        if (options.ShowDialog() != true || options.Result is not { } settings)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Export display image", Filter = settings.Jpeg ? "JPEG image|*.jpg;*.jpeg" : "PNG image|*.png",
            DefaultExt = settings.Jpeg ? ".jpg" : ".png", AddExtension = true, OverwritePrompt = true,
            FileName = settings.SelectedPreview ? System.IO.Path.GetFileNameWithoutExtension(Selected!.Image.SourcePath) + "_preview" : "SkyBrowser-sky"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ExportImageAsync(dialog.FileName, settings, overwrite: true);
        }
    }

    public async Task<bool> ExportImageAsync(string destination, ImageExportOptions options, bool overwrite = false)
    {
        if (_operation is not null || _closing || _applyingOptions || (options.SelectedPreview && Selected is null))
        {
            return false;
        }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        var loading = BeginLoadProgress(operation, "Exporting image", "image", "Cancel preserves originals and any existing destination.");
        loading.SetTotal(1);
        loading.BeginItem("Display image");
        try
        {
            options.Validate();
            string[] sources = Sky.Layers.Select(layer => layer.Image.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var bitmap = options.SelectedPreview ? ImageExport.Preview(Selected!.Image.Preview) : Sky.CaptureExport(options);
            await Task.Run(() => ImageExport.Save(bitmap, destination, sources, options.JpegQuality, overwrite, operation.Token), operation.Token);
            loading.FinishItem(1);
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus($"Exported {bitmap.PixelWidth:N0} × {bitmap.PixelHeight:N0} · {System.IO.Path.GetFileName(destination)}");
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus("Image export cancelled");
            }
        }
        catch (Exception exception)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus("Could not export image: " + exception.Message);
            }
        }
        finally
        {
            EndLoadProgress(loading);
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
        return false;
    }

    private async void SaveSolvedImageClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Manual: null } selected)
        {
            return;
        }
        bool fits = System.IO.Path.GetExtension(selected.Image.SourcePath).ToLowerInvariant() is ".fits" or ".fit" or ".fts";
        var dialog = new SaveFileDialog
        {
            Title = "Save image with astrometric solution",
            Filter = "FITS image|*.fits|XISF image|*.xisf",
            FilterIndex = fits ? 1 : 2,
            DefaultExt = fits ? ".fits" : ".xisf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = System.IO.Path.GetFileNameWithoutExtension(selected.Image.SourcePath) + "_solved"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await SaveSolvedImageAsync(selected, dialog.FileName, overwrite: true);
        }
    }

    public async Task<bool> SaveSolvedImageAsync(SkyImageLayer selected, string destination, bool overwrite = false)
    {
        if (_operation is not null || _closing || _applyingOptions || !Sky.Layers.Contains(selected) || selected.Manual is not null)
        {
            return false;
        }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        var image = selected.Image;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Saving solved image", "image", "Cancel keeps the original and any existing destination file.");
        loadProgress.SetTotal(1);
        loadProgress.BeginItem(image.SourcePath);
        try
        {
            var progress = ProcessingReporter(operation);
            var result = await Task.Run(() => new SolvedImageExporter(_library).Export(image, destination, overwrite, operation.Token, progress), operation.Token);
            loadProgress.FinishItem(1);
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus($"Saved solved copy · {result.Path}" + (result.Warning is null ? "" : $" · {result.Warning}"));
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus("Saving solved copy cancelled");
            }
        }
        catch (Exception exception)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            {
                SetStatus($"Could not save solved copy: {exception.Message}");
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
        return false;
    }

    public async Task SolveImageAsync(SkyImageLayer selected)
    {
        if (_operation is not null || _closing || _applyingOptions || !Sky.Layers.Contains(selected))
        { return; }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        var original = selected.Image;
        SetBusy(true);
        var loadProgress = BeginLoadProgress(operation, "Blind solving with ASTAP", "image", "Cancel stops ASTAP and keeps the existing placement.");
        loadProgress.SetTotal(1);
        loadProgress.BeginItem(original.SourcePath);
        try
        {
            var result = await new AstapPlateSolver(_library).SolveAsync(original, _options, operation.Token, ProcessingReporter(operation));
            operation.Token.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_operation, operation) || !Sky.Layers.Contains(selected) || !ReferenceEquals(selected.Image, original))
            { return; }
            var solved = original with { Placement = result.Placement, PlacementWarning = result.Warning };
            selected.ReplaceDisplay(solved, selected.DisplayMode);
            selected.SetManual(null);
            _collectionSearch.Index(solved);
            _collectionView.Refresh();
            loadProgress.FinishItem(1);
            SetStatus($"ASTAP placed {selected.Name}" + (string.IsNullOrWhiteSpace(result.Warning) ? "" : $" · {result.Warning}"));
        }
        catch (OperationCanceledException)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            { SetStatus("ASTAP solving cancelled · existing placement retained"); }
        }
        catch (Exception exception)
        {
            if (!_closing && ReferenceEquals(_operation, operation))
            { SetStatus($"ASTAP solve failed: {exception.Message}"); }
        }
        finally
        {
            EndLoadProgress(loadProgress);
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_closing)
                { SetBusy(false); UpdateInspector(); }
            }
        }
    }
}
