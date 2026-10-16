using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBrowser.Core.Astronomy;

[Flags]
public enum TargetCatalog
{
    All = 0,
    Messier = 1,
    Caldwell = 2,
    Ngc = 4,
    Ic = 8
}

public sealed record DeepSkyTarget(
    string Id,
    string[] Designations,
    string[] CommonNames,
    SkyCoordinate Position,
    string TypeCode,
    string Constellation,
    double? MajorArcMinutes,
    double? MinorArcMinutes,
    double? PositionAngleDegrees,
    double? Magnitude,
    string? MagnitudeBand,
    string? Note
)
{
    public string PreferredDesignation =>
        Designations.FirstOrDefault(id => id.StartsWith('M') && id.Length > 1 && char.IsDigit(id[1])) ??
        Designations.FirstOrDefault(id => id.StartsWith('C') && id.Length > 1 && char.IsDigit(id[1])) ??
        Id;

    public string Name => $"{FormatId(PreferredDesignation)}{(CommonNames.FirstOrDefault() is { Length: > 0 } name
        ? $" · {name}"
        : PreferredDesignation != Id
            ? $" · {FormatId(Id)}"
            : "")}";

    public string AliasText => string.Join(" · ", Designations.Select(FormatId));

    public string TypeName => TypeCode switch
    {
        "G" => "Galaxy",
        "GPair" => "Galaxy pair",
        "GTrpl" => "Galaxy triplet",
        "GGroup" => "Galaxy group",
        "OCl" => "Open cluster",
        "GCl" => "Globular cluster",
        "Cl+N" => "Cluster + nebula",
        "*Ass" => "Stellar association",
        "PN" => "Planetary nebula",
        "HII" => "Emission nebula",
        "EmN" => "Emission nebula",
        "RfN" => "Reflection nebula",
        "DrkN" => "Dark nebula",
        "Neb" => "Nebula",
        "SNR" => "Supernova remnant",
        "*" => "Star",
        "**" => "Double star",
        "Nova" => "Nova",
        _ => "Other object"
    };

    public string Summary => $"{AliasText} · {TypeName} · {Constellation}";

    public string Details =>
        $"{Summary}\nJ2000: {Position.RaText}  {Position.DecText}" +
        (MajorArcMinutes is { } size
            ? $"\nSize: {size:0.##}′{(MinorArcMinutes is { } minor ? $" × {minor:0.##}′" : "")}"
            : "") +
        (Magnitude is { } magnitude ? $"\nMagnitude: {magnitude:0.##} ({MagnitudeBand})" : "") +
        (CommonNames.Length > 1 ? $"\nNames: {string.Join(", ", CommonNames)}" : "") +
        (Note is { Length: > 0 } ? $"\n{Note}" : "");

    public double SuggestedFieldOfView => Math.Clamp(Math.Max(MajorArcMinutes ?? 0, MinorArcMinutes ?? 0) / 60 * 3, 1.5, 30);

    public TargetCatalog Catalogs => Designations.Aggregate(
        TargetCatalog.All,
        (catalogs, id) => catalogs | (id switch
        {
            _ when id.StartsWith("NGC", StringComparison.Ordinal) => TargetCatalog.Ngc,
            _ when id.StartsWith("IC", StringComparison.Ordinal) => TargetCatalog.Ic,
            _ when id.Length > 1 && id[0] == 'M' && char.IsDigit(id[1]) => TargetCatalog.Messier,
            _ when id.Length > 1 && id[0] == 'C' && char.IsDigit(id[1]) => TargetCatalog.Caldwell,
            _ => TargetCatalog.All
        }));

    private static string FormatId(string id) => id.StartsWith("NGC", StringComparison.Ordinal)
        ? "NGC " + id[3..]
        : id.StartsWith("IC", StringComparison.Ordinal) ? "IC " + id[2..] : id;

    public override string ToString() => Name;
}

/// <summary>Offline, cross-identified J2000 targets. A small curated set supplies default sky labels.</summary>
public static class DeepSkyCatalog
{
    private sealed record SearchEntry(DeepSkyTarget Target, string Text, TargetCatalog Catalogs, int Order);

    private sealed record CatalogData(DeepSkyTarget[] Targets, FrozenDictionary<string, DeepSkyTarget> Aliases, SearchEntry[] Index);

    private static readonly CatalogData Data = Load();

    public static IReadOnlyList<DeepSkyTarget> Targets => Data.Targets;

    public static IReadOnlyList<DeepSkyTarget> PopularTargets { get; } = new[]
    {
        "M31",
        "M42",
        "M45",
        "M51",
        "M81",
        "M82",
        "IC434",
        "C20",
        "M8",
        "M16",
        "M33",
        "C14",
        "M13",
        "C65",
        "M1",
        "M27",
        "M57",
        "M20",
        "M17",
        "M101",
        "C49",
        "C63",
        "C92",
        "C80",
        "C77",
        "C106",
        "IC1396",
        "IC1805",
        "IC1848",
        "NGC1499"
    }
        .Select(id => Find(id) ?? throw new InvalidDataException($"Missing prominent target {id}."))
        .ToArray();

    public static DeepSkyTarget? Find(string designation) => Data.Aliases.GetValueOrDefault(CanonicalIdentifier(designation));

    public static IReadOnlyList<DeepSkyTarget> Search(string query, TargetCatalog catalog = TargetCatalog.All)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .ToArray();
        List<string> terms = [];
        TargetCatalog required = catalog;
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i] switch
            {
                "MESSIER" => "M",
                "CALDWELL" => "C",
                _ => tokens[i]
            };
            var prefix = token switch
            {
                "M" => TargetCatalog.Messier,
                "C" => TargetCatalog.Caldwell,
                "NGC" => TargetCatalog.Ngc,
                "IC" => TargetCatalog.Ic,
                _ => TargetCatalog.All
            };
            if (prefix != TargetCatalog.All)
            {
                if (i + 1 < tokens.Length && char.IsDigit(tokens[i + 1][0]))
                {
                    token += tokens[++i];
                }
                else
                {
                    required |= prefix;
                    continue;
                }
            }

            terms.Add(CanonicalIdentifier(token));
        }

        string exact = CanonicalIdentifier(query);
        return Data.Index.Where(entry => (entry.Catalogs & required) == required &&
            terms.All(term => IsCatalogueIdentifier(term)
            ? entry.Target.Designations.Any(id => id.StartsWith(term, StringComparison.Ordinal))
            : entry.Text.Contains(term, StringComparison.Ordinal)))
            .OrderBy(entry => entry.Target.Designations.Contains(exact, StringComparer.Ordinal) ? 0 : 1)
            .ThenBy(entry => entry.Order)
            .Select(entry => entry.Target)
            .ToArray();
    }

    private static CatalogData Load()
    {
        using var stream = typeof(DeepSkyCatalog).Assembly.GetManifestResourceStream("SkyBrowser.Core.Data.deep-sky.json") ?? throw new InvalidOperationException("Missing embedded deep-sky catalogue.");
        var document = JsonSerializer.Deserialize(stream, TargetJsonContext.Default.DeepSkyCatalogDocument) ?? throw new InvalidDataException("The deep-sky catalogue is empty.");
        if (document.Version != 1)
        {
            throw new InvalidDataException("Unsupported deep-sky catalogue version.");
        }

        var targets = document.Targets.OrderBy(target => SortKey(target.PreferredDesignation), StringComparer.Ordinal).ToArray();
        Dictionary<string, DeepSkyTarget> aliases = new(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!double.IsFinite(target.Position.RightAscension) ||
                target.Position.RightAscension is < 0 or >= 360 ||
                !double.IsFinite(target.Position.Declination) ||
                Math.Abs(target.Position.Declination) > 90 ||
                target.Designations.Length == 0)
            {
                throw new InvalidDataException($"Invalid catalogue position or identifier for {target.Id}.");
            }

            foreach (string id in target.Designations)
            {
                string key = CanonicalIdentifier(id);
                if (aliases.TryGetValue(key, out var existing) && existing.Id != target.Id)
                {
                    throw new InvalidDataException($"Conflicting catalogue identifier {id}.");
                }

                aliases[key] = target;
            }
        }

        var index = targets.Select((target, order) => new SearchEntry(
            target,
            string.Join(
                '\0',
                target.Designations.Concat(target.CommonNames)
                .Append(target.TypeName)
                .Append(target.Constellation)
                .Select(Normalize)
            ),
            target.Catalogs,
            order
        ))
            .ToArray();
        return new(targets, aliases.ToFrozenDictionary(StringComparer.Ordinal), index);
    }

    private static string SortKey(string id)
    {
        int offset = id.StartsWith("NGC", StringComparison.Ordinal) ? 3 : id.StartsWith("IC", StringComparison.Ordinal) ? 2 : 1;
        int order = id.StartsWith('M') && id.Length > 1 && char.IsDigit(id[1])
            ? 0
            : id.StartsWith('C') && id.Length > 1 && char.IsDigit(id[1])
            ? 1
            : id.StartsWith("NGC", StringComparison.Ordinal) ? 2 : id.StartsWith("IC", StringComparison.Ordinal) ? 3 : 4;
        int end = offset;
        while (end < id.Length && char.IsDigit(id[end]))
        {
            end++;
        }

        return end > offset && int.TryParse(id.AsSpan(offset, end - offset), out int number)
            ? $"{order}{number:D6}{id[end..]}"
            : $"4{id}";
    }

    private static string CanonicalIdentifier(string text)
    {
        string value = Normalize(text);
        if (value.StartsWith("MESSIER", StringComparison.Ordinal))
        {
            value = "M" + value[7..];
        }

        if (value.StartsWith("CALDWELL", StringComparison.Ordinal))
        {
            value = "C" + value[8..];
        }

        int prefix = value.StartsWith("NGC", StringComparison.Ordinal)
            ? 3
            : value.StartsWith("IC", StringComparison.Ordinal)
            ? 2
            : value.Length > 0 && value[0] is 'M' or 'C' or 'B' ? 1 : 0;
        if (prefix == 0)
        {
            return value;
        }

        int start = prefix;
        while (start + 1 < value.Length && value[start] == '0' && char.IsDigit(value[start + 1]))
        {
            start++;
        }

        return value[..prefix] + value[start..];
    }

    private static bool IsCatalogueIdentifier(string text)
    {
        int prefix = text.StartsWith("NGC", StringComparison.Ordinal)
            ? 3
            : text.StartsWith("IC", StringComparison.Ordinal) ? 2 : text.Length > 0 && text[0] is 'M' or 'C' ? 1 : 0;
        return prefix > 0 && text.Length > prefix && char.IsDigit(text[prefix]);
    }

    private static string Normalize(string text)
    {
        string normalized;
        try
        {
            normalized = text.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            normalized = text;
        }

        StringBuilder result = new(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                result.Append(Rune.ToUpperInvariant(rune));
            }
        }

        return result.ToString();
    }
}

internal sealed record DeepSkyCatalogDocument(int Version, DeepSkyTarget[] Targets);
[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(DeepSkyCatalogDocument))]
internal partial class TargetJsonContext : JsonSerializerContext;
