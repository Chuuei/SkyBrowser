using Microsoft.Data.Sqlite;

namespace SkyBrowser.Core.Library;

public sealed record LibraryImageId(string SourcePath, int ImageIndex);

public sealed record ImageOrganization(bool IsFavorite = false, string Tags = "", string Group = "")
{
    public static string NormalizeTags(string tags) => string.Join(", ", tags.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Null fields leave existing values unchanged; empty strings clear them.</summary>
public sealed record OrganizationUpdate(bool? IsFavorite = null, string? Tags = null, string? Group = null);

public sealed partial class ImageLibrary
{
    public ImageOrganization GetOrganization(string sourcePath, int imageIndex)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT favorite,tags,group_name FROM organization WHERE path=$path AND image_index=$index";
        command.Parameters.AddWithValue("$path", Key(sourcePath));
        command.Parameters.AddWithValue("$index", imageIndex);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetBoolean(0), reader.GetString(1), reader.GetString(2)) : new();
    }

    /// <summary>Annotations belong to a path and image index, independent of preview/source revisions.</summary>
    public void UpdateOrganization(IEnumerable<LibraryImageId> images, OrganizationUpdate update, CancellationToken token = default)
    {
        string? tags = update.Tags is null ? null : ImageOrganization.NormalizeTags(update.Tags);
        string? group = update.Group?.Trim();
        if (tags?.Length > 1024 || group?.Length > 128)
        {
            throw new ArgumentException("Use at most 1,024 characters for tags and 128 for a group name.");
        }

        token.ThrowIfCancellationRequested();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO organization(path,image_index,favorite,tags,group_name)
            VALUES($path,$index,COALESCE($favorite,0),COALESCE($tags,''),COALESCE($group,''))
            ON CONFLICT(path,image_index) DO UPDATE SET
            favorite=COALESCE($favorite,favorite), tags=COALESCE($tags,tags), group_name=COALESCE($group,group_name)
            """;
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var index = command.Parameters.Add("$index", SqliteType.Integer);
        command.Parameters.AddWithValue("$favorite", (object?)update.IsFavorite ?? DBNull.Value);
        command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
        command.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            if (image.ImageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(images));
            }
            path.Value = Key(image.SourcePath);
            index.Value = image.ImageIndex;
            command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }
}
