using System.Runtime.CompilerServices;
using System.Text;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop.Models;

/// <summary>Searches detached image metadata without opening scientific files.</summary>
public sealed class ImageCollectionSearch
{
    private readonly ConditionalWeakTable<LoadedSkyImage, SearchDocument> _documents = new();
    private string[] _terms = [];

    public bool IsActive => _terms.Length > 0;

    public void SetQuery(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .ToArray();
        List<string> terms = [];
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            // A catalogue identifier is one term, including when the prefix and number are separated.
            if (IsCatalogPrefix(token) && i + 1 < tokens.Length && char.IsDigit(tokens[i + 1][0]))
            {
                token += tokens[++i];
            }

            terms.Add(token);
        }

        _terms = terms.Distinct(StringComparer.Ordinal).ToArray();
    }

    public void Index(LoadedSkyImage image) => _ = _documents.GetValue(image, SearchDocument.Create);

    public bool Matches(LoadedSkyImage image) => !IsActive || _documents.GetValue(image, SearchDocument.Create).Matches(_terms);

    private static bool IsCatalogPrefix(string text) => text is
        "IC" or "NGC" or "M" or "SH2" or "SH" or "LDN" or "LBN" or "B" or
        "ARP" or "ABELL" or "UGC" or "PGC" or "CED" or "VDB" or "CR" or "MEL" or "HCG";

    private static string Normalize(string text)
    {
        StringBuilder result = new(text.Length);
        string normalized;
        try
        {
            normalized = text.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            // An incomplete pasted surrogate must not break typing.
            normalized = text;
        }

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                result.Append(Rune.ToUpperInvariant(rune));
            }
        }

        return result.ToString();
    }

    private sealed class SearchDocument(string text)
    {
        public bool Matches(string[] terms) => terms.All(term => text.Contains(term, StringComparison.Ordinal));

        public static SearchDocument Create(LoadedSkyImage image)
        {
            StringBuilder text = new();

            // Null separators stop a single identifier matching across unrelated metadata fields.
            void Add(string value)
            {
                text.Append(Normalize(value));
                text.Append('\0');
            }

            Add(System.IO.Path.GetFileName(image.SourcePath));
            Add(image.Name);
            Add(image.Metadata);
            foreach (var entry in image.Details.Entries)
            {
                Add(entry.Name);
                Add(entry.Value);
                Add(entry.Comment);
            }

            return new(text.ToString());
        }
    }
}
