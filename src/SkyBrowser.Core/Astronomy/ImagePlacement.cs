using System.Globalization;
using XISFLib;

namespace SkyBrowser.Core.Astronomy;

public sealed record ManualPlacement(double RightAscension, double Declination, double WidthDegrees, double RotationDegrees, bool Mirror = false)
{
    public void Validate()
    {
        if (!double.IsFinite(RightAscension) ||
            !double.IsFinite(Declination) ||
            !double.IsFinite(WidthDegrees) ||
            !double.IsFinite(RotationDegrees) ||
            Declination is < -90 or > 90 ||
            WidthDegrees is <= 0 or >= 170)
        {
            throw new ArgumentException("Enter finite coordinates, declination from −90° to +90°, and a width between 0° and 170°.");
        }
    }
}

/// <summary>Maps normalized texture coordinates to celestial directions using stored pixel geometry.</summary>
public sealed class ImagePlacement
{
    private readonly Func<double, double, SkyCoordinate> _map;
    private SkyCoordinate[]? _sampleGrid;

    public string Description { get; }

    public SkyCoordinate Center => At(0.5, 0.5);

    public double AngularWidth => At(0, 0.5).DistanceTo(At(1, 0.5));

    public double AngularHeight => At(0.5, 0).DistanceTo(At(0.5, 1));

    /// <summary>Angular size of a stored source pixel at the image center, in arcseconds.</summary>
    public (double X, double Y) PixelScale(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        double dx = 0.5 / width, dy = 0.5 / height;
        return (At(0.5 - dx, 0.5).DistanceTo(At(0.5 + dx, 0.5)) * 3600,
            At(0.5, 0.5 - dy).DistanceTo(At(0.5, 0.5 + dy)) * 3600);
    }

    private ImagePlacement(string description, Func<double, double, SkyCoordinate> map) => (Description, _map) = (description, map);

    /// <summary>Restores the renderer's 33 × 33 sky samples without reparsing the scientific file.</summary>
    public static ImagePlacement FromSampleGrid(string description, IReadOnlyList<SkyCoordinate> samples)
    {
        const int divisions = 32, stride = divisions + 1;
        if (samples.Count != stride * stride)
        {
            throw new InvalidDataException("Unexpected cached sky mesh dimensions.");
        }

        SkyCoordinate[] points = samples.ToArray();
        foreach (var point in points)
        {
            if (!double.IsFinite(point.RightAscension) ||
                !double.IsFinite(point.Declination) ||
                Math.Abs(point.Declination) > 90)
            {
                throw new InvalidDataException("Invalid cached sky coordinate.");
            }
        }

        return new(description, (u, v) =>
        {
            if (u is < 0 or > 1 || v is < 0 or > 1 || !double.IsFinite(u) || !double.IsFinite(v))
            {
                throw new ArgumentOutOfRangeException(nameof(u), "Cached sky coordinates require normalized image coordinates.");
            }

            double x = u * divisions, y = v * divisions;
            int ix = (int)x, iy = (int)y;
            if (x == ix && y == iy)
            {
                return points[iy * stride + ix];
            }

            ix = Math.Min(ix, divisions - 1);
            iy = Math.Min(iy, divisions - 1);
            double tx = x - ix, ty = y - iy;
            // Interpolate directions, not RA values, to cross the 0/360° seam and poles correctly.
            return SkyCoordinate.FromVector(
                points[iy * stride + ix].Vector * ((1 - tx) * (1 - ty)) +
                points[iy * stride + ix + 1].Vector * (tx * (1 - ty)) +
                points[(iy + 1) * stride + ix].Vector * ((1 - tx) * ty) +
                points[(iy + 1) * stride + ix + 1].Vector * (tx * ty));
        })
        {
            _sampleGrid = points
        };
    }

    public SkyCoordinate[] CaptureSampleGrid()
    {
        // Placements are immutable, so their mesh can be sampled once and shared internally.
        // Return a copy so a renderer or cache writer cannot alter subsequent captures.
        var grid = Volatile.Read(ref _sampleGrid) ?? LazyInitializer.EnsureInitialized(ref _sampleGrid, () =>
        {
            var result = new SkyCoordinate[33 * 33];
            for (int y = 0; y <= 32; y++)
            {
                for (int x = 0; x <= 32; x++)
                {
                    result[y * 33 + x] = At(x / 32.0, y / 32.0);
                }
            }

            return result;
        });
        return (SkyCoordinate[])grid.Clone();
    }

    public SkyCoordinate At(double u, double v)
    {
        SkyCoordinate coordinate = _map(u, v);
        if (!double.IsFinite(coordinate.RightAscension) ||
            !double.IsFinite(coordinate.Declination) ||
            Math.Abs(coordinate.Declination) > 90)
        {
            throw new InvalidDataException("The astrometric solution returned an invalid sky coordinate.");
        }

        return coordinate;
    }

    public static ImagePlacement FromManual(ManualPlacement placement, int width, int height)
    {
        placement.Validate();
        var center = new SkyCoordinate(placement.RightAscension, placement.Declination);
        SkyVector east = SkyVector.Cross(center.North, center.Vector);
        var (sin, cos) = Math.SinCos(placement.RotationDegrees * SkyCoordinate.Radians);
        double extent = 2 * Math.Tan(placement.WidthDegrees * SkyCoordinate.Radians / 2);
        return new("Manual · tangent plane", (u, v) =>
        {
            double x = (0.5 - u) * extent * (placement.Mirror ? -1 : 1);
            double y = (0.5 - v) * extent * height / width;
            return SkyCoordinate.FromVector(center.Vector + east * (x * cos + y * sin) + center.North * (-x * sin + y * cos));
        });
    }

    public static ImagePlacement FromNative(XisfAstrometricSolution solution, int width, int height) => new($"XISF · {solution.ProjectionSystem} · layer {solution.AvailableLayer}", (u, v) =>
    {
        // XISF pixel centers are (column + .5, row + .5); texture edges are (0, 0) and (width, height).
        var sky = solution.ImageToCelestial(u * width, v * height);
        return new(sky.RightAscension, sky.Declination);
    });

    public static ImagePlacement FromFits(CelestialWorldCoordinateSystem wcs, int width, int height) => new("FITS WCS · TAN", (u, v) =>
        {
            // Library FITS API uses zero-based pixel centers; texture edges sit half a pixel outside them.
            var sky = wcs.PixelToSky(u * width - 0.5, v * height - 0.5);
            return new(sky.RightAscension, sky.Declination);
        });

    public static ImagePlacement? TryFromMetadata(XisfImageData image, XisfAstrometricSolution? native, out string? warning)
    {
        warning = null;
        int width = image.Dimensions[0], height = image.Dimensions[1];
        if (native is { AvailableLayer: > 0 })
        {
            if (native.CelestialReferenceSystem is not (null or "ICRS" or "FK5"))
            {
                warning = $"Reference system {native.CelestialReferenceSystem} needs conversion to J2000. Use manual placement.";
            }
            else
            {
                return FromNative(native, width, height);
            }
        }

        var keywords = image.FitsKeywords.GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last()
            .Value.Trim()
            .Trim('\'')
                .Trim(), StringComparer.OrdinalIgnoreCase);

        string Get(string name) => keywords.GetValueOrDefault(name, "");
        bool hasWcs = keywords.ContainsKey("CRVAL1");
        bool unsupportedDistortion = keywords.Keys.Any(k => k.StartsWith("PV1_", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("PV2_", StringComparison.OrdinalIgnoreCase) ||
            k is "A_ORDER" or "B_ORDER");
        string frame = Get("RADESYS") is { Length: > 0 } system ? system : Get("RADECSYS");

        bool NumberIs(string key, double expected) => !keywords.ContainsKey(key) ||
                    (double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                    double.IsFinite(value) &&
                    Math.Abs(value - expected) < 0.0001);
        bool wrongFrame = frame is not ("" or "ICRS" or "FK5") || !NumberIs("EQUINOX", 2000);
        bool wrongUnits = Get("CUNIT1") is not ("" or "deg") || Get("CUNIT2") is not ("" or "deg");
        bool hasCd = keywords.Keys.Any(k => k is "CD1_1" or "CD1_2" or "CD2_1" or "CD2_2");
        bool hasPc = keywords.Keys.Any(k => k.StartsWith("PC1_", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("PC2_", StringComparison.OrdinalIgnoreCase));
        bool unsupportedOrientation = !NumberIs("LONPOLE", 180) ||
            !NumberIs("LATPOLE", 90) ||
            (!hasCd &&
            !hasPc &&
            (!NumberIs("CROTA1", 0) ||
            !NumberIs("CROTA2", 0)));
        if (hasWcs &&
            (Get("CTYPE1") != "RA---TAN" ||
            Get("CTYPE2") != "DEC--TAN" ||
            unsupportedDistortion ||
            wrongFrame ||
            wrongUnits ||
            unsupportedOrientation))
        {
            warning = "This FITS WCS uses an unsupported projection, distortion or coordinate frame. Use manual placement; automatic placement requires J2000/ICRS TAN in degrees.";
            return null;
        }

        // FITS CD matrix elements omitted from an otherwise present matrix default to zero.
        var normalized = image.FitsKeywords.ToList();
        if (hasCd)
        {
            foreach (string key in new[]
            {
                "CD1_1",
                "CD1_2",
                "CD2_1",
                "CD2_2"
            }

            )
            {
                if (!keywords.ContainsKey(key))
                {
                    normalized.Add(new(key, "0", null));
                }
            }
        }

        if (hasWcs && CelestialWorldCoordinateSystem.TryCreate(normalized, out var wcs))
        {
            return FromFits(wcs!, width, height);
        }

        warning ??= native is not null
            ? "The native XISF solution is not evaluable. " + string.Join(" ", native.Diagnostics)
            : "No usable sky solution. Enter a center, image width and rotation to place this image.";
        return null;
    }
}
