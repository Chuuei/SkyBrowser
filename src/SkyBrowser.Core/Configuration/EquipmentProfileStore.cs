using System.Text.Json;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Core.Configuration;

/// <summary>Custom equipment lives beside settings, independently of previews and factory presets.</summary>
public sealed class EquipmentProfileStore(string filePath)
{
    private const int MaximumFileBytes = 256 * 1024;
    public string FilePath { get; } = Path.GetFullPath(filePath);
    public string? LastError { get; private set; }
    private readonly object _gate = new();

    public EquipmentProfile[] Read()
    {
        lock (_gate)
        {
            LastError = null;
            if (TryRead(FilePath, out var profiles))
            { return profiles!; }
            if (TryRead(FilePath + ".bak", out profiles))
            {
                LastError = "Recovered equipment profiles from the previous saved version.";
                return profiles!;
            }
            return [];
        }
    }

    public void Write(IEnumerable<EquipmentProfile> profiles)
    {
        var items = profiles.ToArray();
        Validate(items);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(items, FramingJsonContext.Default.EquipmentProfileArray);
        if (content.Length > MaximumFileBytes)
        {
            throw new ArgumentException("Equipment profiles exceed the 256 KB file limit.");
        }
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content);
                    stream.Flush(true);
                }
                if (TryRead(FilePath, out _))
                { File.Copy(FilePath, FilePath + ".bak", true); }
                File.Move(temporary, FilePath, true);
                LastError = null;
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
    }

    private static void Validate(EquipmentProfile[] profiles)
    {
        if (profiles.Length > 100 || profiles.Select(p => p?.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Length)
        {
            throw new ArgumentException("Save at most 100 custom profiles with distinct names.");
        }
        foreach (var profile in profiles)
        {
            if (profile is null)
            { throw new ArgumentException("Invalid equipment profile."); }
            profile.Validate();
        }
    }

    private bool TryRead(string path, out EquipmentProfile[]? profiles)
    {
        profiles = null;
        try
        {
            if (!File.Exists(path))
            { return false; }
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumFileBytes)
            { throw new InvalidDataException("Equipment file is too large."); }
            profiles = JsonSerializer.Deserialize(stream, FramingJsonContext.Default.EquipmentProfileArray) ?? throw new InvalidDataException("Empty equipment file.");
            Validate(profiles);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LastError = "Equipment profiles could not be read: " + exception.Message;
            return false;
        }
    }
}
