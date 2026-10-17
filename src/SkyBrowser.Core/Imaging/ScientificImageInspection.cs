using System.Buffers.Binary;
using System.Globalization;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Core.Imaging;

public sealed record ScientificChannelStatistics(string Channel, long FiniteCount, long NonFiniteCount,
    double Minimum, double Maximum, double Mean, double StandardDeviation, IReadOnlyList<long> Histogram);

/// <summary>On-demand full-resolution display and original, unstretched scientific samples.</summary>
public sealed class ScientificImageInspection
{
    private readonly XisfImageData _source;

    public LoadedSkyImage Image { get; }
    public ImagePlacement? Placement { get; }
    public IReadOnlyList<ScientificChannelStatistics> Statistics { get; }
    public string SampleDescription { get; }

    private ScientificImageInspection(XisfImageData source, LoadedSkyImage image, ImagePlacement? placement,
        IReadOnlyList<ScientificChannelStatistics> statistics, bool fits)
    {
        _source = source;
        Image = image;
        Placement = placement;
        Statistics = statistics;
        SampleDescription = (fits ? "FITS physical values (BSCALE/BZERO applied; BLANK is NaN)" : "Original XISF sample values") +
            $" · {source.SampleFormat} · {source.ChannelCount} channel(s)" +
            (SkyImageDebayer.Detect(source).Pattern is not null ? " · Readout/statistics use the original Bayer mosaic" : "");
    }

    public static ScientificImageInspection Load(LoadedSkyImage selected, ManualPlacement? manual = null,
        ImageLibrary? library = null, CancellationToken token = default, IProgress<ImageProcessingProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = SourceSnapshot.Capture(selected.SourcePath, token);
        progress?.Report(new("Reading full-resolution scientific image"));
        XisfImageData source;
        XisfAstrometricSolution? native = null;
        bool fits = Path.GetExtension(selected.SourcePath).ToLowerInvariant() is ".fits" or ".fit" or ".fts";
        if (fits)
        {
            var images = FitsConverter.ToXisf(FitsReader.Load(selected.SourcePath), new()
            {
                AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
                LossPolicy = ConversionLossPolicy.Allow,
                IncludeStructuralKeywords = true
            });
            source = images.ElementAtOrDefault(selected.ImageIndex) ?? throw new InvalidDataException("The selected image is no longer in this document.");
        }
        else
        {
            var document = XisfReader.Load(selected.SourcePath);
            var image = document.Images.ElementAtOrDefault(selected.ImageIndex) ?? throw new InvalidDataException("The selected image is no longer in this document.");
            source = XisfImageData.FromImage(image);
            native = image.AstrometricSolution;
        }
        token.ThrowIfCancellationRequested();
        if (source.Dimensions.Count != 2 || source.Dimensions[0] != selected.Width || source.Dimensions[1] != selected.Height)
        {
            throw new InvalidDataException("Source dimensions changed. Reload the image before inspecting it.");
        }
        ValidateSampleFormat(source.SampleFormat);
        progress?.Report(new("Measuring original samples and histogram"));
        var statistics = Measure(source, token);
        var full = SkyImageLoader.Prepare(source, native, selected.SourcePath, selected.ImageIndex, selected.RenderMode,
            token, selected.FinishSettings, progress, Math.Max(selected.Width, selected.Height));
        full = full with { RenderMode = selected.RenderMode, SourceImageCount = selected.SourceImageCount };
        if (library is not null)
        {
            full = library.WithSavedSolution(full, snapshot);
        }
        var placement = manual is null ? full.Placement : ImagePlacement.FromManual(manual, full.Width, full.Height);
        token.ThrowIfCancellationRequested();
        if (!snapshot.IsCurrent(token))
        {
            throw new IOException("The source changed during inspection. Reload it and try again.");
        }
        return new(source, full, placement, statistics, fits);
    }

    public string ReadSample(int x, int y, int channel)
    {
        ValidatePixel(x, y);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, _source.ChannelCount);
        return new SampleReader(_source).Format(y * Image.Width + x, channel);
    }

    public SkyCoordinate? SkyAtPixel(int x, int y)
    {
        ValidatePixel(x, y);
        // Texture coordinates describe edges; scientific readouts describe pixel centers.
        return Placement?.At((x + 0.5) / Image.Width, (y + 0.5) / Image.Height);
    }

    private void ValidatePixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Image.Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Image.Height);
    }

    private static void ValidateSampleFormat(XisfSampleFormat format)
    {
        if (format is not (XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or XisfSampleFormat.UInt32 or
            XisfSampleFormat.UInt64 or XisfSampleFormat.Float32 or XisfSampleFormat.Float64))
        {
            throw new NotSupportedException("Inspection supports real integer and floating-point scientific samples.");
        }
    }

    private static IReadOnlyList<ScientificChannelStatistics> Measure(XisfImageData source, CancellationToken token)
    {
        var reader = new SampleReader(source);
        int pixels = checked(source.Dimensions[0] * source.Dimensions[1]);
        List<ScientificChannelStatistics> statistics = [];
        for (int c = 0; c < source.ChannelCount; c++)
        {
            long finite = 0;
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            for (int p = 0; p < pixels; p++)
            {
                if (p % source.Dimensions[0] == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                double value = reader.Read(p, c);
                if (!double.IsFinite(value))
                {
                    continue;
                }
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                finite++;
            }
            long[] bins = new long[256];
            // Scale the range before subtraction so opposite large finite bounds do not overflow.
            double scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
            double low = scale > 0 ? minimum / scale : 0;
            double range = scale > 0 ? maximum / scale - low : 0;
            long measured = 0;
            double mean = 0, m2 = 0;
            for (int p = 0; p < pixels; p++)
            {
                if (p % source.Dimensions[0] == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                double value = reader.Read(p, c);
                if (double.IsFinite(value))
                {
                    double normalized = scale > 0 ? value / scale : 0;
                    double delta = normalized - mean;
                    mean += delta / ++measured;
                    m2 += delta * (normalized - mean);
                    int bin = range > 0 ? Math.Clamp((int)((value / scale - low) / range * 256), 0, 255) : 0;
                    bins[bin]++;
                }
            }
            string channel = source.ColorSpace == "RGB" && c < 3 ? new[] { "R", "G", "B" }[c]
                : c == (source.ColorSpace == "RGB" ? 3 : 1) ? "Alpha" : source.ChannelCount == 1 ? "Gray / CFA" : $"Channel {c + 1}";
            statistics.Add(new(channel, finite, pixels - finite, finite > 0 ? minimum : double.NaN,
                finite > 0 ? maximum : double.NaN, finite > 0 ? mean * scale : double.NaN,
                finite > 0 ? Math.Sqrt(Math.Max(0, m2 / finite)) * scale : double.NaN, Array.AsReadOnly(bins)));
        }
        return statistics.AsReadOnly();
    }

    private readonly ref struct SampleReader(XisfImageData source)
    {
        private ReadOnlySpan<byte> Bytes(int pixel, int channel)
        {
            int size = source.SampleFormat switch
            {
                XisfSampleFormat.UInt8 => 1,
                XisfSampleFormat.UInt16 => 2,
                XisfSampleFormat.UInt32 or XisfSampleFormat.Float32 => 4,
                _ => 8
            };
            int sample = source.PixelStorage == XisfPixelLayout.Planar
                ? checked(channel * source.Dimensions[0] * source.Dimensions[1] + pixel)
                : checked(pixel * source.ChannelCount + channel);
            return source.RawData.Slice(checked(sample * size), size);
        }

        public double Read(int pixel, int channel)
        {
            var bytes = Bytes(pixel, channel);
            bool big = source.IsBigEndian;
            return source.SampleFormat switch
            {
                XisfSampleFormat.UInt8 => bytes[0],
                XisfSampleFormat.UInt16 => big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                XisfSampleFormat.UInt32 => big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                XisfSampleFormat.UInt64 => big ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                XisfSampleFormat.Float32 => big ? BinaryPrimitives.ReadSingleBigEndian(bytes) : BinaryPrimitives.ReadSingleLittleEndian(bytes),
                _ => big ? BinaryPrimitives.ReadDoubleBigEndian(bytes) : BinaryPrimitives.ReadDoubleLittleEndian(bytes)
            };
        }

        public string Format(int pixel, int channel)
        {
            // UInt64 readouts must not pass through double (which loses integer bits above 2^53).
            if (source.SampleFormat == XisfSampleFormat.UInt64)
            {
                var bytes = Bytes(pixel, channel);
                ulong value = source.IsBigEndian ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
                return value.ToString(CultureInfo.InvariantCulture);
            }
            return Read(pixel, channel).ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
