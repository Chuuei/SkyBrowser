using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Tests;

public sealed class CollectionSearchTests
{
    [Theory]
    [InlineData("ic434", true)]
    [InlineData("IC 434", true)]
    [InlineData("IC-434", true)]
    [InlineData("ic_434", true)]
    [InlineData("ic434 h alpha", true)]
    [InlineData("IC 434 Ritchey Chretien", true)]
    [InlineData("hydrogen narrowband", true)]
    [InlineData("observer ada", true)]
    [InlineData("horse head", true)]
    [InlineData("wide_field", true)]
    [InlineData("ic 435", false)]
    [InlineData("IC434 OIII", false)]
    [InlineData("  --  ", true)]
    [InlineData("\uD800ic434", true)]
    public void SearchesFilenameAndMetadataWithFormattingTolerance(string query, bool expected)
    {
        var image = Image(
            "wide_field.xisf",
            "Horsehead Nebula",
            [new("FITS", "OBJECT", "'IC 434'"), new("FITS", "TELESCOP", "Ritchey–Chrétien 8"), new("FITS", "FILTER", "H-alpha", "Hydrogen narrowband"), new("XISF", "Observation:Observer", "Ada")]
        );
        var search = new ImageCollectionSearch();
        search.Index(image);
        search.SetQuery(query);
        Assert.Equal(expected, search.Matches(image));
    }

    [Theory]
    [InlineData("M31", "M 31")]
    [InlineData("NGC 7000", "NGC-7000")]
    [InlineData("Sh2 240", "Sh2-240")]
    public void CataloguePrefixesAndNumbersStayTogether(string query, string target)
    {
        var search = new ImageCollectionSearch();
        search.SetQuery(query);
        Assert.True(search.Matches(Image("image.xisf", target, [])));
        Assert.False(search.Matches(Image("image.xisf", string.Concat(query.TakeWhile(char.IsLetter)), [new("FITS", "EXPTIME", "434 31 7000 240")])));
    }

    [Fact]
    public void QueriesDoNotJoinUnrelatedFieldsOrUseTheParentFolder()
    {
        var search = new ImageCollectionSearch();
        search.SetQuery("IC 434");
        Assert.False(search.Matches(Image(@"D:\IC434\other.xisf", "Other target", [new("FITS", "PREFIX", "IC"), new("FITS", "EXPTIME", "434")])));
        Assert.True(search.Matches(Image("ic_434_details.xisf", "Other target", [])));
        search.SetQuery("");
        Assert.False(search.IsActive);
    }

    private static LoadedSkyImage Image(string path, string name, ImageMetadataEntry[] metadata) => new(path, 0, name, 1, 1, new(1, 1, [0, 0, 0, 255]), "Linear", null, null, "")
    {
        Details = new(metadata)
    };
}
