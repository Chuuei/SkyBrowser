using System.Text.Json;
using System.Text.Json.Serialization;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Configuration;

public sealed record ApplicationOptions
{
    public static string DefaultCacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyBrowser", "Cache");

    public int Version { get; init; } = 1;

    public string CacheDirectory { get; init; } = DefaultCacheDirectory;

    public double CacheSizeGb { get; init; } = 10;

    public long CacheLimitBytes => checked((long)(CacheSizeGb * 1024 * 1024 * 1024));

    public DisplayMode DefaultDisplayMode { get; init; } = DisplayMode.Automatic;

    public SkyFinishSettings DefaultFinishSettings { get; init; } = SkyFinishSettings.Default;

    public ObserverSettings? Observer { get; init; }

    public bool ShowGrid { get; init; } = true;

    public bool ShowConstellations { get; init; } = true;

    public bool ShowLabels { get; init; } = true;

    public bool ShowOutlines { get; init; } = true;

    public bool BlendOverlaps { get; init; } = true;

    public void Validate()
    {
        if (Version != 1)
        {
            throw new ArgumentException($"Unsupported options version {Version}.");
        }

        if (string.IsNullOrWhiteSpace(CacheDirectory) || !Path.IsPathFullyQualified(CacheDirectory))
        {
            throw new ArgumentException("Choose an absolute path for the cache folder.");
        }

        _ = Path.GetFullPath(CacheDirectory);
        if (!double.IsFinite(CacheSizeGb) || CacheSizeGb is < 0.1 or > 10000)
        {
            throw new ArgumentException("Choose a cache size between 0.1 and 10,000 GB.");
        }

        if (!Enum.IsDefined(DefaultDisplayMode))
        {
            throw new ArgumentException("Choose a supported default stretch variant.");
        }

        ArgumentNullException.ThrowIfNull(DefaultFinishSettings);
        DefaultFinishSettings.Validate();
        Observer?.Validate();
    }

    public ApplicationOptions Normalize()
    {
        Validate();
        return this with
        {
            CacheDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(CacheDirectory))
        };
    }
}

/// <summary>Atomic, versioned user options, independent of the disposable image cache.</summary>
public sealed class ApplicationOptionsStore
{
    private readonly object _gate = new();

    public string FilePath { get; }

    public ApplicationOptions Defaults { get; }

    public bool NeedsFirstRunSetup => !File.Exists(FilePath) && !File.Exists(FilePath + ".bak");

    public string? LastError { get; private set; }

    public static ApplicationOptionsStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyBrowser", "settings.json"));

    public ApplicationOptionsStore(string path, ApplicationOptions? defaults = null)
    {
        FilePath = Path.GetFullPath(path);
        Defaults = (defaults ?? new()).Normalize();
    }

    public ApplicationOptions Read()
    {
        lock (_gate)
        {
            LastError = null;
            if (TryRead(FilePath, out var options))
            {
                return options!;
            }

            if (TryRead(FilePath + ".bak", out options))
            {
                LastError = "Recovered options from the previous saved version.";
                return options!;
            }

            if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak"))
            {
                // Upgrade the former location-only preference without tying new options to the cache path.
                try
                {
                    string legacy = Path.Combine(Defaults.CacheDirectory, "observer-location.json");
                    if (File.Exists(legacy))
                    {
                        using var stream = File.OpenRead(legacy);
                        if (stream.Length <= 4096)
                        {
                            var location = JsonSerializer.Deserialize(stream, OptionsJsonContext.Default.ObserverLocation);
                            if (location is not null)
                            {
                                location.Validate();
                                return Defaults with
                                {
                                    Observer = new(location, true, DateTimeOffset.UtcNow)
                                };
                            }
                        }
                    }
                }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    LastError = exception.Message;
                }
            }

            return Defaults;
        }
    }

    public void Write(ApplicationOptions options)
    {
        options = options.Normalize();
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                // Flush the complete replacement before moving it over the live settings.
                // Preserve a known-good backup if the current file can still be read.
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, options, OptionsJsonContext.Default.ApplicationOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (TryRead(FilePath, out _))
                {
                    File.Copy(FilePath, FilePath + ".bak", overwrite: true);
                }

                File.Move(temporary, FilePath, overwrite: true);
                LastError = null;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    private bool TryRead(string path, out ApplicationOptions? options)
    {
        options = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            if (stream.Length > 65536)
            {
                throw new InvalidDataException("The options file is too large.");
            }

            options = (JsonSerializer.Deserialize(stream, OptionsJsonContext.Default.ApplicationOptions) ?? throw new InvalidDataException("The options file is empty.")).Normalize();
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            LastError = exception.Message;
            return false;
        }
    }

    private static bool IsReadFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException;
}

[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyProperties = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ApplicationOptions))]
[JsonSerializable(typeof(ObserverLocation))]
internal partial class OptionsJsonContext : JsonSerializerContext;
