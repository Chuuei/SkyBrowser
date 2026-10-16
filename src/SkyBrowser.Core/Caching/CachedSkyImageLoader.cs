using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Caching;

public delegate IReadOnlyList<LoadedSkyImage> SkyImageProcessor(
    string path,
    DisplayMode mode,
    SkyFinishSettings settings,
    CancellationToken token,
    int? onlyImage,
    IProgress<ImageProcessingProgress>? progress
);

/// <summary>Uses the persistent cache before opening or processing scientific image data.</summary>
public sealed class CachedSkyImageLoader
{
    private readonly ProcessedImageCache _cache;
    private readonly SkyImageProcessor _processor;
    // Bound cold processing memory; cache hits never wait for an unrelated image's decode/finishing.
    private readonly SemaphoreSlim _processing = new(1, 1);

    public DisplayMode DefaultDisplayMode { get; set; } = DisplayMode.Automatic;

    public SkyFinishSettings DefaultFinishSettings { get; set; } = SkyFinishSettings.Default;

    public CachedSkyImageLoader(ProcessedImageCache cache, SkyImageProcessor? processor = null)
    {
        _cache = cache;
        _processor = processor ?? ((path, mode, settings, token, index, progress) => SkyImageLoader.Load(path, mode, settings, token, index, progress));
    }

    public IReadOnlyList<LoadedSkyImage> Load(
        string path,
        DisplayMode mode,
        SkyFinishSettings settings,
        CancellationToken token = default,
        int? onlyImage = null,
        IProgress<ImageProcessingProgress>? progress = null
    )
    {
        token.ThrowIfCancellationRequested();
        settings.Validate();
        string profile = ProcessedImageCache.ProfileKey(mode, settings);
        progress?.Report(new("Checking saved preview"));
        if (_cache.Read(path, profile, onlyImage, token) is { } cached)
        {
            progress?.Report(new("Loading saved preview"));
            return cached;
        }

        progress?.Report(new("Waiting for image processing"));
        _processing.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            // Another request may have populated this variant while this request was queued.
            if (_cache.Read(path, profile, onlyImage, token) is { } queuedCache)
            {
                progress?.Report(new("Loading saved preview"));
                return queuedCache;
            }

            SourceSnapshot? snapshot = null;
            progress?.Report(new("Checking source file"));
            try
            {
                snapshot = SourceSnapshot.Capture(path, token);
            }
            catch (Exception exception) when (ProcessedImageCache.IsCacheFailure(exception) || exception is NotSupportedException)
            {
                // Caching is optional. An unreadable signature or unsupported sidecar policy
                // must still allow the scientific image loader to try opening the source.
            }

            var loaded = _processor(path, mode, settings, token, onlyImage, progress);
            token.ThrowIfCancellationRequested();
            if (snapshot is not null)
            {
                progress?.Report(new("Saving processed preview"));
                _cache.Store(path, profile, snapshot, loaded, token);
            }

            return loaded;
        }
        finally
        {
            _processing.Release();
        }
    }

    public IReadOnlyList<LoadedSkyImage> LoadForBrowsing(string path, CancellationToken token = default, IProgress<ImageProcessingProgress>? progress = null)
    {
        var preferences = _cache.GetPreferences(path);
        var fallback = new ImageRenderPreference(DefaultDisplayMode, DefaultFinishSettings);
        if (preferences.Count == 0)
        {
            return Load(path, fallback.Mode, fallback.FinishSettings, token, progress: progress);
        }

        int? count = _cache.GetImageCount(path, token);
        if (count is { } knownCount)
        {
            List<LoadedSkyImage> images = [];
            for (int i = 0; i < knownCount; i++)
            {
                token.ThrowIfCancellationRequested();
                var preference = preferences.GetValueOrDefault(i) ?? fallback;
                images.AddRange(Load(path, preference.Mode, preference.FinishSettings, token, i, progress));
            }

            return images;
        }

        var defaults = Load(path, fallback.Mode, fallback.FinishSettings, token, progress: progress);
        return defaults.Select(image => preferences.TryGetValue(image.ImageIndex, out var preference) &&
            (preference.Mode != fallback.Mode ||
            preference.FinishSettings != fallback.FinishSettings)
            ? Load(path, preference.Mode, preference.FinishSettings, token, image.ImageIndex, progress)[0]
            : image)
            .ToArray();
    }
}
