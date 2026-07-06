using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Win32;

/// <summary>Deep orchestration module that installs and seeds the Deck sync runtime.</summary>
public interface ISetupInstallModule
{
    /// <summary>Installs the Deck sync runtime and seeds Ludusavi for the current host.</summary>
    /// <returns>Details of what was installed and where.</returns>
    /// <exception cref="SetupInstallException">
    /// Thrown when installation fails for a known reason.
    /// Inspect <see cref="SetupInstallException.Code"/> for the failure category.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task<SetupInstallResult> InstallAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc/>
public sealed class SetupInstallModule : ISetupInstallModule
{
    private readonly IDeckSyncLocationsModule _locationsModule;
    private readonly ISetupProgressReporter _progressReporter;
    private readonly IReleaseAssetInstallModule? _releaseAssetInstallModule;
    private readonly ILudusaviProcessPort? _ludusaviProcessPort;
    private readonly IRcloneProcessPort? _rcloneProcessPort;

    /// <param name="locationsModule">Resolves the runtime and backup locations.</param>
    /// <param name="progressReporter">Receives structured progress messages.</param>
    public SetupInstallModule(
        IDeckSyncLocationsModule locationsModule,
        ISetupProgressReporter progressReporter)
    {
        _locationsModule = locationsModule;
        _progressReporter = progressReporter;
    }

    internal SetupInstallModule(
        IDeckSyncLocationsModule locationsModule,
        ISetupProgressReporter progressReporter,
        IReleaseAssetInstallModule releaseAssetInstallModule,
        ILudusaviProcessPort ludusaviProcessPort,
        IRcloneProcessPort rcloneProcessPort)
    {
        _locationsModule = locationsModule;
        _progressReporter = progressReporter;
        _releaseAssetInstallModule = releaseAssetInstallModule;
        _ludusaviProcessPort = ludusaviProcessPort;
        _rcloneProcessPort = rcloneProcessPort;
    }

    /// <inheritdoc/>
    public async Task<SetupInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        DeckSyncRuntimeLocation runtimeLocation;
        DeckSyncBackupLocation backupLocation;
        try
        {
            runtimeLocation = _locationsModule.ResolveRuntimeLocation();
            backupLocation = _locationsModule.ResolveBackupLocation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupInstallException(
                SetupInstallError.DeckSyncLocationsUnavailable,
                "Could not resolve the Deck sync runtime or backup location.",
                ex);
        }

        if (Directory.Exists(runtimeLocation.Path))
        {
            throw new SetupInstallException(
                SetupInstallError.DeckSyncRuntimeAlreadyExists,
                $"The Deck sync runtime directory already exists at '{runtimeLocation.Path}'. Please run `deck-sync-setup cleanup` before installing again.");
        }

        HttpClient? httpClient = null;
        var releaseAssetInstallModule = _releaseAssetInstallModule;
        if (releaseAssetInstallModule is null)
        {
            httpClient = CreateGitHubClient();
            releaseAssetInstallModule = new ReleaseAssetInstallModule(
                new GitHubReleaseHttpAdapter(httpClient),
                new ArchiveExtractionModule());
        }

        var ludusaviProcessPort = _ludusaviProcessPort ?? new LudusaviProcessAdapter();
        var rcloneProcessPort = _rcloneProcessPort ?? new RcloneProcessAdapter();

        try
        {
            var installedTools = new List<SetupInstallToolResult>();
            var toolRequests = new[]
            {
                new ReleaseAssetInstallRequest(
                    ToolName: "rclone",
                    Repository: new GitHubRepositoryIdentity("rclone", "rclone"),
                    PlatformFragments: PlatformAssetFragments.Rclone,
                    DeckSyncRuntimeLocation: runtimeLocation),
                new ReleaseAssetInstallRequest(
                    ToolName: "syncthing",
                    Repository: new GitHubRepositoryIdentity("syncthing", "syncthing"),
                    PlatformFragments: PlatformAssetFragments.Syncthing,
                    DeckSyncRuntimeLocation: runtimeLocation),
                new ReleaseAssetInstallRequest(
                    ToolName: "ludusavi",
                    Repository: new GitHubRepositoryIdentity("mtkennerly", "ludusavi"),
                    PlatformFragments: PlatformAssetFragments.Ludusavi,
                    DeckSyncRuntimeLocation: runtimeLocation),
            };

            foreach (var request in toolRequests)
            {
                installedTools.Add(await InstallReleaseAssetWithRollbackAsync(
                    releaseAssetInstallModule,
                    request,
                    runtimeLocation,
                    cancellationToken));
            }

            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, "Seeding Ludusavi config..."));
            var configuredRoots = await RunWithRollbackAsync(
                () => SeedLudusaviConfigAsync(
                    ludusaviProcessPort,
                    runtimeLocation.Path,
                    backupLocation.Path,
                    cancellationToken),
                runtimeLocation,
                SetupInstallError.LudusaviConfigSeedingFailed,
                "Failed to seed the Ludusavi config.",
                cancellationToken);

            if (configuredRoots == 0)
            {
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Warning, "  Warning: No Steam library root was auto-detected, so Ludusavi may find zero saves."));
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Warning, "  You can add roots later in .deck-sync/config/config.yaml under `roots`."));
            }

            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, "Running Ludusavi backup..."));
            await RunWithRollbackAsync(
                () => RunLudusaviBackupAsync(
                    ludusaviProcessPort,
                    runtimeLocation.Path,
                    cancellationToken),
                runtimeLocation,
                SetupInstallError.LudusaviBackupFailed,
                "Failed to run the initial Ludusavi backup.",
                cancellationToken);
            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, "  Ludusavi backup completed."));

            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, "Opening Google Drive login page for Rclone setup..."));
            await RunWithRollbackAsync(
                () => RunRcloneGoogleDriveSetupAsync(
                    rcloneProcessPort,
                    runtimeLocation.Path,
                    cancellationToken),
                runtimeLocation,
                SetupInstallError.RcloneGoogleDriveSetupFailed,
                "Failed to set up the Rclone Google Drive remote.",
                cancellationToken);
            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, "  Rclone Google Drive setup completed."));

            return new SetupInstallResult(runtimeLocation, backupLocation, installedTools);
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    private async Task<SetupInstallToolResult> InstallReleaseAssetWithRollbackAsync(
        IReleaseAssetInstallModule releaseAssetInstallModule,
        ReleaseAssetInstallRequest request,
        DeckSyncRuntimeLocation runtimeLocation,
        CancellationToken cancellationToken)
    {
        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"Installing {request.ToolName}..."));

        try
        {
            var result = await releaseAssetInstallModule.InstallAsync(request, cancellationToken);
            var publicResult = new SetupInstallToolResult(
                request.ToolName,
                result.ReleaseTag,
                result.AssetName,
                result.DestinationPath);

            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  {publicResult.AssetName} from {publicResult.ReleaseTag} → {publicResult.DestinationPath}"));
            return publicResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RollbackDeckSyncRuntimeAsync(runtimeLocation.Path, cancellationToken);
            throw new SetupInstallException(
                SetupInstallError.ReleaseAssetInstallFailed,
                $"Failed to install {request.ToolName}.",
                ex);
        }
    }

    private async Task<T> RunWithRollbackAsync<T>(
        Func<Task<T>> action,
        DeckSyncRuntimeLocation runtimeLocation,
        SetupInstallError errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RollbackDeckSyncRuntimeAsync(runtimeLocation.Path, cancellationToken);
            throw new SetupInstallException(errorCode, message, ex);
        }
    }

    private async Task RunWithRollbackAsync(
        Func<Task> action,
        DeckSyncRuntimeLocation runtimeLocation,
        SetupInstallError errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RollbackDeckSyncRuntimeAsync(runtimeLocation.Path, cancellationToken);
            throw new SetupInstallException(errorCode, message, ex);
        }
    }

    private async Task RollbackDeckSyncRuntimeAsync(string runtimePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(runtimePath))
            return;

        _progressReporter.Report(new SetupProgress(SetupProgressKind.Warning, $"Rolling back partial Deck sync runtime at {runtimePath}"));
        try
        {
            Directory.Delete(runtimePath, recursive: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupInstallException(
                SetupInstallError.RollbackFailed,
                $"Failed to roll back the Deck sync runtime at '{runtimePath}' after an installation failure.",
                ex);
        }

        _progressReporter.Report(new SetupProgress(SetupProgressKind.Warning, $"Rolled back partial Deck sync runtime at {runtimePath}"));
    }

    private async Task<int> SeedLudusaviConfigAsync(
        ILudusaviProcessPort ludusaviProcessPort,
        string deckSyncDirectory,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(deckSyncDirectory, "config");
        var configPath = Path.Combine(configDirectory, "config.yaml");
        var rcloneExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "rclone");
        var rcloneConfigPath = Path.Combine(deckSyncDirectory, "rclone.conf");
        var roots = ResolveLudusaviRoots();

        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(backupDirectory);

        var ludusaviExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "ludusavi");
        var arguments = CreateLudusaviConfigShowArguments(configDirectory);
        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  Running command: {FormatCommandLine(ludusaviExecutable, arguments)}"));

        var result = await ludusaviProcessPort.ShowConfigAsync(
            ludusaviExecutable,
            deckSyncDirectory,
            configDirectory,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Ludusavi config seeding failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        var yaml = ApplyLudusaviConfigOverrides(
            result.StandardOutput,
            backupDirectory,
            rcloneExecutable,
            $"--config {ToYamlPath(rcloneConfigPath)}");

        await File.WriteAllTextAsync(
            configPath,
            yaml.EndsWith(Environment.NewLine) ? yaml : yaml + Environment.NewLine,
            cancellationToken);

        return roots.Count;
    }

    private async Task RunLudusaviBackupAsync(
        ILudusaviProcessPort ludusaviProcessPort,
        string deckSyncDirectory,
        CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(deckSyncDirectory, "config");
        var ludusaviExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "ludusavi");
        var arguments = CreateLudusaviBackupArguments(configDirectory);
        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  Running command: {FormatCommandLine(ludusaviExecutable, arguments)}"));

        var result = await ludusaviProcessPort.BackupAsync(
            ludusaviExecutable,
            deckSyncDirectory,
            configDirectory,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            foreach (var line in result.StandardOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  [ludusavi stdout] {line}"));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            foreach (var line in result.StandardError.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  [ludusavi stderr] {line}"));
        }

        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  Ludusavi backup exited with code {result.ExitCode}."));

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Ludusavi backup failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    private async Task RunRcloneGoogleDriveSetupAsync(
        IRcloneProcessPort rcloneProcessPort,
        string deckSyncDirectory,
        CancellationToken cancellationToken)
    {
        var rcloneExecutable = ResolveDeckSyncExecutablePath(deckSyncDirectory, "rclone");
        var rcloneConfigPath = Path.Combine(deckSyncDirectory, "rclone.conf");
        var arguments = CreateRcloneGoogleDriveSetupArguments(rcloneConfigPath);
        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  Running command: {FormatCommandLine(rcloneExecutable, arguments)}"));

        var result = await rcloneProcessPort.CreateGoogleDriveRemoteAsync(
            rcloneExecutable,
            deckSyncDirectory,
            rcloneConfigPath,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            foreach (var line in result.StandardOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  [rclone stdout] {line}"));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            foreach (var line in result.StandardError.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  [rclone stderr] {line}"));
        }

        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"  Rclone setup exited with code {result.ExitCode}."));

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Rclone Google Drive setup failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    private static HttpClient CreateGitHubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("deck-sync-setup", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
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

    private static string[] CreateLudusaviConfigShowArguments(string configDirectory) =>
        ["--config", configDirectory, "config", "show"];

    private static string[] CreateLudusaviBackupArguments(string configDirectory) =>
        ["--config", configDirectory, "backup", "--force"];

    private static string[] CreateRcloneGoogleDriveSetupArguments(string rcloneConfigPath) =>
        ["--config", rcloneConfigPath, "config", "create", "gdrive", "drive", "scope=drive", "config_is_local=true"];

    private static string ApplyLudusaviConfigOverrides(
        string yaml,
        string backupDirectory,
        string rcloneExecutable,
        string rcloneArguments)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var updated = new List<string>(lines.Length);
        var backupHasIgnoredGames = YamlSectionContainsKey(lines, "backup", "ignoredGames");
        string? topLevelSection = null;
        string? nestedSection = null;
        var skipCloudRemoteChildren = false;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                updated.Add(line);
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (skipCloudRemoteChildren)
            {
                if (indent > 2)
                    continue;

                skipCloudRemoteChildren = false;
            }

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
                if (!backupHasIgnoredGames)
                {
                    updated.Add($"{new string(' ', indent)}ignoredGames: []");
                    backupHasIgnoredGames = true;
                }
            }
            else if (indent == 2 && topLevelSection == "restore" && trimmed.StartsWith("path:"))
            {
                updated.Add($"{new string(' ', indent)}path: {ToYamlPath(backupDirectory)}");
            }
            else if (indent == 2 && topLevelSection == "cloud" && trimmed.StartsWith("remote:"))
            {
                updated.Add($"{new string(' ', indent)}remote: ~");
                skipCloudRemoteChildren = true;
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

    private static bool YamlSectionContainsKey(string[] lines, string section, string key)
    {
        var inSection = false;

        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (indent == 0)
            {
                inSection = TryGetYamlKey(trimmed) == section;
                continue;
            }

            if (inSection && indent == 2 && TryGetYamlKey(trimmed) == key)
                return true;
        }

        return false;
    }

    private static string? TryGetYamlKey(string line)
    {
        var separatorIndex = line.IndexOf(':');
        return separatorIndex > 0 ? line[..separatorIndex] : null;
    }
}
