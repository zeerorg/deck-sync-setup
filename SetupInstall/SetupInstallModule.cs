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
    private readonly IArchiveExtractionModule _archiveExtractionModule;
    private readonly string? _deckSyncRuntimeDirectory;

    /// <param name="gitHubPort">Adapter used to list releases and stream asset downloads.</param>
    /// <param name="platformFragments">
    /// Asset name fragments for the current platform. Use the presets on
    /// <see cref="PlatformAssetFragments"/> (e.g. <see cref="PlatformAssetFragments.Rclone"/>)
    /// or supply custom fragments for other tools.
    /// </param>
    /// <param name="archiveExtractionModule">
    /// Module used to unpack the downloaded archive into the Deck sync runtime directory.
    /// </param>
    /// <param name="deckSyncRuntimeDirectory">
    /// Overrides the Deck sync runtime directory. When <see langword="null"/>,
    /// <see cref="DeckSyncRuntimeDirectory.Resolve"/> is used to determine the default.
    /// </param>
    public SetupInstallModule(
        ISetupInstallGitHubPort gitHubPort,
        PlatformAssetFragments platformFragments,
        IArchiveExtractionModule archiveExtractionModule,
        string? deckSyncRuntimeDirectory = null)
    {
        _gitHubPort = gitHubPort;
        _platformFragments = platformFragments;
        _archiveExtractionModule = archiveExtractionModule;
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
                await _archiveExtractionModule.ExtractExecutablesAsync(tempPath, deckSyncDirectory, cancellationToken);
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

    /// <summary>Returns the file extension of the archive, preserving <c>.tar.gz</c> as a unit.</summary>
    private static string ArchiveExtension(string assetName) =>
        assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : ".zip";
}
