namespace SkyBrowser.Core.Astronomy;

/// <summary>A spherical footprint following the same sampled boundary and triangles as the sky renderer.</summary>
public sealed class SkyFootprint
{
    private const double Tolerance = 1e-12;
    private readonly SkyVector[] _boundary;
    private readonly SkyVector[] _normals;
    private readonly SkyVector _center;
    private readonly double _radius, _cosRadius;

    public double AreaSteradians { get; }

    public SkyFootprint(ImagePlacement placement)
    {
        var points = placement.CaptureSampleGrid().Select(point => point.Vector).ToArray();
        _center = points[16 * 33 + 16];
        _cosRadius = points.Min(point => SkyVector.Dot(point, _center));
        _radius = Math.Acos(Math.Clamp(_cosRadius, -1, 1));
        _boundary = new SkyVector[128];
        for (int i = 0; i < 32; i++)
        {
            _boundary[i] = points[i];
            _boundary[32 + i] = points[i * 33 + 32];
            _boundary[64 + i] = points[32 * 33 + 32 - i];
            _boundary[96 + i] = points[(32 - i) * 33];
        }

        _normals = Enumerable.Range(0, _boundary.Length)
            .Select(i => SkyVector.Cross(_boundary[i], _boundary[(i + 1) % _boundary.Length])
            .Normalized)
            .ToArray();
        double area = 0;
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int a = y * 33 + x, b = a + 1, c = a + 33, d = c + 1;
                area += TriangleArea(points[a], points[c], points[b]) + TriangleArea(points[b], points[c], points[d]);
            }
        }

        AreaSteradians = area;
    }

    public bool Overlaps(SkyFootprint other)
    {
        double separation = Math.Acos(Math.Clamp(SkyVector.Dot(_center, other._center), -1, 1));
        if (separation >= _radius + other._radius)
        {
            return false;
        }

        if (Contains(other._center) ||
            other.Contains(_center) ||
            Contains(other._boundary[0]) ||
            other.Contains(_boundary[0]))
        {
            return true;
        }

        for (int i = 0; i < _boundary.Length; i++)
        {
            for (int j = 0; j < other._boundary.Length; j++)
            {
                var intersection = SkyVector.Cross(_normals[i], other._normals[j]);
                if (intersection.Length < Tolerance)
                {
                    continue;
                }

                intersection = intersection.Normalized;
                if ((OnArc(intersection, i, strict: true) &&
                    other.OnArc(intersection, j, strict: true)) ||
                    (OnArc(intersection * -1, i, strict: true) &&
                    other.OnArc(intersection * -1, j, strict: true)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public SkyCoordinate? OverlapCenter(SkyFootprint other)
    {
        if (Contains(other._center)) { return SkyCoordinate.FromVector(other._center); }
        if (other.Contains(_center)) { return SkyCoordinate.FromVector(_center); }
        var points = _boundary.Where(other.Contains).Concat(other._boundary.Where(Contains)).ToList();
        for (int i = 0; i < _boundary.Length; i++)
        {
            for (int j = 0; j < other._boundary.Length; j++)
            {
                var intersection = SkyVector.Cross(_normals[i], other._normals[j]);
                if (intersection.Length < Tolerance) { continue; }
                intersection = intersection.Normalized;
                foreach (var direction in new[] { intersection, intersection * -1 })
                {
                    if (OnArc(direction, i, strict: true) && other.OnArc(direction, j, strict: true))
                    {
                        points.Add(direction);
                    }
                }
            }
        }
        if (points.Count == 0) { return null; }
        var center = points.Aggregate(default(SkyVector), (sum, point) => sum + point).Normalized;
        return Contains(center) && other.Contains(center) ? SkyCoordinate.FromVector(center) : null;
    }

    private bool Contains(SkyVector direction)
    {
        if (SkyVector.Dot(_center, direction) < _cosRadius - Tolerance)
        {
            return false;
        }

        double winding = 0;
        for (int i = 0; i < _boundary.Length; i++)
        {
            // Merely touching a tile edge should not make an adjacent image translucent.
            if (Math.Abs(SkyVector.Dot(direction, _normals[i])) < Tolerance && OnArc(direction, i, strict: false))
            {
                return false;
            }

            var a = _boundary[i];
            var b = _boundary[(i + 1) % _boundary.Length];
            winding += Math.Atan2(
                SkyVector.Dot(direction, SkyVector.Cross(a, b)),
                SkyVector.Dot(a, b) - SkyVector.Dot(a, direction) * SkyVector.Dot(b, direction)
            );
        }

        return Math.Abs(winding) > Math.PI;
    }

    private bool OnArc(SkyVector direction, int edge, bool strict)
    {
        double tolerance = strict ? Tolerance : -Tolerance;
        return SkyVector.Dot(SkyVector.Cross(_boundary[edge], direction), _normals[edge]) > tolerance &&
            SkyVector.Dot(SkyVector.Cross(direction, _boundary[(edge + 1) % _boundary.Length]), _normals[edge]) > tolerance;
    }

    private static double TriangleArea(SkyVector a, SkyVector b, SkyVector c) => 2 * Math.Atan2(
        Math.Abs(SkyVector.Dot(a, SkyVector.Cross(b, c))),
        1 + SkyVector.Dot(a, b) + SkyVector.Dot(b, c) + SkyVector.Dot(c, a)
    );
}
