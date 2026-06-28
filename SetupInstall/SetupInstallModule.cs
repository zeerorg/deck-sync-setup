/// <summary>
/// Installs a single release asset into the Deck sync runtime directory.
/// </summary>
internal interface IReleaseAssetInstallModule
{
    /// <summary>Installs a release asset for the supplied request.</summary>
    Task<ReleaseAssetInstallResult> InstallAsync(
        ReleaseAssetInstallRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Request for a release asset install.</summary>
/// <param name="ToolName">The tool being installed.</param>
/// <param name="Repository">The GitHub repository that hosts the release.</param>
/// <param name="PlatformFragments">Asset-name fragments used to select the correct archive.</param>
/// <param name="DeckSyncRuntimeLocation">The Deck sync runtime location.</param>
internal sealed record ReleaseAssetInstallRequest(
    string ToolName,
    GitHubRepositoryIdentity Repository,
    PlatformAssetFragments PlatformFragments,
    DeckSyncRuntimeLocation DeckSyncRuntimeLocation);

/// <summary>Details of a completed release asset install.</summary>
/// <param name="ToolName">The tool that was installed.</param>
/// <param name="ReleaseTag">The release tag from which the asset was downloaded.</param>
/// <param name="AssetName">The archive file name that was installed.</param>
/// <param name="DeckSyncRuntimeLocation">The runtime location into which the asset was extracted.</param>
/// <param name="DestinationPath">The destination directory path.</param>
internal sealed record ReleaseAssetInstallResult(
    string ToolName,
    string ReleaseTag,
    string AssetName,
    DeckSyncRuntimeLocation DeckSyncRuntimeLocation,
    string DestinationPath);

/// <summary>Thrown when a release asset install fails for a known reason.</summary>
internal sealed class ReleaseAssetInstallException : Exception
{
    /// <summary>The category of failure.</summary>
    public ReleaseAssetInstallError Code { get; }

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    public ReleaseAssetInstallException(ReleaseAssetInstallError code, string message)
        : base(message) => Code = code;

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ReleaseAssetInstallException(ReleaseAssetInstallError code, string message, Exception innerException)
        : base(message, innerException) => Code = code;
}

/// <summary>Categories of failure that can occur during a release asset install.</summary>
internal enum ReleaseAssetInstallError
{
    /// <summary>The GitHub API returned an empty release list.</summary>
    NoReleasesReturned,

    /// <summary>No archive asset compatible with the current host was found in the selected release.</summary>
    NoCompatibleAsset,

    /// <summary>A network error prevented the release list or asset from being fetched.</summary>
    DownloadFailed,

    /// <summary>The downloaded archive could not be written to disk or extracted.</summary>
    WriteFailed,
}

/// <summary>Downloads, extracts, and places a single release asset.</summary>
internal sealed class ReleaseAssetInstallModule : IReleaseAssetInstallModule
{
    private readonly IGitHubReleasePort _gitHubPort;
    private readonly IArchiveExtractionModule _archiveExtractionModule;

    /// <param name="gitHubPort">Adapter used to list releases and stream asset downloads.</param>
    /// <param name="archiveExtractionModule">Module used to unpack downloaded archives.</param>
    public ReleaseAssetInstallModule(
        IGitHubReleasePort gitHubPort,
        IArchiveExtractionModule archiveExtractionModule)
    {
        _gitHubPort = gitHubPort;
        _archiveExtractionModule = archiveExtractionModule;
    }

    /// <inheritdoc/>
    public async Task<ReleaseAssetInstallResult> InstallAsync(
        ReleaseAssetInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubReleaseSnapshot> releases;
        try
        {
            releases = await _gitHubPort.ListReleasesAsync(request.Repository, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ReleaseAssetInstallException(
                ReleaseAssetInstallError.DownloadFailed,
                $"Failed to retrieve releases from GitHub repository '{request.Repository.Owner}/{request.Repository.RepositoryName}'.",
                ex);
        }

        if (releases.Count == 0)
        {
            throw new ReleaseAssetInstallException(
                ReleaseAssetInstallError.NoReleasesReturned,
                $"No releases were returned by the GitHub API for '{request.Repository.Owner}/{request.Repository.RepositoryName}'.");
        }

        var release = SelectRelease(releases);
        var asset = SelectAsset(release, request.PlatformFragments);

        if (asset is null)
        {
            throw new ReleaseAssetInstallException(
                ReleaseAssetInstallError.NoCompatibleAsset,
                $"No downloadable asset was found for release '{release.TagName}'.");
        }

        var deckSyncDirectory = request.DeckSyncRuntimeLocation.Path;
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
                throw new ReleaseAssetInstallException(
                    ReleaseAssetInstallError.WriteFailed,
                    $"Failed to write the downloaded archive to '{tempPath}'.",
                    ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ReleaseAssetInstallException(
                    ReleaseAssetInstallError.DownloadFailed,
                    $"Failed to download asset '{asset.Name}' from '{asset.DownloadUri}'.",
                    ex);
            }

            try
            {
                await _archiveExtractionModule.ExtractExecutablesAsync(tempPath, deckSyncDirectory, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ReleaseAssetInstallException(
                    ReleaseAssetInstallError.WriteFailed,
                    $"Failed to extract executables from '{asset.Name}' to '{deckSyncDirectory}'.",
                    ex);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return new ReleaseAssetInstallResult(
            request.ToolName,
            release.TagName,
            asset.Name,
            request.DeckSyncRuntimeLocation,
            deckSyncDirectory);
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
    private GitHubAssetSnapshot? SelectAsset(GitHubReleaseSnapshot release, PlatformAssetFragments platformFragments)
    {
        if (release.Assets.Count == 0)
            return null;

        var preferredFragments = platformFragments.ForCurrentPlatform();

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
