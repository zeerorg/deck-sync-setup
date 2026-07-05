/// <summary>Typed runtime location for the Deck sync runtime.</summary>
/// <param name="Path">Absolute path to the runtime directory.</param>
public readonly record struct DeckSyncRuntimeLocation(string Path);

/// <summary>Typed backup location for the Deck sync backup directory.</summary>
/// <param name="Path">Absolute path to the backup directory.</param>
public readonly record struct DeckSyncBackupLocation(string Path);

/// <summary>Resolves the Deck sync runtime and backup locations for the current host.</summary>
public interface IDeckSyncLocationsModule
{
    /// <summary>Resolves the Deck sync runtime location.</summary>
    DeckSyncRuntimeLocation ResolveRuntimeLocation();

    /// <summary>Resolves the Deck sync backup location.</summary>
    DeckSyncBackupLocation ResolveBackupLocation();
}

/// <summary>Resolves the Deck sync runtime and backup locations from the current user profile.</summary>
public sealed class DeckSyncLocationsModule : IDeckSyncLocationsModule
{
    /// <inheritdoc/>
    public DeckSyncRuntimeLocation ResolveRuntimeLocation() =>
        new(Path.Combine(ResolveHomeDirectory(), ".deck-sync"));

    /// <inheritdoc/>
    public DeckSyncBackupLocation ResolveBackupLocation() =>
        new(Path.Combine(ResolveHomeDirectory(), ".deck-sync-backup"));

    private static string ResolveHomeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("Could not determine the current user's home directory.");

        return home;
    }
}
