# Handoff: Fetching original backup folder lists from Ludusavi

## Focus
Build a reliable way to list the original source folders for backed-up games using Ludusavi data.

## What we learned
- `ludusavi find` only resolves title matches; it does not expose save folder paths.
- `ludusavi manifest show --api` exposes manifest entries and path templates such as `<winLocalAppData>` and `<winDocuments>`.
- `ludusavi backup --preview --api` exposes actual backed-up file paths, but walking every file was rejected as too indirect.
- Each game's backup folder contains `mapping.yaml`, which Ludusavi documents as the backup metadata file in `docs/help/backup-structure.md`.

## Relevant references
- `docs/help/backup-structure.md` in the Ludusavi repo: explains `mapping.yaml` and backup layout.
- `docs/help/configuration-file.md` and `docs/schema/config.yaml`: Ludusavi config and schema references.
- `C:\Users\<USER>\Documents\dev\deck-sync-setup\docs\ludusavi-handoff.md`: earlier handoff with Ludusavi/Rclone setup context.
- `C:\Users\<USER>\Documents\dev\deck-sync-setup\.copilot\session-state\ba28734b-0aa4-4ef0-9d03-85754597ab1e\plan.md`: session plan.

## Current repo state
- A custom Ludusavi config exists under `.deck-sync\config\config.yaml`.
- `.deck-sync\rclone.exe` and `.deck-sync\rclone.conf` exist for a local Rclone setup.
- Helper scripts exist under `.deck-sync\scripts\`.

## Open problem
Find the cleanest Ludusavi-native way to turn manifest or backup data into a game -> original folder list report.

## Likely direction
Use the manifest output as the source of truth for path templates, then resolve placeholders to real Windows locations for each game. If exact lists are needed from existing backups, `mapping.yaml` is the authoritative per-backup metadata file and can be referenced directly.

## Suggested next steps
1. Decide whether the output should come from manifest templates or from per-backup `mapping.yaml` files.
2. If manifest-based, build a script that parses `manifest show --api` and resolves placeholders for Windows paths.
3. If backup-based, build a script that reads each game's `mapping.yaml` and emits the unique root folders.
4. Verify the output on a few representative games such as Alan Wake II, Brawlhalla, and Valorant.

## Suggested skills
- `diagnosing-bugs` — if the manifest-based extraction still produces the wrong folder list.
- `codebase-design` — if the folder-list helper should become a reusable command or script.
- `request-refactor-plan` — if the extraction logic needs to be broken into safer steps.
