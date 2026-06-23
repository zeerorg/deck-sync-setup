using System.CommandLine;
using System.Net.Http.Headers;

var rootCommand = new RootCommand("Deck sync setup tool — installs or removes the Deck sync runtime on this machine.");

var installCommand = new Command("install", "Download and install the Deck sync runtime for this platform.");
installCommand.SetAction(async (ParseResult _, CancellationToken cancellationToken) =>
{
    try
    {
        using var httpClient = CreateGitHubClient();

        var tools = new (string Label, ISetupInstallModule Module)[]
        {
            ("rclone",    new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "rclone",       "rclone"),    PlatformAssetFragments.Rclone)),
            ("syncthing", new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "syncthing",    "syncthing"), PlatformAssetFragments.Syncthing)),
            ("ludusavi",  new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "mtkennerly",   "ludusavi"),  PlatformAssetFragments.Ludusavi)),
        };

        foreach (var (label, module) in tools)
        {
            Console.WriteLine($"Installing {label}...");
            var result = await module.InstallAsync(cancellationToken);
            Console.WriteLine($"  {result.AssetName} from {result.ReleaseTag} → {result.DestinationPath}");
        }

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
        var deckSyncDirectory = DeckSyncRuntimeDirectory.Resolve();
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


