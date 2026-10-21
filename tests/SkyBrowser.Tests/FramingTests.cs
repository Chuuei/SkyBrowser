using System.Globalization;
using System.IO;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Configuration;

namespace SkyBrowser.Tests;

public sealed class FramingTests
{
    [Fact]
    public void FactoryProfilesHaveProvenanceAndNativeFields()
    {
        Assert.Equal(29, SmartTelescopeProfiles.All.Count);
        Assert.Equal(29, SmartTelescopeProfiles.All.Select(p => p.Name).Distinct().Count());
        foreach (var profile in SmartTelescopeProfiles.All)
        {
            profile.Validate();
            Assert.True(Uri.TryCreate(profile.Source, UriKind.Absolute, out var uri) && uri.Scheme == "https");
            Assert.Contains("2026-10-20", profile.Notes);
            var panel = Assert.Single(MosaicPlan.Create(profile, new(359.9, 89.9)).Panels);
            Assert.Equal(profile.WidthDegrees, panel.At(0, .5).DistanceTo(panel.At(1, .5)), 9);
            Assert.Equal(profile.HeightDegrees, panel.At(.5, 0).DistanceTo(panel.At(.5, 1)), 9);
        }
        var pro = SmartTelescopeProfiles.All.Single(p => p.Name == "ZWO Seestar S30 Pro · telephoto");
        var field = Assert.Single(MosaicPlan.Create(pro, new(0, 0)).Panels);
        Assert.Equal(4.6, field.Corners[0].DistanceTo(field.Corners[2]), 9);
        Assert.InRange(pro.WidthDegrees, 2.25, 2.26);
        Assert.InRange(pro.HeightDegrees, 4.00, 4.02);
    }

    [Theory]
    [InlineData(359.9, 0, 0)]
    [InlineData(0.1, 89.9, 35)]
    [InlineData(120, 90, 90)]
    [InlineData(270, -90, -45)]
    public void MosaicPanelsRetainTrueFieldsAcrossSeamsAndPoles(double ra, double dec, double rotation)
    {
        var plan = MosaicPlan.Create(new("Camera", 2, 1), new(ra, dec), rotation, 3, 3, 25);
        Assert.Equal(9, plan.Panels.Count);
        Assert.InRange(plan.Panels[4].Center.DistanceTo(plan.Center), 0, 1e-10);
        foreach (var panel in plan.Panels)
        {
            Assert.InRange(panel.Center.RightAscension, 0, 360);
            Assert.Equal(2, panel.At(0, .5).DistanceTo(panel.At(1, .5)), 9);
            Assert.Equal(1, panel.At(.5, 0).DistanceTo(panel.At(.5, 1)), 9);
            // Exported per-panel rotation recreates the actual spherical orientation.
            var reconstructed = MosaicPlan.Create(plan.Equipment, panel.Center, panel.RotationDegrees).Panels[0];
            for (int i = 0; i < 4; i++)
            { Assert.InRange(panel.Corners[i].DistanceTo(reconstructed.Corners[i]), 0, 1e-9); }
        }
        Assert.InRange(plan.Panels[4].Center.DistanceTo(plan.Panels[5].Center), 1.49, 1.51);
        Assert.InRange(plan.Panels[4].Center.DistanceTo(plan.Panels[7].Center), .74, .76);
    }

    [Fact]
    public void RotationAndOverlapChangeGeometryAndUseEastLeft()
    {
        var equipment = new EquipmentProfile("Camera", 2, 1);
        var normal = MosaicPlan.Create(equipment, new(30, 0), 0, 1, 2, 0);
        var overlap = MosaicPlan.Create(equipment, new(30, 0), 0, 1, 2, 50);
        Assert.True(normal.Panels[0].Center.RightAscension > normal.Panels[1].Center.RightAscension);
        Assert.Equal(2, normal.Panels[0].Center.DistanceTo(normal.Panels[1].Center), 10);
        Assert.InRange(overlap.Panels[0].Center.DistanceTo(overlap.Panels[1].Center), 1, 1.001);
        var rotated = MosaicPlan.Create(equipment, new(30, 0), 90, 1, 2);
        Assert.Equal(30, rotated.Panels[0].Center.RightAscension, 9);
        Assert.True(rotated.Panels[0].Center.Declination < 0);
    }

    [Fact]
    public void SensorCalculatorUsesEffectiveFocalLength()
    {
        var profile = EquipmentProfile.FromSensor("APS-C", 23.5, 15.7, 500);
        Assert.Equal(2 * Math.Atan(23.5 / 1000) / SkyCoordinate.Radians, profile.WidthDegrees, 10);
        Assert.Throws<ArgumentException>(() => EquipmentProfile.FromSensor("Invalid", 23.5, 15.7, 0));
        Assert.Throws<ArgumentException>(() => EquipmentProfile.FromSensor("Invalid", double.NaN, 15.7, 500));
    }

    [Fact]
    public void InvalidAndExcessivelyWidePlansAreRejected()
    {
        var profile = new EquipmentProfile("Camera", 2, 1);
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(0, 91)));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(double.NaN, 0)));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(0, 0), rows: 11));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(0, 0), columns: 0));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(0, 0), overlapPercent: 81));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(profile, new(0, 0), rotationDegrees: double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => MosaicPlan.Create(new("Wide", 80, 60), new(0, 0), rows: 10, columns: 10));
    }

    [Fact]
    public void CsvIsInvariantAndExportsActualPanelCoordinatesAndRotations()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var plan = MosaicPlan.Create(new("=Camera, \"custom\"", 2, 1), new(359.9, 80), 37, 2, 2);
            var lines = plan.ToCsv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(5, lines.Length);
            Assert.Contains("RA_J2000_deg", lines[0]);
            Assert.Contains("Rotation_CCW_deg", lines[0]);
            Assert.Contains("\"'=Camera, \"\"custom\"\"\"", lines[1]);
            Assert.Contains(plan.Panels[0].Center.RightAscension.ToString("F8", CultureInfo.InvariantCulture), lines[1]);
            Assert.Contains(plan.Panels[0].RotationDegrees.ToString("F6", CultureInfo.InvariantCulture), lines[1]);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void CustomProfilesSurviveRestartAndRecoverFromDamagedFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SkyBrowser-framing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new EquipmentProfileStore(Path.Combine(directory, "equipment.json"));
            Assert.Empty(store.Read());
            var first = new EquipmentProfile("My telescope", 2, 1);
            store.Write([first]);
            store.Write([first with { WidthDegrees = 3 }]);
            Assert.Equal(3, Assert.Single(new EquipmentProfileStore(store.FilePath).Read()).WidthDegrees);
            File.WriteAllText(store.FilePath, "broken");
            Assert.Equal(first, Assert.Single(store.Read()));
            Assert.Contains("Recovered", store.LastError);
            Assert.Throws<ArgumentException>(() => store.Write([first, first with { Name = "MY TELESCOPE" }]));
            Assert.Throws<ArgumentException>(() => store.Write([first with { WidthDegrees = -1 }]));
            Assert.Throws<ArgumentException>(() => store.Write(Enumerable.Range(0, 100).Select(i =>
                new EquipmentProfile("Camera " + i, 2, 1, new string('s', 2048), new string('n', 2048)))));
            Assert.Equal(first, Assert.Single(store.Read()));
        }
        finally { Directory.Delete(directory, true); }
    }
}
