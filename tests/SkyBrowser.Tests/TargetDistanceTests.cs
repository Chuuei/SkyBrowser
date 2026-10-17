using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Tests;

public sealed class TargetDistanceTests
{
    [Fact]
    public void BundledEstimatesHaveUniqueCatalogueIdentitiesAndAttributedFiniteValues()
    {
        Assert.Equal(3283, TargetDistances.All.Count);
        Assert.Equal(TargetDistances.All.Count, TargetDistances.All.Select(entry => entry.Target.Id).Distinct().Count());
        Assert.All(TargetDistances.All, estimate =>
        {
            Assert.Same(DeepSkyCatalog.Find(estimate.Target.Id), estimate.Target);
            Assert.InRange(estimate.LightYears, 1, 1e10);
            Assert.Contains("light-years", estimate.SourceValue);
            Assert.Contains(new Uri(estimate.SourceUrl).Host, new[] { "science.nasa.gov", "apod.nasa.gov", "simbad.cds.unistra.fr" });
            Assert.False(string.IsNullOrWhiteSpace(estimate.SourceName));
            Assert.False(string.IsNullOrWhiteSpace(estimate.Method));
            Assert.Equal("2026-10-17", estimate.AsOf);
        });
    }

    [Theory]
    [InlineData(TargetCatalog.Messier, 107, 110)]
    [InlineData(TargetCatalog.Caldwell, 109, 109)]
    [InlineData(TargetCatalog.Ngc, 2554, 8098)]
    [InlineData(TargetCatalog.Ic, 824, 5505)]
    public void EverySupportedCatalogueHasExplicitOfflineCoverage(TargetCatalog catalog, int available, int total)
    {
        Assert.Equal(new(available, total), TargetDistances.Coverage(catalog));
        var results = TargetDistances.Search("", catalog);
        Assert.Equal(available, results.Count);
        Assert.All(results, entry => Assert.Equal(catalog, entry.Target.Catalogs & catalog));
    }

    [Theory]
    [InlineData("Caldwell 20", "NGC 7000")]
    [InlineData("C 102", "IC 2602")]
    [InlineData("C31", "IC405")]
    [InlineData("C49", "NGC2237")]
    public void CaldwellAndNgcIcAliasesShareOneDistance(string designation, string alias)
    {
        Assert.NotNull(TargetDistances.Find(designation));
        Assert.Same(TargetDistances.Find(designation), TargetDistances.Find(alias));
        Assert.Contains(TargetDistances.Find(alias), TargetDistances.Search(designation));
    }

    [Fact]
    public void LiteratureDistancesRetainUnitsErrorsAndReferencesWithoutInventingUnknownValues()
    {
        var heart = TargetDistances.Find("IC1805")!;
        Assert.Equal(2187 * 3.261563777167433, heart.LightYears, 7);
        Assert.Equal("SIMBAD / CDS", heart.SourceName);
        Assert.Equal("paral", heart.Method);
        Assert.Equal("2021MNRAS.504..356D", heart.Reference);
        Assert.Equal(211 * 3.261563777167433, heart.MinusErrorLightYears!.Value, 8);
        Assert.Equal(heart.MinusErrorLightYears, heart.PlusErrorLightYears);
        Assert.Contains("Literature reference", heart.SourceDetails);
        Assert.Contains("Reported lower error", heart.SourceDetails);
        Assert.Null(TargetDistances.Find("M40"));
        Assert.Null(TargetDistances.Find("M73"));
        Assert.NotSame(TargetDistances.Find("C14"), TargetDistances.Find("NGC869"));
        Assert.NotSame(TargetDistances.Find("C49"), TargetDistances.Find("C50"));
        Assert.Equal(150, TargetDistances.Find("C41")!.LightYears);
    }

    [Theory]
    [InlineData("M 81", "NGC 3031", 11600000)]
    [InlineData("Messier 82", "NGC3034", 12000000)]
    [InlineData("M 031", "NGC224", 2500000)]
    [InlineData("M102", "NGC5866", 44000000)]
    public void DistanceAliasesReferToTheCorrectObject(string designation, string alias, double expected)
    {
        var estimate = TargetDistances.Find(designation);
        Assert.NotNull(estimate);
        Assert.Same(estimate, TargetDistances.Find(alias));
        Assert.Equal(expected, estimate.LightYears);
        Assert.Contains(estimate, TargetDistances.Search(alias));
    }

    [Fact]
    public void SearchAndMissingDistancesNeverSubstituteAnotherObjectsDistance()
    {
        Assert.Contains(TargetDistances.Find("M81"), TargetDistances.Search("Bode"));
        Assert.Contains(TargetDistances.Find("M82"), TargetDistances.Search("Cigar"));
        Assert.Null(TargetDistances.Find("M24"));
        Assert.Null(TargetDistances.Find("IC434"));
        Assert.Null(TargetDistances.Find("nonexistent"));
        Assert.Empty(TargetDistances.Search("Horsehead"));
        Assert.Throws<ArgumentException>(() => TargetDistances.Measure("M81", "IC434"));
        Assert.Throws<ArgumentException>(() => TargetDistances.Measure("IC434", "M81"));
    }

    [Fact]
    public void SelectingM81AndM82MeasuresTheirCentresWithAnExplicitlyApproximateSeparation()
    {
        var result = TargetDistances.Measure("M81", "M82");
        Assert.Equal(DeepSkyCatalog.Find("M81")!.Position, result.Start);
        Assert.Equal(DeepSkyCatalog.Find("M82")!.Position, result.End);
        Assert.InRange(result.Degrees, 0.6, 0.7);
        Assert.InRange(result.SeparationLightYears!.Value, 410000, 430000);
        Assert.Contains("≈420,000 ly", result.DisplayLabel);
        Assert.Same(TargetDistances.Find("M81"), result.StartDistanceSource);
        Assert.Same(TargetDistances.Find("M82"), result.EndDistanceSource);
        Assert.Equal(result.SeparationLightYears, TargetDistances.Measure("M82", "M81").SeparationLightYears);
        Assert.Equal(0, TargetDistances.Measure("M81", "NGC3031").SeparationLightYears);
    }
}
