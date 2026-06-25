/// <summary>
/// Prepares the Deck sync runtime on the current host by selecting and placing
/// the correct package for this platform.
/// </summary>
public interface ISetupInstallModule
{
    /// <summary>
    /// Downloads and places the Deck sync runtime for the current platform.
    /// </summary>
    /// <returns>Details of what was installed and where.</returns>
    /// <exception cref="SetupInstallException">
    /// Thrown when installation fails for a known reason.
    /// Inspect <see cref="SetupInstallException.Code"/> for the failure category.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task<SetupInstallResult> InstallAsync(CancellationToken cancellationToken = default);
}

/// <summary>Details of a completed Deck sync runtime installation.</summary>
/// <param name="ReleaseTag">The release tag from which the runtime was downloaded (e.g. <c>v1.67.0</c>).</param>
/// <param name="AssetName">The file name of the downloaded archive (e.g. <c>rclone-v1.67.0-windows-amd64.zip</c>).</param>
/// <param name="DeckSyncRuntimeDirectory">Absolute path to the Deck sync runtime directory (e.g. <c>~/.deck-sync</c>).</param>
/// <param name="DestinationPath">The Deck sync runtime directory into which the executables were extracted; same value as <paramref name="DeckSyncRuntimeDirectory"/>.</param>
public sealed record SetupInstallResult(
    string ReleaseTag,
    string AssetName,
    string DeckSyncRuntimeDirectory,
    string DestinationPath);

/// <summary>
/// Thrown by <see cref="ISetupInstallModule.InstallAsync"/> when installation fails
/// for a known, categorised reason.
/// </summary>
public sealed class SetupInstallException : Exception
{
    /// <summary>The category of failure that caused this exception.</summary>
    public SetupInstallError Code { get; }

    public SetupInstallException(SetupInstallError code, string message)
        : base(message) => Code = code;

    public SetupInstallException(SetupInstallError code, string message, Exception innerException)
        : base(message, innerException) => Code = code;
}

/// <summary>Categories of failure that can occur during <see cref="ISetupInstallModule.InstallAsync"/>.</summary>
public enum SetupInstallError
{
    /// <summary>The GitHub API returned an empty release list.</summary>
    NoReleasesReturned,
    /// <summary>No archive asset compatible with the current OS and architecture was found in the selected release.</summary>
    NoCompatibleAsset,
    /// <summary>The Deck sync runtime directory path could not be resolved (e.g. home directory unavailable).</summary>
    DeckSyncRuntimeDirectoryUnavailable,
    /// <summary>The Deck sync runtime directory already exists and must be removed before installing again.</summary>
    DeckSyncRuntimeDirectoryAlreadyExists,
    /// <summary>A network error prevented the release list or asset from being fetched.</summary>
    DownloadFailed,
    /// <summary>The downloaded archive could not be written to disk.</summary>
    WriteFailed,
}
