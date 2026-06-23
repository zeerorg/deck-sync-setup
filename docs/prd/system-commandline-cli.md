# Implement CLI with System.CommandLine Library

## Problem Statement

The CLI currently parses commands by manually inspecting `args[0]`, which provides no auto-generated help text, no consistent error formatting for invalid inputs, and no extensible contract for adding new subcommands. Developers maintaining or extending the tool must also maintain the bespoke parsing logic.

## Solution

Replace the manual `args` parsing with `System.CommandLine` 2.0.9, wiring `install` and `cleanup` as first-class subcommands. This gives the CLI auto-generated `--help` output, consistent invalid-input error messages, and a standard extensible command structure — without changing the user-facing command syntax.

## User Stories

1. As a developer running `deck-sync-setup --help`, I want to see a generated list of available subcommands and a description of the tool, so that I can discover what the CLI can do without reading the source code.
2. As a developer running `deck-sync-setup install --help`, I want to see a description of the install subcommand, so that I understand what it does.
3. As a developer running `deck-sync-setup cleanup --help`, I want to see a description of the cleanup subcommand, so that I understand what it does.
4. As a developer running `deck-sync-setup install`, I want the Setup Install module to download the correct Deck sync runtime for my platform, so that my machine is ready for game save syncing.
5. As a developer running `deck-sync-setup cleanup`, I want the Setup Cleanup module to remove all Deck sync runtime files, so that my machine is returned to a clean state.
6. As a developer running `deck-sync-setup` with no arguments, I want to see the help text automatically, so that I know what commands are available.
7. As a developer running an unknown subcommand (e.g. `deck-sync-setup foo`), I want to see a clear error message and exit code 1, so that I know I made a mistake.
8. As a developer experiencing a runtime failure (e.g. no GitHub releases found), I want to see a clean error message and exit code 1, so that I can diagnose the problem without reading a stack trace.
9. As a contributor, I want the CLI entry point to use a standard library for command parsing, so that adding new subcommands in future is straightforward.

## Implementation Decisions

- The `System.CommandLine` 2.0.9 NuGet package will be added to the project.
- The root command will have two subcommands: `install` and `cleanup`, matching the current user-facing syntax exactly.
- Neither subcommand will have options or arguments — they are invoked by name only.
- All code will remain in `Program.cs`. No new files or classes will be introduced as part of this change.
- Each subcommand handler will be `async Task`. Exit codes will be set via `InvocationContext.ExitCode` inside the `SetHandler` lambda.
- Each handler will wrap its logic in a `try/catch(Exception)` block. On failure, the error message will be printed to stderr and exit code set to 1.
- The existing domain logic (HTTP client construction, asset selection, file download, directory resolution) will remain as static helper methods, unchanged in behaviour.
- `System.CommandLine` will handle the "no args" and "unknown command" cases natively via its built-in error and help behaviour.
- The `install` subcommand must preserve its current success output: the downloaded asset name and destination path printed to stdout.
- The `cleanup` subcommand must preserve its "Nothing to delete" message when the Deck sync runtime directory does not exist.

## Testing Decisions

- A good test targets external behaviour only: the exit code and console output produced by a given `args` array. It should not assert on internal method calls or routing details.
- The natural seam is `rootCommand.InvokeAsync(args)` — the single highest entry point. All routing and error behaviour is observable through the return value and captured console output.
- There is currently no test project in the repository. One would need to be created to exercise this seam.
- Tests exercising the `install` subcommand would require mocking the HTTP layer to avoid live GitHub API calls — this is out of scope for this PRD and should be addressed in a follow-up.
- In-scope tests (for a future test-addition effort): argument routing, help output (exits 0), and unknown-command rejection (exits non-zero).

## Out of Scope

- Adding new options or arguments to either subcommand.
- Refactoring `Program.cs` into multiple files or classes.
- Adding a test project or writing tests (noted as a future follow-up).
- Upgrading to a prerelease version of `System.CommandLine` (e.g. the 3.0.0-preview series).
- Changes to the Deck sync runtime download logic or asset selection logic.
- Adding a `--version` flag.

## Further Notes

- The current behaviour of returning exit code 1 and printing a usage line for unknown commands will be replaced by `System.CommandLine`'s native error output, which is richer and more consistent.
- `System.CommandLine` 2.0.9 targets .NET Standard 2.0 and is compatible with net10.0.
