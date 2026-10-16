using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class TargetCatalogTests
{
    [Fact]
    public void OfflineCatalogHasCompleteMessierAndCaldwellCoverageAndValidPositions()
    {
        Assert.Equal(13372, DeepSkyCatalog.Targets.Count);
        Assert.Equal(110, DeepSkyCatalog.Search("", TargetCatalog.Messier).Count);
        Assert.Equal(109, DeepSkyCatalog.Search("", TargetCatalog.Caldwell).Count);
        Assert.True(DeepSkyCatalog.Search("", TargetCatalog.Ngc).Count > 7800);
        Assert.True(DeepSkyCatalog.Search("", TargetCatalog.Ic).Count > 5000);
        foreach (int number in Enumerable.Range(1, 110))
        {
            Assert.NotNull(DeepSkyCatalog.Find($"M{number}"));
        }

        foreach (int number in Enumerable.Range(1, 109))
        {
            Assert.NotNull(DeepSkyCatalog.Find($"C{number}"));
        }

        Assert.NotNull(DeepSkyCatalog.Find("NGC7840"));
        Assert.NotNull(DeepSkyCatalog.Find("IC5386"));
        Assert.All(DeepSkyCatalog.Targets, target =>
        {
            Assert.InRange(target.Position.RightAscension, 0, 359.99999999);
            Assert.InRange(target.Position.Declination, -90, 90);
            Assert.True(double.IsFinite(target.Position.RightAscension) && double.IsFinite(target.Position.Declination));
            Assert.InRange(target.SuggestedFieldOfView, 1.5, 30);
        });
        Assert.InRange(DeepSkyCatalog.PopularTargets.Count, 15, 40);
        Assert.True(DeepSkyCatalog.Find("M31")!.Position.DistanceTo(new(10.6847, 41.2688)) < 0.01);
        Assert.True(DeepSkyCatalog.Find("C109")!.Position.Declination < -80);
    }

    [Theory]
    [InlineData("m 031", "NGC 0224")]
    [InlineData("Caldwell 20", "NGC7000")]
    [InlineData("C 65", "NGC 253")]
    [InlineData("IC11", "NGC281")]
    public void CrossDesignationsResolveToTheSameObject(string first, string second)
    {
        Assert.NotNull(DeepSkyCatalog.Find(first));
        Assert.Same(DeepSkyCatalog.Find(first), DeepSkyCatalog.Find(second));
    }

    [Theory]
    [InlineData("IC 434", "IC434")]
    [InlineData("ic_0434", "IC434")]
    [InlineData("Messier 031", "NGC224")]
    [InlineData("Caldwell 109", "NGC3195")]
    [InlineData("NGC 7840", "NGC7840")]
    public void SearchRanksExactCatalogueIdentifiersFirst(string query, string expected)
    {
        Assert.Equal(expected, DeepSkyCatalog.Search(query).First().Id);
    }

    [Fact]
    public void SearchFindsCommonNamesAndCombinesCatalogueAndTypeFilters()
    {
        Assert.Contains(DeepSkyCatalog.Search("Horsehead"), target => target.Id == "B33");
        Assert.Contains(DeepSkyCatalog.Search("Horsehead"), target => target.Id == "IC434");
        Assert.Contains(DeepSkyCatalog.Search("North America"), target => target.Id == "NGC7000");
        Assert.Single(DeepSkyCatalog.Search("Caldwell 20 nebula", TargetCatalog.Ngc));
        Assert.Empty(DeepSkyCatalog.Search("Caldwell 20", TargetCatalog.Ic));
        Assert.Equal(110, DeepSkyCatalog.Search("Messier").Count);
        Assert.Equal(109, DeepSkyCatalog.Search("Caldwell").Count);
        Assert.Contains("disputed", DeepSkyCatalog.Find("M102")!.Note);
        Assert.Equal("NGC5866", DeepSkyCatalog.Find("M102")!.Id);
        Assert.Empty(DeepSkyCatalog.Search("no-such-catalog-object-987654"));
    }
}
