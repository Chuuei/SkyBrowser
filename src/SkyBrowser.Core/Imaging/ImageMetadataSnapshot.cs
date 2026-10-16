using System.Globalization;
using XISFLib;

namespace SkyBrowser.Core.Imaging;

public sealed record ImageMetadataEntry(string Source, string Name, string Value, string Comment = "");
public sealed record ImageMetadataSnapshot(ImageMetadataEntry[] Entries, bool IsTruncated = false)
{
    public static ImageMetadataSnapshot Empty { get; } = new([]);

    internal static ImageMetadataSnapshot Capture(XisfImageData image)
    {
        // Keep details small enough to retain with every cached preview, even for large processing histories.
        const int characterBudget = 65536, maximumEntries = 2048, maximumValueLength = 2048;
        List<ImageMetadataEntry> entries = [];
        int characters = 0;
        bool shortened = false;

        string Shorten(string text)
        {
            if (text.Length <= maximumValueLength)
            {
                return text;
            }

            shortened = true;
            return text[..maximumValueLength] + "… [shortened]";
        }

        bool Add(string source, string name, string value, string? comment = null)
        {
            name = Shorten(name);
            value = Shorten(value);
            comment = Shorten(comment ?? "");
            int length = source.Length + name.Length + value.Length + comment.Length;
            if (entries.Count >= maximumEntries || characters + length > characterBudget)
            {
                shortened = true;
                return false;
            }

            entries.Add(new(source, name, value, comment));
            characters += length;
            return true;
        }

        string Format(object? value)
        {
            if (value is Array array)
            {
                string dimensions = string.Join(" × ", Enumerable.Range(0, array.Rank).Select(array.GetLength));
                var values = array.Cast<object?>().Take(16).Select(item => Convert.ToString(item, CultureInfo.InvariantCulture));
                if (array.Length > 16)
                {
                    shortened = true;
                }

                return $"[{string.Join(", ", values)}{(array.Length > 16 ? ", …" : "")}] ({dimensions} elements)";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        Add("Image", "Dimensions", string.Join(" × ", image.Dimensions));
        Add("Image", "Channels", image.ChannelCount.ToString(CultureInfo.InvariantCulture));
        Add("Image", "Decoded sample format", image.SampleFormat.ToString());
        Add("Image", "Color space", image.ColorSpace ?? "Unspecified");
        Add("Image", "Pixel storage", image.PixelStorage.ToString());
        Add("Image", "File orientation", image.Orientation ?? "0");
        if (image.Id is { } id)
        {
            Add("Image", "Image identifier", id);
        }

        if (image.ImageType is { } type)
        {
            Add("Image", "Image type", type);
        }

        if (image.Uuid is { } uuid)
        {
            Add("Image", "UUID", uuid.ToString());
        }

        Add("Image", "Stored display function", image.DisplayFunction is null ? "Absent" : "Present");
        foreach (var keyword in image.FitsKeywords)
        {
            if (!Add("FITS", keyword.Name, keyword.Value, keyword.Comment))
            {
                break;
            }
        }

        foreach (var (name, raw) in image.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            (object? value, string? comment) = raw switch
            {
                XisfProperty property when !property.IsAvailable => ($"Unavailable: {property.UnavailableReason}", property.Comment),
                XisfProperty property => (property.Value, property.Comment),
                XisfPropertyData property => (property.Value, property.Comment),
                _ => (raw, null)
            };
            if (!Add("XISF", name, Format(value), comment))
            {
                break;
            }
        }

        return new(entries.ToArray(), shortened);
    }
}
