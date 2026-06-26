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
    public async Task Cleanup_then_install_removes_the_old_runtime_and_recreates_it_with_backup_config()
    {
        var homeDirectory = Path.Combine(Path.GetTempPath(), "deck-sync-setup-it", Guid.NewGuid().ToString("N"));
        var runtimeDirectory = Path.Combine(homeDirectory, ".deck-sync");
        var backupDirectory = Path.Combine(homeDirectory, ".deck-sync-backup");
        var fixtureFile = Path.Combine(homeDirectory, "fixture-save.dat");
        _output.WriteLine($"Home: {homeDirectory}");
        _output.WriteLine($"Runtime: {runtimeDirectory}");
        _output.WriteLine($"Backup: {backupDirectory}");
        _output.WriteLine($"Fixture: {fixtureFile}");

        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(fixtureFile, "fixture-save");
        await File.WriteAllTextAsync(Path.Combine(runtimeDirectory, "sentinel.txt"), "remove-me");
        Directory.CreateDirectory(backupDirectory);
        await File.WriteAllTextAsync(Path.Combine(backupDirectory, "keep-me.txt"), "keep-me");

        try
        {
            _output.WriteLine("Running cleanup...");
            await DeckSyncSetupRuntime.CleanupAsync(runtimeDirectory, _output.WriteLine);
            Assert.False(Directory.Exists(runtimeDirectory));
            Assert.True(Directory.Exists(backupDirectory));
            Assert.True(File.Exists(Path.Combine(backupDirectory, "keep-me.txt")));

            var backupEntriesBeforeInstall = Directory
                .EnumerateFileSystemEntries(backupDirectory, "*", SearchOption.AllDirectories)
                .ToArray();

            _output.WriteLine("Running install...");
            await DeckSyncSetupRuntime.InstallAsync(runtimeDirectory, backupDirectory, [fixtureFile], _output.WriteLine);
            Assert.True(Directory.Exists(runtimeDirectory));

            var configPath = Path.Combine(runtimeDirectory, "config", "config.yaml");
            Assert.True(File.Exists(configPath));

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

            Assert.NotEmpty(resolvedBackupEntries);
        }
        finally
        {
            _output.WriteLine("Cleaning up temp test directory.");
            if (Directory.Exists(homeDirectory))
                Directory.Delete(homeDirectory, recursive: true);
        }
    }
}
