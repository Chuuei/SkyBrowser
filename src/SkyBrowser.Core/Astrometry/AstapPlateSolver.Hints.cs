using System.Globalization;
using SkyBrowser.Core.Astronomy;
using XISFLib;

namespace SkyBrowser.Core.Astrometry;

public sealed partial class AstapPlateSolver
{
    private sealed record SolverInputHints(SkyCoordinate? Position, double? FieldHeightDegrees);

    /// <summary>
    /// Reads an approximate search origin from native XISF center coordinates or
    /// paired FITS RA/DEC, OBJCTRA/OBJCTDEC or equatorial WCS reference values.
    /// Numeric RA is in degrees except OBJCTRA (hours); sexagesimal RA is in hours.
    /// Returns null for absent, incomplete or invalid hints. This is not a plate solution.
    /// </summary>
    public static SkyCoordinate? ReadPositionHint(XisfImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Properties.TryGetValue(XisfKnownProperties.Observation.Center.RA, out double ra) &&
            image.Properties.TryGetValue(XisfKnownProperties.Observation.Center.Dec, out double dec) &&
            Position(ra, dec) is { } native)
        {
            return native;
        }

        string? Value(string name) => FitsHintValue(image, name);

        SkyCoordinate? Pair(string raKey, string decKey, bool raHours = false)
        {
            return Angle(Value(raKey), isRa: true, raHours) is { } rightAscension &&
                Angle(Value(decKey), isRa: false) is { } declination
                ? Position(rightAscension, declination) : null;
        }

        if ((Pair("RA", "DEC") ?? Pair("OBJCTRA", "OBJCTDEC", raHours: true)) is { } pointing)
        {
            return pointing;
        }

        // CRVAL is not necessarily equatorial; require matching RA/Dec axis labels.
        bool DegreeUnit(string key) => Value(key) is null or "" or "deg";
        if (!DegreeUnit("CUNIT1") || !DegreeUnit("CUNIT2"))
        {
            return null;
        }
        string? firstAxis = Value("CTYPE1"), secondAxis = Value("CTYPE2");
        if (firstAxis?.StartsWith("RA---", StringComparison.OrdinalIgnoreCase) == true &&
            secondAxis?.StartsWith("DEC--", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Pair("CRVAL1", "CRVAL2");
        }
        if (firstAxis?.StartsWith("DEC--", StringComparison.OrdinalIgnoreCase) == true &&
            secondAxis?.StartsWith("RA---", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Pair("CRVAL2", "CRVAL1");
        }
        return null;
    }

    /// <summary>
    /// Estimates rectilinear field height in degrees from stored rows, pixel size
    /// and focal length. FITS YPIXSZ is in microns including binning, FOCALLEN in mm;
    /// native XISF focal length is in metres and its sensor pixels are unbinned.
    /// Returns null for missing or invalid metadata; edited images may retain stale
    /// instrument values, so this estimate is used only in the nearby attempt.
    /// </summary>
    public static double? ReadFieldHeightHint(XisfImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Dimensions.Count != 2 || image.Dimensions[1] < 1)
        {
            return null;
        }

        double? Field(double pixelMicrons, double focalLengthMm)
        {
            if (!double.IsFinite(pixelMicrons) || pixelMicrons <= 0 || !double.IsFinite(focalLengthMm) || focalLengthMm <= 0)
            {
                return null;
            }
            double field = 2 * Math.Atan(image.Dimensions[1] * pixelMicrons / 1000 / (2 * focalLengthMm)) / SkyCoordinate.Radians;
            return field is >= .01 and <= 160 ? field : null;
        }

        if (image.Properties.TryGetValue(XisfKnownProperties.Instrument.Sensor.YPixelSize, out float pixel) &&
            image.Properties.TryGetValue(XisfKnownProperties.Instrument.Telescope.FocalLength, out float focalLength))
        {
            int binning = 1;
            bool validBinning = !image.Properties.ContainsKey(XisfKnownProperties.Instrument.Camera.YBinning) ||
                (image.Properties.TryGetValue(XisfKnownProperties.Instrument.Camera.YBinning, out binning) && binning > 0);
            if (validBinning && Field((double)pixel * binning, (double)focalLength * 1000) is { } native)
            {
                return native;
            }
        }
        return TryNumber(FitsHintValue(image, "YPIXSZ"), out double pixelSize) &&
            TryNumber(FitsHintValue(image, "FOCALLEN"), out double focalMm) ? Field(pixelSize, focalMm) : null;
    }

    private static string? FitsHintValue(XisfImageData image, string name)
    {
        var matches = image.FitsKeywords.Where(keyword => keyword.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length != 1)
        {
            return null;
        }
        string value = matches[0].Value.Trim();
        return value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1].Replace("''", "'", StringComparison.Ordinal).Trim() : value;
    }

    private static bool TryNumber(string? value, out double number) => double.TryParse(value?.Replace('D', 'E').Replace('d', 'E'),
        NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static SkyCoordinate? Position(double ra, double dec) =>
        double.IsFinite(ra) && ra is >= 0 and <= 360 && double.IsFinite(dec) && dec is >= -90 and <= 90
            ? new(ra == 360 ? 0 : ra, dec) : null;

    private static double? Angle(string? text, bool isRa, bool raHours = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string[] parts = text.Contains(':') ? text.Split(':', StringSplitOptions.TrimEntries)
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        double angle;
        if (parts.Length == 1 && TryNumber(parts[0], out double scalar))
        {
            angle = scalar * (isRa && raHours ? 15 : 1);
        }
        else if (parts.Length == 3 && TryNumber(parts[0], out double major) &&
            TryNumber(parts[1], out double minutes) && TryNumber(parts[2], out double seconds) &&
            major == Math.Truncate(major) && minutes == Math.Truncate(minutes) &&
            minutes is >= 0 and < 60 && seconds is >= 0 and < 60 && (!isRa || !text.StartsWith('-')))
        {
            angle = (Math.Abs(major) + minutes / 60 + seconds / 3600) * (text.StartsWith('-') ? -1 : 1) * (isRa ? 15 : 1);
        }
        else
        {
            return null;
        }
        return isRa ? angle is >= 0 and <= 360 ? angle : null : angle is >= -90 and <= 90 ? angle : null;
    }
}
