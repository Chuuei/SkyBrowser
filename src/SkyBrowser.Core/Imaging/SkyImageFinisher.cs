using System.Buffers.Binary;
using ImageMagick;
using XISFLib;

namespace SkyBrowser.Core.Imaging;

/// <summary>
/// Adapts the Avalonia viewer's dark-sky stretch, clipped background plane and Magick.NET cosmetics.
/// Keeps the entire frame and stored row order so the image's astrometry stays valid.
/// Adapted from XISFLib.Net ViewerFinishService; Copyright 2026 Chuuei and contributors,
/// Apache-2.0. Changes preserve the full astrometric frame and render bounded previews.
/// </summary>
public static class SkyImageFinisher
{
    public static bool Supports(XisfImageData source) => source.Dimensions.Count == 2 &&
        source.ColorFilterArray is null &&
        (source.ColorSpace is null or "Gray" or "RGB") &&
        source.ChannelCount >= (source.ColorSpace == "RGB" ? 3 : 1) &&
        source.ChannelCount <= (source.ColorSpace == "RGB" ? 4 : 2) &&
        source.SampleFormat is
            XisfSampleFormat.UInt8 or
            XisfSampleFormat.UInt16 or
            XisfSampleFormat.UInt32 or
            XisfSampleFormat.UInt64 or
            XisfSampleFormat.Float32 or
            XisfSampleFormat.Float64;

    public static bool LooksLinear(XisfImageData source, CancellationToken token = default)
    {
        if (!Supports(source))
        {
            return false;
        }

        var reader = new SampleReader(source);
        int colors = source.ColorSpace == "RGB" ? 3 : 1;
        var samples = SampleSource(reader, source.Dimensions[0] * source.Dimensions[1], colors, token);
        var (median, spread) = Levels(samples);
        return double.IsFinite(median) && spread > 1e-10 && (median < 0.18 || spread < median * 0.1);
    }

    public static (DisplayPixels Pixels, string Description) Render(
        XisfImageData source,
        SkyFinishSettings settings,
        CancellationToken token = default,
        IProgress<ImageProcessingProgress>? progress = null
    )
    {
        settings.Validate();
        if (!Supports(source))
        {
            throw new NotSupportedException("Dark-sky finishing supports real two-dimensional grayscale/RGB images. Debayer CFA images first.");
        }

        token.ThrowIfCancellationRequested();
        int sourceWidth = source.Dimensions[0];
        int sourceHeight = source.Dimensions[1];
        int colors = source.ColorSpace == "RGB" ? 3 : 1;
        double ratio = Math.Min(1, 2048d / Math.Max(sourceWidth, sourceHeight));
        int width = Math.Max(1, (int)(sourceWidth * ratio));
        int height = Math.Max(1, (int)(sourceHeight * ratio));
        int count = checked(width * height);
        float[][] channels = Enumerable.Range(0, colors).Select(_ => new float[count]).ToArray();
        float[]? alpha = source.ChannelCount > colors ? new float[count] : null;

        // Average linear scientific samples before quantization, retaining faint detail and full coverage.
        void PrepareRow(int y)
        {
            var reader = new SampleReader(source);
            int y0 = y * sourceHeight / height, y1 = (y + 1) * sourceHeight / height;
            for (int x = 0; x < width; x++)
            {
                int x0 = x * sourceWidth / width, x1 = (x + 1) * sourceWidth / width;
                double red = 0;
                double green = 0;
                double blue = 0;
                double weight = 0;
                int cells = (y1 - y0) * (x1 - x0);
                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int p = sy * sourceWidth + sx;
                        double a = alpha is null ? 1 : FiniteUnit(reader.Read(p, colors));
                        double r = reader.Read(p, 0);
                        double g = colors == 3 ? reader.Read(p, 1) : r;
                        double b = colors == 3 ? reader.Read(p, 2) : r;
                        if (!double.IsFinite(r) || !double.IsFinite(g) || !double.IsFinite(b))
                        {
                            continue;
                        }

                        red += r * a;
                        green += g * a;
                        blue += b * a;
                        weight += a;
                    }
                }

                int target = y * width + x;
                channels[0][target] = weight > 0 ? (float)(red / weight) : float.NaN;
                if (colors == 3)
                {
                    channels[1][target] = weight > 0 ? (float)(green / weight) : float.NaN;
                    channels[2][target] = weight > 0 ? (float)(blue / weight) : float.NaN;
                }

                if (alpha is not null)
                {
                    alpha[target] = (float)(weight / cells);
                }
            }
        }

        ProcessRows("Preparing full-frame preview", PrepareRow);
        token.ThrowIfCancellationRequested();
        Report("Measuring sky background");
        double[][] sampled = SampleChannels(channels, alpha);
        var (medianLevel, spread) = Levels(sampled);
        // Some linear integrations have a large additive offset. Their narrow spread still
        // identifies them as linear; remove the pedestal before choosing the stretch.
        bool highPedestal = medianLevel >= 0.18 && spread > 0 && spread < medianLevel * 0.1;
        bool stretch = double.IsFinite(medianLevel) && spread > 1e-10 && (medianLevel < 0.18 || highPedestal);
        var planes = new BackgroundPlane[colors];
        if (stretch && settings.ReduceGradients)
        {
            Report("Reducing broad sky gradients");
            for (int c = 0; c < colors; c++)
            {
                planes[c] = EstimateGradient(channels[c], alpha, width, height, token);
            }
        }

        if (planes.Any(p => p.IsActive))
        {
            sampled = SampleChannels(channels, alpha, planes, width, height);
        }

        double[] medians = sampled.Select(values => Median(values.ToList())).ToArray();
        medianLevel = medians.Average();
        double pedestal = stretch && highPedestal ? Math.Max(0, medianLevel - 0.1) : 0;
        double[] offsets = medians.Select(m => pedestal + (stretch && colors == 3 && settings.BalanceBackground ? m - medianLevel : 0)).ToArray();
        var function = stretch
            ? XisfDisplayFunctions.CreateAdaptive(
                sampled
                    .Select((values, c) => values.Select(v => v - offsets[c]).ToArray())
                    .ToArray(),
                settings.TargetBackground,
                -2.8,
                true)
            : null;

        // Capture the channel parameters once; the pixel loop must not repeatedly access
        // the library's read-only parameter collections for every sample.
        var transforms = function is null
            ? []
            : Enumerable.Range(0, colors)
                .Select(c => (
                    Midtones: function.Midtones[c],
                    Shadows: function.Shadows[c],
                    Highlights: function.Highlights[c],
                    Low: function.LowRange[c],
                    High: function.HighRange[c]))
                .ToArray();
        byte[] bytes = new byte[checked(count * 4)];

        void StretchRow(int y)
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                for (int c = 0; c < 3; c++)
                {
                    int channel = colors == 1 ? 0 : c;
                    double value = channels[channel][p];
                    double adjusted = value - (stretch ? offsets[channel] : 0) - planes[channel].At(x, y, width, height);
                    if (function is not null)
                    {
                        var transform = transforms[channel];
                        adjusted = XisfDisplayFunctions.Evaluate(adjusted, transform.Midtones, transform.Shadows, transform.Highlights, transform.Low, transform.High);
                    }

                    bytes[p * 4 + 2 - c] = ToByte(adjusted);
                }

                bytes[p * 4 + 3] = alpha is null ? (byte)255 : ToByte(alpha[p]);
            }
        }

        ProcessRows("Stretching onto a dark sky", StretchRow);

        // As in the viewer, leave already-developed/constant images at their existing exposure.
        token.ThrowIfCancellationRequested();
        if (stretch && width >= 3 && height >= 3 && (settings.ReduceNoise || settings.EnhanceDetail))
        {
            double noise = EstimateNoise(bytes, width, height);
            using var picture = new MagickImage();
            picture.ReadPixels(bytes, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, "BGRA"));
            picture.ColorSpace = ColorSpace.sRGB;
            if (settings.ReduceNoise)
            {
                token.ThrowIfCancellationRequested();
                Report("Smoothing background and color noise");
                picture.SelectiveBlur(2, 0.75, new Percentage(Math.Clamp(2.5 * noise / 255 * 100, 3, 15)), Channels.RGB);
                token.ThrowIfCancellationRequested();
            }

            if (settings.EnhanceDetail)
            {
                token.ThrowIfCancellationRequested();
                Report("Enhancing detail and color");
                picture.UnsharpMask(1, 0.7, 0.2, Math.Max(0.02, 2 * noise / 255), Channels.RGB);
                if (colors == 3)
                {
                    picture.Modulate(new Percentage(100), new Percentage(106));
                }

                token.ThrowIfCancellationRequested();
            }

            picture.Depth = 8;
            using var pixels = picture.GetPixels();
            byte[] cleaned = pixels.ToByteArray(PixelMapping.BGRA)!;
            // Cosmetics affect color only; preserve source coverage and transparency exactly.
            for (int p = 0; p < count; p++)
            {
                cleaned[p * 4 + 3] = bytes[p * 4 + 3];
            }

            bytes = cleaned;
        }

        token.ThrowIfCancellationRequested();
        string description = stretch ? $"Dark sky · background {settings.TargetBackground:P0}" : "Existing exposure retained";
        if (stretch && colors == 3 && settings.BalanceBackground)
        {
            description += " · background color balance";
        }

        if (planes.Any(p => p.IsActive))
        {
            description += " · gradient reduction";
        }

        if (stretch && settings.ReduceNoise)
        {
            description += " · noise cleanup";
        }

        if (stretch && settings.EnhanceDetail)
        {
            description += " · gentle detail/color enhancement";
        }

        return (new(width, height, bytes), description);

        void Report(string stage, double? percentage = null) => progress?.Report(new(stage, percentage));

        void ProcessRows(string stage, Action<int> row)
        {
            // Workers write disjoint output rows. Each pixel keeps the same source-sample
            // accumulation order, so parallel execution does not change the preview.
            Report(stage);
            int completed = 0, reported = 0;
            object progressGate = new();

            void Work(int y)
            {
                token.ThrowIfCancellationRequested();
                row(y);
                int done = Interlocked.Increment(ref completed);
                if (progress is not null && (done % 64 == 0 || done == height))
                {
                    lock (progressGate)
                    {
                        // Workers finish out of order. Serialize callbacks and suppress older
                        // counts so progress never moves backwards.
                        if (done > reported)
                        {
                            reported = done;
                            Report(stage, done * 100d / height);
                        }
                    }
                }
            }

            if (count < 262144 || Environment.ProcessorCount == 1)
            {
                for (int y = 0; y < height; y++)
                {
                    Work(y);
                }
            }
            else
            {
                try
                {
                    Parallel.For(
                        0,
                        height,
                        new ParallelOptions
                        {
                            CancellationToken = token,
                            MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount)
                        },
                        Work
                    );
                }
                catch (AggregateException exception)
                {
                    // Preserve the error contract of sequential processing for callers.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
                    throw;
                }
            }
        }
    }

    private static double[][] SampleSource(SampleReader reader, int count, int colors, CancellationToken token)
    {
        int step = Math.Max(1, (int)Math.Ceiling(count / 65536d));
        var samples = Enumerable.Range(0, colors).Select(_ => new List<double>()).ToArray();
        for (int p = 0; p < count; p += step)
        {
            token.ThrowIfCancellationRequested();
            if (reader.Channels > colors && FiniteUnit(reader.Read(p, colors)) <= 0)
            {
                continue;
            }

            for (int c = 0; c < colors; c++)
            {
                double value = reader.Read(p, c);
                if (double.IsFinite(value))
                {
                    samples[c].Add(Math.Clamp(value, 0, 1));
                }
            }
        }

        return samples.Select(list => list.ToArray()).ToArray();
    }

    private static double[][] SampleChannels(float[][] channels, float[]? alpha, BackgroundPlane[]? planes = null, int width = 0, int height = 0)
    {
        int step = Math.Max(1, (int)Math.Ceiling(channels[0].Length / 65536d));
        return channels.Select((channel, c) => Enumerable.Range(0, (channel.Length + step - 1) / step)
            .Select(i => i * step)
            .Where(p => (alpha is null || alpha[p] > 0) && float.IsFinite(channel[p]))
            .Select(p => channel[p] - (planes is null ? 0 : planes[c].At(p % width, p / width, width, height)))
            .ToArray())
            .ToArray();
    }

    private static (double Median, double Spread) Levels(double[][] samples)
    {
        double[] medians = samples.Select(values => Median(values.ToList())).ToArray();
        double[] deviations = samples.Select((values, c) => Median(values.Select(v => Math.Abs(v - medians[c])).ToList()) * 1.4826).ToArray();
        return (medians.Average(), deviations.Average());
    }

    private readonly record struct BackgroundPlane(double X, double Y)
    {
        public bool IsActive => X != 0 || Y != 0;

        public double At(int x, int y, int width, int height) => IsActive ? X * ((x + 0.5) / width - 0.5) + Y * ((y + 0.5) / height - 0.5) : 0;
    }

    // Same conservative two-sided clipped plane used by ViewerFinishService: reject emission/dark
    // clouds, require support in every quadrant, remove 80% of a coherent slope, retain its pedestal.
    private static BackgroundPlane EstimateGradient(float[] channel, float[]? alpha, int width, int height, CancellationToken token)
    {
        if (width < 96 || height < 96)
        {
            return default;
        }

        const int grid = 12, samples = 16;
        var cells = new List<(double X, double Y, double Value)>();
        for (int row = 0; row < grid; row++)
        {
            for (int col = 0; col < grid; col++)
            {
                token.ThrowIfCancellationRequested();
                var values = new List<double>();
                int x0 = col * width / grid;
                int x1 = (col + 1) * width / grid;
                int y0 = row * height / grid;
                int y1 = (row + 1) * height / grid;
                int nx = Math.Min(samples, x1 - x0), ny = Math.Min(samples, y1 - y0);
                for (int sy = 0; sy < ny; sy++)
                {
                    for (int sx = 0; sx < nx; sx++)
                    {
                        int x = x0 + (2 * sx + 1) * (x1 - x0) / (2 * nx);
                        int y = y0 + (2 * sy + 1) * (y1 - y0) / (2 * ny);
                        int p = y * width + x;
                        if ((alpha is null || alpha[p] > 0) && float.IsFinite(channel[p]))
                        {
                            values.Add(channel[p]);
                        }
                    }
                }

                if (values.Count >= nx * ny * 3 / 4)
                {
                    cells.Add(((x0 + x1) / (2d * width) - 0.5, (y0 + y1) / (2d * height) - 0.5, Median(values)));
                }
            }
        }

        if (cells.Count < grid * grid * 3 / 4)
        {
            return default;
        }

        bool[] included = Enumerable.Repeat(true, cells.Count).ToArray();
        double[] coefficients = new double[3];
        double scatter = 0;
        for (int iteration = 0; iteration < 6; iteration++)
        {
            token.ThrowIfCancellationRequested();
            // Fit an intercept and two slopes to the accepted background cells. The fourth
            // column holds the right-hand side of the three normal equations.
            var matrix = new double[3, 4];
            for (int i = 0; i < cells.Count; i++)
            {
                if (!included[i])
                {
                    continue;
                }

                var cell = cells[i];
                double[] basis = [1, cell.X, cell.Y];
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        matrix[r, c] += basis[r] * basis[c];
                    }

                    matrix[r, 3] += basis[r] * cell.Value;
                }
            }

            // Solve the small augmented matrix in place with Gaussian elimination.
            for (int pivot = 0; pivot < 3; pivot++)
            {
                double divisor = matrix[pivot, pivot];
                if (Math.Abs(divisor) < 1e-10)
                {
                    return default;
                }

                for (int c = pivot; c < 4; c++)
                {
                    matrix[pivot, c] /= divisor;
                }

                for (int r = 0; r < 3; r++)
                {
                    if (r == pivot)
                    {
                        continue;
                    }

                    double factor = matrix[r, pivot];
                    for (int c = pivot; c < 4; c++)
                    {
                        matrix[r, c] -= factor * matrix[pivot, c];
                    }
                }
            }

            for (int c = 0; c < 3; c++)
            {
                coefficients[c] = matrix[c, 3];
            }

            double[] residuals = cells.Select(cell => cell.Value - coefficients[0] - coefficients[1] * cell.X - coefficients[2] * cell.Y).ToArray();
            double center = Median(residuals.Where((_, i) => included[i]).ToList());
            scatter = 1.4826 * Median(residuals.Where((_, i) => included[i]).Select(v => Math.Abs(v - center)).ToList());
            if (iteration == 5)
            {
                break;
            }

            double limit = Math.Max(1e-10, 2.5 * scatter);
            for (int i = 0; i < cells.Count; i++)
            {
                included[i] &= Math.Abs(residuals[i] - center) <= limit;
            }

            if (included.Count(v => v) < cells.Count * 0.6)
            {
                return default;
            }
        }

        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            if (cells.Where((cell, i) => included[i] && (cell.X >= 0 ? 1 : 0) + (cell.Y >= 0 ? 2 : 0) == quadrant).Count() < 8)
            {
                return default;
            }
        }

        double span = Math.Abs(coefficients[1]) + Math.Abs(coefficients[2]);
        return !double.IsFinite(span) || !double.IsFinite(scatter) || span < Math.Max(1e-8, 2 * scatter)
            ? default
            : new(coefficients[1] * 0.8, coefficients[2] * 0.8);
    }

    private static double EstimateNoise(byte[] bgra, int width, int height)
    {
        int count = width * height, step = Math.Max(1, (int)Math.Ceiling(count / 65536d));
        var differences = new List<double>();
        for (int p = 0; p < count; p += step)
        {
            if (p % width == width - 1 || bgra[p * 4 + 3] == 0 || bgra[(p + 1) * 4 + 3] == 0)
            {
                continue;
            }

            int i = p * 4;
            differences.Add(0.2126 * (bgra[i + 6] - bgra[i + 2]) + 0.7152 * (bgra[i + 5] - bgra[i + 1]) + 0.0722 * (bgra[i + 4] - bgra[i]));
        }

        double median = Median(differences), sigma = 1.4826 * Median(differences.Select(v => Math.Abs(v - median)).ToList()) / Math.Sqrt(2);
        return double.IsFinite(sigma) ? sigma : 0;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5 : values[middle];
    }

    private static double FiniteUnit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static byte ToByte(double value) => (byte)Math.Round(FiniteUnit(value) * 255);

    private readonly ref struct SampleReader
    {
        // Read scientific samples in their original layout and byte order. Normalization
        // is for the preview only; the source buffer remains read-only.
        private readonly ReadOnlySpan<byte> _data;
        private readonly int _pixels, _size;
        private readonly bool _planar, _big;
        private readonly XisfSampleFormat _format;
        private readonly double _lower, _range;

        public int Channels { get; }

        public SampleReader(XisfImageData source)
        {
            _data = source.RawData;
            _pixels = checked(source.Dimensions[0] * source.Dimensions[1]);
            Channels = source.ChannelCount;
            _planar = source.PixelStorage == XisfPixelLayout.Planar;
            _big = source.IsBigEndian;
            _format = source.SampleFormat;
            _size = _format switch
            {
                XisfSampleFormat.UInt8 => 1,
                XisfSampleFormat.UInt16 => 2,
                XisfSampleFormat.UInt32 or XisfSampleFormat.Float32 => 4,
                _ => 8
            };
            _lower = source.LowerBound ?? 0;
            double upper = source.UpperBound ?? (_format switch
            {
                XisfSampleFormat.UInt8 => byte.MaxValue,
                XisfSampleFormat.UInt16 => ushort.MaxValue,
                XisfSampleFormat.UInt32 => uint.MaxValue,
                XisfSampleFormat.UInt64 => ulong.MaxValue,
                _ => 1
            });
            _range = upper - _lower;
            if (!double.IsFinite(_lower) || !double.IsFinite(_range) || _range <= 0)
            {
                throw new NotSupportedException("Finishing requires finite increasing sample bounds.");
            }
        }

        public double Read(int pixel, int channel)
        {
            long sample = _planar ? (long)channel * _pixels + pixel : (long)pixel * Channels + channel;
            var bytes = _data.Slice(checked((int)(sample * _size)), _size);
            double value = _format switch
            {
                XisfSampleFormat.UInt8 => bytes[0],
                XisfSampleFormat.UInt16 => _big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                XisfSampleFormat.UInt32 => _big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                XisfSampleFormat.UInt64 => _big ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                XisfSampleFormat.Float32 => _big ? BinaryPrimitives.ReadSingleBigEndian(bytes) : BinaryPrimitives.ReadSingleLittleEndian(bytes),
                _ => _big ? BinaryPrimitives.ReadDoubleBigEndian(bytes) : BinaryPrimitives.ReadDoubleLittleEndian(bytes)
            };
            return (value - _lower) / _range;
        }
    }
}
