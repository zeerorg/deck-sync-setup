using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

public sealed class CleanupThenInstallTests
{
    private readonly ITestOutputHelper _output;

    public CleanupThenInstallTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Cleanup_then_install_seeds_ludusavi_config_with_custom_rclone_settings()
    {
        var homeDirectory = Path.Combine(Path.GetTempPath(), "deck-sync-setup-it", Guid.NewGuid().ToString("N"));
        var runtimeDirectory = Path.Combine(homeDirectory, ".deck-sync");
        var backupDirectory = Path.Combine(homeDirectory, ".deck-sync-backup");
        var logMessages = new List<string>();
        _output.WriteLine($"Home: {homeDirectory}");
        _output.WriteLine($"Runtime: {runtimeDirectory}");
        _output.WriteLine($"Backup: {backupDirectory}");

        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(Path.Combine(runtimeDirectory, "sentinel.txt"), "remove-me");
        Directory.CreateDirectory(backupDirectory);
        await File.WriteAllTextAsync(Path.Combine(backupDirectory, "keep-me.txt"), "keep-me");

        try
        {
            var locationsModule = new TestDeckSyncLocationsModule(runtimeDirectory, backupDirectory);
            var progressReporter = new RecordingSetupProgressReporter(_output.WriteLine, logMessages);
            var cleanupModule = new SetupCleanupModule(locationsModule, progressReporter);
            var installModule = new SetupInstallModule(locationsModule, progressReporter);

            _output.WriteLine("Running cleanup...");
            var cleanupResult = await cleanupModule.CleanupAsync();
            Assert.True(cleanupResult.RemovedRuntime);
            Assert.Equal(runtimeDirectory, cleanupResult.DeckSyncRuntimeLocation.Path);
            Assert.Equal(backupDirectory, cleanupResult.DeckSyncBackupLocation.Path);
            Assert.False(Directory.Exists(runtimeDirectory));
            Assert.True(Directory.Exists(backupDirectory));
            Assert.True(File.Exists(Path.Combine(backupDirectory, "keep-me.txt")));

            var backupEntriesBeforeInstall = Directory
                .EnumerateFileSystemEntries(backupDirectory, "*", SearchOption.AllDirectories)
                .ToArray();

            _output.WriteLine("Running install...");
            var installResult = await installModule.InstallAsync();
            Assert.Equal(runtimeDirectory, installResult.DeckSyncRuntimeLocation.Path);
            Assert.Equal(backupDirectory, installResult.DeckSyncBackupLocation.Path);
            Assert.Equal(3, installResult.InstalledTools.Count);
            Assert.Equal(["rclone", "syncthing", "ludusavi"], installResult.InstalledTools.Select(tool => tool.ToolName));
            Assert.True(Directory.Exists(runtimeDirectory));

            var configPath = Path.Combine(runtimeDirectory, "config", "config.yaml");
            Assert.True(File.Exists(configPath));
            var configText = (await File.ReadAllTextAsync(configPath)).Replace("\r\n", "\n");
            var expectedBackupPath = backupDirectory.Replace('\\', '/');
            var expectedRclonePath = Path.Combine(runtimeDirectory, "rclone.exe").Replace('\\', '/');
            var expectedRcloneConfig = Path.Combine(runtimeDirectory, "rclone.conf").Replace('\\', '/');

            Assert.Contains($"backup:\n  path: {expectedBackupPath}", configText);
            Assert.Contains($"restore:\n  path: {expectedBackupPath}", configText);
            Assert.Contains(
                $"apps:\n  rclone:\n    path: {expectedRclonePath}\n    arguments: --config {expectedRcloneConfig}",
                configText);
            Assert.Contains(logMessages, message => message.Contains("config show"));
            Assert.Contains(logMessages, message => message.Contains("backup --force"));
            Assert.Contains(logMessages, message => message.Contains("Ludusavi backup exited with code 0"));

            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "ludusavi.exe")));
            Assert.True(Directory.Exists(backupDirectory));

            var backupEntriesAfterInstall = Directory
                .EnumerateFileSystemEntries(backupDirectory, "*", SearchOption.AllDirectories)
                .ToArray();
            var resolvedBackupEntries = backupEntriesAfterInstall
                .Except(backupEntriesBeforeInstall)
                .ToArray();

            _output.WriteLine($"Backup entries before install: {backupEntriesBeforeInstall.Length}");
            foreach (var entry in backupEntriesBeforeInstall)
                _output.WriteLine($"  before: {entry}");

            _output.WriteLine($"Backup entries after install: {backupEntriesAfterInstall.Length}");
            foreach (var entry in backupEntriesAfterInstall)
                _output.WriteLine($"  after: {entry}");

            _output.WriteLine($"Resolved backup entries: {resolvedBackupEntries.Length}");
            foreach (var entry in resolvedBackupEntries)
                _output.WriteLine($"  {entry}");
        }
        finally
        {
            _output.WriteLine("Cleaning up temp test directory.");
            if (Directory.Exists(backupDirectory))
                Directory.Delete(backupDirectory, recursive: true);
            if (Directory.Exists(homeDirectory))
                Directory.Delete(homeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_when_runtime_is_missing_is_a_successful_no_op()
    {
        var homeDirectory = Path.Combine(Path.GetTempPath(), "deck-sync-setup-it", Guid.NewGuid().ToString("N"));
        var runtimeDirectory = Path.Combine(homeDirectory, ".deck-sync");
        var backupDirectory = Path.Combine(homeDirectory, ".deck-sync-backup");
        var logMessages = new List<string>();

        try
        {
            var locationsModule = new TestDeckSyncLocationsModule(runtimeDirectory, backupDirectory);
            var progressReporter = new RecordingSetupProgressReporter(_output.WriteLine, logMessages);
            var cleanupModule = new SetupCleanupModule(locationsModule, progressReporter);

            var cleanupResult = await cleanupModule.CleanupAsync();

            Assert.False(cleanupResult.RemovedRuntime);
            Assert.Equal(runtimeDirectory, cleanupResult.DeckSyncRuntimeLocation.Path);
            Assert.Equal(backupDirectory, cleanupResult.DeckSyncBackupLocation.Path);
            Assert.Contains(logMessages, message => message.Contains("Nothing to delete"));
            Assert.False(Directory.Exists(runtimeDirectory));
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
                Directory.Delete(backupDirectory, recursive: true);
            if (Directory.Exists(homeDirectory))
                Directory.Delete(homeDirectory, recursive: true);
        }
    }

    private sealed class TestDeckSyncLocationsModule : IDeckSyncLocationsModule
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

    private sealed class RecordingSetupProgressReporter : ISetupProgressReporter
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
}
