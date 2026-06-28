using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>HTTP adapter for <see cref="IGitHubReleasePort"/>.</summary>
internal sealed class GitHubReleaseHttpAdapter : IGitHubReleasePort
{
    private readonly HttpClient _httpClient;
    
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> preconfigured with the GitHub User-Agent and Accept headers.
    /// </param>
    public GitHubReleaseHttpAdapter(HttpClient httpClient) => _httpClient = httpClient;

    /// <inheritdoc/>
    /// <remarks>Fetches up to 30 releases and maps them to snapshots.</remarks>
    public async Task<IReadOnlyList<GitHubReleaseSnapshot>> ListReleasesAsync(
        GitHubRepositoryIdentity repository,
        CancellationToken cancellationToken = default)
    {
        var releasesUrl = $"https://api.github.com/repos/{repository.Owner}/{repository.RepositoryName}/releases?per_page=30";
        var releases = await _httpClient.GetFromJsonAsync<List<ReleaseDto>>(
            releasesUrl,
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
