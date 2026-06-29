using Xunit;
using Xunit.Abstractions;

public sealed class CleanupFlowTests
{
    private readonly ITestOutputHelper _output;

    public CleanupFlowTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Cleanup_removes_deck_sync_runtime_and_preserves_backup_data()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.RuntimeDirectory);
        await File.WriteAllTextAsync(Path.Combine(context.RuntimeDirectory, "remove-me.txt"), "remove me");
        Directory.CreateDirectory(context.BackupDirectory);
        var backupSentinelPath = Path.Combine(context.BackupDirectory, "keep-me.txt");
        await File.WriteAllTextAsync(backupSentinelPath, "keep me");
        var cleanupModule = new SetupCleanupModule(context.LocationsModule, context.ProgressReporter);

        var result = await cleanupModule.CleanupAsync();

        Assert.True(result.RemovedRuntime);
        Assert.Equal(context.RuntimeDirectory, result.DeckSyncRuntimeLocation.Path);
        Assert.Equal(context.BackupDirectory, result.DeckSyncBackupLocation.Path);
        Assert.False(Directory.Exists(context.RuntimeDirectory));
        Assert.True(Directory.Exists(context.BackupDirectory));
        Assert.True(File.Exists(backupSentinelPath));
        Assert.Contains(context.LogMessages, message => message.Contains(context.RuntimeDirectory));
    }

    [Fact]
    public async Task Cleanup_when_deck_sync_runtime_is_missing_succeeds_without_creating_runtime()
    {
        using var context = new DeckSyncTestContext(_output);
        var cleanupModule = new SetupCleanupModule(context.LocationsModule, context.ProgressReporter);

        var result = await cleanupModule.CleanupAsync();

        Assert.False(result.RemovedRuntime);
        Assert.Equal(context.RuntimeDirectory, result.DeckSyncRuntimeLocation.Path);
        Assert.Equal(context.BackupDirectory, result.DeckSyncBackupLocation.Path);
        Assert.False(Directory.Exists(context.RuntimeDirectory));
        Assert.Contains(context.LogMessages, message => message.Contains("Nothing to delete"));
    }
}
