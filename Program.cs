using System.CommandLine;

var rootCommand = new RootCommand("Deck sync setup tool — installs or removes the Deck sync runtime on this machine.");

var installCommand = new Command("install", "Download and install the Deck sync runtime for this platform.");
installCommand.SetAction(async (ParseResult _, CancellationToken cancellationToken) =>
{
    try
    {
        await DeckSyncSetupRuntime.InstallAsync(
            DeckSyncRuntimeDirectory.Resolve(),
            DeckSyncSetupRuntime.ResolveDeckSyncBackupDirectory(),
            Console.WriteLine,
            cancellationToken);

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
});

var cleanupCommand = new Command("cleanup", "Remove all Deck sync runtime files from this machine.");
cleanupCommand.SetAction(async (ParseResult _, CancellationToken cancellationToken) =>
{
    try
    {
        await DeckSyncSetupRuntime.CleanupAsync(DeckSyncRuntimeDirectory.Resolve(), Console.WriteLine, cancellationToken);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
});

rootCommand.Subcommands.Add(installCommand);
rootCommand.Subcommands.Add(cleanupCommand);

return await rootCommand.Parse(args).InvokeAsync();
