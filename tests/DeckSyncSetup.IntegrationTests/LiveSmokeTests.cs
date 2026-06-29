using Xunit;
using Xunit.Abstractions;

public sealed class LiveSmokeTests
{
    private readonly ITestOutputHelper _output;

    public LiveSmokeTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task Cleanup_then_install_can_use_live_release_downloads_and_ludusavi_processes()
    {
        using var context = new DeckSyncTestContext(_output);
        Directory.CreateDirectory(context.RuntimeDirectory);
        await File.WriteAllTextAsync(Path.Combine(context.RuntimeDirectory, "sentinel.txt"), "remove me");
        Directory.CreateDirectory(context.BackupDirectory);
        await File.WriteAllTextAsync(Path.Combine(context.BackupDirectory, "keep-me.txt"), "keep me");
        var cleanupModule = new SetupCleanupModule(context.LocationsModule, context.ProgressReporter);
        var installModule = new SetupInstallModule(context.LocationsModule, context.ProgressReporter);

        var cleanupResult = await cleanupModule.CleanupAsync();
        var installResult = await installModule.InstallAsync();

        Assert.True(cleanupResult.RemovedRuntime);
        Assert.Equal(["rclone", "syncthing", "ludusavi"], installResult.InstalledTools.Select(tool => tool.ToolName));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("rclone"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("syncthing"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, TestPaths.ToolExecutableName("ludusavi"))));
        Assert.True(File.Exists(Path.Combine(context.RuntimeDirectory, "config", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(context.BackupDirectory, "keep-me.txt")));
    }
}
