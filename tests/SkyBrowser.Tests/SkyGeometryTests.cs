using System.IO;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Library;

namespace SkyBrowser.Tests;

public sealed class SkyGeometryTests
{
    [Theory]
    [InlineData(0, 100, 150, 50)]
    [InlineData(180, 100, 150, 250)]
    [InlineData(90, 3, 4, 5)]
    [InlineData(60, 100, 100, 100)]
    [InlineData(0, 100, 100, 0)]
    public void KnownObjectDistancesGiveThreeDimensionalSeparation(double angle, double a, double b, double expected)
    {
        var measurement = new SkyMeasurement(new(0, 0), new(angle, 0))
        {
            StartDistanceLightYears = a,
            EndDistanceLightYears = b
        };
        Assert.Equal(expected, measurement.SeparationLightYears!.Value, 10);
        Assert.Equal(measurement.SeparationLightYears, SkyGeometry.SpatialSeparation(measurement.End, b, measurement.Start, a));
        Assert.Contains("ly", measurement.DisplayLabel);
        Assert.Null((measurement with { EndDistanceLightYears = null }).SeparationLightYears);
    }

    [Fact]
    public void PhysicalSeparationRetainsTinyAnglesAndLargeDistances()
    {
        var start = new SkyCoordinate(0, 0);
        var end = new SkyCoordinate(1e-12, 0);
        double expected = 1e12 * 1e-12 * SkyCoordinate.Radians;
        Assert.InRange(Math.Abs(SkyGeometry.SpatialSeparation(start, 1e12, end, 1e12) - expected), 0, 1e-15);
        Assert.Equal(1e300, SkyGeometry.SpatialSeparation(start, 1e300, new(60, 0), 1e300), 1e286);
        Assert.Equal(100, SkyGeometry.SpatialSeparation(new(359, 0), 100, new(59, 0), 100), 10);
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => SkyGeometry.SpatialSeparation(start, invalid, end, 100));
            Assert.Throws<ArgumentException>(() => SkyGeometry.SpatialSeparation(start, 100, end, invalid));
        }
        Assert.Throws<ArgumentException>(() => SkyGeometry.SpatialSeparation(start, double.MaxValue, new(180, 0), double.MaxValue));
    }

    [Theory]
    [InlineData(359, 0, 1, 0, 2)]
    [InlineData(0, 89, 180, 89, 2)]
    [InlineData(0, 0, 180, 0, 180)]
    [InlineData(40, -90, 320, -90, 0)]
    public void GreatCircleMeasurementsCrossSeamsPolesAndAntipodes(double ra1, double dec1, double ra2, double dec2, double distance)
    {
        var measurement = new SkyMeasurement(new(ra1, dec1), new(ra2, dec2));
        Assert.Equal(distance, measurement.Degrees, 9);
        var midpoint = SkyGeometry.Interpolate(measurement.Start, measurement.End, 0.5);
        Assert.Equal(distance / 2, measurement.Start.DistanceTo(midpoint), 8);
        Assert.Equal(distance / 2, measurement.End.DistanceTo(midpoint), 8);
    }

    [Theory]
    [InlineData(359.9, 0)]
    [InlineData(10, 89.99)]
    [InlineData(180, -90)]
    public void CompassOffsetsRetainAngularLength(double ra, double dec)
    {
        var center = new SkyCoordinate(ra, dec);
        foreach (double angle in new[] { 0d, 90d, 180d, 270d })
        {
            Assert.Equal(0.01, center.DistanceTo(SkyGeometry.Offset(center, angle, 0.01)), 10);
        }
        Assert.Equal("1′", SkyGeometry.FormatAngle(1d / 60));
        Assert.Equal("1″", SkyGeometry.FormatAngle(1d / 3600));
    }

    [Fact]
    public void BookmarksAndNotesPersistAcrossLibraryRestartAndPreviewClearing()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            var bookmark = new SkyBookmark(Guid.NewGuid().ToString("N"), "Winter field", new(359.99, 89.5), 2.4, "Two lines\nUnicode: α β");
            library.SaveBookmark(bookmark);
            using (var cache = new ProcessedImageCache(Path.Combine(directory, "cache")))
            {
                cache.ClearPreviews();
            }
            var reopened = new ImageLibrary(library.DatabasePath);
            Assert.Equal(bookmark, Assert.Single(reopened.GetBookmarks()));
            reopened.SaveBookmark(bookmark with
            {
                Name = "Renamed",
                Notes = "Edited note"
            });
            Assert.Equal("Edited note", Assert.Single(library.GetBookmarks()).Notes);
            Assert.Throws<ArgumentException>(() => reopened.SaveBookmark(bookmark with { Position = new(0, double.NaN) }));
            Assert.Single(library.GetBookmarks());
            reopened.DeleteBookmark(bookmark.Id);
            Assert.Empty(library.GetBookmarks());
        }
        finally { Directory.Delete(directory, true); }
    }
}
