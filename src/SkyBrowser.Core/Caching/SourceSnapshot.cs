using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;

namespace SkyBrowser.Core.Caching;

internal sealed record CachedFileStamp(string Path, long Length, long LastWriteTicks, long CreationTicks, string SampleHash)
{
    public static CachedFileStamp Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = new FileInfo(path);
        if (!before.Exists)
        {
            throw new FileNotFoundException("Image or XISF data file is unavailable.", path);
        }

        long length = before.Length, write = before.LastWriteTimeUtc.Ticks, created = before.CreationTimeUtc.Ticks;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16384,
            FileOptions.RandomAccess
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[16384];
        // Sample the beginning, middle and end rather than hashing a potentially multi-GB
        // integration on every folder visit. Timestamps and lengths are checked as well.
        foreach (long offset in new[]
        {
            0L,
            Math.Max(0, length / 2 - buffer.Length / 2),
            Math.Max(0, length - buffer.Length)
        }.Distinct())
        {
            token.ThrowIfCancellationRequested();
            stream.Position = offset;
            int count = (int)Math.Min(buffer.Length, Math.Max(0, length - offset));
            stream.ReadExactly(buffer.AsSpan(0, count));
            hash.AppendData(buffer.AsSpan(0, count));
        }

        // Reject signatures captured while another process was still writing the source.
        var after = new FileInfo(path);
        if (!after.Exists ||
            after.Length != length ||
            after.LastWriteTimeUtc.Ticks != write ||
            after.CreationTimeUtc.Ticks != created)
        {
            throw new IOException("The image changed while its cache signature was read.");
        }

        return new(System.IO.Path.GetFullPath(path), length, write, created, Convert.ToHexString(hash.GetHashAndReset()));
    }
}

internal sealed record SourceSnapshot(CachedFileStamp[] Files)
{
    public static SourceSnapshot Capture(string sourcePath, CancellationToken token)
    {
        var main = CachedFileStamp.Read(sourcePath, token);
        List<CachedFileStamp> files = [main];
        if (System.IO.Path.GetExtension(sourcePath).ToLowerInvariant() is ".xisf" or ".xish")
        {
            foreach (string dependency in ReadDependencies(sourcePath, token).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(dependency, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(CachedFileStamp.Read(dependency, token));
                }
            }
        }

        files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
        var snapshot = new SourceSnapshot(files.ToArray());
        if (!snapshot.IsCurrent(token))
        {
            throw new IOException("The source changed while its cache signature was read.");
        }

        return snapshot;
    }

    public bool IsCurrent(CancellationToken token)
    {
        if (Files is null || Files.Length is < 1 or > 4096)
        {
            return false;
        }

        foreach (var file in Files)
        {
            token.ThrowIfCancellationRequested();
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || file.Length < 0)
            {
                return false;
            }

            if (file != CachedFileStamp.Read(file.Path, token))
            {
                return false;
            }
        }

        return true;
    }

    // Only locations allowed by XISFLib's default policy are cacheable. Persist every sidecar
    // dependency, including metadata/property blocks, rather than trusting just the .xish timestamp.
    internal static IEnumerable<string> ReadDependencies(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        byte[] prefix = new byte[16];
        int read = stream.Read(prefix);
        Stream xml = stream;
        MemoryStream? header = null;
        if (read == 16 && prefix.AsSpan(0, 8).SequenceEqual("XISF0100"u8))
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(8, 4));
            if (length is 0 or > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Unexpected XISF header size.");
            }

            byte[] bytes = new byte[checked((int)length)];
            stream.ReadExactly(bytes);
            header = new(bytes, writable: false);
            xml = header;
        }
        else
        {
            stream.Position = 0;
        }

        try
        {
            using var reader = XmlReader.Create(
                xml,
                new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 64 * 1024 * 1024
                }
            );
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.GetAttribute("location") is not { } location)
                {
                    continue;
                }

                if (location.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("External URL blocks are not cached.");
                }

                if (!location.StartsWith("path(", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int end = location.LastIndexOf(')');
                if (end < 5)
                {
                    throw new InvalidDataException("Invalid external data location.");
                }

                string resource = location[5..end];
                if (!resource.StartsWith("@header_dir/", StringComparison.Ordinal))
                {
                    throw new NotSupportedException("Only header-relative XISF blocks are cached.");
                }

                string absolute = System.IO.Path.GetFullPath(resource[12..].Replace('/', System.IO.Path.DirectorySeparatorChar), directory);
                string relative = System.IO.Path.GetRelativePath(directory, absolute);
                if (relative == ".." ||
                    relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new NotSupportedException("External blocks outside the header directory are not cached.");
                }

                yield return absolute;
            }
        }
        finally
        {
            header?.Dispose();
        }
    }
}
