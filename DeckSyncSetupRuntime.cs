using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Win32;

public static class DeckSyncSetupRuntime
{
    public static async Task InstallAsync(
        string deckSyncDirectory,
        string backupDirectory,
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

        log?.Invoke("Seeding Ludusavi config...");
        var configuredRoots = await CreateLudusaviConfigAsync(
            deckSyncDirectory,
            backupDirectory,
            log,
            cancellationToken);
        if (configuredRoots == 0)
        {
            log?.Invoke("  Warning: No Steam library root was auto-detected, so Ludusavi may find zero saves.");
            log?.Invoke("  You can add roots later in .deck-sync/config/config.yaml under `roots`.");
        }

        log?.Invoke("Running Ludusavi backup...");
        await RunLudusaviBackupAsync(deckSyncDirectory, log, cancellationToken);
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
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(deckSyncDirectory, "config");
        var configPath = Path.Combine(configDirectory, "config.yaml");
        var rcloneExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "rclone");
        var rcloneConfigPath = Path.Combine(deckSyncDirectory, "rclone.conf");
        var roots = ResolveLudusaviRoots();

        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(backupDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDeckSyncExecutablePath(deckSyncDirectory, "ludusavi"),
            WorkingDirectory = deckSyncDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configDirectory);
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("show");

        log?.Invoke($"  Running command: {FormatCommandLine(startInfo.FileName, startInfo.ArgumentList)}");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new SetupInstallException(
                SetupInstallError.WriteFailed,
                "Failed to start the Ludusavi config seeding process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var yaml = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new SetupInstallException(
                SetupInstallError.WriteFailed,
                $"Ludusavi config seeding failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }

        yaml = ApplyLudusaviConfigOverrides(
            yaml,
            backupDirectory,
            rcloneExecutable,
            $"--config {ToYamlPath(rcloneConfigPath)}");

        await File.WriteAllTextAsync(
            configPath,
            yaml.EndsWith(Environment.NewLine) ? yaml : yaml + Environment.NewLine,
            cancellationToken);
        return roots.Count;
    }

    private static async Task RunLudusaviBackupAsync(
        string deckSyncDirectory,
        Action<string>? log,
        CancellationToken cancellationToken)
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
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configDirectory);
        startInfo.ArgumentList.Add("backup");
        startInfo.ArgumentList.Add("--force");

        log?.Invoke($"  Running command: {FormatCommandLine(startInfo.FileName, startInfo.ArgumentList)}");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new SetupInstallException(
                SetupInstallError.WriteFailed,
                "Failed to start the Ludusavi backup process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            foreach (var line in stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                log?.Invoke($"  [ludusavi stdout] {line}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            foreach (var line in stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                log?.Invoke($"  [ludusavi stderr] {line}");
        }

        log?.Invoke($"  Ludusavi backup exited with code {process.ExitCode}.");

        if (process.ExitCode != 0)
        {
            throw new SetupInstallException(
                SetupInstallError.WriteFailed,
                $"Ludusavi backup failed with exit code {process.ExitCode}: {stderr.Trim()}");
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

    private static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
        string.Join(" ", new[] { QuoteCommandLineArgument(fileName) }.Concat(arguments.Select(QuoteCommandLineArgument)));

    private static string QuoteCommandLineArgument(string argument)
    {
        if (argument.Length == 0 || argument.Any(char.IsWhiteSpace) || argument.Contains('"'))
        {
            return $"\"{argument.Replace("\"", "\\\"")}\"";
        }

        return argument;
    }

    private static string ApplyLudusaviConfigOverrides(
        string yaml,
        string backupDirectory,
        string rcloneExecutable,
        string rcloneArguments)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var updated = new List<string>(lines.Length);
        string? topLevelSection = null;
        string? nestedSection = null;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                updated.Add(line);
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (indent == 0)
            {
                topLevelSection = TryGetYamlKey(trimmed);
                nestedSection = null;
            }
            else if (indent == 2)
            {
                nestedSection = TryGetYamlKey(trimmed);
            }

            if (indent == 2 && topLevelSection == "backup" && trimmed.StartsWith("path:"))
            {
                updated.Add($"{new string(' ', indent)}path: {ToYamlPath(backupDirectory)}");
            }
            else if (indent == 2 && topLevelSection == "restore" && trimmed.StartsWith("path:"))
            {
                updated.Add($"{new string(' ', indent)}path: {ToYamlPath(backupDirectory)}");
            }
            else if (indent == 4 && topLevelSection == "apps" && nestedSection == "rclone" && trimmed.StartsWith("path:"))
            {
                updated.Add($"{new string(' ', indent)}path: {ToYamlPath(rcloneExecutable)}");
            }
            else if (indent == 4 && topLevelSection == "apps" && nestedSection == "rclone" && trimmed.StartsWith("arguments:"))
            {
                updated.Add($"{new string(' ', indent)}arguments: {rcloneArguments}");
            }
            else
            {
                updated.Add(line);
            }
        }

        return string.Join("\n", updated);
    }

    private static string? TryGetYamlKey(string line)
    {
        var separatorIndex = line.IndexOf(':');
        return separatorIndex > 0 ? line[..separatorIndex] : null;
    }
}
