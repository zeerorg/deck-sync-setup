/// <summary>
/// Resolves the path to the Deck sync runtime directory on the current host.
/// Centralises the directory name (<c>.deck-sync</c>) and the home-directory
/// resolution strategy so both the Setup Install module and the Setup Cleanup module
/// refer to the same location.
/// </summary>
public static class DeckSyncRuntimeDirectory
{
    /// <summary>
    /// Returns <paramref name="directoryOverride"/> when provided; otherwise resolves
    /// the default Deck sync runtime directory (<c>~/.deck-sync</c>) by reading the
    /// <c>HOME</c> environment variable and falling back to
    /// <see cref="Environment.SpecialFolder.UserProfile"/>.
    /// </summary>
    /// <param name="directoryOverride">
    /// An explicit directory path to use instead of the default.
    /// When non-<see langword="null"/>, returned as-is without further resolution.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="directoryOverride"/> is <see langword="null"/> and
    /// the home directory cannot be determined.
    /// </exception>
    public static string Resolve(string? directoryOverride = null)
    {
        if (directoryOverride is not null)
            return directoryOverride;

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("Could not determine the current user's home directory.");

        return Path.Combine(home, ".deck-sync");
    }
}
