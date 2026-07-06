using System.Diagnostics;

/// <summary>Port for running Ludusavi config-show and backup commands.</summary>
internal interface ILudusaviProcessPort
{
    /// <summary>Runs <c>ludusavi config show</c> and returns the captured process output.</summary>
    Task<LudusaviProcessResult> ShowConfigAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Runs <c>ludusavi backup --force</c> and returns the captured process output.</summary>
    Task<LudusaviProcessResult> BackupAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>Captured output from a Ludusavi process invocation.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
internal sealed record LudusaviProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>Production adapter for <see cref="ILudusaviProcessPort"/>.</summary>
internal sealed class LudusaviProcessAdapter : ILudusaviProcessPort
{
    /// <inheritdoc/>
    public Task<LudusaviProcessResult> ShowConfigAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default) =>
        RunAsync(executablePath, workingDirectory, ["--config", configDirectory, "config", "show"], cancellationToken);

    /// <inheritdoc/>
    public Task<LudusaviProcessResult> BackupAsync(
        string executablePath,
        string workingDirectory,
        string configDirectory,
        CancellationToken cancellationToken = default) =>
        RunAsync(executablePath, workingDirectory, ["--config", configDirectory, "backup", "--force"], cancellationToken);

    private static async Task<LudusaviProcessResult> RunAsync(
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
            throw new InvalidOperationException($"Failed to start the Ludusavi process at '{executablePath}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new LudusaviProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
