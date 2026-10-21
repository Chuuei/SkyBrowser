using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Astronomy;

public sealed record SurveyDefinition(string Id, string Name, string Attribution)
{
    public static SurveyDefinition Color
    {
        get;
    } = new("CDS/P/DSS2/color", "DSS2 colour (HiPS)",
        "Digitized Sky Survey – STScI/NASA; coloured and HEALPix by CDS (CNRS/Unistra, ODbL-1.0). Cutouts: CDS hips2fits.");
    public static SurveyDefinition Red
    {
        get;
    } = new("CDS/P/DSS2/red", "DSS2 red (HiPS)",
        "Digitized Sky Survey – STScI/NASA; HiPS by CDS (CNRS/Unistra, ODbL-1.0). Cutouts: CDS hips2fits.");
    public static IReadOnlyList<SurveyDefinition> All { get; } = [Color, Red];
    public override string ToString() => Name;
}

public sealed record SurveyRequest(string SurveyId, SkyCoordinate Center, double FieldOfView, int Size = 1024)
{
    public SurveyDefinition Survey => SurveyDefinition.All.Single(s => s.Id == SurveyId);
    public void Validate()
    {
        SkyGeometry.Validate(Center);
        if (!SurveyDefinition.All.Any(s => s.Id == SurveyId) || !double.IsFinite(FieldOfView) || FieldOfView is < 0.01 or > 160 || Size is < 128 or > 2048)
        {
            throw new ArgumentException("Choose a supported DSS survey, a field from 0.01 to 160 degrees and 128–2048 pixels.");
        }
    }

    public Uri DownloadUri
    {
        get
        {
            Validate();
            double scale = 2 * Math.Tan(FieldOfView * SkyCoordinate.Radians / 2) / (Size * SkyCoordinate.Radians);
            // Explicit WCS puts the center between the central pixels. The returned FITS
            // WCS, including its stored row direction, remains the authority for placement.
            string wcs = FormattableString.Invariant($$"""
                {"NAXIS":2,"NAXIS1":{{Size}},"NAXIS2":{{Size}},"WCSAXES":2,"CTYPE1":"RA---TAN","CTYPE2":"DEC--TAN","CUNIT1":"deg","CUNIT2":"deg","CRPIX1":{{(Size + 1) / 2d}},"CRPIX2":{{(Size + 1) / 2d}},"CRVAL1":{{SkyCoordinate.Normalize(Center.RightAscension)}},"CRVAL2":{{Center.Declination}},"CDELT1":{{-scale:R}},"CDELT2":{{scale:R}},"RADESYS":"ICRS"}
                """);
            return new("https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=" + Uri.EscapeDataString(SurveyId) +
                "&width=" + Size.ToString(CultureInfo.InvariantCulture) + "&height=" + Size.ToString(CultureInfo.InvariantCulture) +
                "&format=fits&wcs=" + Uri.EscapeDataString(wcs));
        }
    }
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DownloadUri.AbsoluteUri)));
    public override string ToString() => $"{Survey.Name} · {Center.RaText} {Center.DecText} · {FieldOfView:0.###}°";
}

public sealed record CachedSurvey(SurveyRequest Request, long Bytes);
public sealed record SurveyFrame(SurveyRequest Request, LoadedSkyImage Image, bool FromCache);

/// <summary>Opt-in CDS cutouts, bounded downloads and a separate, persistent LRU cache.</summary>
public sealed class SurveyImageService
{
    public const long CacheLimitBytes = 512L * 1024 * 1024;
    public const int MaximumResponseBytes = 32 * 1024 * 1024;
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    public string DatabasePath
    {
        get;
    }
    public long LimitBytes { get; }

    public SurveyImageService(string directory, HttpClient? client = null, long limitBytes = CacheLimitBytes)
    {
        if (limitBytes <= 0 || limitBytes > CacheLimitBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(limitBytes));
        }
        LimitBytes = limitBytes;
        _directory = Path.GetFullPath(directory);
        DatabasePath = Path.Combine(_directory, "surveys-v1.sqlite3");
        _client = client ?? DefaultClient;
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(_directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, DefaultTimeout = 10 }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS frames(key TEXT PRIMARY KEY,survey TEXT NOT NULL,ra REAL NOT NULL,
                dec REAL NOT NULL,fov REAL NOT NULL,size INTEGER NOT NULL,data BLOB NOT NULL,used INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    public CachedSurvey[] GetCached()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // SQLite permits values with the wrong storage type. Numeric getters can
        // silently turn damaged text into zero, so reject those rows before reading.
        command.CommandText = """
            SELECT key,survey,ra,dec,fov,size,length(data) FROM frames
            WHERE typeof(ra) IN ('integer','real') AND typeof(dec) IN ('integer','real')
                AND typeof(fov) IN ('integer','real') AND typeof(size)='integer'
                AND size BETWEEN 128 AND 2048 AND typeof(data)='blob' AND length(data)<=$limit
            ORDER BY used DESC,key;
            """;
        command.Parameters.AddWithValue("$limit", MaximumResponseBytes);
        using var reader = command.ExecuteReader();
        List<CachedSurvey> result = [];
        while (reader.Read())
        {
            try
            {
                var request = new SurveyRequest(reader.GetString(1), new(reader.GetDouble(2), reader.GetDouble(3)), reader.GetDouble(4), reader.GetInt32(5));
                request.Validate();
                if (request.Key == reader.GetString(0))
                {
                    result.Add(new(request, reader.GetInt64(6)));
                }
            }
            catch (ArgumentException)
            {
                // Skip invalid metadata just as offline restoration skips damaged FITS
                // payloads. Other valid regions must remain available without a download.
            }
        }
        return result.ToArray();
    }

    public void ClearCache()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM frames; VACUUM;";
        command.ExecuteNonQuery();
    }

    public async Task<SurveyFrame[]> LoadCachedAsync(CancellationToken token = default)
    {
        var entries = await Task.Run(GetCached, token).ConfigureAwait(false);
        List<SurveyFrame> frames = [];
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                frames.Add(await LoadCoreAsync(entry.Request, offlineOnly: true, touch: false, token).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
            {
                // A removed or damaged region must not prevent the remaining offline sky from loading.
            }
        }
        return frames.ToArray();
    }

    public Task<SurveyFrame> LoadAsync(SurveyRequest request, bool offlineOnly, CancellationToken token = default) =>
        LoadCoreAsync(request, offlineOnly, touch: true, token);

    private async Task<SurveyFrame> LoadCoreAsync(SurveyRequest request, bool offlineOnly, bool touch, CancellationToken token)
    {
        request.Validate();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            byte[]? cached = await Task.Run(() => Read(request.Key, touch), token).ConfigureAwait(false);
            if (cached is not null)
            {
                try
                {
                    var image = await Task.Run(() => Decode(request, cached, token), token).ConfigureAwait(false);
                    return new(request, image, true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await Task.Run(() => Delete(request.Key), token).ConfigureAwait(false);
                    if (offlineOnly)
                    {
                        throw new InvalidDataException("This saved survey region is damaged. Go online and load it again.", exception);
                    }
                }
            }
            if (offlineOnly)
            {
                throw new FileNotFoundException("This exact survey region is not cached. Choose a saved region or disable Offline only.");
            }
            using var message = new HttpRequestMessage(HttpMethod.Get, request.DownloadUri);
            message.Headers.UserAgent.ParseAdd("SkyBrowser/0.x (+https://github.com/Chuuei/SkyBrowser)");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                throw new InvalidDataException("The survey response exceeds the 32 MB download limit.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var data = new MemoryStream();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (data.Length + count > MaximumResponseBytes)
                {
                    throw new InvalidDataException("The survey response exceeds the 32 MB download limit.");
                }
                data.Write(buffer, 0, count);
            }
            byte[] bytes = data.ToArray();
            var decoded = await Task.Run(() => Decode(request, bytes, token), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => Store(request, bytes, token), token).ConfigureAwait(false);
            return new(request, decoded, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[]? Read(string key, bool touch)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM frames WHERE key=$key AND length(data)<=$limit";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$limit", MaximumResponseBytes);
        var bytes = command.ExecuteScalar() as byte[];
        if (bytes is not null && touch)
        {
            command.CommandText = "UPDATE frames SET used=$used WHERE key=$key";
            command.Parameters.AddWithValue("$used", DateTimeOffset.UtcNow.UtcTicks);
            command.ExecuteNonQuery();
        }
        return bytes;
    }

    private LoadedSkyImage Decode(SurveyRequest request, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < 2880 || bytes.Length > MaximumResponseBytes || !bytes.AsSpan(0, 9).SequenceEqual("SIMPLE  ="u8))
        {
            throw new InvalidDataException("The survey service did not return a valid FITS image.");
        }
        ValidateDimensions(request, bytes);
        Directory.CreateDirectory(_directory);
        string temporary = Path.Combine(_directory, ".survey-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            var images = SkyImageLoader.Load(temporary, DisplayMode.Automatic, token);
            if (images.Count != 1 || images[0].Width != request.Size || images[0].Height != request.Size || images[0].Placement is null)
            {
                throw new InvalidDataException("The survey response has unexpected dimensions or no supported astrometry.");
            }
            if (images[0].Placement!.Center.DistanceTo(request.Center) > Math.Max(0.001, request.FieldOfView / request.Size * 2))
            {
                throw new InvalidDataException("The survey response is centered on a different part of the sky.");
            }
            return images[0] with
            {
                Name = request.Survey.Name,
                Metadata = request.Survey.Attribution,
                Details = ImageMetadataSnapshot.Empty
            };
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateDimensions(SurveyRequest request, byte[] bytes)
    {
        // Reject unexpected allocations before asking the scientific reader to decode.
        Dictionary<string, int> dimensions = [];
        bool ended = false;
        for (int offset = 0; offset + 80 <= Math.Min(bytes.Length, 28800); offset += 80)
        {
            string name = Encoding.ASCII.GetString(bytes, offset, 8).Trim();
            if (name == "END")
            {
                ended = true;
                break;
            }
            if (name is not ("NAXIS" or "NAXIS1" or "NAXIS2" or "NAXIS3"))
            {
                continue;
            }
            string value = Encoding.ASCII.GetString(bytes, offset + 10, 70).Split('/')[0].Trim();
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || !dimensions.TryAdd(name, count))
            {
                throw new InvalidDataException("Invalid survey FITS dimensions.");
            }
        }
        if (!ended || dimensions.GetValueOrDefault("NAXIS") is not (2 or 3) ||
            dimensions.GetValueOrDefault("NAXIS1") != request.Size || dimensions.GetValueOrDefault("NAXIS2") != request.Size ||
            (dimensions.GetValueOrDefault("NAXIS") == 3 && dimensions.GetValueOrDefault("NAXIS3") is not (1 or 3 or 4)))
        {
            throw new InvalidDataException("The survey FITS dimensions do not match the requested, bounded image size.");
        }
    }

    private void Delete(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM frames WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private void Store(SurveyRequest request, byte[] bytes, CancellationToken token)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO frames(key,survey,ra,dec,fov,size,data,used)
            VALUES($key,$survey,$ra,$dec,$fov,$size,$data,$used)
            """;
        command.Parameters.AddWithValue("$key", request.Key);
        command.Parameters.AddWithValue("$survey", request.SurveyId);
        command.Parameters.AddWithValue("$ra", request.Center.RightAscension);
        command.Parameters.AddWithValue("$dec", request.Center.Declination);
        command.Parameters.AddWithValue("$fov", request.FieldOfView);
        command.Parameters.AddWithValue("$size", request.Size);
        command.Parameters.AddWithValue("$data", bytes);
        command.Parameters.AddWithValue("$used", DateTimeOffset.UtcNow.UtcTicks);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = """
            DELETE FROM frames WHERE key IN (SELECT key FROM
                (SELECT key,SUM(length(data)) OVER (ORDER BY used DESC,key) AS total FROM frames)
                WHERE total>$limit);
            """;
        command.Parameters.AddWithValue("$limit", LimitBytes);
        command.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }
}
