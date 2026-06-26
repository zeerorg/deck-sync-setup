using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Win32;
using SharpYaml;

public static class DeckSyncSetupRuntime
{
    public static async Task InstallAsync(
        string deckSyncDirectory,
        string backupDirectory,
        IReadOnlyList<string>? customBackupFiles = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
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
            ("rclone",    new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "rclone",       "rclone"),    PlatformAssetFragments.Rclone,    archiveExtractionModule, deckSyncDirectory)),
            ("syncthing", new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "syncthing",    "syncthing"), PlatformAssetFragments.Syncthing, archiveExtractionModule, deckSyncDirectory)),
            ("ludusavi",  new SetupInstallModule(new GitHubSetupInstallAdapter(httpClient, "mtkennerly",   "ludusavi"),  PlatformAssetFragments.Ludusavi,    archiveExtractionModule, deckSyncDirectory)),
        };

        foreach (var (label, module) in tools)
        {
            log?.Invoke($"Installing {label}...");
            var result = await module.InstallAsync(cancellationToken);
            log?.Invoke($"  {result.AssetName} from {result.ReleaseTag} → {result.DestinationPath}");
        }

        log?.Invoke("Creating Ludusavi config...");
        var configuredRoots = await CreateLudusaviConfigAsync(
            deckSyncDirectory,
            backupDirectory,
            customBackupFiles,
            cancellationToken);
        if (configuredRoots == 0)
        {
            log?.Invoke("  Warning: No Steam library root was auto-detected, so Ludusavi may find zero saves.");
            log?.Invoke("  You can add roots later in .deck-sync/config/config.yaml under `roots`.");
        }

        log?.Invoke("Running Ludusavi backup...");
        await RunLudusaviBackupAsync(deckSyncDirectory, cancellationToken);
        log?.Invoke("  Ludusavi backup completed.");
    }

    public static Task CleanupAsync(string deckSyncDirectory, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(deckSyncDirectory))
        {
            log?.Invoke($"Nothing to delete: {deckSyncDirectory}");
            return Task.CompletedTask;
        }

        Directory.Delete(deckSyncDirectory, recursive: true);
        log?.Invoke($"Deleted {deckSyncDirectory}");
        return Task.CompletedTask;
    }

    public static string ResolveDeckSyncBackupDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("Could not determine the current user's home directory.");

        return Path.Combine(home, ".deck-sync-backup");
    }

    private static HttpClient CreateGitHubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("deck-sync-setup", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<int> CreateLudusaviConfigAsync(
        string deckSyncDirectory,
        string backupDirectory,
        IReadOnlyList<string>? customBackupFiles,
        CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(deckSyncDirectory, "config");
        var configPath = Path.Combine(configDirectory, "config.yaml");
        var roots = ResolveLudusaviRoots();

        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(backupDirectory);

        var config = new Dictionary<string, object?>
        {
            ["manifest"] = new Dictionary<string, string>
            {
                ["url"] = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml",
            },
            ["roots"] = roots
                .Select(root => new Dictionary<string, string>
                {
                    ["path"] = ToYamlPath(root.Path),
                    ["store"] = root.Store,
                })
                .ToList(),
            ["backup"] = new Dictionary<string, string>
            {
                ["path"] = ToYamlPath(backupDirectory),
            },
            ["restore"] = new Dictionary<string, string>
            {
                ["path"] = ToYamlPath(backupDirectory),
            },
        };

        if (customBackupFiles is { Count: > 0 })
        {
            config["customGames"] = customBackupFiles.Select((filePath, index) => new Dictionary<string, object?>
            {
                ["name"] = $"Integration Test Fixture {index + 1}",
                ["integration"] = "override",
                ["files"] = new[] { ToYamlPath(filePath) },
                ["registry"] = Array.Empty<string>(),
                ["installDir"] = Array.Empty<string>(),
            }).ToList();
        }

        var yaml = YamlSerializer.Serialize(config);
        await File.WriteAllTextAsync(configPath, yaml.EndsWith(Environment.NewLine) ? yaml : yaml + Environment.NewLine, cancellationToken);
        return roots.Count;
    }

    private static async Task RunLudusaviBackupAsync(string deckSyncDirectory, CancellationToken cancellationToken)
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
        startInfo.ArgumentList.Add("--no-manifest-update");
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configDirectory);
        startInfo.ArgumentList.Add("backup");
        startInfo.ArgumentList.Add("--force");

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

    private static string ResolveDeckSyncExecutablePath(string deckSyncDirectory, string executableName)
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(deckSyncDirectory, $"{executableName}.exe");

        return Path.Combine(deckSyncDirectory, executableName);
    }

    private static IReadOnlyList<(string Path, string Store)> ResolveLudusaviRoots()
    {
        var roots = new List<(string Path, string Store)>();

        static void AddRootIfExists(List<(string Path, string Store)> roots, string? path, string store)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!Directory.Exists(path))
                return;

            if (roots.Any(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;

            roots.Add((path, store));
        }

        if (OperatingSystem.IsWindows())
        {
            var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            AddRootIfExists(roots, steamPath, "steam");

            AddRootIfExists(
                roots,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                "steam");
            AddRootIfExists(
                roots,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                "steam");
        }

        return roots;
    }

    private static string ToYamlPath(string path) => path.Replace('\\', '/');
}
