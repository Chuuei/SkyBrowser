using System.Buffers.Binary;
using System.Globalization;
using XISFLib;

namespace SkyBrowser.Core.Imaging;

/// <summary>Reconstructs preview colors in stored coordinates, before stretching or resizing.</summary>
internal static class SkyImageDebayer
{
    private static bool IsPattern(string pattern) => pattern is "RGGB" or "BGGR" or "GRBG" or "GBRG";

    internal static (string? Pattern, string? Warning) Detect(XisfImageData image)
    {
        // Stale CFA keywords on an already RGB image must never trigger reconstruction.
        if (image.ChannelCount != 1)
        {
            return (null, null);
        }

        string? Text(string name) => image.FitsKeywords
            .FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                k.Name.Equals($"HIERARCH {name}", StringComparison.OrdinalIgnoreCase))?.Value.Trim().Trim('\'').Trim();

        string? pattern = Text("BAYERPAT");
        if (image.ColorFilterArray is null && pattern is null)
        {
            return (null, null);
        }

        const string unsupported = "CFA mosaic displayed as grayscale: unsupported Bayer metadata.";
        if (image.Dimensions.Count != 2 || image.Dimensions[0] < 2 || image.Dimensions[1] < 2 ||
            (image.ColorSpace is not null && !image.ColorSpace.Equals("Gray", StringComparison.OrdinalIgnoreCase)) ||
            image.SampleFormat is not (XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or XisfSampleFormat.UInt32 or
                XisfSampleFormat.UInt64 or XisfSampleFormat.Float32 or XisfSampleFormat.Float64))
        {
            return (null, unsupported);
        }

        // Native XISF CFA describes the stored tile and takes precedence over FITS cards.
        if (image.ColorFilterArray is { } cfa)
        {
            string native = cfa.Pattern.Trim().ToUpperInvariant();
            return cfa.Width == 2 && cfa.Height == 2 && IsPattern(native)
                ? (native, null)
                : (null, unsupported);
        }

        pattern = pattern!.ToUpperInvariant();
        if (!IsPattern(pattern))
        {
            return (null, unsupported);
        }

        bool Offset(string shortName, string longName, out long value)
        {
            string? text = Text(shortName) ?? Text(longName);
            value = 0;
            return text is null || long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        if (!Offset("XBAYROFF", "XBAYEROFF", out long x) || !Offset("YBAYROFF", "YBAYEROFF", out long y))
        {
            return (null, "CFA mosaic displayed as grayscale: invalid Bayer offsets in metadata.");
        }

        string? rowOrder = Text("ROWORDER")?.ToUpperInvariant();
        if (rowOrder is not (null or "TOP-DOWN" or "BOTTOM-UP"))
        {
            return (null, "CFA mosaic displayed as grayscale: unsupported ROWORDER in metadata.");
        }

        // With no ROWORDER, the tile starts at stored (0,0). Explicit BOTTOM-UP maps
        // the sensor's first row to the last stored row; never flip the astrometric pixels.
        int rowParity = (int)(y & 1) ^ (rowOrder == "BOTTOM-UP" ? (image.Dimensions[1] - 1) & 1 : 0);
        string effective = string.Concat(Enumerable.Range(0, 4).Select(i =>
            pattern[(((i / 2) + rowParity) & 1) * 2 + (((i % 2) + (int)(x & 1)) & 1)]));
        return (effective, null);
    }

    // Bilinear interpolation adapted from XISFLib.Net ViewerDebayerService;
    // Copyright 2026 Chuuei and contributors, Apache-2.0.
    internal static XisfImageData Create(XisfImageData source, string pattern, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int width = source.Dimensions[0], height = source.Dimensions[1];
        int size = source.RawData.Length / checked(width * height);
        byte[] output = new byte[checked(source.RawData.Length * 3)];
        bool integer = source.SampleFormat is XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or
            XisfSampleFormat.UInt32 or XisfSampleFormat.UInt64;

        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    char color = "RGB"[c];
                    var destination = output.AsSpan(((y * width + x) * 3 + c) * size, size);
                    if (pattern[(y & 1) * 2 + (x & 1)] == color)
                    {
                        source.RawData.Slice((y * width + x) * size, size).CopyTo(destination);
                        continue;
                    }

                    UInt128 sum = 0;
                    double floating = 0;
                    int count = 0;
                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
                    {
                        for (int xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++)
                        {
                            if (pattern[(yy & 1) * 2 + (xx & 1)] == color)
                            {
                                count++;
                            }
                        }
                    }

                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
                    {
                        for (int xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++)
                        {
                            if (pattern[(yy & 1) * 2 + (xx & 1)] != color)
                            {
                                continue;
                            }

                            ulong bits = Read(source.RawData.Slice((yy * width + xx) * size, size), source.IsBigEndian);
                            if (integer)
                            {
                                // Exact even for UInt64 values above 2^53. Integer means round down.
                                sum += bits;
                            }
                            else
                            {
                                // Divide before summing to avoid overflow of finite floating values.
                                floating += (size == 4 ? BitConverter.Int32BitsToSingle((int)bits) :
                                    BitConverter.Int64BitsToDouble((long)bits)) / count;
                            }
                        }
                    }

                    ulong result = integer ? (ulong)(sum / (uint)count) : size == 4
                        ? (uint)BitConverter.SingleToInt32Bits((float)floating)
                        : (ulong)BitConverter.DoubleToInt64Bits(floating);
                    Write(destination, result, source.IsBigEndian);
                }
            }
        });
        token.ThrowIfCancellationRequested();

        var image = XisfImageData.FromRaw(output, source.Dimensions, 3, source.SampleFormat,
            XisfPixelLayout.Interleaved, source.IsBigEndian);
        image.LowerBound = source.LowerBound;
        image.UpperBound = source.UpperBound;
        image.Offset = source.Offset;
        image.RgbWorkingSpace = source.RgbWorkingSpace;
        image.Orientation = "0";
        if (source.DisplayFunction is { } display)
        {
            // A Gray display function uses component zero. Apply that same transfer to all
            // reconstructed colors instead of the unused green/blue identity parameters.
            static double[] Linked(IReadOnlyList<double> values) => [values[0], values[0], values[0], values[3]];
            image.DisplayFunction = new(Linked(display.Midtones), Linked(display.Shadows), Linked(display.Highlights),
                Linked(display.LowRange), Linked(display.HighRange))
            { Name = display.Name };
        }

        token.ThrowIfCancellationRequested();
        return image;
    }

    private static ulong Read(ReadOnlySpan<byte> bytes, bool big) => bytes.Length switch
    {
        1 => bytes[0],
        2 => big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        4 => big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        _ => big ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes)
    };

    private static void Write(Span<byte> bytes, ulong value, bool big)
    {
        switch (bytes.Length)
        {
            case 1:
                bytes[0] = (byte)value;
                break;
            case 2:
                if (big)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
                }
                break;
            case 4:
                if (big)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
                }
                break;
            default:
                if (big)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
                }
                break;
        }
    }
}
