using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SkyBrowser.Core.Astronomy;

public sealed record TargetDistanceEstimate(DeepSkyTarget Target, double LightYears, string SourceValue, string SourceUrl, string AsOf,
    string SourceName, string Method, string Reference, double? MinusErrorLightYears, double? PlusErrorLightYears)
{
    public override string ToString() => Target.Name + " · " + SourceValue;

    public string SourceDetails => $"{SourceName}\nSnapshot: {AsOf}\nMethod: {Method}" +
        (Reference.Length > 0 ? $"\nLiterature reference: {Reference}" : "") +
        (MinusErrorLightYears is { } minus ? $"\nReported lower error: −{TargetDistances.FormatApproximate(minus).TrimStart('≈')} light-years" : "") +
        (PlusErrorLightYears is { } plus ? $"\nReported upper error: +{TargetDistances.FormatApproximate(plus).TrimStart('≈')} light-years" : "") +
        (MinusErrorLightYears is null && PlusErrorLightYears is null ? "\nNo error bars supplied by this source." : "") +
        "\nSeparation uncertainty is not propagated.\n" + SourceUrl;
}

public sealed record TargetDistanceCoverage(int Available, int Total);

/// <summary>Bundled published estimates, cross-identified through the offline position catalogue.</summary>
public static class TargetDistances
{
    private sealed record Catalogue(TargetDistanceEstimate[] Entries, FrozenDictionary<string, TargetDistanceEstimate> ById);
    private static readonly Catalogue Data = Load();

    public static IReadOnlyList<TargetDistanceEstimate> All => Array.AsReadOnly(Data.Entries);

    public static TargetDistanceEstimate? Find(string designation) => DeepSkyCatalog.Find(designation) is { } target
        ? Data.ById.GetValueOrDefault(target.Id) : null;

    public static IReadOnlyList<TargetDistanceEstimate> Search(string query, TargetCatalog catalog = TargetCatalog.All) => DeepSkyCatalog.Search(query, catalog)
        .Select(target => Data.ById.GetValueOrDefault(target.Id))
        .OfType<TargetDistanceEstimate>().ToArray();

    public static TargetDistanceCoverage Coverage(TargetCatalog catalog) => new(
        Data.Entries.Count(entry => catalog == TargetCatalog.All || (entry.Target.Catalogs & catalog) == catalog),
        DeepSkyCatalog.Targets.Count(target => catalog == TargetCatalog.All || (target.Catalogs & catalog) == catalog));

    public static SkyMeasurement Measure(string first, string second)
    {
        var a = Find(first) ?? throw new ArgumentException("The first target has no bundled distance estimate.");
        var b = Find(second) ?? throw new ArgumentException("The second target has no bundled distance estimate.");
        return new(a.Target.Position, b.Target.Position)
        {
            StartDistanceLightYears = a.LightYears,
            EndDistanceLightYears = b.LightYears,
            StartDistanceSource = a,
            EndDistanceSource = b
        };
    }

    public static string FormatApproximate(double lightYears)
    {
        // Two significant figures are a presentation limit, not a claimed uncertainty.
        double rounded = double.Parse(lightYears.ToString("G2", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return "≈" + rounded.ToString(rounded >= 1 ? "#,0" : "G2", CultureInfo.InvariantCulture);
    }

    private static Catalogue Load()
    {
        using var stream = typeof(TargetDistances).Assembly.GetManifestResourceStream("SkyBrowser.Core.Data.target-distances.json")
            ?? throw new InvalidOperationException("Missing embedded target distances.");
        var document = JsonSerializer.Deserialize(stream, DistanceJsonContext.Default.TargetDistanceDocument)
            ?? throw new InvalidDataException("The target distance catalogue is empty.");
        if (document.Version != 2 || !DateOnly.TryParseExact(document.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidDataException("Unsupported target distance catalogue.");
        }
        List<TargetDistanceEstimate> entries = [];
        foreach (var entry in document.Targets)
        {
            var target = DeepSkyCatalog.Find(entry.Designation) ?? throw new InvalidDataException("Unknown target distance identity.");
            SkyGeometry.ValidateObjectDistance(entry.LightYears);
            if (string.IsNullOrWhiteSpace(entry.SourceValue) || !Uri.TryCreate(entry.SourceUrl, UriKind.Absolute, out var source) ||
                source.Scheme != "https" || !ValidSource(entry, target, source) ||
                string.IsNullOrWhiteSpace(entry.SourceName) || string.IsNullOrWhiteSpace(entry.Method) ||
                entry.SourceSha256.Length != 64 || entry.SourceSha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("Invalid target distance provenance.");
            }
            foreach (var error in new[] { entry.MinusErrorLightYears, entry.PlusErrorLightYears })
            {
                if (error is { } value && (!double.IsFinite(value) || value <= 0 || value >= entry.LightYears))
                {
                    throw new InvalidDataException("Invalid published distance uncertainty.");
                }
            }
            entries.Add(new(target, entry.LightYears, entry.SourceValue, entry.SourceUrl, document.AsOf,
                entry.SourceName, entry.Method, entry.Reference, entry.MinusErrorLightYears, entry.PlusErrorLightYears));
        }
        return new(entries.ToArray(), entries.ToFrozenDictionary(entry => entry.Target.Id));
    }

    private static bool ValidSource(TargetDistanceEntry entry, DeepSkyTarget target, Uri source)
    {
        if (DeepSkyCatalog.Find(entry.SourceDesignation)?.Id != target.Id) { return false; }
        return entry.SourceKind switch
        {
            "NASA" => source.Host == "science.nasa.gov" && entry.SourceDesignation.Length > 1 &&
                entry.SourceDesignation[0] is 'M' or 'C' &&
                (source.AbsolutePath == "/mission/hubble/science/explore-the-night-sky/hubble-" +
                    (entry.SourceDesignation[0] == 'M' ? "messier-catalog/messier-" : "caldwell-catalog/caldwell-") + entry.SourceDesignation[1..] + "/" ||
                 entry.SourceDesignation is "C60" or "C61" && source.AbsolutePath == "/mission/hubble/science/explore-the-night-sky/hubble-caldwell-catalog/caldwell-60-61/"),
            "SIMBAD" => source.Host == "simbad.cds.unistra.fr" && source.AbsolutePath == "/simbad/sim-id" &&
                source.Query == "?Ident=" + Uri.EscapeDataString(entry.SourceDesignation) && entry.Reference.Length >= 19 &&
                int.TryParse(entry.Reference[..4], out int year) && year is >= 1800 and <= 2100,
            "NASA_APOD" => source.Host == "apod.nasa.gov" && Regex.IsMatch(source.AbsolutePath, @"^/apod/ap\d{6}\.html$"),
            _ => false
        };
    }
}

internal sealed record TargetDistanceDocument(int Version, string AsOf, TargetDistanceEntry[] Targets);
internal sealed record TargetDistanceEntry(string Designation, double LightYears, string SourceValue, string SourceUrl, string SourceSha256,
    string SourceDesignation, string SourceName, string SourceKind, string Method, string Reference, double? MinusErrorLightYears, double? PlusErrorLightYears);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TargetDistanceDocument))]
internal partial class DistanceJsonContext : JsonSerializerContext;
