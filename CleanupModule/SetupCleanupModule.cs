/// <summary>Removes the Deck sync runtime while preserving backup data.</summary>
public interface ISetupCleanupModule
{
    /// <summary>Deletes the Deck sync runtime if it exists.</summary>
    Task<SetupCleanupResult> CleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>Details of a cleanup operation.</summary>
/// <param name="DeckSyncRuntimeLocation">The resolved Deck sync runtime location.</param>
/// <param name="DeckSyncBackupLocation">The resolved Deck sync backup location.</param>
/// <param name="RemovedRuntime">Whether the runtime directory was deleted.</param>
public sealed record SetupCleanupResult(
    DeckSyncRuntimeLocation DeckSyncRuntimeLocation,
    DeckSyncBackupLocation DeckSyncBackupLocation,
    bool RemovedRuntime);

/// <summary>Thrown when cleanup fails for a known reason.</summary>
public sealed class SetupCleanupException : Exception
{
    /// <summary>The category of cleanup failure.</summary>
    public SetupCleanupError Code { get; }

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    public SetupCleanupException(SetupCleanupError code, string message)
        : base(message) => Code = code;

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SetupCleanupException(SetupCleanupError code, string message, Exception innerException)
        : base(message, innerException) => Code = code;
}

/// <summary>Categories of cleanup failure.</summary>
public enum SetupCleanupError
{
    /// <summary>The Deck sync locations could not be resolved.</summary>
    DeckSyncLocationsUnavailable,

    /// <summary>The runtime directory could not be deleted.</summary>
    DeleteFailed,
}

/// <summary>Implementation of the Setup Cleanup module.</summary>
public sealed class SetupCleanupModule : ISetupCleanupModule
{
    private readonly IDeckSyncLocationsModule _locationsModule;
    private readonly ISetupProgressReporter _progressReporter;

    /// <param name="locationsModule">Resolves the runtime and backup locations.</param>
    /// <param name="progressReporter">Receives structured progress messages.</param>
    public SetupCleanupModule(
        IDeckSyncLocationsModule locationsModule,
        ISetupProgressReporter progressReporter)
    {
        _locationsModule = locationsModule;
        _progressReporter = progressReporter;
    }

    /// <inheritdoc/>
    public Task<SetupCleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DeckSyncRuntimeLocation runtimeLocation;
        DeckSyncBackupLocation backupLocation;
        try
        {
            runtimeLocation = _locationsModule.ResolveRuntimeLocation();
            backupLocation = _locationsModule.ResolveBackupLocation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupCleanupException(
                SetupCleanupError.DeckSyncLocationsUnavailable,
                "Could not resolve the Deck sync runtime or backup location.",
                ex);
        }

        if (!Directory.Exists(runtimeLocation.Path))
        {
            _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"Nothing to delete: {runtimeLocation.Path}"));
            return Task.FromResult(new SetupCleanupResult(runtimeLocation, backupLocation, false));
        }

        try
        {
            Directory.Delete(runtimeLocation.Path, recursive: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SetupCleanupException(
                SetupCleanupError.DeleteFailed,
                $"Failed to delete the Deck sync runtime directory at '{runtimeLocation.Path}'.",
                ex);
        }

        _progressReporter.Report(new SetupProgress(SetupProgressKind.Info, $"Deleted {runtimeLocation.Path}"));
        return Task.FromResult(new SetupCleanupResult(runtimeLocation, backupLocation, true));
    }
}
