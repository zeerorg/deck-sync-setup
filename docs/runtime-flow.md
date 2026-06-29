# Runtime Flow

The runtime flow is split across three public modules:

- **Setup Install module** — installs the Deck sync runtime, seeds Ludusavi, and runs the initial backup.
- **Setup Cleanup module** — removes the Deck sync runtime and leaves backup data alone.
- **Deck sync locations module** — resolves the runtime and backup locations as typed values.

## Progress

Public modules report structured progress through `ISetupProgressReporter`. Use the console reporter when you want the current command-line output, or the no-op reporter when progress is not needed.

## Install flow

The install module resolves locations, rejects an existing runtime, installs rclone, syncthing, and Ludusavi, patches Ludusavi config, and then runs the initial backup. Partial failures roll back the runtime directory best-effort.

## Cleanup flow

Cleanup resolves the same locations and deletes the runtime directory if it exists. Missing runtime is a successful no-op.

## Testing

Default integration tests cross the public Setup Install module, Setup Cleanup module, Deck sync locations module, and CLI command seams while replacing live GitHub downloads and Ludusavi process execution with internal fakes. This keeps normal test runs hermetic while still using real isolated filesystem locations for the Deck sync runtime and backup data.

The live cleanup-then-install smoke test is skipped by default. Set `DECK_SYNC_SETUP_LIVE_TESTS=1` when you intentionally want to verify real release downloads and real Ludusavi execution.
