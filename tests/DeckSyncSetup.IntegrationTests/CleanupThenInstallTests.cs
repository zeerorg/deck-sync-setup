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
            void Log(string message)
            {
                logMessages.Add(message);
                _output.WriteLine(message);
            }

            _output.WriteLine("Running cleanup...");
            await DeckSyncSetupRuntime.CleanupAsync(runtimeDirectory, Log);
            Assert.False(Directory.Exists(runtimeDirectory));
            Assert.True(Directory.Exists(backupDirectory));
            Assert.True(File.Exists(Path.Combine(backupDirectory, "keep-me.txt")));

            var backupEntriesBeforeInstall = Directory
                .EnumerateFileSystemEntries(backupDirectory, "*", SearchOption.AllDirectories)
                .ToArray();

            _output.WriteLine("Running install...");
            await DeckSyncSetupRuntime.InstallAsync(runtimeDirectory, backupDirectory, Log);
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
}
