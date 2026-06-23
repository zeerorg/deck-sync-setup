/// <summary>
/// Port for accessing GitHub release data needed by the Setup Install module.
/// Implement with an HTTP adapter for production or a mock adapter for tests.
/// </summary>
public interface ISetupInstallGitHubPort
{
    /// <summary>
    /// Returns the most recent releases for the rclone repository, newest first.
    /// </summary>
    Task<IReadOnlyList<GitHubReleaseSnapshot>> ListReleasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a readable stream positioned at the start of the asset at <paramref name="downloadUri"/>.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    Task<Stream> OpenAssetStreamAsync(
        Uri downloadUri,
        CancellationToken cancellationToken = default);
}

/// <summary>A point-in-time snapshot of a GitHub release.</summary>
/// <param name="TagName">The release tag (e.g. <c>v1.67.0-beta</c>).</param>
/// <param name="Name">The human-readable release title, or <see langword="null"/> if unset.</param>
/// <param name="IsPrerelease">Whether GitHub marks this release as a pre-release.</param>
/// <param name="Assets">Downloadable files attached to this release.</param>
public sealed record GitHubReleaseSnapshot(
    string TagName,
    string? Name,
    bool IsPrerelease,
    IReadOnlyList<GitHubAssetSnapshot> Assets);

/// <summary>A downloadable file attached to a GitHub release.</summary>
/// <param name="Name">The file name of the asset (e.g. <c>rclone-v1.67.0-windows-amd64.zip</c>).</param>
/// <param name="DownloadUri">The direct download URL for this asset.</param>
public sealed record GitHubAssetSnapshot(
    string Name,
    Uri DownloadUri);
