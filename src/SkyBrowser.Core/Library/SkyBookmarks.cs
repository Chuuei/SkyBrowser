using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Core.Library;

public sealed partial class ImageLibrary
{
    public SkyBookmark[] GetBookmarks()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,ra,dec,fov,notes FROM bookmarks ORDER BY name COLLATE NOCASE,id";
        using var reader = command.ExecuteReader();
        List<SkyBookmark> bookmarks = [];
        while (reader.Read())
        {
            var bookmark = new SkyBookmark(reader.GetString(0), reader.GetString(1), new(reader.GetDouble(2), reader.GetDouble(3)),
                reader.GetDouble(4), reader.GetString(5));
            bookmark.Validate();
            bookmarks.Add(bookmark);
        }
        return bookmarks.ToArray();
    }

    public void SaveBookmark(SkyBookmark bookmark, CancellationToken token = default)
    {
        bookmark.Validate();
        token.ThrowIfCancellationRequested();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bookmarks(id,name,ra,dec,fov,notes) VALUES($id,$name,$ra,$dec,$fov,$notes)
            ON CONFLICT(id) DO UPDATE SET name=$name,ra=$ra,dec=$dec,fov=$fov,notes=$notes
            """;
        command.Parameters.AddWithValue("$id", bookmark.Id);
        command.Parameters.AddWithValue("$name", bookmark.Name.Trim());
        command.Parameters.AddWithValue("$ra", SkyCoordinate.Normalize(bookmark.Position.RightAscension));
        command.Parameters.AddWithValue("$dec", bookmark.Position.Declination);
        command.Parameters.AddWithValue("$fov", bookmark.FieldOfView);
        command.Parameters.AddWithValue("$notes", bookmark.Notes);
        command.ExecuteNonQuery();
    }

    public void DeleteBookmark(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM bookmarks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}
