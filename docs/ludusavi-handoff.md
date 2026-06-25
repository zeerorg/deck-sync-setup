# Handoff: Ludusavi custom config and Rclone paths

## Goal
Continue work on Ludusavi configuration so it uses a custom, local Rclone executable and Rclone config file.

## Current state
- The roaming Ludusavi config was reverted to the WinGet-installed Rclone executable and default arguments.
- A separate custom Ludusavi config exists under `.deck-sync\config\config.yaml`.
- A local Rclone config exists at `.deck-sync\rclone.conf`.
- The local Rclone executable exists at `.deck-sync\rclone.exe`.

## Relevant files
- `C:\Users\<USER>\AppData\Roaming\ludusavi\config.yaml`
- `C:\Users\<USER>\.deck-sync\config\config.yaml`
- `C:\Users\<USER>\.deck-sync\rclone.exe`
- `C:\Users\<USER>\.deck-sync\rclone.conf`
- `C:\Users\<USER>\.deck-sync\scripts\backup-with-steam-disabled.ps1`
- `C:\Users\<USER>\.deck-sync\scripts\get-ludusavi-source-roots.ps1`
- `C:\Users\<USER>\Documents\dev\deck-sync-setup\.copilot\session-state\ba28734b-0aa4-4ef0-9d03-85754597ab1e\plan.md`

## Notes
- Ludusavi’s config schema exposes the Rclone executable and arguments under `apps.rclone.path` and `apps.rclone.arguments`.
- `cloud set custom --id <REMOTE>` selects the Rclone remote; executable/config paths are handled separately in `config.yaml`.
- Keep the custom config isolated under `.deck-sync` unless the roaming config must be changed again.

## Suggested next steps
1. Verify `.deck-sync\config\config.yaml` still points to `.deck-sync\rclone.exe` and includes `--config .deck-sync\rclone.conf`.
2. Set or verify the cloud remote with Ludusavi’s `cloud set custom --id ...` command if cloud sync is needed.
3. Run a small backup/cloud-sync test to confirm Ludusavi is using the local Rclone config.

## Suggested skills
- `diagnosing-bugs` — if Ludusavi does not pick up the local Rclone executable/config or cloud sync fails.
- `codebase-design` — if the config/script layout needs cleanup or consolidation.
- `request-refactor-plan` — if the helper scripts should be turned into a tighter, safer workflow.
