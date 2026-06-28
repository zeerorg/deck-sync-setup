/// <summary>Port for accessing GitHub release data used by release asset installs.</summary>
internal interface IGitHubReleasePort
{
    /// <summary>Returns the most recent releases for <paramref name="repository"/>, newest first.</summary>
    Task<IReadOnlyList<GitHubReleaseSnapshot>> ListReleasesAsync(
        GitHubRepositoryIdentity repository,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a readable stream positioned at the start of the asset at <paramref name="downloadUri"/>.</summary>
    Task<Stream> OpenAssetStreamAsync(
        Uri downloadUri,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies a GitHub repository.</summary>
/// <param name="Owner">The repository owner.</param>
/// <param name="RepositoryName">The repository name.</param>
internal sealed record GitHubRepositoryIdentity(
    string Owner,
    string RepositoryName);

/// <summary>A point-in-time snapshot of a GitHub release.</summary>
/// <param name="TagName">The release tag (e.g. <c>v1.67.0-beta</c>).</param>
/// <param name="Name">The human-readable release title, or <see langword="null"/> if unset.</param>
/// <param name="IsPrerelease">Whether GitHub marks this release as a pre-release.</param>
/// <param name="Assets">Downloadable files attached to this release.</param>
internal sealed record GitHubReleaseSnapshot(
    string TagName,
    string? Name,
    bool IsPrerelease,
    IReadOnlyList<GitHubAssetSnapshot> Assets);

/// <summary>A downloadable file attached to a GitHub release.</summary>
/// <param name="Name">The file name of the asset.</param>
/// <param name="DownloadUri">The direct download URL for this asset.</param>
internal sealed record GitHubAssetSnapshot(
    string Name,
    Uri DownloadUri);
