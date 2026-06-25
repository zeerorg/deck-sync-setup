using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Unpacks archive files into the Deck sync runtime directory and preserves executable bits
/// on Linux and macOS.
/// </summary>
public interface IArchiveExtractionModule
{
    /// <summary>
    /// Extracts executable files from <paramref name="archivePath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    Task ExtractExecutablesAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Extracts executable files from supported archive formats.
/// </summary>
public sealed class ArchiveExtractionModule : IArchiveExtractionModule
{
    private static readonly IArchiveFormatExtractor[] Extractors =
    [
        new TarGzArchiveExtractor(),
        new ZipArchiveExtractor(),
    ];

    /// <inheritdoc/>
    public async Task ExtractExecutablesAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var extractor = SelectExtractor(archivePath);
        await extractor.ExtractExecutablesAsync(archivePath, destinationDirectory, cancellationToken);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetExecutablePermissions(destinationDirectory);
    }

    private static IArchiveFormatExtractor SelectExtractor(string archivePath)
    {
        foreach (var extractor in Extractors)
        {
            if (extractor.CanHandle(archivePath))
                return extractor;
        }

        throw new NotSupportedException($"Unsupported archive format for '{archivePath}'.");
    }

    private interface IArchiveFormatExtractor
    {
        bool CanHandle(string archivePath);

        Task ExtractExecutablesAsync(
            string archivePath,
            string destinationDirectory,
            CancellationToken cancellationToken);
    }

    private sealed class TarGzArchiveExtractor : IArchiveFormatExtractor
    {
        public bool CanHandle(string archivePath) =>
            archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

        public async Task ExtractExecutablesAsync(
            string archivePath,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            await using var fileStream = File.OpenRead(archivePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);

            while (await tarReader.GetNextEntryAsync(copyData: false, cancellationToken) is TarEntry entry)
            {
                if (!IsExecutableTarEntry(entry))
                    continue;

                var fileName = Path.GetFileName(entry.Name);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                await entry.ExtractToFileAsync(
                    Path.Combine(destinationDirectory, fileName),
                    true,
                    cancellationToken);
            }
        }
    }

    private sealed class ZipArchiveExtractor : IArchiveFormatExtractor
    {
        public bool CanHandle(string archivePath) =>
            archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        public Task ExtractExecutablesAsync(
            string archivePath,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                if (!IsExecutableZipEntry(entry))
                    continue;

                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                entry.ExtractToFile(Path.Combine(destinationDirectory, fileName), overwrite: true);
            }

            return Task.CompletedTask;
        }
    }

    private static bool IsExecutableTarEntry(TarEntry entry) =>
        entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
        && (entry.Mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    private static bool IsExecutableZipEntry(ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith('/'))
            return false;

        if (entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return true;

        var unixMode = entry.ExternalAttributes >> 16;
        return unixMode != 0 && (unixMode & 0b001_001_001) != 0;
    }

    [UnsupportedOSPlatform("windows")]
    private static void SetExecutablePermissions(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var current = File.GetUnixFileMode(file);
            File.SetUnixFileMode(
                file,
                current | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }
}
