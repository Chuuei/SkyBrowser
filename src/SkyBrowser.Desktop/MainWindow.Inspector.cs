using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public partial class MainWindow
{
    private void LayerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateComparisonSelection();
        if (Selected is not null && Sky.IsMeasuring)
        {
            Sky.ClearMeasurement();
        }
        UpdateInspector();
    }

    private void ImageInfoClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: SkyImageLayer layer })
        {
            new ImageDetailsWindow(layer, Library)
            {
                Owner = this
            }.ShowDialog();
        }
    }

    private void InspectImageClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is { } layer)
        {
            OpenInspection(layer);
        }
    }

    private void OpenInspection(SkyImageLayer layer) =>
        new ImageInspectionWindow(layer, Library) { Owner = this }.Show();

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
            if (placement is not null && selected is not null)
            {
                var scale = placement.PixelScale(selected.Image.Width, selected.Image.Height);
                ImagePosition.Text += $"\nPixel scale {scale.X:0.###}″ × {scale.Y:0.###}″/pixel";
            }
            PlacementWarning.Text = selected is { Manual: null }
                ? selected.Image.PlacementWarning ?? ""
                : "Manual placement overrides the file's solution.";
            FitButton.IsEnabled = placement is not null;
            SolveButton.IsEnabled = selected is not null && _operation is null;
            SaveSolvedImageButton.IsEnabled = selected is { Manual: null, Image.Placement: { } savedPlacement } &&
                savedPlacement.Description == "ASTAP · blind solve" && _operation is null;
            SolveButton.ToolTip = _options.AstapExecutablePath.Length == 0
                ? "Configure ASTAP in Options → Plate solving, then blind solve this image."
                : "Search the whole sky with ASTAP; store the placement without changing the original.";
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
        SetStatus($"Removed {layer.Name} from the sky");
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
}
