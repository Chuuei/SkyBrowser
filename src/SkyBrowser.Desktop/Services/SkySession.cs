using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop.Services;

public sealed record SessionLayer(
    string SourcePath,
    int ImageIndex,
    DisplayMode DisplayMode,
    bool Visible,
    double Opacity,
    ManualPlacement? Manual
)
{
    public SkyFinishSettings? FinishSettings { get; init; }
}

public sealed record SkySession(
    int Version,
    SkyCoordinate Center,
    double FieldOfView,
    bool Grid,
    bool Constellations,
    bool Labels,
    bool Outlines,
    SessionLayer[] Images
)
{
    public bool BlendOverlaps { get; init; } = true;

    public ObserverSettings? Observer { get; init; }

    public string[] AddedTargets { get; init; } = [];

    public void Validate()
    {
        if (Version != 1)
        {
            throw new InvalidDataException($"Unsupported session version {Version}.");
        }

        Observer?.Validate();
        if (AddedTargets is null ||
            AddedTargets.Length > 20000 ||
            AddedTargets.Any(id => string.IsNullOrWhiteSpace(id) ||
            id.Length > 64))
        {
            throw new InvalidDataException("Invalid catalogue target list in this session.");
        }

        if (!double.IsFinite(Center.RightAscension) ||
            !double.IsFinite(Center.Declination) ||
            Math.Abs(Center.Declination) > 90 ||
            !double.IsFinite(FieldOfView) ||
            FieldOfView is < 0.05 or > 120 ||
            Images is null ||
            Images.Length > 1000)
        {
            throw new InvalidDataException("Invalid camera settings or image list in this session.");
        }

        foreach (var image in Images)
        {
            if (image is null ||
                string.IsNullOrWhiteSpace(image.SourcePath) ||
                image.ImageIndex < 0 ||
                !Enum.IsDefined(image.DisplayMode) ||
                !double.IsFinite(image.Opacity) ||
                image.Opacity is < 0 or > 1)
            {
                throw new InvalidDataException("Invalid image settings in this session.");
            }

            image.Manual?.Validate();
            image.FinishSettings?.Validate();
        }
    }

    public static async Task<SkySession> ReadAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("The session file is too large.");
        }

        var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Default.SkySession, token) ?? throw new InvalidDataException("The session file is empty.");
        session.Validate();
        return session;
    }

    public async Task WriteAsync(string path, CancellationToken token)
    {
        Validate();
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, this, SessionJsonContext.Default.SkySession, token);
            }

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

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(SkySession))]
internal partial class SessionJsonContext : JsonSerializerContext;
