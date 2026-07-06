using Xunit;
using Xunit.Abstractions;

public sealed class InstallFlowTests
{
    private readonly ITestOutputHelper _output;

    public InstallFlowTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Install_creates_deck_sync_runtime_and_ludusavi_config_with_platform_tool_paths()
    {
        using var context = new DeckSyncTestContext(_output);
        var releaseAssets = new FakeReleaseAssetInstallModule();
        var ludusavi = new FakeLudusaviProcessPort
        {
            ShowConfigResult = FakeLudusaviProcessPort.SuccessfulConfigShowResult(),
            BackupResult = FakeLudusaviProcessPort.SuccessfulBackupResult(),
        };
        var rclone = new FakeRcloneProcessPort
        {
            CreateGoogleDriveRemoteResult = FakeRcloneProcessPort.SuccessfulCreateGoogleDriveRemoteResult(),
        };
        var installModule = new SetupInstallModule(
            context.LocationsModule,
            context.ProgressReporter,
            releaseAssets,
            ludusavi,
            rclone);

        var result = await installModule.InstallAsync();

        Assert.Equal(context.RuntimeDirectory, result.DeckSyncRuntimeLocation.Path);
        Assert.Equal(context.BackupDirectory, result.DeckSyncBackupLocation.Path);
        Assert.Equal(["rclone", "syncthing", "ludusavi"], result.InstalledTools.Select(tool => tool.ToolName));
        Assert.Equal(["rclone", "syncthing", "ludusavi"], releaseAssets.Requests.Select(request => request.ToolName));
        Assert.True(Directory.Exists(context.RuntimeDirectory));
        Assert.True(Directory.Exists(context.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("rclone"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("syncthing"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("ludusavi"))));

        var configPath = Path.Combine(context.RuntimeDirectory, "config", "config.yaml");
        Assert.True(File.Exists(configPath));
        var configText = (await File.ReadAllTextAsync(configPath)).Replace("\r\n", "\n");
        var expectedBackupPath = TestPaths.YamlPath(context.BackupDirectory);
        var expectedRclonePath = TestPaths.YamlPath(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("rclone")));
        var expectedRcloneConfig = TestPaths.YamlPath(Path.Combine(context.RuntimeDirectory, "rclone.conf"));

        Assert.Contains($"backup:\n  path: {expectedBackupPath}", configText);
        Assert.Contains($"backup:\n  path: {expectedBackupPath}\n  ignoredGames: []", configText);
        Assert.Contains($"restore:\n  path: {expectedBackupPath}", configText);
        Assert.Contains("cloud:\n  remote: ~\n  path: ludusavi-backup\n  synchronize: true", configText);
        Assert.DoesNotContain("Custom:", configText);
        Assert.Contains(
            $"apps:\n  rclone:\n    path: {expectedRclonePath}\n    arguments: --config {expectedRcloneConfig}",
            configText);
        Assert.Single(ludusavi.ShowConfigCalls);
        Assert.Single(ludusavi.BackupCalls);
        Assert.Single(rclone.CreateGoogleDriveRemoteCalls);
        Assert.Equal(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("ludusavi")), ludusavi.ShowConfigCalls[0].ExecutablePath);
        Assert.Equal(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("ludusavi")), ludusavi.BackupCalls[0].ExecutablePath);
        Assert.Equal(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("rclone")), rclone.CreateGoogleDriveRemoteCalls[0].ExecutablePath);
        Assert.Equal(Path.Combine(context.RuntimeDirectory, "rclone.conf"), rclone.CreateGoogleDriveRemoteCalls[0].ConfigPath);
        Assert.Contains(context.LogMessages, message => message.Contains("config show"));
        Assert.Contains(context.LogMessages, message => message.Contains("backup --force"));
        Assert.Contains(context.LogMessages, message => message.Contains("config create gdrive drive scope=drive config_is_local=true"));
        Assert.Contains(context.LogMessages, message => message.Contains("Ludusavi backup exited with code 0"));
        Assert.Contains(context.LogMessages, message => message.Contains("Ludusavi backup completed"));
        Assert.Contains(context.LogMessages, message => message.Contains("Rclone setup exited with code 0"));
        Assert.Contains(context.LogMessages, message => message.Contains("Rclone Google Drive setup completed"));
    }

    [Fact]
    public async Task Cleanup_then_install_removes_existing_deck_sync_runtime_and_installs_hermetically()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.RuntimeDirectory);
        await File.WriteAllTextAsync(Path.Combine(context.RuntimeDirectory, "remove-me.txt"), "remove me");
        Directory.CreateDirectory(context.BackupDirectory);
        var backupSentinelPath = Path.Combine(context.BackupDirectory, "keep-me.txt");
        await File.WriteAllTextAsync(backupSentinelPath, "keep me");
        var cleanupModule = new SetupCleanupModule(context.LocationsModule, context.ProgressReporter);
        var releaseAssets = new FakeReleaseAssetInstallModule();
        var ludusavi = new FakeLudusaviProcessPort
        {
            ShowConfigResult = FakeLudusaviProcessPort.SuccessfulConfigShowResult(),
            BackupResult = FakeLudusaviProcessPort.SuccessfulBackupResult(),
        };
        var rclone = new FakeRcloneProcessPort
        {
            CreateGoogleDriveRemoteResult = FakeRcloneProcessPort.SuccessfulCreateGoogleDriveRemoteResult(),
        };
        var installModule = new SetupInstallModule(
            context.LocationsModule,
            context.ProgressReporter,
            releaseAssets,
            ludusavi,
            rclone);

        var cleanupResult = await cleanupModule.CleanupAsync();
        var installResult = await installModule.InstallAsync();

        Assert.True(cleanupResult.RemovedRuntime);
        Assert.Equal(["rclone", "syncthing", "ludusavi"], installResult.InstalledTools.Select(tool => tool.ToolName));
        Assert.True(Directory.Exists(context.RuntimeDirectory));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("rclone"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("syncthing"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("ludusavi"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, "config", "config.yaml")));
        Assert.True(File.Exists(backupSentinelPath));
        Assert.Equal(["rclone", "syncthing", "ludusavi"], releaseAssets.Requests.Select(request => request.ToolName));
        Assert.Single(ludusavi.ShowConfigCalls);
        Assert.Single(ludusavi.BackupCalls);
        Assert.Single(rclone.CreateGoogleDriveRemoteCalls);
    }

    [Fact]
    public async Task Install_rejects_existing_deck_sync_runtime()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.RuntimeDirectory);
        var releaseAssets = new FakeReleaseAssetInstallModule();
        var ludusavi = new FakeLudusaviProcessPort();
        var rclone = new FakeRcloneProcessPort();
        var installModule = new SetupInstallModule(
            context.LocationsModule,
            context.ProgressReporter,
            releaseAssets,
            ludusavi,
            rclone);

        var exception = await Assert.ThrowsAsync<SetupInstallException>(() => installModule.InstallAsync());

        Assert.Equal(SetupInstallError.DeckSyncRuntimeAlreadyExists, exception.Code);
        Assert.Empty(releaseAssets.Requests);
        Assert.Empty(ludusavi.ShowConfigCalls);
        Assert.Empty(rclone.CreateGoogleDriveRemoteCalls);
        Assert.True(Directory.Exists(context.RuntimeDirectory));
    }

    [Fact]
    public async Task Failed_ludusavi_backup_rolls_back_deck_sync_runtime_and_preserves_backup_data()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.BackupDirectory);
        var backupSentinelPath = Path.Combine(context.BackupDirectory, "keep-me.txt");
        await File.WriteAllTextAsync(backupSentinelPath, "keep me");
        var releaseAssets = new FakeReleaseAssetInstallModule();
        var ludusavi = new FakeLudusaviProcessPort
        {
            ShowConfigResult = FakeLudusaviProcessPort.SuccessfulConfigShowResult(),
            BackupResult = new LudusaviProcessResult(42, "", "backup failed"),
        };
        var rclone = new FakeRcloneProcessPort();
        var installModule = new SetupInstallModule(
            context.LocationsModule,
            context.ProgressReporter,
            releaseAssets,
            ludusavi,
            rclone);

        var exception = await Assert.ThrowsAsync<SetupInstallException>(() => installModule.InstallAsync());

        Assert.Equal(SetupInstallError.LudusaviBackupFailed, exception.Code);
        Assert.False(Directory.Exists(context.RuntimeDirectory));
        Assert.True(Directory.Exists(context.BackupDirectory));
        Assert.True(File.Exists(backupSentinelPath));
        Assert.Empty(rclone.CreateGoogleDriveRemoteCalls);
        Assert.Contains(context.LogMessages, message => message.Contains("Ludusavi backup exited with code 42"));
        Assert.Contains(context.LogMessages, message => message.Contains("Rolled back partial Deck sync runtime"));
    }

    [Fact]
    public async Task Failed_rclone_google_drive_setup_rolls_back_deck_sync_runtime_and_preserves_backup_data()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.BackupDirectory);
        var backupSentinelPath = Path.Combine(context.BackupDirectory, "keep-me.txt");
        await File.WriteAllTextAsync(backupSentinelPath, "keep me");
        var releaseAssets = new FakeReleaseAssetInstallModule();
        var ludusavi = new FakeLudusaviProcessPort
        {
            ShowConfigResult = FakeLudusaviProcessPort.SuccessfulConfigShowResult(),
            BackupResult = FakeLudusaviProcessPort.SuccessfulBackupResult(),
        };
        var rclone = new FakeRcloneProcessPort
        {
            CreateGoogleDriveRemoteResult = new RcloneProcessResult(43, "", "rclone failed"),
        };
        var installModule = new SetupInstallModule(
            context.LocationsModule,
            context.ProgressReporter,
            releaseAssets,
            ludusavi,
            rclone);

        var exception = await Assert.ThrowsAsync<SetupInstallException>(() => installModule.InstallAsync());

        Assert.Equal(SetupInstallError.RcloneGoogleDriveSetupFailed, exception.Code);
        Assert.False(Directory.Exists(context.RuntimeDirectory));
        Assert.True(Directory.Exists(context.BackupDirectory));
        Assert.True(File.Exists(backupSentinelPath));
        Assert.Single(rclone.CreateGoogleDriveRemoteCalls);
        Assert.Contains(context.LogMessages, message => message.Contains("Rclone setup exited with code 43"));
        Assert.Contains(context.LogMessages, message => message.Contains("Rolled back partial Deck sync runtime"));
    }
}
