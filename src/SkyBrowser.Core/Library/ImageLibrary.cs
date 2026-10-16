using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Library;

public sealed record LibraryImage(string SourcePath, int ImageIndex, string Name, int Width, int Height,
    string Summary, bool HasPlacement, bool Missing, string? Error)
{
    public string FileName => Path.GetFileName(SourcePath);
    public string Status => Missing ? "Missing" : Error is not null ? "Could not index" : HasPlacement ? "Placed" : "Needs placement";
    public string Dimensions => Width > 0 ? $"{Width:N0} × {Height:N0}" : "";
}

public sealed record LibraryScanProgress(int Checked, int Updated, int Failed, string Path);

/// <summary>Durable archive metadata and external solutions, independent of disposable previews.</summary>
public sealed class ImageLibrary
{
    private readonly object _initialization = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private bool _initialized;
    public string DatabasePath { get; }

    public ImageLibrary(string databasePath) => DatabasePath = Path.GetFullPath(databasePath);

    private SqliteConnection Open()
    {
        lock (_initialization)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Pooling = false,
                DefaultTimeout = 10
            }.ToString());
            connection.Open();
            if (!_initialized)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE IF NOT EXISTS roots(path TEXT PRIMARY KEY);
                    CREATE TABLE IF NOT EXISTS images(
                        path TEXT NOT NULL, image_index INTEGER NOT NULL, name TEXT NOT NULL,
                        width INTEGER NOT NULL, height INTEGER NOT NULL, summary TEXT NOT NULL,
                        search_text TEXT NOT NULL, placed INTEGER NOT NULL, missing INTEGER NOT NULL DEFAULT 0,
                        error TEXT, stamp TEXT, PRIMARY KEY(path,image_index));
                    CREATE TABLE IF NOT EXISTS solutions(
                        path TEXT NOT NULL, image_index INTEGER NOT NULL, stamp TEXT NOT NULL,
                        width INTEGER NOT NULL, height INTEGER NOT NULL, grid TEXT NOT NULL,
                        PRIMARY KEY(path,image_index));
                    """;
                command.ExecuteNonQuery();
                _initialized = true;
            }
            return connection;
        }
    }

    private static string Key(string path) => ProcessedImageCache.CanonicalPath(path);

    public string[] GetFolders()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM roots ORDER BY path";
        using var reader = command.ExecuteReader();
        List<string> paths = [];
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }
        return paths.ToArray();
    }

    public void AddFolder(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("Choose an available image archive folder.");
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO roots(path) VALUES($path)";
        command.Parameters.AddWithValue("$path", Key(path));
        command.ExecuteNonQuery();
    }

    /// <summary>Stops monitoring this folder. Indexed images and solutions remain available.</summary>
    public void RemoveFolder(string path)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM roots WHERE path=$path";
        command.Parameters.AddWithValue("$path", Key(path));
        command.ExecuteNonQuery();
    }

    /// <summary>Commits a complete folder list without deleting retained metadata or solutions.</summary>
    public void SetFolders(IEnumerable<string> folders, CancellationToken token = default)
    {
        string[] paths = folders.Select(path => Key(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        token.ThrowIfCancellationRequested();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM roots";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO roots(path) VALUES($path)";
        var parameter = command.Parameters.Add("$path", SqliteType.Text);
        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            parameter.Value = path;
            command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    public static bool ContainsSource(string folder, string sourcePath)
    {
        string root = Path.TrimEndingDirectorySeparator(Key(folder));
        return Key(sourcePath).StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Discovers supported files without opening or decoding any image.</summary>
    public static string[] FindFolderImages(IEnumerable<string> folders, CancellationToken token = default)
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string folder in folders)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(folder, "*", enumeration))
            {
                token.ThrowIfCancellationRequested();
                if (SkyImageLoader.IsSupported(file))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<LibraryImage> Search(string query = "", CancellationToken token = default)
    {
        string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(term => term.Length > 0).Distinct().ToArray();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,image_index,name,width,height,summary,placed,missing,error FROM images WHERE 1=1";
        for (int i = 0; i < terms.Length; i++)
        {
            command.CommandText += $" AND instr(search_text,$term{i})>0";
            command.Parameters.AddWithValue($"$term{i}", terms[i]);
        }
        command.CommandText += " ORDER BY name COLLATE NOCASE,path,image_index";
        using var reader = command.ExecuteReader();
        List<LibraryImage> entries = [];
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            entries.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        return entries;
    }

    private static string Normalize(string text)
    {
        StringBuilder result = new();
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            decomposed = text;
        }
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                result.Append(Rune.ToUpperInvariant(rune));
            }
        }
        return result.ToString();
    }

    private static string Stamp(SourceSnapshot snapshot) => JsonSerializer.Serialize(
        new SourceSnapshot(snapshot.Files.Select(file => file with { Path = Key(file.Path) }).ToArray()),
        CacheJsonContext.Default.SourceSnapshot);

    internal IReadOnlyList<LoadedSkyImage> Remember(IReadOnlyList<LoadedSkyImage> images, SourceSnapshot snapshot, CancellationToken token)
    {
        if (images.Count == 0 || !snapshot.IsCurrent(token))
        {
            return images;
        }
        string stamp = Stamp(snapshot);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        List<LoadedSkyImage> result = [];
        foreach (var original in images)
        {
            token.ThrowIfCancellationRequested();
            var image = ApplySolution(original, connection, transaction, stamp);
            result.Add(image);
            var fields = new[] { Path.GetFileName(image.SourcePath), image.Name, image.Metadata }
                .Concat(image.Details.Entries.SelectMany(entry => new[] { entry.Name, entry.Value, entry.Comment }));
            string search = string.Join('\0', fields.Select(Normalize));
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO images(path,image_index,name,width,height,summary,search_text,placed,missing,error,stamp)
                VALUES($path,$index,$name,$width,$height,$summary,$search,$placed,0,NULL,$stamp)
                ON CONFLICT(path,image_index) DO UPDATE SET name=$name,width=$width,height=$height,
                summary=$summary,search_text=$search,placed=$placed,missing=0,error=NULL,stamp=$stamp;
                DELETE FROM images WHERE path=$path AND image_index >= $count;
                """;
            command.Parameters.AddWithValue("$path", Key(image.SourcePath));
            command.Parameters.AddWithValue("$index", image.ImageIndex);
            command.Parameters.AddWithValue("$name", image.Name);
            command.Parameters.AddWithValue("$width", image.Width);
            command.Parameters.AddWithValue("$height", image.Height);
            command.Parameters.AddWithValue("$summary", image.Metadata);
            command.Parameters.AddWithValue("$search", search);
            command.Parameters.AddWithValue("$placed", image.Placement is not null);
            command.Parameters.AddWithValue("$stamp", stamp);
            command.Parameters.AddWithValue("$count", image.SourceImageCount);
            command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return result;
    }

    private static LoadedSkyImage ApplySolution(LoadedSkyImage image, SqliteConnection connection, SqliteTransaction transaction, string stamp)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT grid FROM solutions WHERE path=$path AND image_index=$index AND stamp=$stamp AND width=$width AND height=$height";
        command.Parameters.AddWithValue("$path", Key(image.SourcePath));
        command.Parameters.AddWithValue("$index", image.ImageIndex);
        command.Parameters.AddWithValue("$stamp", stamp);
        command.Parameters.AddWithValue("$width", image.Width);
        command.Parameters.AddWithValue("$height", image.Height);
        if (command.ExecuteScalar() is not string json)
        {
            return image;
        }
        var grid = JsonSerializer.Deserialize(json, LibraryJsonContext.Default.SkyCoordinateArray)
            ?? throw new InvalidDataException("Empty saved plate solution.");
        return image with { Placement = ImagePlacement.FromSampleGrid("ASTAP · blind solve", grid), PlacementWarning = null };
    }

    internal void SaveSolution(string sourcePath, int imageIndex, int width, int height, SourceSnapshot snapshot,
        ImagePlacement placement, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!snapshot.IsCurrent(token))
        {
            throw new IOException("The source changed while ASTAP was solving. Solve the current image again.");
        }
        string stamp = Stamp(snapshot);
        string grid = JsonSerializer.Serialize(placement.CaptureSampleGrid(), LibraryJsonContext.Default.SkyCoordinateArray);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO solutions(path,image_index,stamp,width,height,grid) VALUES($path,$index,$stamp,$width,$height,$grid)
            ON CONFLICT(path,image_index) DO UPDATE SET stamp=$stamp,width=$width,height=$height,grid=$grid;
            UPDATE images SET placed=1 WHERE path=$path AND image_index=$index AND stamp=$stamp;
            """;
        command.Parameters.AddWithValue("$path", Key(sourcePath));
        command.Parameters.AddWithValue("$index", imageIndex);
        command.Parameters.AddWithValue("$stamp", stamp);
        command.Parameters.AddWithValue("$width", width);
        command.Parameters.AddWithValue("$height", height);
        command.Parameters.AddWithValue("$grid", grid);
        command.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    internal ImagePlacement SolutionForExport(LoadedSkyImage image, SourceSnapshot snapshot)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var current = ApplySolution(image with { Placement = null }, connection, transaction, Stamp(snapshot));
        return current.Placement ?? throw new InvalidOperationException("No saved ASTAP solution matches the current source. Reload and blind solve the image before saving a solved copy.");
    }

    /// <summary>Scans archives recursively; unchanged files reuse retained metadata without decoding.</summary>
    public async Task ScanAsync(CachedSkyImageLoader loader, IProgress<LibraryScanProgress>? progress = null,
        CancellationToken token = default)
    {
        await _scanGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() => Scan(loader, progress, token), token).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void Scan(CachedSkyImageLoader loader, IProgress<LibraryScanProgress>? progress, CancellationToken token)
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        // Recheck individual imports too, including files no longer inside a watched root.
        foreach (var entry in Search(token: token))
        {
            files.Add(entry.SourcePath);
        }
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (string root in GetFolders())
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(root))
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", enumeration))
                {
                    token.ThrowIfCancellationRequested();
                    if (SkyImageLoader.IsSupported(file))
                    {
                        files.Add(Key(file));
                    }
                }
            }
        }
        int checkedCount = 0, updated = 0, failed = 0;
        foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(file))
                {
                    MarkUnavailable(file, "Source file is unavailable.", true);
                }
                else
                {
                    if (IsUnchanged(file, token))
                    {
                        progress?.Report(new(++checkedCount, updated, failed, file));
                        continue;
                    }
                    var snapshot = SourceSnapshot.Capture(file, token);
                    var loaded = loader.LoadForBrowsing(file, token);
                    Remember(loaded, snapshot, token);
                    updated++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
                NotSupportedException or ArgumentException or InvalidOperationException or OverflowException or System.Xml.XmlException)
            {
                MarkUnavailable(file, exception.Message, !File.Exists(file));
                failed++;
            }
            progress?.Report(new(++checkedCount, updated, failed, file));
        }
    }

    private bool IsUnchanged(string path, CancellationToken token)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stamp FROM images WHERE path=$path AND error IS NULL AND missing=0 LIMIT 1";
        command.Parameters.AddWithValue("$path", Key(path));
        if (command.ExecuteScalar() is not string json)
        { return false; }
        SourceSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(json, CacheJsonContext.Default.SourceSnapshot);
        }
        catch (JsonException)
        {
            return false;
        }
        if (snapshot?.Files is not { Length: > 0 and <= 4096 })
        { return false; }
        // Frequent background scans inspect file facts only; actual loads additionally verify
        // sampled hashes through the preview cache and validate external solution signatures.
        foreach (var stamp in snapshot.Files)
        {
            token.ThrowIfCancellationRequested();
            var file = new FileInfo(stamp.Path);
            if (!file.Exists || file.Length != stamp.Length || file.LastWriteTimeUtc.Ticks != stamp.LastWriteTicks ||
                file.CreationTimeUtc.Ticks != stamp.CreationTicks)
            { return false; }
        }
        return true;
    }

    private void MarkUnavailable(string path, string error, bool missing)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO images(path,image_index,name,width,height,summary,search_text,placed,missing,error)
            VALUES($path,0,$name,0,0,'',$search,0,$missing,$error)
            ON CONFLICT(path,image_index) DO NOTHING;
            UPDATE images SET missing=$missing,error=$error WHERE path=$path;
            """;
        command.Parameters.AddWithValue("$path", Key(path));
        command.Parameters.AddWithValue("$name", Path.GetFileNameWithoutExtension(path));
        command.Parameters.AddWithValue("$search", Normalize(Path.GetFileName(path)));
        command.Parameters.AddWithValue("$missing", missing);
        command.Parameters.AddWithValue("$error", error.Length > 2048 ? error[..2048] : error);
        command.ExecuteNonQuery();
    }
}

[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(SkyCoordinate[]))]
internal partial class LibraryJsonContext : JsonSerializerContext;
