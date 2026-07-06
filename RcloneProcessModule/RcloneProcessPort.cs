using System.Diagnostics;

/// <summary>Port for running Rclone setup commands.</summary>
internal interface IRcloneProcessPort
{
    /// <summary>Runs <c>rclone config create gdrive drive</c> for the Deck sync Rclone config.</summary>
    Task<RcloneProcessResult> CreateGoogleDriveRemoteAsync(
        string executablePath,
        string workingDirectory,
        string configPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Captured output from an Rclone process invocation.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
internal sealed record RcloneProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>Production adapter for <see cref="IRcloneProcessPort"/>.</summary>
internal sealed class RcloneProcessAdapter : IRcloneProcessPort
{
    /// <inheritdoc/>
    public Task<RcloneProcessResult> CreateGoogleDriveRemoteAsync(
        string executablePath,
        string workingDirectory,
        string configPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            executablePath,
            workingDirectory,
            ["--config", configPath, "config", "create", "ludusavi", "drive", "scope=drive", "config_is_local=true"],
            cancellationToken);

    private static async Task<RcloneProcessResult> RunAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start the Rclone process at '{executablePath}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new RcloneProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
