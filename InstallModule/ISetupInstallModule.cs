/// <summary>Details of an installed release asset.</summary>
/// <param name="ToolName">The tool that was installed (for example, <c>rclone</c>).</param>
/// <param name="ReleaseTag">The release tag from which the asset was downloaded.</param>
/// <param name="AssetName">The file name of the downloaded archive.</param>
/// <param name="DestinationPath">The runtime directory into which the asset was extracted.</param>
public sealed record SetupInstallToolResult(
    string ToolName,
    string ReleaseTag,
    string AssetName,
    string DestinationPath);

/// <summary>Details of a completed Deck sync runtime installation.</summary>
/// <param name="DeckSyncRuntimeLocation">The resolved Deck sync runtime location.</param>
/// <param name="DeckSyncBackupLocation">The resolved Deck sync backup location.</param>
/// <param name="InstalledTools">The release assets installed during the run.</param>
public sealed record SetupInstallResult(
    DeckSyncRuntimeLocation DeckSyncRuntimeLocation,
    DeckSyncBackupLocation DeckSyncBackupLocation,
    IReadOnlyList<SetupInstallToolResult> InstalledTools);

/// <summary>
/// Thrown by <see cref="ISetupInstallModule.InstallAsync"/> when installation fails
/// for a known, categorised reason.
/// </summary>
public sealed class SetupInstallException : Exception
{
    /// <summary>The category of failure that caused this exception.</summary>
    public SetupInstallError Code { get; }

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    public SetupInstallException(SetupInstallError code, string message)
        : base(message) => Code = code;

    /// <param name="code">The failure category.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SetupInstallException(SetupInstallError code, string message, Exception innerException)
        : base(message, innerException) => Code = code;
}

/// <summary>Categories of failure that can occur during <see cref="ISetupInstallModule.InstallAsync"/>.</summary>
public enum SetupInstallError
{
    /// <summary>The Deck sync locations could not be resolved.</summary>
    DeckSyncLocationsUnavailable,

    /// <summary>The Deck sync runtime directory already exists and must be removed before installing again.</summary>
    DeckSyncRuntimeAlreadyExists,

    /// <summary>Installing one of the release assets failed.</summary>
    ReleaseAssetInstallFailed,

    /// <summary>Seeding the Ludusavi config failed.</summary>
    LudusaviConfigSeedingFailed,

    /// <summary>Running the initial Ludusavi backup failed.</summary>
    LudusaviBackupFailed,

    /// <summary>Creating the Rclone Google Drive remote failed.</summary>
    RcloneGoogleDriveSetupFailed,

    /// <summary>Rollback of a partial install failed.</summary>
    RollbackFailed,
}
