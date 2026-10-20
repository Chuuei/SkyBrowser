using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Controls;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public enum ComparisonMode { SideBySide, Blink, Swipe }

/// <summary>Independent display snapshots projected through a shared sky camera, never raw pixel alignment.</summary>
public partial class ComparisonWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private DispatcherOperation? _swipeCapture;
    private readonly SkyImageLayer[] _images;
    private bool _changingPair;
    private bool _ready, _syncing, _showB, _closed;
    public SkyViewport LeftView => ViewA;
    public SkyViewport RightView => ViewB;
    public ComparisonMode Mode => (ComparisonMode)ModeChoice.SelectedIndex;

    public ComparisonWindow(IEnumerable<SkyImageLayer> layers)
    {
        var placed = layers.Distinct().ToArray();
        if (placed.Length < 2 || placed.Any(layer => layer.Placement is null))
        {
            throw new ArgumentException("Choose at least two images with sky placements.", nameof(layers));
        }
        if (!ImageComparisonGeometry.CanCompare(placed.Select(layer => layer.Placement!).ToArray()))
        {
            throw new ArgumentException("Choose images whose sky areas overlap.", nameof(layers));
        }
        _images = placed;
        InitializeComponent();
        ImageA.ItemsSource = placed;
        ImageA.SelectedIndex = 0;
        UpdateImageBChoices();
        foreach (var view in new[] { ViewA, ViewB })
        {
            view.ShowGrid = false;
            view.ShowLabels = false;
            view.ShowFootprints = false;
            view.ShowConstellations = false;
            view.BlendOverlaps = false;
            view.ShowImageActions = false;
            view.ViewChanged += SynchronizeViews;
        }
        _timer.Interval = TimeSpan.FromSeconds(BlinkInterval.Value);
        _timer.Tick += TimerTick;
        _ready = true;
        LoadPair();
        Loaded += (_, _) =>
        {
            UpdateMode();
            FramePair();
        };
        Activated += (_, _) => UpdateTimer();
        Deactivated += (_, _) => _timer.Stop();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= TimerTick;
            ClearSwipeFrames();
            ViewA.ViewChanged -= SynchronizeViews;
            ViewB.ViewChanged -= SynchronizeViews;
            ViewA.Layers.Clear();
            ViewB.Layers.Clear();
            ImageA.ItemsSource = null;
            ImageB.ItemsSource = null;
        };
    }

    private static SkyImageLayer Copy(SkyImageLayer source)
    {
        var copy = new SkyImageLayer(source.Image, source.DisplayMode);
        copy.SetManual(source.Manual);
        return copy;
    }

    private void LoadPair()
    {
        if (ImageA.SelectedItem is not SkyImageLayer a || ImageB.SelectedItem is not SkyImageLayer b)
        {
            return;
        }
        using (ViewA.DeferLayerUpdates())
        {
            ViewA.Layers.Clear();
            ViewA.Layers.Add(Copy(a));
        }
        using (ViewB.DeferLayerUpdates())
        {
            ViewB.Layers.Clear();
            ViewB.Layers.Add(Copy(b));
        }
        _showB = false;
        UpdateMode();
        if (IsLoaded) { FramePair(); }
    }

    private void UpdateImageBChoices()
    {
        var previous = ImageB.SelectedItem as SkyImageLayer;
        var choices = _images.Where(image => !ReferenceEquals(image, ImageA.SelectedItem)).ToArray();
        _changingPair = true;
        try
        {
            ImageB.ItemsSource = choices;
            ImageB.SelectedItem = previous is not null && choices.Contains(previous) ? previous : choices[0];
        }
        finally { _changingPair = false; }
    }

    private void FramePair()
    {
        Views.UpdateLayout();
        var frame = ImageComparisonGeometry.FramePair(ViewA.Layers[0].Placement!, ViewB.Layers[0].Placement!,
            Math.Max(1, ViewA.ActualWidth) / Math.Max(1, ViewA.ActualHeight));
        ViewA.CenterOn(frame.Center, frame.FieldOfView);
    }

    private void SynchronizeViews(object? sender, EventArgs e)
    {
        if (!_ready || _syncing || _closed || sender is not SkyViewport source)
        {
            return;
        }
        _syncing = true;
        try
        {
            var target = ReferenceEquals(source, ViewA) ? ViewB : ViewA;
            target.Camera.Observer = source.Camera.Observer;
            target.CenterOn(source.Camera.Center, source.Camera.FieldOfView);
            QueueSwipeCapture();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ImagesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_changingPair)
        {
            if (ReferenceEquals(sender, ImageA)) { UpdateImageBChoices(); }
            LoadPair();
        }
    }

    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
        {
            UpdateMode();
            FramePair();
        }
    }

    private void UpdateMode()
    {
        bool split = Mode == ComparisonMode.SideBySide;
        Grid.SetColumnSpan(ViewA, split ? 1 : 2);
        Grid.SetColumn(ViewB, split ? 1 : 0);
        Grid.SetColumnSpan(ViewB, split ? 1 : 2);
        ViewB.IsHitTestVisible = split;
        ViewB.Visibility = Mode == ComparisonMode.Blink && !_showB ? Visibility.Hidden : Visibility.Visible;
        BlinkControls.Visibility = Mode == ComparisonMode.Blink ? Visibility.Visible : Visibility.Collapsed;
        SwipeControls.Visibility = SwipeHandle.Visibility = Mode == ComparisonMode.Swipe ? Visibility.Visible : Visibility.Collapsed;
        if (Mode == ComparisonMode.Swipe)
        {
            QueueSwipeCapture();
        }
        else
        {
            ClearSwipeFrames();
        }
        UpdateClip();
        UpdateTimer();
        ComparisonStatus.Text = $"A: {((SkyImageLayer)ImageA.SelectedItem).Name}   ·   B: {((SkyImageLayer)ImageB.SelectedItem).Name}\n" +
            (Mode == ComparisonMode.Blink ? $"Showing {(_showB ? "B" : "A")} · Space pauses/resumes blink; Show A/B steps frames. " : "") +
            "Drag either view to pan; scroll to zoom. Sky coordinates stay linked. Uncovered areas show the sky background.";
    }

    private void UpdateClip()
    {
        var dpi = VisualTreeHelper.GetDpi(Views);
        double x = Math.Round(Views.ActualWidth * DividerPosition.Value * dpi.DpiScaleX) / dpi.DpiScaleX;
        SwipeFrameB.Clip = Mode == ComparisonMode.Swipe
            ? new RectangleGeometry(new Rect(x, 0, Math.Max(0, Views.ActualWidth - x), Math.Max(0, Views.ActualHeight))) : null;
        SwipeHandle.Margin = new(Math.Clamp(x - 6, 0, Math.Max(0, Views.ActualWidth - 12)), 0, 0, 0);
    }

    private void QueueSwipeCapture()
    {
        if (_closed || !IsLoaded || Mode != ComparisonMode.Swipe || _swipeCapture is not null)
        {
            return;
        }
        // Capture after pending layout/camera updates, coalescing navigation and resize events.
        _swipeCapture = Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            try
            {
                if (_closed || Mode != ComparisonMode.Swipe)
                {
                    return;
                }
                Views.UpdateLayout();
                if (ViewA.ActualWidth <= 0 || ViewA.ActualHeight <= 0 || ViewB.RenderSize != ViewA.RenderSize)
                {
                    return;
                }
                // Clipping a live Viewport3D lets tiny dirty regions cut through mesh triangles,
                // leaving dark columns on some renderers. Only clip complete, opaque 2D frames.
                SwipeFrameA.Source = CaptureFrame(ViewA);
                SwipeFrameB.Source = CaptureFrame(ViewB);
                SwipeFrames.Visibility = Visibility.Visible;
                UpdateClip();
            }
            finally
            {
                _swipeCapture = null;
            }
        });
    }

    private static BitmapSource CaptureFrame(SkyViewport view)
    {
        var size = view.RenderSize;
        var dpi = VisualTreeHelper.GetDpi(view);
        int width = (int)Math.Ceiling(size.Width * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(size.Height * dpi.DpiScaleY);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Fractional layout sizes leave a partially covered final device pixel. Fill
            // the whole bitmap so live content cannot bleed through its outer edges.
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(5, 10, 18)), null,
                new Rect(0, 0, width / dpi.DpiScaleX, height / dpi.DpiScaleY));
            var brush = new VisualBrush(view)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(size),
                Stretch = Stretch.Fill
            };
            context.DrawRectangle(brush, null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap(
            width, height,
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearSwipeFrames()
    {
        _swipeCapture?.Abort();
        _swipeCapture = null;
        SwipeFrames.Visibility = Visibility.Collapsed;
        SwipeFrameA.Source = null;
        SwipeFrameB.Source = null;
        SwipeFrameB.Clip = null;
    }

    private void UpdateTimer()
    {
        if (_ready && !_closed && IsActive && Mode == ComparisonMode.Blink && BlinkRunning.IsChecked == true)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void TimerTick(object? sender, EventArgs e) => ToggleFrame();

    public void ToggleFrame()
    {
        _showB = !_showB;
        UpdateMode();
    }

    private void SwitchFrame(object sender, RoutedEventArgs e) => ToggleFrame();
    private void BlinkRunningChanged(object sender, RoutedEventArgs e) => UpdateTimer();

    private void IntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
        {
            _timer.Interval = TimeSpan.FromSeconds(e.NewValue);
        }
    }

    private void DividerChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
        {
            UpdateClip();
        }
    }

    private void DividerDragged(object sender, DragDeltaEventArgs e) => DividerPosition.Value += e.HorizontalChange / Math.Max(1, Views.ActualWidth);

    private void ViewsResized(object sender, SizeChangedEventArgs e)
    {
        if (_ready)
        {
            UpdateClip();
            QueueSwipeCapture();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_ready)
        {
            UpdateClip();
            QueueSwipeCapture();
        }
    }

    private void FitA(object sender, RoutedEventArgs e) => ViewA.Fit(ViewA.Layers[0]);
    private void FitB(object sender, RoutedEventArgs e) => ViewB.Fit(ViewB.Layers[0]);
    private void FitBoth(object sender, RoutedEventArgs e) => FramePair();
    private void ComparisonKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Mode == ComparisonMode.Blink && Keyboard.FocusedElement is SkyViewport)
        {
            BlinkRunning.IsChecked = BlinkRunning.IsChecked != true;
            UpdateTimer();
            e.Handled = true;
        }
    }
}
