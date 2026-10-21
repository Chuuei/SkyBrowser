using SkyBrowser.Core.Astronomy;
using XISFLib;

namespace SkyBrowser.Core.Imaging;

public enum DisplayMode
{
    Automatic,
    Linear,
    AutoStretch,
    StoredDisplay,
    Finished
}

public sealed record DisplayPixels(int Width, int Height, byte[] Bgra);
public sealed record LoadedSkyImage(
    string SourcePath,
    int ImageIndex,
    string Name,
    int Width,
    int Height,
    DisplayPixels Preview,
    string DisplayDescription,
    ImagePlacement? Placement,
    string? PlacementWarning,
    string Metadata
)
{
    private sealed record FileAstrometry(ImagePlacement? Placement, string? Warning);
    private FileAstrometry? _fileAstrometry;

    /// <summary>Whether the current placement comes from a locally saved ASTAP solve.</summary>
    public bool HasAstapSolution => _fileAstrometry is not null;

    /// <summary>The source file's own placement, before a local ASTAP override.</summary>
    public ImagePlacement? FilePlacement => _fileAstrometry is { } file ? file.Placement : Placement;

    /// <summary>The source file's placement warning, before a local ASTAP override.</summary>
    public string? FilePlacementWarning => _fileAstrometry is { } file ? file.Warning : PlacementWarning;

    /// <summary>Applies a local solve while retaining the file's astrometry for later restoration.</summary>
    public LoadedSkyImage WithAstapSolution(ImagePlacement placement, string? warning = null)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return this with
        {
            _fileAstrometry = _fileAstrometry ?? new(Placement, PlacementWarning),
            Placement = placement,
            PlacementWarning = warning
        };
    }

    /// <summary>Restores retained source astrometry without decoding or changing preview pixels.</summary>
    public LoadedSkyImage WithoutAstapSolution() => _fileAstrometry is { } file
        ? this with { Placement = file.Placement, PlacementWarning = file.Warning, _fileAstrometry = null }
        : this;

    public SkyFinishSettings FinishSettings { get; init; } = SkyFinishSettings.Default;

    public int SourceImageCount { get; init; } = 1;

    public bool IsFromCache { get; init; }

    public DisplayMode RenderMode { get; init; }

    public ImageMetadataSnapshot Details { get; init; } = ImageMetadataSnapshot.Empty;
}

/// <summary>Read-only astronomy import. Work is synchronous and belongs on a worker thread.</summary>
public static class SkyImageLoader
{
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xisf" or ".xish" or ".fits" or ".fit" or ".fts";

    public static IReadOnlyList<LoadedSkyImage> Load(
        string path,
        DisplayMode mode = DisplayMode.Automatic,
        CancellationToken cancellationToken = default,
        int? onlyImage = null
    ) => Load(path, mode, SkyFinishSettings.Default, cancellationToken, onlyImage);

    public static IReadOnlyList<LoadedSkyImage> Load(
        string path,
        DisplayMode mode,
        SkyFinishSettings finishSettings,
        CancellationToken cancellationToken = default,
        int? onlyImage = null,
        IProgress<ImageProcessingProgress>? progress = null
    )
    {
        ArgumentNullException.ThrowIfNull(finishSettings);
        finishSettings.Validate();
        path = Path.GetFullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported(path))
        {
            throw new NotSupportedException("Choose an XISF, XISH, FITS, FIT or FTS file.");
        }

        List<LoadedSkyImage> result = [];
        int imageCount;
        if (Path.GetExtension(path).ToLowerInvariant() is ".fits" or ".fit" or ".fts")
        {
            progress?.Report(new("Reading FITS image data"));
            var document = FitsReader.Load(path);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Preparing FITS image samples"));
            var images = FitsConverter.ToXisf(
                document,
                new()
                {
                    AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
                    LossPolicy = ConversionLossPolicy.Allow,
                    IncludeStructuralKeywords = true
                }
            );
            cancellationToken.ThrowIfCancellationRequested();
            imageCount = images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                if (onlyImage is null || onlyImage == i)
                {
                    result.Add(Prepare(images[i], null, path, i, mode, cancellationToken, finishSettings, progress));
                }
            }
        }
        else
        {
            progress?.Report(new("Reading XISF image data"));
            var document = XisfReader.Load(path);
            cancellationToken.ThrowIfCancellationRequested();
            imageCount = document.Images.Count;
            for (int i = 0; i < document.Images.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (onlyImage is not null && onlyImage != i)
                {
                    continue;
                }

                var source = document.Images[i];
                result.Add(Prepare(
                    XisfImageData.FromImage(source),
                    source.AstrometricSolution,
                    path,
                    i,
                    mode,
                    cancellationToken,
                    finishSettings,
                    progress
                ));
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("No image was found in this document.");
        }

        return result.Select(image => image with { SourceImageCount = imageCount, RenderMode = mode }).ToArray();
    }

    internal static LoadedSkyImage Prepare(
        XisfImageData image,
        XisfAstrometricSolution? native,
        string path,
        int index,
        DisplayMode mode,
        CancellationToken token,
        SkyFinishSettings? finishSettings,
        IProgress<ImageProcessingProgress>? progress,
        int maximumSize = 2048
    )
    {
        token.ThrowIfCancellationRequested();
        progress?.Report(new("Reading image metadata and sky coordinates"));
        if (image.Dimensions.Count != 2)
        {
            throw new NotSupportedException("Only two-dimensional images and RGB/RGBA FITS cubes are supported.");
        }

        int width = image.Dimensions[0], height = image.Dimensions[1];
        var metadata = AstronomyMetadata.From(image);
        var placement = ImagePlacement.TryFromMetadata(image, native, out string? warning);
        if (placement is not null)
        {
            try
            {
                // Match the renderer's mesh sample points before adding this image to the scene.
                _ = placement.CaptureSampleGrid();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
            {
                warning = $"The stored solution cannot map this image footprint: {exception.Message} Use manual placement.";
                placement = null;
            }
        }

        // Render stored rows unchanged. Astrometry maps these same rows; applying a display orientation
        // without transforming the solution would rotate or mirror the texture relative to the sky.
        var imageDetails = ImageMetadataSnapshot.Capture(image);
        image.Orientation = "0";
        var (bayerPattern, cfaWarning) = SkyImageDebayer.Detect(image);
        var displayImage = image;
        if (bayerPattern is not null)
        {
            progress?.Report(new($"Debayering {bayerPattern} mosaic to RGB"));
            displayImage = SkyImageDebayer.Create(image, bayerPattern, token);
        }

        finishSettings ??= SkyFinishSettings.Default;
        DisplayPixels preview;
        string description;
        if (cfaWarning is null && (mode == DisplayMode.Finished ||
            (mode == DisplayMode.Automatic &&
                displayImage.DisplayFunction is null &&
                SkyImageFinisher.LooksLinear(displayImage, token))))
        {
            // Automatic uses the same dark-sky stretch without cosmetic processing. Keep
            // the user's full settings on the result so switching to cleanup restores them.
            var processing = mode == DisplayMode.Automatic
                ? finishSettings with
                {
                    BalanceBackground = false,
                    ReduceGradients = false,
                    ReduceNoise = false,
                    EnhanceDetail = false
                }
                : finishSettings;
            (preview, description) = SkyImageFinisher.Render(displayImage, processing, token, progress, maximumSize);
            if (mode == DisplayMode.Automatic)
            {
                description += " · stretch only";
            }
        }
        else
        {
            progress?.Report(new("Rendering display preview"));
            var rendered = Render(displayImage, cfaWarning is not null && mode == DisplayMode.Finished ? DisplayMode.Linear : mode);
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Resizing display preview"));
            preview = Downsample(rendered.Pixels, maximumSize, token);
            description = rendered.Description;
        }

        static string Clean(string text) => text.Trim().Trim('\'').Trim().Replace("''", "'", StringComparison.Ordinal);
        string name = Clean(metadata.ObjectName ?? image.Id ?? Path.GetFileNameWithoutExtension(path));
        string details = $"{width:N0} × {height:N0} · {image.ChannelCount} channel(s) · {image.SampleFormat}";
        if (!string.IsNullOrWhiteSpace(metadata.Telescope))
        {
            details += $"\n{Clean(metadata.Telescope)}";
        }

        if (!string.IsNullOrWhiteSpace(metadata.Filter))
        {
            details += $" · {Clean(metadata.Filter)}";
        }

        if (metadata.ExposureSeconds is { } exposure)
        {
            details += $" · {exposure:g} s";
        }

        if (bayerPattern is not null)
        {
            string debayered = $"Automatically debayered {bayerPattern} to RGB (bilinear)";
            details += $"\n{debayered}.";
            description += $" · {debayered}";
        }
        else if (cfaWarning is not null)
        {
            details += $"\n{cfaWarning}";
        }

        return new(path, index, name, width, height, preview, description, placement, warning, details)
        {
            FinishSettings = finishSettings,
            Details = imageDetails
        };
    }

    private static (XisfBgra32Image Pixels, string Description) Render(XisfImageData image, DisplayMode mode)
    {
        if (mode == DisplayMode.Linear)
        {
            return (XisfDisplayRenderer.Render(image), "Linear display");
        }

        if (mode == DisplayMode.AutoStretch)
        {
            return (XisfDisplayRenderer.Render(image, XisfStretchOptions.Auto), "Library auto stretch");
        }

        if (image.DisplayFunction is not null && mode is DisplayMode.Automatic or DisplayMode.StoredDisplay)
        {
            return (XisfDisplayRenderer.Render(image, XisfStretchOptions.DisplayFunction), "Stored display function");
        }

        if (mode == DisplayMode.StoredDisplay)
        {
            return (XisfDisplayRenderer.Render(image), "No stored display function · linear");
        }

        var linear = XisfDisplayRenderer.Render(image);
        // A heuristic, not a scientific linearity test. The UI always offers explicit overrides.
        int pixelCount = linear.Width * linear.Height;
        int step = Math.Max(1, pixelCount / 16384);
        List<byte> samples = new(16385);
        for (int i = 0; i < pixelCount; i += step)
        {
            int p = i * 4;
            samples.Add((byte)((linear.Pixels[p] + linear.Pixels[p + 1] + linear.Pixels[p + 2]) / 3));
        }

        samples.Sort();
        if (!SkyImageFinisher.Supports(image) &&
            samples[samples.Count / 2] < 20 &&
            samples[(int)((samples.Count - 1) * 0.95)] < 90)
        {
            return (XisfDisplayRenderer.Render(image, XisfStretchOptions.Auto with { TargetBackground = 0.10 }), "Automatic dark-sky stretch · stretch only");
        }

        return (linear, "Auto detected stretched · linear display");
    }

    private static DisplayPixels Downsample(XisfBgra32Image source, int maximum, CancellationToken token)
    {
        double ratio = Math.Min(1, (double)maximum / Math.Max(source.Width, source.Height));
        if (ratio >= 1)
        {
            return new(source.Width, source.Height, source.Pixels);
        }

        int width = Math.Max(1, (int)(source.Width * ratio)), height = Math.Max(1, (int)(source.Height * ratio));
        byte[] pixels = new byte[checked(width * height * 4)];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            int y0 = y * source.Height / height, y1 = (y + 1) * source.Height / height;
            for (int x = 0; x < width; x++)
            {
                int x0 = x * source.Width / width, x1 = (x + 1) * source.Width / width;
                long b = 0, g = 0, r = 0, a = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int p = (sy * source.Width + sx) * 4;
                        b += source.Pixels[p];
                        g += source.Pixels[p + 1];
                        r += source.Pixels[p + 2];
                        a += source.Pixels[p + 3];
                    }
                }

                int count = (y1 - y0) * (x1 - x0), target = (y * width + x) * 4;
                pixels[target] = (byte)(b / count);
                pixels[target + 1] = (byte)(g / count);
                pixels[target + 2] = (byte)(r / count);
                pixels[target + 3] = (byte)(a / count);
            }
        });
        return new(width, height, pixels);
    }
}
