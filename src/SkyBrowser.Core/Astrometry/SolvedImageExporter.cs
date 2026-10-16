using System.Text.RegularExpressions;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using XISFLib;

namespace SkyBrowser.Core.Astrometry;

public sealed record SolvedImageExportResult(string Path, string? Warning);

/// <summary>Writes scientific source data with a source-bound ASTAP solution to a separate file.</summary>
public sealed partial class SolvedImageExporter(ImageLibrary library)
{
    [GeneratedRegex("\\A(?:(?:CRPIX|CRVAL|CTYPE|CUNIT|CDELT|CROTA)\\d+|(?:CD|PC|PV|PS)\\d+_\\d+|(?:A|B|AP|BP)_(?:ORDER|DMAX|\\d+_\\d+)|WCSAXES|WCSDIM|RADESYS|RADECSYS|EQUINOX|LONPOLE|LATPOLE|PLTSOLVD)[A-Z]?\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SolutionKeyword();

    public SolvedImageExportResult Export(LoadedSkyImage image, string destination, bool overwrite = false,
        CancellationToken token = default, IProgress<ImageProcessingProgress>? progress = null)
    {
        destination = Path.GetFullPath(destination);
        bool fitsOutput = IsFits(destination);
        if (!fitsOutput && !Path.GetExtension(destination).Equals(".xisf", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Save a solved copy as .fits, .fit, .fts or .xisf.");
        }
        token.ThrowIfCancellationRequested();
        var snapshot = SourceSnapshot.Capture(image.SourcePath, token);
        if (snapshot.Files.Any(file => string.Equals(file.Path, destination, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("Choose a different filename. A solved copy cannot replace its source or XISF sidecars.");
        }
        if (!overwrite && File.Exists(destination))
        {
            throw new IOException("The destination already exists. Choose a new filename.");
        }
        var placement = library.SolutionForExport(image, snapshot);
        var solution = RecoverLinearSolution(placement, image.Width, image.Height);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".solved-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            progress?.Report(new("Reading original scientific pixels"));
            FitsDocument? fits = null;
            XisfDocumentData? xisf = null;
            List<ConversionFinding> findings = [];
            if (IsFits(image.SourcePath))
            {
                fits = FitsReader.Load(image.SourcePath);
                if (!fitsOutput)
                {
                    var conversion = FitsConverter.ConvertToXisf(fits, new()
                    {
                        AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
                        LossPolicy = ConversionLossPolicy.Allow
                    });
                    findings.AddRange(conversion.Findings);
                    xisf = new();
                    foreach (var source in conversion.Value)
                    {
                        xisf.Images.Add(source);
                    }
                }
            }
            else
            {
                xisf = XisfDocumentData.FromDocument(XisfReader.Load(image.SourcePath));
            }
            token.ThrowIfCancellationRequested();
            if (xisf is not null)
            {
                var selected = xisf.Images[image.ImageIndex];
                ValidateDimensions(selected.Dimensions, image);
                foreach (string key in selected.Properties.Keys.Where(key => key.StartsWith("PCL:AstrometricSolution:", StringComparison.Ordinal)).ToArray())
                {
                    selected.Properties.Remove(key);
                }
                foreach (var keyword in selected.FitsKeywords.Where(keyword => SolutionKeyword().IsMatch(keyword.Name)).ToArray())
                {
                    selected.FitsKeywords.Remove(keyword);
                }
                foreach (var card in solution.Header)
                {
                    selected.FitsKeywords.Add(XisfFitsKeyword.FromValue(card.Keyword, card.Value!));
                }
                solution.Native.ApplyTo(selected);
                if (fitsOutput)
                {
                    // Native CFA metadata has no FITS equivalent; retain the actual stored tile
                    // as BAYERPAT, without debayering or otherwise changing scientific samples.
                    if (selected.ColorFilterArray is not null && SkyImageDebayer.Detect(selected).Pattern is { } pattern)
                    {
                        foreach (var keyword in selected.FitsKeywords.Where(keyword => keyword.Name.ToUpperInvariant() is
                            "BAYERPAT" or "XBAYROFF" or "YBAYROFF" or "XBAYEROFF" or "YBAYEROFF" or "ROWORDER").ToArray())
                        {
                            selected.FitsKeywords.Remove(keyword);
                        }
                        selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", pattern.ToString()));
                        selected.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", "TOP-DOWN"));
                    }
                    var conversion = FitsConverter.ConvertToFits(xisf, ConversionLossPolicy.Allow);
                    fits = conversion.Value;
                    findings.AddRange(conversion.Findings);
                }
            }
            if (fitsOutput)
            {
                var hdu = fits!.Hdus.Where(hdu => hdu.Content is FitsImage).ElementAt(image.ImageIndex);
                ValidateDimensions(((FitsImage)hdu.Content!).Dimensions, image);
                foreach (var card in hdu.Header.Where(card => SolutionKeyword().IsMatch(card.Keyword)).ToArray())
                {
                    hdu.Header.Remove(card);
                }
                foreach (var card in solution.Header)
                {
                    hdu.Header.Set(card.Keyword, card.Value, card.Comment);
                }
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Writing solved scientific copy"));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (fitsOutput)
                {
                    FitsWriter.Write(stream, fits!);
                }
                else
                {
                    XisfWriter.Write(stream, xisf!);
                }
            }
            token.ThrowIfCancellationRequested();
            if (!snapshot.IsCurrent(token))
            {
                throw new IOException("The source changed while saving. Reload and solve the current image again.");
            }
            File.Move(temporary, destination, overwrite);
            string[] losses = findings.Where(finding => finding.Effect == ConversionEffect.Omitted)
                .Select(finding => finding.Message).Distinct().ToArray();
            return new(destination, losses.Length == 0 ? null : "Format conversion: " + string.Join(" ", losses));
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool IsFits(string path) => Path.GetExtension(path).ToLowerInvariant() is ".fits" or ".fit" or ".fts";

    private static void ValidateDimensions(IReadOnlyList<int> dimensions, LoadedSkyImage image)
    {
        if (dimensions.Count < 2 || dimensions[0] != image.Width || dimensions[1] != image.Height)
        {
            throw new IOException("The source dimensions changed. Reload and solve the image again.");
        }
    }

    private sealed record LinearSolution(FitsHeader Header, XisfAstrometricSolution Native);

    private static LinearSolution RecoverLinearSolution(ImagePlacement placement, int width, int height)
    {
        // Library solutions retain exact sky samples at the 33 x 33 mesh vertices.
        // Recover ASTAP's centered TAN plane from those vertices, never from the
        // interpolated preview or rounded field-size labels. Validate every vertex.
        var center = placement.Center;
        var forward = center.Vector;
        var north = center.North;
        var east = SkyVector.Cross(north, forward);
        (double X, double Y) Project(SkyCoordinate sky)
        {
            var vector = sky.Vector;
            double divisor = SkyVector.Dot(vector, forward) * SkyCoordinate.Radians;
            if (divisor <= 0)
            {
                throw new InvalidDataException("The saved solution is outside a TAN projection.");
            }
            return (SkyVector.Dot(vector, east) / divisor, SkyVector.Dot(vector, north) / divisor);
        }
        var left = Project(placement.At(0, 0.5));
        var right = Project(placement.At(1, 0.5));
        var top = Project(placement.At(0.5, 0));
        var bottom = Project(placement.At(0.5, 1));
        double a = (right.X - left.X) / width, b = (bottom.X - top.X) / height;
        double c = (right.Y - left.Y) / width, d = (bottom.Y - top.Y) / height;
        var native = XisfAstrometricSolution.CreateLinear("Gnomonic", [center.RightAscension, center.Declination],
            [width / 2.0, height / 2.0], new double[,] { { a, b }, { c, d } });
        var restored = ImagePlacement.FromNative(native, width, height);
        var grid = placement.CaptureSampleGrid();
        for (int y = 0; y <= 32; y++)
        {
            for (int x = 0; x <= 32; x++)
            {
                if (restored.At(x / 32.0, y / 32.0).DistanceTo(grid[y * 33 + x]) > 1e-8)
                {
                    throw new InvalidDataException("The saved solution cannot be exported as an exact linear TAN solution. Blind solve this image again.");
                }
            }
        }
        FitsHeader header = new();
        header.Set("WCSAXES", 2);
        header.Set("CTYPE1", "RA---TAN");
        header.Set("CTYPE2", "DEC--TAN");
        header.Set("CUNIT1", "deg");
        header.Set("CUNIT2", "deg");
        header.Set("RADESYS", "ICRS");
        header.Set("EQUINOX", 2000.0);
        // FITS centers are one-based; XISF centers are half-integers.
        header.Set("CRPIX1", (width + 1) / 2.0);
        header.Set("CRPIX2", (height + 1) / 2.0);
        header.Set("CRVAL1", center.RightAscension);
        header.Set("CRVAL2", center.Declination);
        header.Set("CD1_1", a);
        header.Set("CD1_2", b);
        header.Set("CD2_1", c);
        header.Set("CD2_2", d);
        header.Set("PLTSOLVD", true);
        return new(header, native);
    }
}
