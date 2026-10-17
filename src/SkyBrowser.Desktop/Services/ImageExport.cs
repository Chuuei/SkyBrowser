using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop.Services;

public sealed record ImageExportOptions
{
    public bool SelectedPreview { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public bool Labels { get; init; } = true;
    public bool Outlines { get; init; } = true;
    public bool Grid { get; init; }
    public bool Coordinates { get; init; } = true;
    public bool Jpeg { get; init; }
    public int JpegQuality { get; init; } = 92;

    public void Validate()
    {
        if (Width is < 128 or > 8192 || Height is < 128 or > 8192 || (long)Width * Height > 32_000_000)
        {
            throw new ArgumentException("Choose dimensions from 128 to 8192 pixels, up to 32 million pixels in total.");
        }
        if (JpegQuality is < 1 or > 100)
        {
            throw new ArgumentException("JPEG quality must be from 1 to 100.");
        }
    }
}

public static class ImageExport
{
    public static BitmapSource Preview(DisplayPixels pixels)
    {
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Bgra, checked(pixels.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource WithCoordinates(BitmapSource source, string coordinates)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new(0, 0, source.PixelWidth, source.PixelHeight));
            double fontSize = Math.Clamp(source.PixelWidth / 95d, 10, 24);
            var text = new FormattedText(coordinates, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, Brushes.White, 1)
            {
                MaxTextWidth = Math.Max(1, source.PixelWidth - 24), Trimming = TextTrimming.CharacterEllipsis
            };
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 8, 14, 22)), null,
                new(0, source.PixelHeight - text.Height - 18, source.PixelWidth, text.Height + 18));
            context.DrawText(text, new(12, source.PixelHeight - text.Height - 9));
        }
        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    public static void Save(BitmapSource source, string destination, IEnumerable<string> sources,
        int jpegQuality = 92, bool overwrite = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
        {
            throw new NotSupportedException("Export as PNG or JPEG.");
        }
        if (jpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(jpegQuality));
        }
        string[] originals = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ImageExportDestination.Validate(destination, originals, token);
        if (!overwrite && File.Exists(destination))
        {
            throw new IOException("The destination already exists. Choose a new filename.");
        }
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".sky-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            BitmapEncoder encoder = extension == ".png" ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = jpegQuality };
            BitmapSource bitmap = extension == ".png" ? source : Flatten(source, token);
            // Construct a new frame without source EXIF, paths, location or private metadata.
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(output);
            }
            token.ThrowIfCancellationRequested();
            ImageExportDestination.Validate(destination, originals, token);
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static BitmapSource Flatten(BitmapSource source, CancellationToken token)
    {
        var straight = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(source.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * source.PixelHeight)];
        straight.CopyPixels(pixels, stride, 0);
        for (int y = 0; y < source.PixelHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < source.PixelWidth; x++)
            {
                int p = y * stride + x * 4, alpha = pixels[p + 3];
                pixels[p] = (byte)((pixels[p] * alpha + 22 * (255 - alpha) + 127) / 255);
                pixels[p + 1] = (byte)((pixels[p + 1] * alpha + 14 * (255 - alpha) + 127) / 255);
                pixels[p + 2] = (byte)((pixels[p + 2] * alpha + 8 * (255 - alpha) + 127) / 255);
                pixels[p + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
