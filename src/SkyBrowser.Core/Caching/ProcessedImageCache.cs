using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Caching;

public sealed record ImageRenderPreference(DisplayMode Mode, SkyFinishSettings FinishSettings);
public sealed record RecentImageFolder(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : Path;

    public override string ToString() => Name;
}

public sealed record ImageCacheStatistics(
    int PreviewCount,
    long StoredBytes,
    long LimitBytes,
    long Hits,
    long Misses,
    string DatabasePath,
    string? LastError
)
{
    public bool IsAvailable { get; init; } = true;
}

/// <summary>Persistent SQLite preview store. All I/O is synchronous and belongs on a worker thread.</summary>
public sealed class ProcessedImageCache : IDisposable
{
    public const long DefaultLimitBytes = 10L * 1024 * 1024 * 1024;
    private const int MaximumPreviewBytes = 2048 * 2048 * 4;
    private const int MaximumMetadataCharacters = 1048576;
    private readonly object _initialization = new();
    private readonly string _connectionString;
    private bool _initialized, _disposed;
    private long _hits, _misses;

    public string DatabasePath { get; }

    public long LimitBytes { get; }

    public string? LastError { get; private set; }

    public static ProcessedImageCache Default { get; } = new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyBrowser", "Cache"));

    public ProcessedImageCache(string directory, long limitBytes = DefaultLimitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
        DatabasePath = System.IO.Path.Combine(System.IO.Path.GetFullPath(directory), "previews-v1.sqlite3");
        LimitBytes = limitBytes;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 1
        }.ToString();
    }

    internal static string CanonicalPath(string path) => OperatingSystem.IsWindows()
        ? System.IO.Path.GetFullPath(path).ToUpperInvariant()
        : System.IO.Path.GetFullPath(path);

    internal static string ProfileKey(DisplayMode mode, SkyFinishSettings settings)
    {
        // Cached pixels depend on both the selected settings and the processing assemblies.
        // A changed pipeline gets a new key instead of reusing a preview from an older build.
        string pipeline = $"preview-3/debayer/2048/mesh32/{typeof(SkyImageLoader).Assembly.ManifestModule.ModuleVersionId}/{typeof(XISFLib.XisfReader).Assembly.ManifestModule.ModuleVersionId}/{typeof(ImageMagick.MagickImage).Assembly.ManifestModule.ModuleVersionId}";
        string parameters = JsonSerializer.Serialize(new ImageRenderPreference(mode, settings), CacheJsonContext.Default.ImageRenderPreference);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pipeline + parameters)));
    }

    internal IReadOnlyList<LoadedSkyImage>? Read(string sourcePath, string profile, int? onlyImage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT image_index, image_count, source_json, metadata_json, pixels, checksum FROM previews WHERE source_path=$path AND profile_key=$profile" +
                (onlyImage is null ? "" : " AND image_index=$index") +
                " ORDER BY image_index";
            command.Parameters.AddWithValue("$path", CanonicalPath(sourcePath));
            command.Parameters.AddWithValue("$profile", profile);
            if (onlyImage is { } index)
            {
                command.Parameters.AddWithValue("$index", index);
            }

            List<LoadedSkyImage> images = [];
            int expectedCount = -1;
            using (var reader = command.ExecuteReader())
            {
                SourceSnapshot? snapshot = null;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (snapshot is null)
                    {
                        snapshot = JsonSerializer.Deserialize(reader.GetString(2), CacheJsonContext.Default.SourceSnapshot);
                        if (snapshot is null ||
                            !snapshot.IsCurrent(token) ||
                            !snapshot.Files.Any(f => CanonicalPath(f.Path) == CanonicalPath(sourcePath)))
                        {
                            Miss();
                            return null;
                        }

                        expectedCount = reader.GetInt32(1);
                        if (expectedCount is < 1 or > 10000)
                        {
                            throw new InvalidDataException("Invalid cached source image count.");
                        }
                    }

                    string metadataJson = reader.GetString(3);
                    if (metadataJson.Length > MaximumMetadataCharacters)
                    {
                        throw new InvalidDataException("Invalid cached image metadata size.");
                    }

                    byte[] compressed = (byte[])reader[4];
                    if (compressed.Length > MaximumPreviewBytes + 65536 ||
                        Checksum(metadataJson, compressed) != reader.GetString(5))
                    {
                        throw new InvalidDataException("Cached preview checksum failed.");
                    }

                    var metadata = JsonSerializer.Deserialize(metadataJson, CacheJsonContext.Default.CachedImageMetadata) ?? throw new InvalidDataException("Empty cached image metadata.");
                    if (metadata.PreviewWidth is < 1 or > 2048 ||
                        metadata.PreviewHeight is < 1 or > 2048 ||
                        metadata.Width < 1 ||
                        metadata.Height < 1 ||
                        metadata.ImageIndex != reader.GetInt32(0) ||
                        metadata.ImageCount != expectedCount ||
                        metadata.ImageIndex < 0 ||
                        metadata.ImageIndex >= expectedCount)
                    {
                        throw new InvalidDataException("Invalid cached image dimensions or index.");
                    }

                    if (metadata.FinishSettings is null)
                    {
                        throw new InvalidDataException("Missing cached finishing settings.");
                    }

                    metadata.FinishSettings.Validate();
                    if (metadata.Details?.Entries is not { Length: <= 2048 } detailEntries ||
                        detailEntries.Any(entry => entry is null ||
                        entry.Name is null ||
                        entry.Value is null ||
                        entry.Source is null ||
                        entry.Comment is null))
                    {
                        throw new InvalidDataException("Invalid cached image details.");
                    }

                    byte[] pixels = new byte[checked(metadata.PreviewWidth * metadata.PreviewHeight * 4)];
                    // Dimensions bound the allocation before decompression. Read exactly that
                    // many bytes and reject both truncated and oversized cached payloads.
                    using var input = new MemoryStream(compressed, writable: false);
                    using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
                    decompressor.ReadExactly(pixels);
                    if (decompressor.ReadByte() != -1)
                    {
                        throw new InvalidDataException("Unexpected cached preview length.");
                    }

                    ImagePlacement? placement = metadata.SkyGrid is null
                        ? null
                        : ImagePlacement.FromSampleGrid(metadata.PlacementDescription ?? "Cached sky solution", metadata.SkyGrid);
                    images.Add(new LoadedSkyImage(
                        System.IO.Path.GetFullPath(sourcePath),
                        metadata.ImageIndex,
                        metadata.Name,
                        metadata.Width,
                        metadata.Height,
                        new(metadata.PreviewWidth, metadata.PreviewHeight, pixels),
                        metadata.DisplayDescription,
                        placement,
                        metadata.PlacementWarning,
                        metadata.Metadata
                    )
                    {
                        FinishSettings = metadata.FinishSettings,
                        SourceImageCount = expectedCount,
                        IsFromCache = true,
                        RenderMode = metadata.Mode,
                        Details = metadata.Details
                    });
                }
            }

            if (images.Count == 0 ||
                (onlyImage is null &&
                (images.Count != expectedCount ||
                images.Where((image, index) => image.ImageIndex != index)
                .Any())))
            {
                Miss();
                return null;
            }

            // Refresh recency only after every requested image has passed validation.
            using var touch = connection.CreateCommand();
            touch.CommandText = "UPDATE previews SET last_used=$time WHERE source_path=$path AND profile_key=$profile";
            touch.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
            touch.Parameters.AddWithValue("$path", CanonicalPath(sourcePath));
            touch.Parameters.AddWithValue("$profile", profile);
            touch.ExecuteNonQuery();
            Interlocked.Increment(ref _hits);
            return images;
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            Miss();
            return null;
        }
    }

    internal void Store(
        string sourcePath,
        string profile,
        SourceSnapshot snapshot,
        IReadOnlyList<LoadedSkyImage> images,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();
            List<(LoadedSkyImage Image, string Metadata, byte[] Compressed)> rows = [];
            foreach (var image in images)
            {
                token.ThrowIfCancellationRequested();
                var metadata = new CachedImageMetadata(
                    image.ImageIndex,
                    image.SourceImageCount,
                    image.Name,
                    image.Width,
                    image.Height,
                    image.Preview.Width,
                    image.Preview.Height,
                    image.DisplayDescription,
                    image.PlacementWarning,
                    image.Metadata,
                    image.FinishSettings,
                    image.Placement?.Description,
                    image.Placement?.CaptureSampleGrid()
                )
                {
                    Mode = image.RenderMode,
                    Details = image.Details
                };
                string json = JsonSerializer.Serialize(metadata, CacheJsonContext.Default.CachedImageMetadata);
                using var output = new MemoryStream();
                using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
                {
                    compressor.Write(image.Preview.Bgra);
                }

                byte[] bytes = output.ToArray();
                if (json.Length <= MaximumMetadataCharacters && bytes.LongLength + json.Length * 2L < LimitBytes)
                {
                    rows.Add((image, json, bytes));
                }
            }

            if (rows.Count == 0 || !snapshot.IsCurrent(token))
            {
                return;
            }

            string sourceJson = JsonSerializer.Serialize(snapshot, CacheJsonContext.Default.SourceSnapshot);
            using var connection = Open();
            // A multi-image document is saved as one transaction; readers must never see a
            // partially replaced document after a failed write or cancellation.
            using var transaction = connection.BeginTransaction();
            using (var stale = connection.CreateCommand())
            {
                stale.Transaction = transaction;
                stale.CommandText = "DELETE FROM previews WHERE source_path=$path AND source_json<>$source";
                stale.Parameters.AddWithValue("$path", CanonicalPath(sourcePath));
                stale.Parameters.AddWithValue("$source", sourceJson);
                stale.ExecuteNonQuery();
            }

            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO previews(source_path,profile_key,image_index,image_count,source_json,metadata_json,pixels,checksum,size_bytes,last_used)
                    VALUES($path,$profile,$index,$count,$source,$metadata,$pixels,$checksum,$size,$time)
                    ON CONFLICT(source_path,profile_key,image_index) DO UPDATE SET image_count=excluded.image_count,source_json=excluded.source_json,
                    metadata_json=excluded.metadata_json,pixels=excluded.pixels,checksum=excluded.checksum,size_bytes=excluded.size_bytes,last_used=excluded.last_used;
                    """;
                command.Parameters.AddWithValue("$path", CanonicalPath(sourcePath));
                command.Parameters.AddWithValue("$profile", profile);
                command.Parameters.AddWithValue("$index", row.Image.ImageIndex);
                command.Parameters.AddWithValue("$count", row.Image.SourceImageCount);
                command.Parameters.AddWithValue("$source", sourceJson);
                command.Parameters.AddWithValue("$metadata", row.Metadata);
                command.Parameters.AddWithValue("$pixels", row.Compressed);
                command.Parameters.AddWithValue("$checksum", Checksum(row.Metadata, row.Compressed));
                command.Parameters.AddWithValue("$size", row.Compressed.LongLength + (row.Metadata.Length + sourceJson.Length) * 2L);
                command.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                command.ExecuteNonQuery();
            }

            Prune(connection, transaction, token);
            token.ThrowIfCancellationRequested();
            transaction.Commit();
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "PRAGMA incremental_vacuum(128)";
            vacuum.ExecuteNonQuery();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
        }
    }

    public bool HasPreview(string path)
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM previews WHERE source_path=$path LIMIT 1";
            command.Parameters.AddWithValue("$path", CanonicalPath(path));
            return command.ExecuteScalar() is not null;
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            return false;
        }
    }

    public HashSet<string> GetCachedSourcePaths(CancellationToken token = default)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT source_path FROM previews";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                paths.Add(reader.GetString(0));
            }
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
        }

        return paths;
    }

    public static string SourceKey(string path) => CanonicalPath(path);

    internal int? GetImageCount(string path, CancellationToken token)
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT image_count,source_json FROM previews WHERE source_path=$path ORDER BY last_used DESC LIMIT 1";
            command.Parameters.AddWithValue("$path", CanonicalPath(path));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize(reader.GetString(1), CacheJsonContext.Default.SourceSnapshot);
            int count = reader.GetInt32(0);
            return count is > 0 and <= 10000 && snapshot is not null && snapshot.IsCurrent(token) ? count : null;
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            return null;
        }
    }

    public Dictionary<int, ImageRenderPreference> GetPreferences(string path)
    {
        Dictionary<int, ImageRenderPreference> result = [];
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT image_index,preference_json FROM preferences WHERE source_path=$path";
            command.Parameters.AddWithValue("$path", CanonicalPath(path));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var preference = JsonSerializer.Deserialize(reader.GetString(1), CacheJsonContext.Default.ImageRenderPreference);
                if (preference?.FinishSettings is null || !Enum.IsDefined(preference.Mode))
                {
                    continue;
                }

                preference.FinishSettings.Validate();
                result[reader.GetInt32(0)] = preference;
            }
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
        }

        return result;
    }

    public void SavePreference(string path, int imageIndex, DisplayMode mode, SkyFinishSettings settings)
    {
        settings.Validate();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO preferences(source_path,image_index,preference_json) VALUES($path,$index,$json) ON CONFLICT(source_path,image_index) DO UPDATE SET preference_json=excluded.preference_json";
            command.Parameters.AddWithValue("$path", CanonicalPath(path));
            command.Parameters.AddWithValue("$index", imageIndex);
            command.Parameters.AddWithValue(
                "$json",
                JsonSerializer.Serialize(new ImageRenderPreference(mode, settings), CacheJsonContext.Default.ImageRenderPreference)
            );
            command.ExecuteNonQuery();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
        }
    }

    public void RememberFolder(string path)
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO recent_folders(path,last_used) VALUES($path,$time) ON CONFLICT(path) DO UPDATE SET last_used=excluded.last_used; DELETE FROM recent_folders WHERE path NOT IN (SELECT path FROM recent_folders ORDER BY last_used DESC LIMIT 12)";
            command.Parameters.AddWithValue("$path", System.IO.Path.GetFullPath(path));
            command.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
            command.ExecuteNonQuery();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
        }
    }

    public RecentImageFolder[] GetRecentFolders()
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path FROM recent_folders ORDER BY last_used DESC LIMIT 12";
            using var reader = command.ExecuteReader();
            List<RecentImageFolder> result = [];
            while (reader.Read())
            {
                result.Add(new(reader.GetString(0)));
            }

            return result.ToArray();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            return [];
        }
    }

    public ImageCacheStatistics GetStatistics()
    {
        int count = 0;
        long size = 0;
        bool available = true;
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), COALESCE(SUM(size_bytes),0) FROM previews";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                count = reader.GetInt32(0);
                size = reader.GetInt64(1);
            }
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            available = false;
        }

        return new(count, size, LimitBytes, Interlocked.Read(ref _hits), Interlocked.Read(ref _misses), DatabasePath, LastError)
        {
            IsAvailable = available
        };
    }

    public void ClearPreviews()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM previews; PRAGMA wal_checkpoint(TRUNCATE); VACUUM";
        command.ExecuteNonQuery();
    }

    /// <summary>Checks that the chosen cache is writable and applies a changed limit immediately.</summary>
    public void ApplySizeLimit()
    {
        using var connection = Open();
        using (var transaction = connection.BeginTransaction())
        {
            Prune(connection, transaction, CancellationToken.None);
            transaction.Commit();
        }

        Compact(connection);
    }

    /// <summary>Merges a snapshot into another cache, retaining the original database and target-only entries.</summary>
    public bool CopyTo(ProcessedImageCache destination)
    {
        if (CanonicalPath(DatabasePath) == CanonicalPath(destination.DatabasePath))
        {
            destination.ApplySizeLimit();
            return true;
        }

        SqliteConnection source;
        try
        {
            source = Open();
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            LastError = exception.Message;
            destination.ApplySizeLimit();
            return false;
        }

        using var sourceScope = source;
        using var connection = destination.Open();
        using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $source AS previous";
            attach.Parameters.AddWithValue("$source", DatabasePath);
            attach.ExecuteNonQuery();
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            using var merge = connection.CreateCommand();
            merge.Transaction = transaction;
            merge.CommandTimeout = 0;
            merge.CommandText = """
                INSERT INTO previews(source_path,profile_key,image_index,image_count,source_json,metadata_json,pixels,checksum,size_bytes,last_used)
                  SELECT source_path,profile_key,image_index,image_count,source_json,metadata_json,pixels,checksum,size_bytes,last_used FROM previous.previews WHERE 1
                  ON CONFLICT(source_path,profile_key,image_index) DO UPDATE SET image_count=excluded.image_count,source_json=excluded.source_json,
                    metadata_json=excluded.metadata_json,pixels=excluded.pixels,checksum=excluded.checksum,size_bytes=excluded.size_bytes,last_used=excluded.last_used
                    WHERE excluded.last_used > previews.last_used;
                INSERT INTO preferences(source_path,image_index,preference_json)
                  SELECT source_path,image_index,preference_json FROM previous.preferences WHERE 1
                  ON CONFLICT(source_path,image_index) DO UPDATE SET preference_json=excluded.preference_json;
                INSERT INTO recent_folders(path,last_used) SELECT path,last_used FROM previous.recent_folders WHERE 1
                  ON CONFLICT(path) DO UPDATE SET last_used=MAX(recent_folders.last_used,excluded.last_used);
                DELETE FROM recent_folders WHERE path NOT IN (SELECT path FROM recent_folders ORDER BY last_used DESC LIMIT 12);
                """;
            merge.ExecuteNonQuery();
            destination.Prune(connection, transaction, CancellationToken.None);
            transaction.Commit();
        }
        finally
        {
            using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE previous";
            detach.ExecuteNonQuery();
        }

        Compact(connection);
        return true;
    }

    private static void Compact(SqliteConnection connection)
    {
        using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "PRAGMA incremental_vacuum";
            using var reader = vacuum.ExecuteReader();
            while (reader.Read())
            {
            }
        }

        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        checkpoint.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            lock (_initialization)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath)!);
                connection.Open();
                if (!_initialized)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        PRAGMA auto_vacuum=INCREMENTAL;
                        PRAGMA journal_mode=WAL;
                        CREATE TABLE IF NOT EXISTS previews(
                          source_path TEXT NOT NULL, profile_key TEXT NOT NULL, image_index INTEGER NOT NULL, image_count INTEGER NOT NULL,
                          source_json TEXT NOT NULL, metadata_json TEXT NOT NULL, pixels BLOB NOT NULL, checksum TEXT NOT NULL,
                          size_bytes INTEGER NOT NULL, last_used INTEGER NOT NULL, PRIMARY KEY(source_path,profile_key,image_index));
                        CREATE INDEX IF NOT EXISTS previews_lru ON previews(last_used);
                        CREATE TABLE IF NOT EXISTS preferences(source_path TEXT NOT NULL,image_index INTEGER NOT NULL,preference_json TEXT NOT NULL,PRIMARY KEY(source_path,image_index));
                        CREATE TABLE IF NOT EXISTS recent_folders(path TEXT PRIMARY KEY,last_used INTEGER NOT NULL);
                        """;
                    command.ExecuteNonQuery();
                    _initialized = true;
                }
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void Prune(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        using var total = connection.CreateCommand();
        total.Transaction = transaction;
        total.CommandText = "SELECT COALESCE(SUM(size_bytes),0) FROM previews";
        long remaining = (long)total.ExecuteScalar()!;
        if (remaining <= LimitBytes)
        {
            return;
        }

        List<long> remove = [];
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT rowid,size_bytes FROM previews ORDER BY last_used,rowid";
            using var reader = query.ExecuteReader();
            while (remaining > LimitBytes && reader.Read())
            {
                remove.Add(reader.GetInt64(0));
                remaining -= reader.GetInt64(1);
            }
        }

        foreach (long id in remove)
        {
            token.ThrowIfCancellationRequested();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM previews WHERE rowid=$id";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }
    }

    private static string Checksum(string metadata, byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(metadata));
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void Miss() => Interlocked.Increment(ref _misses);

    internal static bool IsCacheFailure(Exception exception) => exception is
        SqliteException or
        IOException or
        InvalidDataException or
        UnauthorizedAccessException or
        JsonException or
        ArgumentException or
        InvalidOperationException or
        OverflowException or
        XmlException;

    public void Dispose()
    {
        _disposed = true;
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}

internal sealed record CachedImageMetadata(
    int ImageIndex,
    int ImageCount,
    string Name,
    int Width,
    int Height,
    int PreviewWidth,
    int PreviewHeight,
    string DisplayDescription,
    string? PlacementWarning,
    string Metadata,
    SkyFinishSettings FinishSettings,
    string? PlacementDescription,
    SkyCoordinate[]? SkyGrid
)
{
    public DisplayMode Mode { get; init; }

    public ImageMetadataSnapshot Details { get; init; } = ImageMetadataSnapshot.Empty;
}

[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CachedImageMetadata))]
[JsonSerializable(typeof(SourceSnapshot))]
[JsonSerializable(typeof(ImageRenderPreference))]
internal partial class CacheJsonContext : JsonSerializerContext;
