using SkyBrowser.Core.Caching;

namespace SkyBrowser.Core.Imaging;

public static class ImageExportDestination
{
    public static void Validate(string destination, IEnumerable<string> sources, CancellationToken token = default)
    {
        destination = Path.GetFullPath(destination);
        if (Path.GetFileName(destination).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Choose a normal destination filename without alternate streams or invalid characters.");
        }
        foreach (string source in sources)
        {
            token.ThrowIfCancellationRequested();
            if (string.Equals(destination, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("An exported image cannot replace an original source.");
            }
            if (Path.GetExtension(source).ToLowerInvariant() is not (".xisf" or ".xish"))
            {
                continue;
            }
            if (!File.Exists(source))
            {
                if (File.Exists(destination))
                {
                    throw new IOException("The XISF header is unavailable, so its sidecars cannot be checked. Choose a new filename.");
                }
                continue;
            }
            if (SourceSnapshot.ReadDependencies(source, token).Any(file => string.Equals(destination, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)))
            {
                throw new IOException("An exported image cannot replace an original XISF sidecar.");
            }
        }
    }
}
