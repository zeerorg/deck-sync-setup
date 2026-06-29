var locationsModule = new DeckSyncLocationsModule();
var progressReporter = new ConsoleSetupProgressReporter(Console.WriteLine);
var installModule = new SetupInstallModule(locationsModule, progressReporter);
var cleanupModule = new SetupCleanupModule(locationsModule, progressReporter);
var rootCommand = DeckSyncSetupCommandFactory.Create(installModule, cleanupModule);

return await rootCommand.Parse(args).InvokeAsync();
