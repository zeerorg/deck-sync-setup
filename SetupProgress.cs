/// <summary>Structured progress information emitted by setup modules.</summary>
public enum SetupProgressKind
{
    /// <summary>Informational progress.</summary>
    Info,

    /// <summary>Progress that should be treated as a warning.</summary>
    Warning,
}

/// <summary>Structured progress emitted by setup modules.</summary>
/// <param name="Kind">The category of progress.</param>
/// <param name="Message">The human-readable progress message.</param>
public sealed record SetupProgress(
    SetupProgressKind Kind,
    string Message);

/// <summary>Consumes structured progress emitted by setup modules.</summary>
public interface ISetupProgressReporter
{
    /// <summary>Reports a progress value.</summary>
    void Report(SetupProgress progress);
}

/// <summary>Writes progress messages to a text sink such as the console.</summary>
public sealed class ConsoleSetupProgressReporter : ISetupProgressReporter
{
    private readonly Action<string> _writeLine;

    /// <param name="writeLine">The text sink that receives each progress message.</param>
    public ConsoleSetupProgressReporter(Action<string> writeLine) => _writeLine = writeLine;

    /// <inheritdoc/>
    public void Report(SetupProgress progress) => _writeLine(progress.Message);
}

/// <summary>A progress reporter that intentionally ignores all progress.</summary>
public sealed class NoOpSetupProgressReporter : ISetupProgressReporter
{
    /// <summary>A shared instance for callers that do not care about progress.</summary>
    public static readonly NoOpSetupProgressReporter Instance = new();

    private NoOpSetupProgressReporter()
    {
    }

    /// <inheritdoc/>
    public void Report(SetupProgress progress)
    {
    }
}
