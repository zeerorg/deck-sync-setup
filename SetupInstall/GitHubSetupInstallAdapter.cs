using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>
/// HTTP adapter for <see cref="ISetupInstallGitHubPort"/>. Queries the GitHub Releases API
/// for a configurable repository and streams asset downloads directly to the caller.
/// </summary>
public sealed class GitHubSetupInstallAdapter : ISetupInstallGitHubPort
{
    private readonly HttpClient _httpClient;
    private readonly string _releasesUrl;

    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> preconfigured with the GitHub User-Agent and Accept headers.
    /// </param>
    /// <param name="owner">The GitHub repository owner (e.g. <c>rclone</c>).</param>
    /// <param name="repo">The GitHub repository name (e.g. <c>rclone</c>).</param>
    public GitHubSetupInstallAdapter(HttpClient httpClient, string owner, string repo)
    {
        _httpClient = httpClient;
        _releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=30";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Fetches up to 30 releases from <c>GET /repos/rclone/rclone/releases</c>
    /// and maps them to <see cref="GitHubReleaseSnapshot"/> instances.
    /// Returns an empty list if the API returns no releases.
    /// </remarks>
    public async Task<IReadOnlyList<GitHubReleaseSnapshot>> ListReleasesAsync(
        CancellationToken cancellationToken = default)
    {
        var releases = await _httpClient.GetFromJsonAsync<List<ReleaseDto>>(
            _releasesUrl,
            cancellationToken);

        if (releases is null || releases.Count == 0)
            return [];

        return releases
            .Select(r => new GitHubReleaseSnapshot(
                TagName: r.TagName,
                Name: r.Name,
                IsPrerelease: r.Prerelease,
                Assets: r.Assets
                    .Select(a => new GitHubAssetSnapshot(
                        Name: a.Name,
                        DownloadUri: new Uri(a.BrowserDownloadUrl)))
                    .ToList()))
            .ToList();
    }

    /// <inheritdoc/>
    /// <remarks>Uses <see cref="HttpCompletionOption.ResponseHeadersRead"/> to begin streaming before the full response body arrives.</remarks>
    public async Task<Stream> OpenAssetStreamAsync(
        Uri downloadUri,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<AssetDto> Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
