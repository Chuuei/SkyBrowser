using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Desktop.Models;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Desktop.Controls;

/// <summary>A GPU textured celestial sphere with a perspective camera fixed at its center.</summary>
public sealed partial class SkyViewport : Grid
{
    private readonly SkyCatalog _catalog = new();
    private readonly Viewport3D _viewport = new();
    private readonly Grid _surveyViewports = new() { IsHitTestVisible = false };
    private readonly PerspectiveCamera _perspectiveCamera = new()
    {
        NearPlaneDistance = 0.001,
        FarPlaneDistance = 4
    };
    private readonly Model3DGroup _images = new();
    private static readonly PointCollection MeshTextureCoordinates = CreateTextureCoordinates();
    private static readonly Int32Collection MeshTriangleIndices = CreateTriangleIndices();
    private readonly Dictionary<GeometryModel3D, SkyImageLayer> _models = [];
    private readonly Dictionary<SkyImageLayer, (ImagePlacement Placement, MeshGeometry3D Mesh, SkyFootprint Footprint)> _meshes = [];
    private readonly Dictionary<SkyImageLayer, ImageBrush> _brushes = [];
    private readonly HashSet<SkyImageLayer> _insets = [];
    private readonly HashSet<SkyImageLayer> _subscribedLayers = [];
    private int _deferredUpdates;
    private bool _rebuildPending;
    private readonly Dictionary<string, DeepSkyTarget> _addedTargets = new(StringComparer.Ordinal);
    private Point? _pointer;
    private readonly SkyDrawing _backdrop;
    private readonly SkyDrawing _annotations;
    private readonly Canvas _imageActions = new();
    private readonly Button _openSourceButton = new()
    {
        Name = "OpenSourceButton",
        Width = 26,
        Height = 26,
        Padding = new(0),
        Visibility = Visibility.Collapsed,
        ToolTip = "Open original in default application",
        Cursor = Cursors.Arrow
    };
    private Point? _drag;
    private Point _dragStart;
    private double _dragDistance;
    private bool _exporting;
    private SurveyFrame[] _surveyFrames = [];
    private readonly List<ImageBrush> _surveyBrushes = [];
    private SkyCoordinate? _measurementStart;

    public SurveyFrame? SurveyBackground => _surveyFrames.FirstOrDefault();
    public IReadOnlyList<SurveyFrame> SurveyBackgrounds => Array.AsReadOnly(_surveyFrames);
    public long SurveyRevision { get; private set; }
    public IReadOnlyList<SkyBookmark> Bookmarks { get; private set; } = [];
    public long BookmarkRevision { get; private set; }
    public SkyMeasurement? Measurement { get; private set; }
    public bool IsMeasuring { get; private set; }
    public event EventHandler? MeasurementChanged;

    public bool ShowCatalogTargets { get; set { field = value; Refresh(); } }
    public bool ShowScaleBar { get; set { field = value; Refresh(); } }
    public bool ShowNorthEast { get; set { field = value; Refresh(); } }
    public bool ShowBookmarks { get; set { field = value; Refresh(); } } = true;
    public double SurveyOpacity
    {
        get;
        set
        {
            field = Math.Clamp(value, 0, 1);
            foreach (var brush in _surveyBrushes) { brush.Opacity = field; }
            _annotations.InvalidateVisual();
        }
    } = 0.75;

    public void SetSurveyBackground(SurveyFrame? frame) => SetSurveyBackgrounds(frame is null ? [] : [frame]);

    public void AddSurveyBackground(SurveyFrame frame) => SetSurveyBackgrounds(new[] { frame }.Concat(_surveyFrames));

    public void SetSurveyBackgrounds(IEnumerable<SurveyFrame> frames)
    {
        _surveyFrames = frames.DistinctBy(frame => frame.Request.Key).ToArray();
        SurveyRevision++;
        _surveyViewports.Children.Clear();
        _surveyBrushes.Clear();
        // Differently sampled spherical meshes can intersect even at distinct radii.
        // Composite each survey in its own viewport/depth buffer, oldest to newest,
        // below the photo viewport. All viewports share the exact same camera.
        for (int i = _surveyFrames.Length - 1; i >= 0; i--)
        {
            var frame = _surveyFrames[i];
            var layer = new SkyImageLayer(frame.Image);
            var brush = new ImageBrush(layer.Bitmap) { Opacity = SurveyOpacity, Stretch = Stretch.Fill };
            _surveyBrushes.Add(brush);
            var material = new DiffuseMaterial(brush);
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Colors.White));
            scene.Children.Add(new GeometryModel3D(CreateMesh(frame.Image.Placement!, 1.5), material) { BackMaterial = material });
            var viewport = new Viewport3D { Camera = _perspectiveCamera };
            viewport.Children.Add(new ModelVisual3D { Content = scene });
            _surveyViewports.Children.Add(viewport);
        }
        RebuildImages();
    }

    public void SetBookmarks(IEnumerable<SkyBookmark> bookmarks)
    {
        Bookmarks = bookmarks.ToArray();
        BookmarkRevision++;
        _annotations.InvalidateVisual();
    }

    public void BeginMeasurement()
    {
        _measurementStart = null;
        Measurement = null;
        IsMeasuring = true;
        Cursor = Cursors.Cross;
        _annotations.InvalidateVisual();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PickMeasurementPoint(SkyCoordinate coordinate)
    {
        SkyGeometry.Validate(coordinate);
        if (!IsMeasuring) { BeginMeasurement(); }
        if (_measurementStart is not { } start)
        {
            _measurementStart = coordinate;
            Measurement = new(coordinate, coordinate);
        }
        else
        {
            Measurement = new(start, coordinate);
            IsMeasuring = false;
            Cursor = Cursors.Hand;
        }
        _annotations.InvalidateVisual();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearMeasurement()
    {
        IsMeasuring = false;
        _measurementStart = null;
        Measurement = null;
        Cursor = Cursors.Hand;
        _annotations.InvalidateVisual();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMeasurementDistances(double? start, double? end)
    {
        if (start is { } a) { SkyGeometry.ValidateObjectDistance(a); }
        if (end is { } b) { SkyGeometry.ValidateObjectDistance(b); }
        if (Measurement is null || IsMeasuring) { return; }
        var updated = Measurement with
        {
            StartDistanceLightYears = start, EndDistanceLightYears = end,
            StartDistanceSource = null, EndDistanceSource = null
        };
        _ = updated.SeparationLightYears;
        if (updated == Measurement) { return; }
        Measurement = updated;
        _annotations.InvalidateVisual();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    public SkyCamera Camera { get; } = new();

    public void MeasureTargets(string first, string second)
    {
        var measurement = TargetDistances.Measure(first, second);
        _measurementStart = null;
        IsMeasuring = false;
        Cursor = Cursors.Hand;
        Measurement = measurement;
        _annotations.InvalidateVisual();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool ShowImageActions { get; set; } = true;

    public ObservableCollection<SkyImageLayer> Layers { get; } = [];

    public IReadOnlyCollection<DeepSkyTarget> AddedTargets => _addedTargets.Values;

    public SkyImageLayer? SelectedLayer
    {
        get;
        set
        {
            field = value;
            _annotations.InvalidateVisual();
            UpdateSourceButton();
        }
    }

    public SkyImageLayer? HoveredLayer { get; private set; }

    public ObserverSettings? Observer { get; private set; }

    public bool BlendOverlaps
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RebuildImages();
        }
    } = true;

    public const double InsetOpacity = 0.60;

    public bool ShowGrid
    {
        get;
        set
        {
            field = value;
            Refresh();
        }
    } = true;

    public bool ShowConstellations
    {
        get;
        set
        {
            field = value;
            Refresh();
        }
    } = true;

    public bool ShowLabels
    {
        get;
        set
        {
            field = value;
            Refresh();
        }
    } = true;

    public bool ShowFootprints
    {
        get;
        set
        {
            field = value;
            Refresh();
        }
    } = true;

    public event EventHandler? ViewChanged;

    public event EventHandler? TargetsChanged;

    public event EventHandler<SkyImageLayer>? LayerSelected;

    public event EventHandler<SkyImageLayer>? OpenSourceRequested;

    public event EventHandler<SkyImageLayer>? InspectImageRequested;

    public SkyViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _backdrop = new(this, false)
        {
            IsHitTestVisible = false
        };
        _annotations = new(this, true)
        {
            IsHitTestVisible = false
        };
        Children.Add(_backdrop);
        Children.Add(_surveyViewports);
        Children.Add(_viewport);
        Children.Add(_annotations);
        Children.Add(_imageActions);
        _imageActions.Children.Add(_openSourceButton);
        _openSourceButton.Content = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 2,5 L 2,12 L 12,12 L 12,9 M 7,2 L 14,2 L 14,9 M 14,2 L 6,10"),
            Stroke = (Brush)FindResource("ControlTextBrush"),
            StrokeThickness = 1.4,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        AutomationProperties.SetName(_openSourceButton, "Open original in default application");
        _openSourceButton.Click += (_, e) =>
        {
            e.Handled = true;
            if (_openSourceButton.DataContext is SkyImageLayer layer && _brushes.ContainsKey(layer))
            {
                LayerSelected?.Invoke(this, layer);
                OpenSourceRequested?.Invoke(this, layer);
            }
        };
        _viewport.Camera = _perspectiveCamera;
        _viewport.Children.Add(new ModelVisual3D { Content = _images });
        Layers.CollectionChanged += LayersChanged;
        SizeChanged += (_, _) => Refresh();
    }

    private void LayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Clear() raises Reset without listing the removed items. Track subscriptions
            // separately so old layers cannot keep this viewport alive or trigger rebuilds.
            foreach (var layer in _subscribedLayers)
            {
                layer.PropertyChanged -= LayerChanged;
            }

            _subscribedLayers.Clear();
            foreach (var layer in Layers)
            {
                if (_subscribedLayers.Add(layer))
                {
                    layer.PropertyChanged += LayerChanged;
                }
            }
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (SkyImageLayer layer in e.OldItems)
                {
                    if (_subscribedLayers.Remove(layer))
                    {
                        layer.PropertyChanged -= LayerChanged;
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (SkyImageLayer layer in e.NewItems)
                {
                    if (_subscribedLayers.Add(layer))
                    {
                        layer.PropertyChanged += LayerChanged;
                    }
                }
            }
        }

        RebuildImages();
    }

    /// <summary>Batches scene reconstruction until the UI-thread scope is disposed. Nesting is supported.</summary>
    public IDisposable DeferLayerUpdates()
    {
        _deferredUpdates++;
        return new LayerUpdateScope(this);
    }

    private sealed class LayerUpdateScope(SkyViewport owner) : IDisposable
    {
        private SkyViewport? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } current)
            {
                return;
            }

            // Disposal is idempotent, and only the outermost scope rebuilds the scene.
            _owner = null;
            if (--current._deferredUpdates == 0 && current._rebuildPending)
            {
                current.RebuildImages();
            }
        }
    }

    private void LayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SkyImageLayer.Opacity) &&
            sender is SkyImageLayer layer &&
            _brushes.TryGetValue(layer, out var brush) &&
            (brush.Opacity > 0) == (layer.Opacity > 0))
        {
            UpdateOpacity(layer);
            return;
        }

        if (e.PropertyName is nameof(SkyImageLayer.Placement) or nameof(SkyImageLayer.Bitmap) or nameof(SkyImageLayer.IsVisible) or nameof(SkyImageLayer.Opacity))
        {
            RebuildImages();
        }
    }

    public void RebuildImages()
    {
        if (_deferredUpdates > 0)
        {
            _rebuildPending = true;
            return;
        }

        _rebuildPending = false;
        _images.Children.Clear();
        _models.Clear();
        _brushes.Clear();
        _insets.Clear();
        _images.Children.Add(new AmbientLight(Colors.White));
        foreach (var removed in _meshes.Keys.Where(l => !Layers.Contains(l)).ToArray())
        {
            _meshes.Remove(removed);
        }

        var visible = Layers.Where(l => l.IsVisible && l.Opacity > 0 && l.Placement is not null).ToArray();
        foreach (var layer in visible)
        {
            if (!_meshes.TryGetValue(layer, out var cached) || cached.Placement != layer.Placement)
            {
                _meshes[layer] = (layer.Placement!, CreateMesh(layer.Placement!), new(layer.Placement!));
            }
        }

        // Order by sky coverage, regardless of pixel dimensions or the order files arrived in.
        var ordered = BlendOverlaps
            ? visible.OrderByDescending(layer => _meshes[layer].Footprint.AreaSteradians).ToArray()
            : visible;
        int order = 0;
        foreach (var layer in ordered)
        {
            var cached = _meshes[layer];
            if (BlendOverlaps &&
                ordered.Any(background => _meshes[background].Footprint.AreaSteradians > cached.Footprint.AreaSteradians * 1.000001 &&
                cached.Footprint.Overlaps(_meshes[background].Footprint)))
            {
                _insets.Add(layer);
            }

            // Move each successive sphere slightly inward. This gives smaller insets a
            // deterministic depth order without coplanar texture flicker.
            double radius = 1 - order++ * 0.0001;
            var brush = new ImageBrush(layer.Bitmap)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            _brushes.Add(layer, brush);
            UpdateOpacity(layer);
            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(cached.Mesh, material)
            {
                BackMaterial = material,
                Transform = new ScaleTransform3D(radius, radius, radius)
            };
            _images.Children.Add(model);
            _models.Add(model, layer);
        }

        Refresh();
    }

    private void UpdateOpacity(SkyImageLayer layer)
    {
        if (_brushes.TryGetValue(layer, out var brush))
        {
            brush.Opacity = _insets.Contains(layer) ? (HoveredLayer == layer ? 1 : layer.Opacity * InsetOpacity) : layer.Opacity;
        }
    }

    public bool IsBlendedInset(SkyImageLayer layer) => _insets.Contains(layer);

    public SkyImageLayer? ImageAt(Point position)
    {
        if (position.X < 0 || position.Y < 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
        {
            return null;
        }

        SkyImageLayer? found = null;
        VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult hit &&
                hit.ModelHit is GeometryModel3D model &&
                _models.TryGetValue(model, out found))
            {
                return HitTestResultBehavior.Stop;
            }

            return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(position));
        return found;
    }

    /// <summary>Tracks a pointer in viewport coordinates; null restores the normal blended view.</summary>
    public void UpdateHover(Point? position)
    {
        _pointer = position;
        var hovered = position is { } point ? ImageAt(point) : null;
        if (HoveredLayer == hovered)
        {
            return;
        }

        var previous = HoveredLayer;
        HoveredLayer = hovered;
        if (previous is not null)
        {
            UpdateOpacity(previous);
        }

        if (hovered is not null)
        {
            UpdateOpacity(hovered);
        }

        _annotations.InvalidateVisual();
        UpdateSourceButton();
    }

    /// <summary>Opens the foremost sky image at the supplied viewport position for inspection.</summary>
    public bool InspectImageAt(Point position)
    {
        if (ImageAt(position) is not { } layer)
        {
            return false;
        }
        InspectLayer(layer);
        return true;
    }

    private void InspectLayer(SkyImageLayer layer)
    {
        LayerSelected?.Invoke(this, layer);
        InspectImageRequested?.Invoke(this, layer);
    }

    private void UpdateSourceButton()
    {
        var layer = _openSourceButton.IsKeyboardFocusWithin
            ? _openSourceButton.DataContext as SkyImageLayer
            : HoveredLayer ?? SelectedLayer;
        if (!_exporting && ShowImageActions && _drag is null && layer?.Placement is { } placement && _brushes.ContainsKey(layer) && ActualWidth > 0 && ActualHeight > 0)
        {
            Point? Project(double u, double v) => Camera.Project(placement.At(u, v).Vector, ActualWidth, ActualHeight) is { } p
                ? new(p.X, p.Y) : null;

            // Follow real image corners rather than a screen-aligned bounding box: fields
            // can be rotated, mirrored, overlap, or cross the RA seam and celestial poles.
            foreach (var (u, v) in new[] { (1d, 0d), (0d, 0d), (1d, 1d), (0d, 1d) })
            {
                if (Project(u, v) is not { } corner || Project(1 - u, v) is not { } horizontal || Project(u, 1 - v) is not { } vertical)
                {
                    continue;
                }
                Vector x = horizontal - corner, y = vertical - corner;
                if (x.Length < 80 || y.Length < 80)
                {
                    continue;
                }
                x.Normalize();
                y.Normalize();
                if (PlaceButton(corner + (x + y) * 24))
                {
                    return;
                }
            }
            // A zoomed field can extend beyond the viewport. Keep the action inside
            // its visible portion, checking ownership so a covered background cannot win.
            foreach (var point in new[]
            {
                new Point(ActualWidth - 22, 22), new Point(22, 22),
                new Point(ActualWidth - 22, ActualHeight - 22), new Point(22, ActualHeight - 22),
                new Point(ActualWidth / 2, 22), new Point(ActualWidth - 22, ActualHeight / 2),
                new Point(22, ActualHeight / 2), new Point(ActualWidth / 2, ActualHeight - 22)
            })
            {
                if (PlaceButton(point))
                {
                    return;
                }
            }

            bool PlaceButton(Point center)
            {
                const double half = 13;
                if (center.X < half || center.Y < half || center.X >= ActualWidth - half || center.Y >= ActualHeight - half)
                {
                    return false;
                }
                foreach (var offset in new[] { new Vector(-half, -half), new Vector(half, -half), new Vector(half, half), new Vector(-half, half), new Vector() })
                {
                    if (ImageAt(center + offset) != layer)
                    {
                        return false;
                    }
                }
                _openSourceButton.DataContext = layer;
                AutomationProperties.SetHelpText(_openSourceButton, layer.Name);
                Canvas.SetLeft(_openSourceButton, center.X - half);
                Canvas.SetTop(_openSourceButton, center.Y - half);
                _openSourceButton.Visibility = Visibility.Visible;
                return true;
            }
        }
        _openSourceButton.Visibility = Visibility.Collapsed;
        _openSourceButton.DataContext = null;
    }

    public static MeshGeometry3D CreateMesh(ImagePlacement placement, double radius = 1)
    {
        var samples = placement.CaptureSampleGrid();
        var positions = new Point3D[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            var direction = samples[i].Vector * radius;
            positions[i] = new(direction.X, direction.Y, direction.Z);
        }

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TextureCoordinates = MeshTextureCoordinates,
            TriangleIndices = MeshTriangleIndices
        };
        mesh.Freeze();
        return mesh;
    }

    private static PointCollection CreateTextureCoordinates()
    {
        var coordinates = new PointCollection(Enumerable.Range(0, 33 * 33).Select(i => new Point(i % 33 / 32.0, i / 33 / 32.0)));
        coordinates.Freeze();
        return coordinates;
    }

    private static Int32Collection CreateTriangleIndices()
    {
        const int divisions = 32;
        var indices = new int[divisions * divisions * 6];
        int index = 0;
        for (int y = 0; y < divisions; y++)
        {
            for (int x = 0; x < divisions; x++)
            {
                int a = y * (divisions + 1) + x, b = a + 1, c = a + divisions + 1, d = c + 1;
                indices[index++] = a;
                indices[index++] = c;
                indices[index++] = b;
                indices[index++] = b;
                indices[index++] = c;
                indices[index++] = d;
            }
        }

        var collection = new Int32Collection(indices);
        collection.Freeze();
        return collection;
    }

    public void CenterOn(SkyCoordinate coordinate, double? fieldOfView = null)
    {
        Camera.Center = coordinate;
        if (fieldOfView is { } fov)
        {
            Camera.FieldOfView = fov;
        }

        Refresh();
    }

    public void AddTarget(DeepSkyTarget target)
    {
        target = DeepSkyCatalog.Find(target.Id) ?? throw new ArgumentException("Choose a known catalogue target.", nameof(target));
        if (!_addedTargets.TryAdd(target.Id, target))
        {
            return;
        }

        Refresh();
        TargetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAddedTargets()
    {
        if (_addedTargets.Count == 0)
        {
            return;
        }

        _addedTargets.Clear();
        Refresh();
        TargetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreTargets(IEnumerable<string> identifiers)
    {
        _addedTargets.Clear();
        foreach (string identifier in identifiers)
        {
            if (DeepSkyCatalog.Find(identifier) is { } target)
            {
                _addedTargets.TryAdd(target.Id, target);
            }
        }

        Refresh();
        TargetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetObserver(ObserverSettings? observer, bool resetView = true)
    {
        observer?.Validate();
        var frame = observer is null ? null : new ObserverFrame(observer.Location, observer.ResolveTime());
        Observer = observer;
        Camera.Observer = frame;
        if (resetView)
        {
            ResetView();
        }
        else
        {
            Refresh();
        }
    }

    public void AdvanceObserverTime(DateTimeOffset instant)
    {
        if (Observer is not { UseSystemTime: true } observer ||
            Camera.Observer is not { } previous ||
            _drag is not null)
        {
            return;
        }

        var direction = previous.ToHorizontal(Camera.Center);
        Camera.Observer = new(observer.Location, instant);
        // Keep looking in the same compass direction as the Earth turns beneath the sky.
        Camera.Center = Camera.Observer.FromHorizontal(direction.Azimuth, direction.Altitude);
        Refresh();
    }

    public void SetSceneTime(DateTimeOffset instant, bool useSystemTime = false)
    {
        if (Observer is not { } observer || Camera.Observer is not { } previous)
        {
            return;
        }

        var settings = observer with { UseSystemTime = useSystemTime, TimeUtc = instant.ToUniversalTime() };
        settings.Validate();
        var direction = previous.ToHorizontal(Camera.Center);
        var frame = new ObserverFrame(settings.Location, settings.TimeUtc);
        Observer = settings;
        Camera.Observer = frame;
        Camera.Center = frame.FromHorizontal(direction.Azimuth, direction.Altitude);
        // Only the horizon/camera changes; image textures and their J2000 placement stay intact.
        Refresh();
    }

    public void ResetView()
    {
        if (Camera.Observer is { } observer)
        {
            CenterOn(observer.FromHorizontal(180, 20), 90);
        }
        else
        {
            CenterOn(new(25, 38), 75);
        }
    }

    public void Fit(SkyImageLayer layer)
    {
        if (layer.Placement is not { } placement)
        {
            return;
        }

        var center = placement.Center;
        // Fit every edge point in camera space, including rotated and non-square footprints.
        Camera.Center = center;
        double tangent = 0;
        for (int i = 0; i <= 32; i++)
        {
            foreach (var (u, v) in new[]
            {
                (i / 32.0, 0.0),
                (i / 32.0, 1.0),
                (0.0, i / 32.0),
                (1.0, i / 32.0)
            })
            {
                var direction = placement.At(u, v).Vector;
                double z = SkyVector.Dot(direction, Camera.Forward);
                if (z <= 0)
                {
                    tangent = 10;
                    continue;
                }

                tangent = Math.Max(
                    tangent,
                    Math.Max(
                        Math.Abs(SkyVector.Dot(direction, Camera.Right) / z),
                        Math.Abs(SkyVector.Dot(direction, Camera.Up) / z) * ActualWidth / Math.Max(1, ActualHeight)
                    )
                );
            }
        }

        Camera.FieldOfView = 2 * Math.Atan(tangent * 1.25) / SkyCoordinate.Radians;
        Refresh();
    }

    public void Zoom(double multiplier)
    {
        Camera.FieldOfView *= multiplier;
        Refresh();
    }

    public BitmapSource CaptureExport(ImageExportOptions options)
    {
        options.Validate();
        var copy = new SkyViewport
        {
            _exporting = true, Width = options.Width, Height = options.Height,
            BlendOverlaps = BlendOverlaps, ShowLabels = options.Labels, ShowFootprints = options.Outlines,
            ShowGrid = options.Grid, ShowConstellations = ShowConstellations
        };
        try
        {
            copy.Observer = Observer;
            copy.Camera.Observer = Camera.Observer;
            copy.Camera.Center = Camera.Center;
            copy.Camera.FieldOfView = Camera.FieldOfView;
            copy.SurveyOpacity = SurveyOpacity;
            copy.SetSurveyBackgrounds(_surveyFrames);
            copy.ShowCatalogTargets = ShowCatalogTargets;
            copy.ShowScaleBar = ShowScaleBar;
            copy.ShowNorthEast = ShowNorthEast;
            copy.ShowBookmarks = ShowBookmarks;
            copy.SetBookmarks(Bookmarks);
            copy.Measurement = Measurement;
            copy.RestoreTargets(AddedTargets.Select(t => t.Id));
            using (copy.DeferLayerUpdates())
            {
                foreach (var layer in Layers)
                {
                    copy.Layers.Add(layer);
                }
            }
            copy.Measure(new(options.Width, options.Height));
            copy.Arrange(new Rect(0, 0, options.Width, options.Height));
            copy.UpdateLayout();
            copy.Refresh();
            var bitmap = new RenderTargetBitmap(options.Width, options.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(copy);
            bitmap.Freeze();
            if (!options.Coordinates)
            {
                return bitmap;
            }
            string coordinate = $"J2000 / ICRS · RA {Camera.Center.RaText} · Dec {Camera.Center.DecText} · Horizontal field {Camera.FieldOfView:0.###}°";
            if (Camera.Observer is { } observer)
            {
                var horizontal = observer.ToHorizontal(Camera.Center);
                coordinate += $" · Az {horizontal.Azimuth:0.##}° · Alt {horizontal.Altitude:0.##}°";
            }
            return ImageExport.WithCoordinates(bitmap, coordinate);
        }
        finally
        {
            copy.Layers.Clear();
        }
    }

    public void Refresh()
    {
        var f = Camera.Forward;
        var u = Camera.Up;
        _perspectiveCamera.LookDirection = new(f.X, f.Y, f.Z);
        _perspectiveCamera.UpDirection = new(u.X, u.Y, u.Z);
        _perspectiveCamera.FieldOfView = Camera.FieldOfView;
        if (_drag is null)
        {
            UpdateHover(_pointer);
        }

        _backdrop.InvalidateVisual();
        _annotations.InvalidateVisual();
        UpdateSourceButton();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (IsMeasuring)
        {
            var point = e.GetPosition(this);
            PickMeasurementPoint(Camera.Unproject(point.X, point.Y, ActualWidth, ActualHeight));
            Focus();
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2 && _dragDistance < 4 && ImageAt(e.GetPosition(this)) is { } layer)
        {
            Focus();
            e.Handled = true;
            InspectLayer(layer);
            return;
        }

        Focus();
        CaptureMouse();
        _drag = _dragStart = e.GetPosition(this);
        _dragDistance = 0;
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
        UpdateHover(null);
        UpdateSourceButton();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMeasuring && _measurementStart is { } start)
        {
            var point = e.GetPosition(this);
            Measurement = new(start, Camera.Unproject(point.X, point.Y, ActualWidth, ActualHeight));
            _annotations.InvalidateVisual();
            MeasurementChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_drag is not { } previous)
        {
            UpdateHover(e.GetPosition(this));
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - previous;
        _dragDistance += delta.Length;
        Camera.Pan(delta.X, delta.Y, ActualWidth);
        _drag = current;
        Refresh();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Hand;
        if (_dragDistance < 4)
        {
            if (ImageAt(_dragStart) is { } layer)
            {
                LayerSelected?.Invoke(this, layer);
            }
        }

        UpdateHover(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = null;
        Cursor = Cursors.Hand;
        UpdateSourceButton();
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        UpdateHover(null);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Zoom(Math.Pow(0.82, e.Delta / 120.0));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsMeasuring)
        {
            ClearMeasurement();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Left:
                Camera.Pan(ActualWidth * 0.08, 0, ActualWidth);
                break;
            case Key.Right:
                Camera.Pan(-ActualWidth * 0.08, 0, ActualWidth);
                break;
            case Key.Up:
                Camera.Pan(0, ActualWidth * 0.08, ActualWidth);
                break;
            case Key.Down:
                Camera.Pan(0, -ActualWidth * 0.08, ActualWidth);
                break;
            case Key.Add or Key.OemPlus:
                Zoom(0.8);
                break;
            case Key.Subtract or Key.OemMinus:
                Zoom(1.25);
                break;
            case Key.Home:
                ResetView();
                break;
            case Key.F when SelectedLayer is { } layer:
                Fit(layer);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        Refresh();
        e.Handled = true;
    }

}
