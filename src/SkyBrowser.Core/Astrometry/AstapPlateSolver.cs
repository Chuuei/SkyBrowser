using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Core.Astrometry;

public sealed record AstapSolveResult(ImagePlacement Placement, string? Warning);

/// <summary>Runs the user's ASTAP installation against a temporary, linear scientific copy.</summary>
public sealed partial class AstapPlateSolver(ImageLibrary library)
{
    // ASTAP compares its initial search scale with the fitted scale. This advisory does
    // not describe an error in the returned CD matrix, which supplies our image sizing.
    [GeneratedRegex("\\A(?:Warning\\s+)?inexact scale!\\s*Set FOV\\s*=\\s*\\d+(?:[.,]\\d+)?d\\s+or scale\\s*=\\s*\\d+(?:[.,]\\d+)?\"/pix(?:\\s+or FL\\s*=\\s*\\d+mm)?\\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InitialScaleWarning();

    public async Task<AstapSolveResult> SolveAsync(LoadedSkyImage image, ApplicationOptions options,
        CancellationToken token = default, IProgress<ImageProcessingProgress>? progress = null)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(options.AstapExecutablePath) || !File.Exists(options.AstapExecutablePath))
        {
            throw new FileNotFoundException("Choose your ASTAP executable in Options → Plate solving.", options.AstapExecutablePath);
        }
        if (options.AstapDatabaseDirectory.Length > 0 && !Directory.Exists(options.AstapDatabaseDirectory))
        {
            throw new DirectoryNotFoundException("The configured ASTAP star database folder is unavailable.");
        }

        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(Path.GetTempPath(), "SkyBrowser", "solve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            progress?.Report(new("Preparing linear scientific image for ASTAP"));
            var snapshot = await Task.Run(() => SourceSnapshot.Capture(image.SourcePath, token), token).ConfigureAwait(false);
            string input = Path.Combine(directory, "input.fits");
            var hints = await Task.Run(() => WriteInput(image, input, token), token).ConfigureAwait(false);
            string output = await FindSolutionAsync(options, input, directory, hints, token, progress).ConfigureAwait(false);

            progress?.Report(new("Validating ASTAP sky solution"));
            var result = await Task.Run(() => ReadSolution(output + ".ini", image.Width, image.Height), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => library.SaveSolution(image.SourcePath, image.ImageIndex, image.Width, image.Height,
                snapshot, result.Placement, token), token).ConfigureAwait(false);
            return result;
        }
        finally
        {
            // This directory was created uniquely by this invocation; ASTAP never receives the source path.
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // An external scanner may briefly retain a temporary file.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Builds a source-preserving ASTAP invocation; nearby searches use a 5° radius and 1,000 stars.</summary>
    public static ProcessStartInfo CreateStartInfo(ApplicationOptions options, string input, string output,
        SkyCoordinate? positionHint = null, bool nearbySearch = false, double? fieldHeightHint = null)
    {
        if (positionHint is { } hint)
        {
            SkyGeometry.Validate(hint);
        }
        if (nearbySearch && positionHint is null)
        {
            throw new ArgumentException("A nearby search requires a position hint.", nameof(positionHint));
        }
        if (fieldHeightHint is { } field && (!double.IsFinite(field) || field is <= 0 or > 180))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldHeightHint));
        }
        var start = new ProcessStartInfo(options.AstapExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(input))!
        };
        // Explicit user scale takes precedence. Metadata scale is only used in the
        // nearby attempt; the fallback retains the original full-sky/automatic behavior.
        double fieldHeight = options.AstapFieldHeightDegrees > 0 ? options.AstapFieldHeightDegrees
            : nearbySearch ? fieldHeightHint ?? 0 : 0;
        double raHours = positionHint is { } position ? SkyCoordinate.Normalize(position.RightAscension) / 15 : 0;
        double southPoleDistance = (positionHint?.Declination ?? 0) + 90;
        foreach (string argument in new[] { "-f", input, "-o", output, "-r", nearbySearch ? "5" : "180",
            "-ra", raHours.ToString("G17", CultureInfo.InvariantCulture),
            "-spd", southPoleDistance.ToString("G17", CultureInfo.InvariantCulture),
            "-fov", fieldHeight.ToString("G17", CultureInfo.InvariantCulture),
            "-s", nearbySearch ? "1000" : "500", "-t", "0.007", "-speed", "auto", "-z", "0", "-sip", "n" })
        {
            start.ArgumentList.Add(argument);
        }
        if (options.AstapDatabaseDirectory.Length > 0)
        {
            start.ArgumentList.Add("-d");
            start.ArgumentList.Add(options.AstapDatabaseDirectory);
        }
        return start;
    }

    private static async Task<string> ReadTailAsync(StreamReader reader)
    {
        char[] buffer = new char[2048];
        StringBuilder tail = new();
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 8192)
            {
                tail.Remove(0, tail.Length - 8192);
            }
        }
        return tail.ToString();
    }

    public static AstapSolveResult ReadSolution(string iniPath, int width, int height)
    {
        var file = new FileInfo(iniPath);
        if (!file.Exists || file.Length > 1048576)
        {
            throw new InvalidDataException("ASTAP did not produce a valid solution report.");
        }
        var values = File.ReadLines(iniPath).Where(line => line.Contains('=')).Select(line => line.Split('=', 2))
            .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("PLTSOLVD", out string? solved) || !solved.Equals("T", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ASTAP did not find a sky solution. " + values.GetValueOrDefault("ERROR", ""));
        }
        string[] required = ["CRPIX1", "CRPIX2", "CRVAL1", "CRVAL2", "CD1_1", "CD1_2", "CD2_1", "CD2_2"];
        List<XisfFitsKeyword> keywords = [];
        foreach (string key in required)
        {
            if (!values.TryGetValue(key, out string? text) || !double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            {
                throw new InvalidDataException($"ASTAP returned an invalid {key} value.");
            }
            keywords.Add(XisfFitsKeyword.FromValue(key, number));
        }
        keywords.Add(XisfFitsKeyword.FromValue("CTYPE1", "RA---TAN"));
        keywords.Add(XisfFitsKeyword.FromValue("CTYPE2", "DEC--TAN"));
        if (double.Parse(values["CRVAL2"], CultureInfo.InvariantCulture) is < -90 or > 90)
        {
            throw new InvalidDataException("ASTAP returned a declination outside the celestial sphere.");
        }
        if (width < 2 || height < 2 || !CelestialWorldCoordinateSystem.TryCreate(keywords, out var wcs))
        {
            throw new InvalidDataException("ASTAP returned an invalid or singular TAN solution.");
        }
        var placement = ImagePlacement.FromFits(wcs!, width, height);
        // Evaluate the complete mesh before any solution can be persisted or shown.
        var grid = placement.CaptureSampleGrid();
        string? warning = values.GetValueOrDefault("WARNING");
        if (warning is not null)
        {
            // Only dismiss the known advisory after validating the complete solution.
            // Keep other diagnostics, including any appended to the scale advisory.
            warning = InitialScaleWarning().Replace(warning, "").Trim();
        }
        return new(ImagePlacement.FromSampleGrid("ASTAP · blind solve", grid), string.IsNullOrEmpty(warning) ? null : warning);
    }

    private static SolverInputHints WriteInput(LoadedSkyImage selected, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        XisfImageData source;
        if (Path.GetExtension(selected.SourcePath).ToLowerInvariant() is ".fits" or ".fit" or ".fts")
        {
            var images = FitsConverter.ToXisf(FitsReader.Load(selected.SourcePath), new()
            {
                AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
                LossPolicy = ConversionLossPolicy.Allow
            });
            source = images[selected.ImageIndex];
        }
        else
        {
            source = XisfImageData.FromImage(XisfReader.Load(selected.SourcePath).Images[selected.ImageIndex]);
        }
        if (source.Dimensions.Count != 2 || source.Dimensions[0] != selected.Width || source.Dimensions[1] != selected.Height)
        {
            throw new IOException("The source dimensions changed. Reload the image before solving.");
        }
        var hints = new SolverInputHints(ReadPositionHint(source), ReadFieldHeightHint(source));
        var detection = SkyImageDebayer.Detect(source);
        if (detection.Pattern is { } pattern)
        {
            source = SkyImageDebayer.Create(source, pattern, token);
        }
        if (source.SampleFormat is not (XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or XisfSampleFormat.UInt32 or
            XisfSampleFormat.UInt64 or XisfSampleFormat.Float32 or XisfSampleFormat.Float64) ||
            source.ColorSpace is not (null or "Gray" or "RGB"))
        {
            throw new NotSupportedException("ASTAP input requires a real Gray or RGB image.");
        }
        int width = selected.Width, height = selected.Height;
        float[] samples = new float[checked(width * height)];
        int colors = source.ColorSpace == "RGB" ? 3 : 1;
        var reader = new SkyImageFinisher.SampleReader(source);
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                double value = 0;
                for (int c = 0; c < colors; c++)
                {
                    value += reader.Read(p, c) / colors;
                }
                if (source.ChannelCount > colors)
                {
                    value *= Math.Clamp(reader.Read(p, colors), 0, 1);
                }
                samples[p] = double.IsFinite(value) ? (float)value : 0;
            }
        }
        token.ThrowIfCancellationRequested();
        // Only a validated pointing hint is passed separately on the command line.
        // No source metadata, orientation, display function or original path enters this
        // temporary FITS. The full frame and stored rows are retained.
        var gray = XisfImageData.FromSamples(samples, [width, height], 1);
        FitsWriter.Write(destination, FitsConverter.FromXisf([gray], ConversionLossPolicy.Allow));
        token.ThrowIfCancellationRequested();
        return hints;
    }
}
