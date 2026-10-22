using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public partial class ImageInspectionWindow : Window
{
    // Full-resolution decoding/finishing can allocate several source-sized buffers.
    private static readonly SemaphoreSlim LoadingGate = new(1, 1);
    private readonly LoadedSkyImage _selected;
    private readonly ManualPlacement? _manual;
    private readonly ImageLibrary? _library;
    private readonly CancellationTokenSource _cancellation = new();
    private ScientificImageInspection? _inspection;
    private Task? _loading;
    private bool _closed, _loadFinished, _fit = true, _held;
    private double _zoom = 1;
    private Point? _dragStart;
    private Point _startOffset;
    private int _pixelX, _pixelY;

    public ImageInspectionWindow(SkyImageLayer layer, ImageLibrary? library = null)
    {
        _selected = layer.Image with { RenderMode = layer.DisplayMode };
        _manual = layer.Manual;
        _library = library;
        InitializeComponent();
        ImageName.Text = layer.Name;
        Title = $"SkyBrowser · {layer.Name} · Full-resolution inspection";
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation.Cancel();
            _inspection = null;
            FullImage.Source = null;
            // Dispose only after native readers/filters have returned and callbacks have unwound.
            if (_loading is null || _loadFinished)
            {
                _cancellation.Dispose();
            }
        };
        DpiChanged += (_, _) => UpdateZoom();
    }

    public Task InitializeAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var token = _cancellation.Token;
        var progress = new Progress<ImageProcessingProgress>(update =>
        {
            if (!_closed && !_loadFinished && !token.IsCancellationRequested)
            {
                StatusText.Text = update.Stage;
            }
        });
        try
        {
            var result = await Task.Run(async () =>
            {
                await LoadingGate.WaitAsync(token);
                try
                {
                    var inspection = ScientificImageInspection.Load(_selected, _manual, _library, token, progress);
                    var pixels = inspection.Image.Preview;
                    var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null,
                        pixels.Bgra, checked(pixels.Width * 4));
                    bitmap.Freeze();
                    return (Inspection: inspection, Bitmap: bitmap);
                }
                finally
                {
                    LoadingGate.Release();
                }
            }, token);
            if (_closed || token.IsCancellationRequested)
            {
                return;
            }
            _inspection = result.Inspection;
            FullImage.Source = result.Bitmap;
            ZoomControls.IsEnabled = true;
            SampleDescription.Text = _inspection.SampleDescription;
            StatusText.Text = $"{_selected.Width:N0} × {_selected.Height:N0} · {_inspection.Image.DisplayDescription}";
            CloseButton.Content = "Close";
            UpdateZoom();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing cancels queued work and suppresses all subsequent UI updates.
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                StatusText.Text = $"Could not inspect this source: {exception.Message}";
                CloseButton.Content = "Close";
            }
        }
        finally
        {
            _loadFinished = true;
            if (_closed)
            {
                _cancellation.Dispose();
            }
        }
    }

    private void UpdateZoom(Point? anchor = null)
    {
        if (_inspection is null)
        {
            return;
        }
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (_fit)
        {
            _zoom = Math.Min(1, Math.Min(Math.Max(1, ImageScroll.ViewportWidth - 2) / _selected.Width,
                Math.Max(1, ImageScroll.ViewportHeight - 2) / _selected.Height) * dpi);
        }
        double oldWidth = FullImage.Width;
        Point position = anchor ?? new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2);
        double x = (ImageScroll.HorizontalOffset + position.X) / (double.IsFinite(oldWidth) && oldWidth > 0 ? oldWidth : 1);
        double y = (ImageScroll.VerticalOffset + position.Y) / (double.IsFinite(FullImage.Height) && FullImage.Height > 0 ? FullImage.Height : 1);
        FullImage.Width = _selected.Width * _zoom / dpi;
        FullImage.Height = _selected.Height * _zoom / dpi;
        ZoomText.Text = $"{_zoom:P0}" + (_fit ? " · fit" : "");
        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(_fit ? 0 : x * FullImage.Width - position.X);
        ImageScroll.ScrollToVerticalOffset(_fit ? 0 : y * FullImage.Height - position.Y);
    }

    private void ChangeZoom(double factor, Point? anchor = null)
    {
        _fit = false;
        _zoom = Math.Clamp(_zoom * factor, 0.01, 16);
        UpdateZoom(anchor);
    }

    private void FitClicked(object sender, RoutedEventArgs e)
    {
        _fit = true;
        UpdateZoom();
    }
    private void ActualSizeClicked(object sender, RoutedEventArgs e)
    {
        _fit = false;
        _zoom = 1;
        UpdateZoom();
    }
    private void ZoomInClicked(object sender, RoutedEventArgs e) => ChangeZoom(1.25);
    private void ZoomOutClicked(object sender, RoutedEventArgs e) => ChangeZoom(0.8);
    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fit)
        {
            UpdateZoom();
        }
    }
    private void ImageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inspection is not null)
        {
            ChangeZoom(e.Delta > 0 ? 1.25 : 0.8, e.GetPosition(ImageScroll));
            e.Handled = true;
        }
    }

    public void ReadPixel(int x, int y)
    {
        if (_inspection is null)
        {
            return;
        }
        _pixelX = x;
        _pixelY = y;
        PixelX.Text = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PixelY.Text = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string samples = string.Join("   ", _inspection.Statistics.Select((s, c) => $"{s.Channel}: {_inspection.ReadSample(x, y, c)}"));
        var display = _inspection.Image.Preview;
        int offset = checked((y * display.Width + x) * 4);
        string color = $"Display RGB: #{display.Bgra[offset + 2]:X2}{display.Bgra[offset + 1]:X2}{display.Bgra[offset]:X2}";
        string sky;
        try
        {
            var coordinate = _inspection.SkyAtPixel(x, y);
            sky = coordinate is { } point
                ? $"J2000  RA {point.RaText}   Dec {point.DecText}  ({point.RightAscension:F8}°, {point.Declination:F8}°) · {_inspection.Placement!.Description}"
                : "Sky coordinates unavailable: no supported placement.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            sky = "Sky coordinates unavailable at this pixel.";
        }
        PixelReadout.Text = $"X {x:N0}   Y {y:N0} (zero-based stored rows)   FITS pixel center ({x + 1}, {y + 1})\n{color}\nOriginal samples · {samples}\n{sky}";
    }

    private void ReadPixelClicked(object sender, RoutedEventArgs e)
    {
        if (_inspection is null)
        {
            StatusText.Text = "Wait for the image to finish loading.";
            return;
        }
        if (!int.TryParse(PixelX.Text, out int x) || !int.TryParse(PixelY.Text, out int y) ||
            x < 0 || y < 0 || x >= _selected.Width || y >= _selected.Height)
        {
            StatusText.Text = $"Enter X from 0 to {_selected.Width - 1} and Y from 0 to {_selected.Height - 1}.";
            return;
        }
        _held = true;
        ReadPixel(x, y);
        PixelReadout.Focus();
    }

    private void ImageKeyDown(object sender, KeyEventArgs e)
    {
        if (_inspection is null)
        {
            return;
        }
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left:
                _pixelX = Math.Max(0, _pixelX - step);
                break;
            case Key.Right:
                _pixelX = Math.Min(_selected.Width - 1, _pixelX + step);
                break;
            case Key.Up:
                _pixelY = Math.Max(0, _pixelY - step);
                break;
            case Key.Down:
                _pixelY = Math.Min(_selected.Height - 1, _pixelY + step);
                break;
            case Key.Home:
                _pixelX = _pixelY = 0;
                break;
            case Key.End:
                _pixelX = _selected.Width - 1;
                _pixelY = _selected.Height - 1;
                break;
            case Key.Add or Key.OemPlus:
                ChangeZoom(1.25);
                e.Handled = true;
                return;
            case Key.Subtract or Key.OemMinus:
                ChangeZoom(0.8);
                e.Handled = true;
                return;
            default:
                return;
        }
        _held = true;
        ReadPixel(_pixelX, _pixelY);
        Services.Accessibility.Announce(PixelReadout);
        ImageScroll.ScrollToHorizontalOffset(_pixelX * FullImage.Width / _selected.Width - ImageScroll.ViewportWidth / 2);
        ImageScroll.ScrollToVerticalOffset(_pixelY * FullImage.Height / _selected.Height - ImageScroll.ViewportHeight / 2);
        e.Handled = true;
    }

    private void ReadAt(Point point)
    {
        if (_inspection is not null && point.X >= 0 && point.Y >= 0 && point.X < FullImage.ActualWidth && point.Y < FullImage.ActualHeight)
        {
            ReadPixel((int)(point.X / FullImage.ActualWidth * _selected.Width), (int)(point.Y / FullImage.ActualHeight * _selected.Height));
        }
    }

    private void ReadoutMouseMove(object sender, MouseEventArgs e)
    {
        if (!_held && _dragStart is null)
        {
            ReadAt(e.GetPosition(FullImage));
        }
    }
    private void ReadoutMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_held && _dragStart is null)
        {
            PixelReadout.Text = "Move over the image for pixel coordinates, display RGB, original samples and sky coordinates.";
        }
    }
    private void ImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_inspection is null || e.OriginalSource is not Image)
        {
            return;
        }
        _dragStart = e.GetPosition(ImageScroll);
        _startOffset = new(ImageScroll.HorizontalOffset, ImageScroll.VerticalOffset);
        ImageScroll.CaptureMouse();
        e.Handled = true;
    }
    private void ImageMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is { } start)
        {
            var delta = e.GetPosition(ImageScroll) - start;
            ImageScroll.ScrollToHorizontalOffset(_startOffset.X - delta.X);
            ImageScroll.ScrollToVerticalOffset(_startOffset.Y - delta.Y);
        }
    }
    private void ImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is { } start)
        {
            if ((e.GetPosition(ImageScroll) - start).Length < 4)
            {
                _held = !_held;
                ReadAt(e.GetPosition(FullImage));
            }
            _dragStart = null;
            ImageScroll.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    private void ImageLostCapture(object sender, MouseEventArgs e) => _dragStart = null;
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
