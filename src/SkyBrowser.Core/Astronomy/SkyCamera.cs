namespace SkyBrowser.Core.Astronomy;

/// <summary>Perspective observer at the origin; increasing RA appears to the left.</summary>
public sealed class SkyCamera
{
    private bool _basisValid;
    private SkyCoordinate _basisCenter;
    private ObserverFrame? _basisObserver;
    private SkyVector _forward, _up, _right;
    private double _halfFieldTangent = Math.Tan(75 * SkyCoordinate.Radians / 2);

    public SkyCoordinate Center { get; set; } = new(25, 38);

    public double FieldOfView
    {
        get;
        set
        {
            field = Math.Clamp(value, 0.05, 120);
            _halfFieldTangent = Math.Tan(field * SkyCoordinate.Radians / 2);
        }
    } = 75;

    public SkyVector Forward
    {
        get
        {
            EnsureBasis();
            return _forward;
        }
    }

    public ObserverFrame? Observer { get; set; }

    public SkyVector Up
    {
        get
        {
            EnsureBasis();
            return _up;
        }
    }

    public SkyVector Right
    {
        get
        {
            EnsureBasis();
            return _right;
        }
    }

    private void EnsureBasis()
    {
        // Projection runs for every star and grid point. Reuse the camera axes until the
        // center changes or a new observer frame supplies a different local horizon.
        if (_basisValid && _basisCenter == Center && ReferenceEquals(_basisObserver, Observer))
        {
            return;
        }

        _forward = Center.Vector;
        _up = Observer?.UpAt(_forward) ?? Center.North;
        _right = SkyVector.Cross(_forward, _up).Normalized;
        _basisCenter = Center;
        _basisObserver = Observer;
        _basisValid = true;
    }

    public (double X, double Y)? Project(SkyVector direction, double width, double height)
    {
        EnsureBasis();
        double depth = SkyVector.Dot(direction, _forward);
        // Directions behind the observer cannot be projected onto the viewing plane.
        if (depth <= 0.001 || width <= 0 || height <= 0)
        {
            return null;
        }

        double scale = width / (2 * _halfFieldTangent);
        return (width / 2 + SkyVector.Dot(direction, _right) * scale / depth, height / 2 - SkyVector.Dot(direction, _up) * scale / depth);
    }

    public SkyCoordinate Unproject(double x, double y, double width, double height)
    {
        EnsureBasis();
        double scale = width / (2 * _halfFieldTangent);
        return SkyCoordinate.FromVector(_forward + _right * ((x - width / 2) / scale) + _up * ((height / 2 - y) / scale));
    }

    public void Pan(double dx, double dy, double width)
    {
        EnsureBasis();
        double scale = 2 * _halfFieldTangent / Math.Max(1, width);
        Center = SkyCoordinate.FromVector(_forward - _right * (dx * scale) + _up * (dy * scale));
    }
}
