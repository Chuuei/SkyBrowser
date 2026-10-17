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
    internal FitsSolutionData? FitsSolution { get; private init; }

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

    internal static ImagePlacement FromFits(FitsSolutionData data, int width, int height)
    {
        var solution = new FitsSkySolution(data);
        return new(solution.Description, (u, v) => solution.PixelToSky(u * width - 0.5, v * height - 0.5))
        {
            FitsSolution = solution.Capture()
        };
    }

    private static ImagePlacement? FromLegacy(XisfImageData image, out string? warning)
    {
        const string prefix = "PCL:AstrometricSolution:";
        warning = null;
        bool hasLegacy = image.Properties.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)) ||
            image.Properties.ContainsKey("Transformation_ImageToProjection");
        if (!hasLegacy)
        {
            return null;
        }
        if (image.Properties.Keys.Any(k => k.StartsWith(prefix + "SplineWorldTransformation", StringComparison.Ordinal) || k == "Transformation_ImageToProjection"))
        {
            warning = "This legacy PixInsight solution contains a spline/serialized distortion model that cannot be safely interpreted. Use a new plate solution; its linear approximation is not used.";
            return null;
        }
        static XisfPropertyData Typed(object? property) => property switch
        {
            XisfPropertyData data => data,
            XisfProperty { IsAvailable: true } parsed => new(parsed.Type, parsed.Value),
            string text => new("String", text),
            double[] vector => new("F64Vector", vector),
            double[,] matrix => new("F64Matrix", matrix),
            double number => new("Float64", number),
            _ => throw new InvalidDataException("Unavailable or invalid legacy property.")
        };
        try
        {
            var properties = new Dictionary<string, XisfPropertyData> { ["AstrometricSolution:Version"] = new("String", "1.0") };
            foreach (string name in new[] { "ProjectionSystem", "ReferenceCelestialCoordinates", "ReferenceImageCoordinates", "ReferenceNativeCoordinates", "CelestialPoleNativeCoordinates", "LinearTransformationMatrix" })
            {
                if (image.Properties.TryGetValue(prefix + name, out var property))
                {
                    properties["AstrometricSolution:" + name] = Typed(property);
                }
            }
            if (image.Properties.TryGetValue("Observation:CelestialReferenceSystem", out var frame))
            {
                properties["AstrometricSolution:CelestialReferenceSystem"] = Typed(frame);
            }
            if (image.Properties.TryGetValue("Observation:Equinox", out var equinox) &&
                (Typed(equinox).Value is not double value || value != 2000))
            {
                warning = "Legacy PixInsight coordinates require a J2000 equinox. Use manual placement or a new plate solution.";
                return null;
            }
            var solution = XisfAstrometricSolution.Create(properties);
            if (solution.CelestialReferenceSystem is not ("ICRS" or "FK5"))
            {
                warning = "Unsupported legacy PixInsight reference frame.";
                return null;
            }
            int width = image.Dimensions[0], height = image.Dimensions[1];
            return new("PixInsight legacy · " + solution.ProjectionSystem, (u, v) =>
            {
                var point = solution.ImageToCelestial(u * width, v * height);
                return new(point.RightAscension, point.Declination);
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or XisfFormatException or ArgumentException)
        {
            warning = "Invalid or unsupported legacy PixInsight solution. " + exception.Message;
            return null;
        }
    }

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

        var legacy = FromLegacy(image, out warning);
        if (legacy is not null || warning is not null)
        {
            return legacy;
        }
        var fits = FitsSkySolution.TryCreate(image.FitsKeywords, out warning);
        if (fits is not null)
        {
            return FromFits(fits.Capture(), width, height);
        }
        warning ??= native is not null
            ? "The native XISF solution is not evaluable. " + string.Join(" ", native.Diagnostics)
            : "No usable sky solution. Enter a center, image width and rotation to place this image.";
        return null;
    }
}
