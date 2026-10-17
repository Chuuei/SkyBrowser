using System.IO;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Library;

namespace SkyBrowser.Tests;

public sealed class OrganizationTests
{
    [Fact]
    public void AnnotationsMigratePersistPerImageAndSurviveCacheAndFolderRemoval()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            string database = Path.Combine(directory, "library.sqlite3");
            using (var legacy = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                legacy.Open();
                using var command = legacy.CreateCommand();
                command.CommandText = "CREATE TABLE roots(path TEXT PRIMARY KEY)";
                command.ExecuteNonQuery();
            }
            var library = new ImageLibrary(database);
            string source = Path.Combine(directory, "multi.xisf");
            File.WriteAllText(source, "Original scientific file placeholder");
            var first = new LibraryImageId(source, 0);
            var second = new LibraryImageId(source, 1);
            library.AddFolder(directory);
            library.UpdateOrganization([first], new(true, "Narrowband; Ha, ha,  Winter ", " Horsehead "));
            library.UpdateOrganization([second], new(false, "OIII", "Other"));
            library.UpdateOrganization([first, second], new(Group: "Campaign"));
            library.RemoveFolder(directory);
            using (var cache = new ProcessedImageCache(Path.Combine(directory, "cache")))
            {
                cache.ClearPreviews();
            }
            var reopened = new ImageLibrary(database);
            Assert.Equal(new(true, "Ha, Narrowband, Winter", "Campaign"), reopened.GetOrganization(source.ToUpperInvariant(), 0));
            Assert.Equal(new(false, "OIII", "Campaign"), reopened.GetOrganization(source, 1));
            Assert.Equal(new(), reopened.GetOrganization(source, 2));
            reopened.UpdateOrganization([first], new(Tags: "", Group: ""));
            Assert.Equal(new(true), reopened.GetOrganization(source, 0));
            Assert.Equal("Original scientific file placeholder", File.ReadAllText(source));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void BatchValidationAndMidTransactionCancellationRollBackAllRows()
    {
        string directory = TestImages.NewDirectory();
        try
        {
            var library = new ImageLibrary(Path.Combine(directory, "library.sqlite3"));
            string source = Path.Combine(directory, "image.fits");
            var first = new LibraryImageId(source, 0);
            library.UpdateOrganization([first], new(Tags: "original"));
            Assert.Throws<ArgumentOutOfRangeException>(() => library.UpdateOrganization([first, new(source, -1)], new(true)));
            Assert.False(library.GetOrganization(source, 0).IsFavorite);
            using var cancellation = new CancellationTokenSource();
            IEnumerable<LibraryImageId> CancelDuringBatch()
            {
                yield return first;
                cancellation.Cancel();
                yield return new(source, 1);
            }
            Assert.Throws<OperationCanceledException>(() => library.UpdateOrganization(CancelDuringBatch(), new(Tags: "changed"), cancellation.Token));
            Assert.Equal("original", library.GetOrganization(source, 0).Tags);
            Assert.Equal(new(), library.GetOrganization(source, 1));
            Assert.Throws<ArgumentException>(() => library.UpdateOrganization([first], new(Group: new('g', 129))));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
