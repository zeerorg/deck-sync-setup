using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Implements <see cref="ISetupInstallModule"/>. Fetches the latest stable release via the
/// GitHub API, selects the best archive asset for the current OS and architecture, downloads
/// it to a temp file, extracts only the executable files into the Deck sync runtime directory,
/// and deletes the temp archive. On Linux and macOS, also sets the execute permission on the
/// extracted files.
/// </summary>
public sealed class SetupInstallModule : ISetupInstallModule
{
    private readonly ISetupInstallGitHubPort _gitHubPort;
    private readonly PlatformAssetFragments _platformFragments;
    private readonly string? _deckSyncRuntimeDirectory;

    /// <param name="gitHubPort">Adapter used to list releases and stream asset downloads.</param>
    /// <param name="platformFragments">
    /// Asset name fragments for the current platform. Use the presets on
    /// <see cref="PlatformAssetFragments"/> (e.g. <see cref="PlatformAssetFragments.Rclone"/>)
    /// or supply custom fragments for other tools.
    /// </param>
    /// <param name="deckSyncRuntimeDirectory">
    /// Overrides the Deck sync runtime directory. When <see langword="null"/>,
    /// <see cref="DeckSyncRuntimeDirectory.Resolve"/> is used to determine the default.
    /// </param>
    public SetupInstallModule(
        ISetupInstallGitHubPort gitHubPort,
        PlatformAssetFragments platformFragments,
        string? deckSyncRuntimeDirectory = null)
    {
        _gitHubPort = gitHubPort;
        _platformFragments = platformFragments;
        _deckSyncRuntimeDirectory = deckSyncRuntimeDirectory;
    }

    /// <inheritdoc/>
    public async Task<SetupInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubReleaseSnapshot> releases;
        try
        {
            releases = await _gitHubPort.ListReleasesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupInstallException(
                SetupInstallError.DownloadFailed,
                "Failed to retrieve releases from GitHub.",
                ex);
        }

        if (releases.Count == 0)
        {
            throw new SetupInstallException(
                SetupInstallError.NoReleasesReturned,
                "No releases were returned by the GitHub API.");
        }

        var release = SelectRelease(releases);
        var asset = SelectAsset(release);

        if (asset is null)
        {
            throw new SetupInstallException(
                SetupInstallError.NoCompatibleAsset,
                $"No downloadable asset was found for release '{release.TagName}'.");
        }

        string deckSyncDirectory;
        try
        {
            deckSyncDirectory = DeckSyncRuntimeDirectory.Resolve(_deckSyncRuntimeDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupInstallException(
                SetupInstallError.DeckSyncRuntimeDirectoryUnavailable,
                "Could not resolve the deck sync runtime directory.",
                ex);
        }

        Directory.CreateDirectory(deckSyncDirectory);
        var tempPath = Path.Combine(Path.GetTempPath(), $"deck-sync-{Guid.NewGuid()}{ArchiveExtension(asset.Name)}");
        try
        {
            try
            {
                await using var assetStream = await _gitHubPort.OpenAssetStreamAsync(asset.DownloadUri, cancellationToken);
                await using var tempStream = File.Create(tempPath);
                await assetStream.CopyToAsync(tempStream, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new SetupInstallException(
                    SetupInstallError.WriteFailed,
                    $"Failed to write the downloaded archive to '{tempPath}'.",
                    ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new SetupInstallException(
                    SetupInstallError.DownloadFailed,
                    $"Failed to download asset '{asset.Name}' from '{asset.DownloadUri}'.",
                    ex);
            }

            try
            {
                await ExtractExecutablesAsync(tempPath, deckSyncDirectory, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not SetupInstallException)
            {
                throw new SetupInstallException(
                    SetupInstallError.WriteFailed,
                    $"Failed to extract executables from '{asset.Name}' to '{deckSyncDirectory}'.",
                    ex);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return new SetupInstallResult(
            ReleaseTag: release.TagName,
            AssetName: asset.Name,
            DeckSyncRuntimeDirectory: deckSyncDirectory,
            DestinationPath: deckSyncDirectory);
    }

    /// <summary>
    /// Returns the newest stable release — the first release in the list that is not
    /// flagged as a pre-release and whose tag and name contain neither "beta" nor "master".
    /// Falls back to the first release in the list if no stable release is found.
    /// </summary>
    private static GitHubReleaseSnapshot SelectRelease(IReadOnlyList<GitHubReleaseSnapshot> releases)
    {
        var stableRelease = releases.FirstOrDefault(static r =>
            !r.IsPrerelease
            && !r.TagName.Contains("beta", StringComparison.OrdinalIgnoreCase)
            && !r.TagName.Contains("master", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(r.Name)
                || (!r.Name.Contains("beta", StringComparison.OrdinalIgnoreCase)
                    && !r.Name.Contains("master", StringComparison.OrdinalIgnoreCase))));

        return stableRelease ?? releases[0];
    }

    /// <summary>
    /// Chooses the best archive asset for the current host from <paramref name="release"/>.
    /// Iterates the platform-specific name fragments in preference order and returns the
    /// first matching archive. Falls back to the first archive of any platform, or
    /// <see langword="null"/> if the release has no archive assets at all.
    /// </summary>
    private GitHubAssetSnapshot? SelectAsset(GitHubReleaseSnapshot release)
    {
        if (release.Assets.Count == 0)
            return null;

        var preferredFragments = _platformFragments.ForCurrentPlatform();

        foreach (var fragment in preferredFragments)
        {
            var match = release.Assets.FirstOrDefault(a =>
                a.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                && IsArchiveAsset(a.Name));

            if (match is not null)
                return match;
        }

        return release.Assets.FirstOrDefault(a => IsArchiveAsset(a.Name));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="assetName"/> names a downloadable
    /// archive (<c>.zip</c> or <c>.tar.gz</c>), and <see langword="false"/> for checksums,
    /// signatures, and plain-text files.
    /// </summary>
    private static bool IsArchiveAsset(string assetName)
    {
        if (assetName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith(".asc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts executable files from <paramref name="archivePath"/> into
    /// <paramref name="destinationDirectory"/>. Dispatches to the tar.gz or zip extractor
    /// based on the archive extension. On Linux and macOS, also sets the execute permission
    /// on every extracted file.
    /// </summary>
    private static async Task ExtractExecutablesAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            await ExtractExecutablesFromTarGzAsync(archivePath, destinationDirectory, cancellationToken);
        else
            ExtractExecutablesFromZip(archivePath, destinationDirectory);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetExecutablePermissions(destinationDirectory);
    }

    /// <summary>
    /// Streams a gzip-compressed tar archive and extracts entries that have any Unix execute
    /// bit set in their mode.
    /// </summary>
    private static async Task ExtractExecutablesFromTarGzAsync(
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

    /// <summary>
    /// Reads a zip archive and extracts entries identified as executables (Unix execute bits
    /// in external attributes, or <c>.exe</c> extension for Windows-created zips).
    /// </summary>
    private static void ExtractExecutablesFromZip(string archivePath, string destinationDirectory)
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
    }

    /// <summary>
    /// Returns <see langword="true"/> for tar entries that are regular files with any execute
    /// bit set in their Unix mode.
    /// </summary>
    private static bool IsExecutableTarEntry(TarEntry entry) =>
        entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
        && (entry.Mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    /// <summary>
    /// Returns <see langword="true"/> for zip entries that are executable files.
    /// Always includes <c>.exe</c> files (Windows executables are not marked with Unix execute
    /// bits when zipped on Linux). For non-<c>.exe</c> entries, checks Unix permission bits in
    /// the upper 16 bits of <see cref="ZipArchiveEntry.ExternalAttributes"/> to identify
    /// Linux/macOS binaries in Unix-created zip archives.
    /// </summary>
    private static bool IsExecutableZipEntry(ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith('/'))
            return false;

        // .exe files are Windows executables — always include them regardless of Unix attributes.
        // Linux CI tools (syncthing, ludusavi) zip .exe files with 0644 (no execute bits), so
        // checking Unix attributes here would incorrectly exclude them.
        if (entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return true;

        // For non-.exe entries (Linux/macOS binaries in zip archives): check execute bits.
        var unixMode = entry.ExternalAttributes >> 16;
        return unixMode != 0 && (unixMode & 0b001_001_001) != 0;
    }

    /// <summary>
    /// Adds user, group, and other execute bits to every file in <paramref name="directory"/>.
    /// Called after extraction on Linux and macOS.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private static void SetExecutablePermissions(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var current = File.GetUnixFileMode(file);
            File.SetUnixFileMode(file,
                current | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>Returns the file extension of the archive, preserving <c>.tar.gz</c> as a unit.</summary>
    private static string ArchiveExtension(string assetName) =>
        assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : ".zip";

}
