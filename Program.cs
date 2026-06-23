using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

var rootCommand = new RootCommand("Deck sync setup tool — installs or removes the Deck sync runtime on this machine.");

var installCommand = new Command("install", "Download and install the Deck sync runtime for this platform.");
installCommand.SetAction(async (ParseResult _, CancellationToken cancellationToken) =>
{
    try
    {
        using var httpClient = CreateGitHubClient();
        var release = await GetLatestMasterReleaseAsync(httpClient, cancellationToken);
        var asset = SelectAssetForCurrentPlatform(release.Assets);

        if (asset is null)
        {
            throw new InvalidOperationException($"No downloadable rclone asset was found for release '{release.TagName}'.");
        }

        var deckSyncDirectory = GetDeckSyncDirectory();
        Directory.CreateDirectory(deckSyncDirectory);
        var destinationPath = Path.Combine(deckSyncDirectory, asset.Name);
        await DownloadFileAsync(httpClient, asset.BrowserDownloadUrl, destinationPath, cancellationToken);

        Console.WriteLine($"Downloaded {asset.Name} from {release.TagName}");
        Console.WriteLine(destinationPath);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
});

var cleanupCommand = new Command("cleanup", "Remove all Deck sync runtime files from this machine.");
cleanupCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    try
    {
        var deckSyncDirectory = GetDeckSyncDirectory();
        if (!Directory.Exists(deckSyncDirectory))
        {
            Console.WriteLine($"Nothing to delete: {deckSyncDirectory}");
            return Task.FromResult(0);
        }

        Directory.Delete(deckSyncDirectory, recursive: true);
        Console.WriteLine($"Deleted {deckSyncDirectory}");
        return Task.FromResult(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return Task.FromResult(1);
    }
});

rootCommand.Subcommands.Add(installCommand);
rootCommand.Subcommands.Add(cleanupCommand);

return await rootCommand.Parse(args).InvokeAsync();

static HttpClient CreateGitHubClient()
{
    var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("deck-sync-setup", "1.0"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    return client;
}

static async Task<GitHubRelease> GetLatestMasterReleaseAsync(HttpClient client, CancellationToken cancellationToken)
{
    var releases = await client.GetFromJsonAsync<List<GitHubRelease>>(
        "https://api.github.com/repos/rclone/rclone/releases?per_page=30",
        cancellationToken);

    if (releases is null || releases.Count == 0)
    {
        throw new InvalidOperationException("No releases were returned by the GitHub API.");
    }

    var masterRelease = releases.FirstOrDefault(static release =>
        release.Prerelease
        || release.TagName.Contains("beta", StringComparison.OrdinalIgnoreCase)
        || release.TagName.Contains("master", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(release.Name)
            && (release.Name.Contains("beta", StringComparison.OrdinalIgnoreCase)
                || release.Name.Contains("master", StringComparison.OrdinalIgnoreCase))));

    return masterRelease ?? releases[0];
}

static GitHubAsset? SelectAssetForCurrentPlatform(IReadOnlyList<GitHubAsset> assets)
{
    if (assets.Count == 0)
    {
        return null;
    }

    var preferredNames = GetPreferredAssetNameFragments();

    foreach (var nameFragment in preferredNames)
    {
        var match = assets.FirstOrDefault(asset =>
            asset.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)
            && IsArchiveAsset(asset.Name));

        if (match is not null)
        {
            return match;
        }
    }

    return assets.FirstOrDefault(asset => IsArchiveAsset(asset.Name));
}

static string[] GetPreferredAssetNameFragments()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => ["windows-amd64"],
            Architecture.Arm64 => ["windows-arm64", "windows-amd64"],
            _ => ["windows"]
        };
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => ["linux-amd64"],
            Architecture.Arm64 => ["linux-arm64", "linux-amd64"],
            _ => ["linux"]
        };
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => ["osx-amd64"],
            Architecture.Arm64 => ["osx-arm64", "osx-amd64"],
            _ => ["osx"]
        };
    }

    return ["rclone"];
}

static bool IsArchiveAsset(string assetName)
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

static string GetDeckSyncDirectory()
{
    var homeDirectory = Environment.GetEnvironmentVariable("HOME");
    if (string.IsNullOrWhiteSpace(homeDirectory))
    {
        homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    if (string.IsNullOrWhiteSpace(homeDirectory))
    {
        throw new InvalidOperationException("Could not determine the current user's home directory.");
    }

    return Path.Combine(homeDirectory, ".deck-sync");
}

static async Task DownloadFileAsync(HttpClient client, string url, string destinationPath, CancellationToken cancellationToken)
{
    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
    await using var destinationStream = File.Create(destinationPath);
    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
