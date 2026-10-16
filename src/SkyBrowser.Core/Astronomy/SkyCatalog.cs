using System.Text.Json;

namespace SkyBrowser.Core.Astronomy;

public sealed record CatalogStar(int Id, SkyCoordinate Position, double Magnitude, double ColorIndex);
public sealed record SkyLabel(string Name, SkyCoordinate Position)
{
    public override string ToString() => Name;
}

public sealed record SkyPath(IReadOnlyList<SkyCoordinate> Points);
public sealed class SkyCatalog
{
    public IReadOnlyList<CatalogStar> Stars { get; }

    public IReadOnlyList<SkyPath> ConstellationLines { get; }

    public IReadOnlyList<SkyLabel> ConstellationNames { get; }

    public static IReadOnlyList<SkyLabel> Targets { get; } = DeepSkyCatalog.PopularTargets.Select(target => new SkyLabel(target.Name, target.Position)).ToArray();

    public SkyCatalog()
    {
        using var stars = Read("stars.6.json");
        Stars = stars.RootElement.GetProperty("features")
            .EnumerateArray()
            .Select(f => new CatalogStar(
                f.GetProperty("id")
                .GetInt32(),
                Position(f),
                f.GetProperty("properties")
                .GetProperty("mag")
                .GetDouble(),
                double.TryParse(
                    f.GetProperty("properties")
                    .GetProperty("bv")
                    .GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double bv
                )
                ? bv
                : 0.5
            ))
            .ToArray();
        using var lines = Read("constellations.lines.json");
        ConstellationLines = lines.RootElement.GetProperty("features")
            .EnumerateArray()
            .SelectMany(f => f.GetProperty("geometry")
            .GetProperty("coordinates")
            .EnumerateArray()
            .Select(line => new SkyPath(line.EnumerateArray()
            .Select(Coordinate)
            .ToArray())))
            .ToArray();
        using var names = Read("constellations.json");
        ConstellationNames = names.RootElement.GetProperty("features")
            .EnumerateArray()
            .Select(f => new SkyLabel(f.GetProperty("properties")
            .GetProperty("name")
                .GetString()!, Position(f)))
            .ToArray();
    }

    private static SkyCoordinate Position(JsonElement f) => Coordinate(f.GetProperty("geometry").GetProperty("coordinates"));

    private static SkyCoordinate Coordinate(JsonElement p) => new(SkyCoordinate.Normalize(p[0].GetDouble()), p[1].GetDouble());

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(SkyCatalog).Assembly.GetManifestResourceStream($"SkyBrowser.Core.Data.{name}") ?? throw new InvalidOperationException($"Missing embedded sky catalogue {name}.");
        return JsonDocument.Parse(stream);
    }
}
