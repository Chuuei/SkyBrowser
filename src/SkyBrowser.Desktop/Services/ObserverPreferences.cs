using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Services;

public sealed class ObserverPreferences(string path)
{
    public ObserverLocation? Read()
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            if (stream.Length > 4096)
            {
                return null;
            }

            var location = JsonSerializer.Deserialize(stream, ObserverJsonContext.Default.ObserverLocation);
            location?.Validate();
            return location;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public void Write(ObserverLocation? location)
    {
        location?.Validate();
        if (location is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(location, ObserverJsonContext.Default.ObserverLocation));
            File.Move(temporary, path, overwrite: true);
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

public static class ObserverTimeInput
{
    public static DateTimeOffset Resolve(DateTime date, TimeSpan time, TimeZoneInfo zone)
    {
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            throw new ArgumentException("Enter a time between 00:00:00 and 23:59:59.");
        }

        var local = DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            throw new ArgumentException("This local time is skipped by daylight saving. Choose another time or use UTC.");
        }

        if (zone.IsAmbiguousTime(local))
        {
            throw new ArgumentException("This local time occurs twice during daylight saving changes. Use UTC to choose the exact instant.");
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ObserverLocation))]
internal partial class ObserverJsonContext : JsonSerializerContext;
