using Xunit;
using Xunit.Abstractions;

internal sealed class DeckSyncTestContext : IDisposable
{
    public DeckSyncTestContext(ITestOutputHelper output)
    {
        HomeDirectory = Path.Combine(Path.GetTempPath(), "deck-sync-setup-it", Guid.NewGuid().ToString("N"));
        RuntimeDirectory = Path.Combine(HomeDirectory, ".deck-sync");
        BackupDirectory = Path.Combine(HomeDirectory, ".deck-sync-backup");
        LocationsModule = new TestDeckSyncLocationsModule(RuntimeDirectory, BackupDirectory);
        ProgressReporter = new RecordingSetupProgressReporter(output.WriteLine, LogMessages);

        output.WriteLine($"Home: {HomeDirectory}");
        output.WriteLine($"Runtime: {RuntimeDirectory}");
        output.WriteLine($"Backup: {BackupDirectory}");
    }

    public string HomeDirectory { get; }

    public string RuntimeDirectory { get; }

    public string BackupDirectory { get; }

    public TestDeckSyncLocationsModule LocationsModule { get; }

    public RecordingSetupProgressReporter ProgressReporter { get; }

    public List<string> LogMessages { get; } = [];

    public void Dispose()
    {
        if (Directory.Exists(HomeDirectory))
            Directory.Delete(HomeDirectory, recursive: true);
    }
}

internal sealed class TestDeckSyncLocationsModule : IDeckSyncLocationsModule
{
    private readonly DeckSyncRuntimeLocation _runtimeLocation;
    private readonly DeckSyncBackupLocation _backupLocation;

    public TestDeckSyncLocationsModule(string runtimeDirectory, string backupDirectory)
    {
        _runtimeLocation = new DeckSyncRuntimeLocation(runtimeDirectory);
        _backupLocation = new DeckSyncBackupLocation(backupDirectory);
    }

    public DeckSyncRuntimeLocation ResolveRuntimeLocation() => _runtimeLocation;

    public DeckSyncBackupLocation ResolveBackupLocation() => _backupLocation;
}

internal sealed class RecordingSetupProgressReporter : ISetupProgressReporter
{
    private readonly Action<string> _writeLine;
    private readonly List<string> _messages;

    public RecordingSetupProgressReporter(Action<string> writeLine, List<string> messages)
    {
        _writeLine = writeLine;
        _messages = messages;
    }

    public void Report(SetupProgress progress)
    {
        _messages.Add(progress.Message);
        _writeLine(progress.Message);
    }
}

internal sealed class FakeReleaseAssetInstallModule : IReleaseAssetInstallModule
{
    public List<ReleaseAssetInstallRequest> Requests { get; } = [];

    public async Task<ReleaseAssetInstallResult> InstallAsync(
        ReleaseAssetInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        Directory.CreateDirectory(request.DeckSyncRuntimeLocation.Path);

        var executablePath = Path.Combine(
            request.DeckSyncRuntimeLocation.Path,
            TestPaths.ToolExecutableName(request.ToolName));
        await File.WriteAllTextAsync(executablePath, $"fake {request.ToolName}", cancellationToken);

        return new ReleaseAssetInstallResult(
            request.ToolName,
            $"v-test-{request.ToolName}",
            $"{request.ToolName}-{TestPaths.PlatformAssetSuffix}.zip",
            request.DeckSyncRuntimeLocation,
            request.DeckSyncRuntimeLocation.Path);
    }
}

internal sealed class FakeLudusaviProcessPort : ILudusaviProcessPort
{
    public LudusaviProcessResult? ShowConfigResult { get; set; }

    public LudusaviProcessResult? BackupResult { get; set; }

    public List<LudusaviProcessCall> ShowConfigCalls { get; } = [];

    public List<LudusaviProcessCall> BackupCalls { get; } = [];

    public static LudusaviProcessResult SuccessfulConfigShowResult() => new(
        0,
        """
        manifest: {}
        roots: []
        backup:
          path: old-backup
        restore:
          path: old-restore
        cloud:
          remote:
            Custom:
              id: ludusavi
          path: ludusavi-backup
          synchronize: true
        apps:
          rclone:
            path: "C:\\Users\\rggup\\.deck-sync\\rclone.exe"
            arguments: "--config=C:/Users/rggup/.deck-sync/rclone.conf --fast-list --ignore-checksum"
        """,
        "");

    public static LudusaviProcessResult SuccessfulBackupResult() => new(0, "backup ok", "");

    public Task<LudusaviProcessResult> ShowConfigAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default)
    {
        ShowConfigCalls.Add(new LudusaviProcessCall(executablePath, workingDirectory, configDirectory));
        return Task.FromResult(ShowConfigResult ?? throw new InvalidOperationException("Fake Ludusavi config-show result was not configured."));
    }

    public Task<LudusaviProcessResult> BackupAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default)
    {
        BackupCalls.Add(new LudusaviProcessCall(executablePath, workingDirectory, configDirectory));
        return Task.FromResult(BackupResult ?? throw new InvalidOperationException("Fake Ludusavi backup result was not configured."));
    }
}

internal sealed record LudusaviProcessCall(
    string ExecutablePath,
    string WorkingDirectory,
    string ConfigDirectory);

internal sealed class FakeRcloneProcessPort : IRcloneProcessPort
{
    public RcloneProcessResult? CreateGoogleDriveRemoteResult { get; set; }

    public List<RcloneProcessCall> CreateGoogleDriveRemoteCalls { get; } = [];

    public static RcloneProcessResult SuccessfulCreateGoogleDriveRemoteResult() => new(0, "rclone ok", "");

    public Task<RcloneProcessResult> CreateGoogleDriveRemoteAsync(
        string executablePath,
        string workingDirectory,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        CreateGoogleDriveRemoteCalls.Add(new RcloneProcessCall(executablePath, workingDirectory, configPath));
        return Task.FromResult(CreateGoogleDriveRemoteResult ?? throw new InvalidOperationException("Fake Rclone Google Drive setup result was not configured."));
    }
}

internal sealed record RcloneProcessCall(
    string ExecutablePath,
    string WorkingDirectory,
    string ConfigPath);

internal sealed class FakeSetupInstallModule : ISetupInstallModule
{
    private readonly Exception? _exception;

    public FakeSetupInstallModule(Exception? exception = null) => _exception = exception;

    public int CallCount { get; private set; }

    public Task<SetupInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(new SetupInstallResult(
            new DeckSyncRuntimeLocation(Path.Combine(Path.GetTempPath(), "runtime")),
            new DeckSyncBackupLocation(Path.Combine(Path.GetTempPath(), "backup")),
            []));
    }
}

internal sealed class FakeSetupCleanupModule : ISetupCleanupModule
{
    private readonly Exception? _exception;

    public FakeSetupCleanupModule(Exception? exception = null) => _exception = exception;

    public int CallCount { get; private set; }

    public Task<SetupCleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(new SetupCleanupResult(
            new DeckSyncRuntimeLocation(Path.Combine(Path.GetTempPath(), "runtime")),
            new DeckSyncBackupLocation(Path.Combine(Path.GetTempPath(), "backup")),
            RemovedRuntime: false));
    }
}

internal static class TestPaths
{
    public static string PlatformAssetSuffix => OperatingSystem.IsWindows() ? "windows" : "linux";

    public static string ToolExecutableName(string toolName) =>
        OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;

    public static string YamlPath(string path) => path.Replace('\\', '/');
}

internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DECK_SYNC_SETUP_LIVE_TESTS") != "1")
            Skip = "Set DECK_SYNC_SETUP_LIVE_TESTS=1 to run live Deck sync runtime smoke tests.";
    }
}
