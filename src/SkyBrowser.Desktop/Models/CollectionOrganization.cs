using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;

namespace SkyBrowser.Desktop.Models;

public enum CollectionSort { LayerOrder, Name, Object, Filter, Telescope, Camera, Date, Favorites, Group }
public enum MetadataField { Object, Filter, Telescope, Camera, Date }

/// <summary>Field-specific matching over retained metadata; never opens a source image.</summary>
public sealed class CollectionFilter
{
    public MetadataField Field { get; set; }
    public string MetadataValue { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Group { get; set; } = "";
    public bool FavoritesOnly { get; set; }
    public bool IsActive => FavoritesOnly || MetadataValue.Length > 0 || Tag.Length > 0 || Group.Length > 0;

    public bool Matches(SkyImageLayer layer) =>
        (!FavoritesOnly || layer.Organization.IsFavorite) &&
        (MetadataValue.Length == 0 || Metadata(layer.Image, Field).Contains(MetadataValue, StringComparison.OrdinalIgnoreCase)) &&
        (Tag.Length == 0 || layer.Organization.Tags.Split(", ").Contains(Tag, StringComparer.OrdinalIgnoreCase)) &&
        (Group.Length == 0 || string.Equals(layer.Organization.Group, Group, StringComparison.OrdinalIgnoreCase));

    public static string Metadata(LoadedSkyImage image, MetadataField field)
    {
        string[] keys = field switch
        {
            MetadataField.Object => ["OBJECT", "Observation:Object:Name"],
            MetadataField.Filter => ["FILTER", "Instrument:Filter:Name"],
            MetadataField.Telescope => ["TELESCOP", "Instrument:Telescope:Name"],
            MetadataField.Camera => ["INSTRUME", "Instrument:Camera:Name"],
            MetadataField.Date => ["DATE-OBS", "Observation:Time:Start"],
            _ => []
        };
        return string.Join(" · ", image.Details.Entries.Where(e => keys.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim().Trim('\'')).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static object SortKey(SkyImageLayer layer, CollectionSort sort) => sort switch
    {
        CollectionSort.Name => layer.Name,
        CollectionSort.Object => Metadata(layer.Image, MetadataField.Object),
        CollectionSort.Filter => Metadata(layer.Image, MetadataField.Filter),
        CollectionSort.Telescope => Metadata(layer.Image, MetadataField.Telescope),
        CollectionSort.Camera => Metadata(layer.Image, MetadataField.Camera),
        CollectionSort.Date => Metadata(layer.Image, MetadataField.Date),
        CollectionSort.Favorites => layer.Organization.IsFavorite ? 0 : 1,
        CollectionSort.Group => layer.Organization.Group,
        _ => ""
    };
}
