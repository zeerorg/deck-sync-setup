using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;

var rootCommand = new RootCommand("Deck sync setup tool — installs or removes the Deck sync runtime on this machine.");

var installCommand = new Command("install", "Download and install the Deck sync runtime for this platform.");
installCommand.SetAction(async (ParseResult _, CancellationToken cancellationToken) =>
{
    try
    {
        var deckSyncDirectory = DeckSyncRuntimeDirectory.Resolve();
        if (Directory.Exists(deckSyncDirectory))
        {
            throw new SetupInstallException(
                SetupInstallError.DeckSyncRuntimeDirectoryAlreadyExists,
                $"The Deck sync runtime directory already exists at '{deckSyncDirectory}'. Please run `deck-sync-setup cleanup` before installing again.");
        }

        using var httpClient = CreateGitHubClient();
        var archiveExtractionModule = new ArchiveExtractionModule();

        var tools = new (string Label, ISetupInstallModule Module)[]
        {
            ("rclone",    new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "rclone",       "rclone"),    PlatformAssetFragments.Rclone,    archiveExtractionModule)),
            ("syncthing", new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "syncthing",    "syncthing"), PlatformAssetFragments.Syncthing, archiveExtractionModule)),
            ("ludusavi",   new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "mtkennerly",   "ludusavi"),  PlatformAssetFragments.Ludusavi,    archiveExtractionModule)),
        };

        foreach (var (label, module) in tools)
        {
            Console.WriteLine($"Installing {label}...");
            var result = await module.InstallAsync(cancellationToken);
            Console.WriteLine($"  {result.AssetName} from {result.ReleaseTag} → {result.DestinationPath}");
        }

        Console.WriteLine("Creating Ludusavi config...");
        await CreateLudusaviConfigAsync(deckSyncDirectory, cancellationToken);

        Console.WriteLine("Running Ludusavi backup...");
        await RunLudusaviBackupAsync(deckSyncDirectory, cancellationToken);
        Console.WriteLine("  Ludusavi backup completed.");

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

static async Task CreateLudusaviConfigAsync(string deckSyncDirectory, CancellationToken cancellationToken)
{
    var configDirectory = Path.Combine(deckSyncDirectory, "config");
    var configPath = Path.Combine(configDirectory, "config.yaml");
    var backupDirectory = ResolveDeckSyncBackupDirectory();

    Directory.CreateDirectory(configDirectory);
    Directory.CreateDirectory(backupDirectory);

    var configContents = $$"""
    manifest:
      url: "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml"
    backup:
      path: '{{backupDirectory}}'
    restore:
      path: '{{backupDirectory}}'
    """;

    await File.WriteAllTextAsync(configPath, configContents, cancellationToken);
}

static async Task RunLudusaviBackupAsync(string deckSyncDirectory, CancellationToken cancellationToken)
{
    var configDirectory = Path.Combine(deckSyncDirectory, "config");
    var backupDirectory = ResolveDeckSyncBackupDirectory();
    var ludusaviExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "ludusavi");

    if (!File.Exists(ludusaviExecutable))
    {
        throw new SetupInstallException(
            SetupInstallError.WriteFailed,
            $"Expected Ludusavi executable was not found at '{ludusaviExecutable}'.");
    }

    Directory.CreateDirectory(backupDirectory);

    var startInfo = new ProcessStartInfo
    {
        FileName = ludusaviExecutable,
        WorkingDirectory = deckSyncDirectory,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("backup");
    startInfo.ArgumentList.Add("--config");
    startInfo.ArgumentList.Add(configDirectory);

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        throw new SetupInstallException(
            SetupInstallError.WriteFailed,
            "Failed to start the Ludusavi backup process.");
    }

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode != 0)
    {
        throw new SetupInstallException(
            SetupInstallError.WriteFailed,
            $"Ludusavi backup failed with exit code {process.ExitCode}.");
    }
}

static string ResolveDeckSyncBackupDirectory()
{
    var home = Environment.GetEnvironmentVariable("HOME");
    if (string.IsNullOrWhiteSpace(home))
        home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    if (string.IsNullOrWhiteSpace(home))
        throw new InvalidOperationException("Could not determine the current user's home directory.");

    return Path.Combine(home, ".deck-sync-backup");
}

static string ResolveDeckSyncExecutablePath(string deckSyncDirectory, string executableName)
{
    if (OperatingSystem.IsWindows())
        return Path.Combine(deckSyncDirectory, $"{executableName}.exe");

    return Path.Combine(deckSyncDirectory, executableName);
}
