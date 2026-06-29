using System.CommandLine;
using Xunit;

public sealed class CliCommandTests
{
    [Fact]
    public async Task Install_success_returns_zero_and_invokes_setup_install_module()
    {
        var installModule = new FakeSetupInstallModule();
        var cleanupModule = new FakeSetupCleanupModule();

        var result = await InvokeAsync(
            DeckSyncSetupCommandFactory.Create(installModule, cleanupModule),
            ["install"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, installModule.CallCount);
        Assert.Equal(0, cleanupModule.CallCount);
        Assert.Equal("", result.StandardError);
    }

    [Fact]
    public async Task Cleanup_success_returns_zero_and_invokes_setup_cleanup_module()
    {
        var installModule = new FakeSetupInstallModule();
        var cleanupModule = new FakeSetupCleanupModule();

        var result = await InvokeAsync(
            DeckSyncSetupCommandFactory.Create(installModule, cleanupModule),
            ["cleanup"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, installModule.CallCount);
        Assert.Equal(1, cleanupModule.CallCount);
        Assert.Equal("", result.StandardError);
    }

    [Fact]
    public async Task Install_failure_returns_one_and_prints_error()
    {
        var installModule = new FakeSetupInstallModule(new SetupInstallException(
            SetupInstallError.LudusaviBackupFailed,
            "install failed"));
        var cleanupModule = new FakeSetupCleanupModule();

        var result = await InvokeAsync(
            DeckSyncSetupCommandFactory.Create(installModule, cleanupModule),
            ["install"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(1, installModule.CallCount);
        Assert.Equal("Error: install failed", result.StandardError.Trim());
    }

    [Fact]
    public async Task Cleanup_failure_returns_one_and_prints_error()
    {
        var installModule = new FakeSetupInstallModule();
        var cleanupModule = new FakeSetupCleanupModule(new SetupCleanupException(
            SetupCleanupError.DeleteFailed,
            "cleanup failed"));

        var result = await InvokeAsync(
            DeckSyncSetupCommandFactory.Create(installModule, cleanupModule),
            ["cleanup"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(1, cleanupModule.CallCount);
        Assert.Equal("Error: cleanup failed", result.StandardError.Trim());
    }

    private static async Task<CommandResult> InvokeAsync(RootCommand rootCommand, string[] args)
    {
        var originalError = Console.Error;
        using var errorWriter = new StringWriter();

        try
        {
            Console.SetError(errorWriter);
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            return new CommandResult(exitCode, errorWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed record CommandResult(int ExitCode, string StandardError);
}
