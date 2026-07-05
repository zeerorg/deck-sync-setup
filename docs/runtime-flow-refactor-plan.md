# Runtime Flow Refactor Plan

## Problem Statement

The current runtime flow is hard to change safely because one public static module owns too many responsibilities at once. Install, cleanup, host-location policy, GitHub release acquisition, archive placement, Ludusavi config shaping, and Ludusavi backup execution are tangled together, while the narrower installer logic already exists separately under a different meaning of "Setup Install module". The result is mixed coding styles, uneven documentation, weak locality, and a test surface that sits too high in the stack.

The codebase needs a refactor that makes the public seams match the domain language, shrinks the public interface, and pushes the orchestration depth into the right modules without changing user-visible install and cleanup behavior.

## Solution

Refactor the runtime flow around three public modules: Setup Install module, Setup Cleanup module, and Deck sync locations module. Remove the current public static runtime orchestrator from the runtime flow. Keep the public interface small and explicit, and move the current per-tool installer behind the Setup Install module as a narrower internal module with a narrower name.

Use strong ports-and-adapters discipline inside the implementation for the external dependencies that genuinely vary: GitHub release access and Ludusavi process execution. Keep location lookup pure and synchronous. Use structured progress reporting, typed location values, explicit result values, and separate failure types for install and cleanup. Preserve current behavior while broadening repository documentation around the runtime flow, public modules, testing expectations, and glossary terms.

## Commits

1. Add or refine the glossary and public terminology so the refactor has one stable language. Record the Deck sync locations module alongside the existing Setup Install module and Setup Cleanup module terms, and align public documentation with those names.
2. Introduce the new public interfaces and result types without moving existing logic yet. Add the Setup Install module interface, Setup Cleanup module interface, Deck sync locations module interface, typed location values, structured progress values, and separate failure/result shapes for install and cleanup.
3. Introduce no-op and console progress adapters so callers can adopt the new reporter seam without changing behavior.
4. Introduce a concrete Deck sync locations module implementation that resolves the Deck sync runtime location and backup location as pure synchronous lookups. Keep it side-effect free and make it the single owner of location policy.
5. Move the current runtime-directory and backup-directory resolution logic behind the Deck sync locations module and delete duplicate path-policy logic once the new module owns it.
6. Rename the current narrow per-tool installer to Release asset install module so the top-level Setup Install module can own the domain term. Keep the narrow module internal.
7. Adapt the renamed Release asset install module to work with typed location values and structured progress without changing its behavior.
8. Introduce an internal generic GitHub release port that takes repository identity as data, plus a production HTTP adapter and a test adapter shape.
9. Move the existing GitHub release listing and asset download logic behind that internal GitHub release port.
10. Introduce an internal Ludusavi process port that owns config-show and backup execution, plus a production process adapter and a test adapter shape.
11. Move the existing Ludusavi process-launch logic behind that internal Ludusavi process port without changing the command behavior.
12. Introduce the new Setup Cleanup module implementation, backed by the Deck sync locations module. Make missing runtime a successful no-op and preserve backup data.
13. Introduce the new Setup Install module implementation as the deep orchestration module. Make it own the full fixed ordering: resolve locations, guard against existing runtime, install rclone, install syncthing, install ludusavi, seed config, then run the initial backup.
14. Move the current config-shaping logic under the Setup Install module implementation, keeping the same output and same behavior while switching the caller-facing seam to the new module.
15. Add best-effort rollback of the Deck sync runtime for partial install failure, and surface rollback failure explicitly as its own install failure mode.
16. Update the CLI entrypoint into a thin composition module that wires adapters, invokes the Setup Install module or Setup Cleanup module, maps failures to exit codes, and preserves the current command behavior.
17. Remove the old public static runtime orchestrator from the runtime flow once all callers have moved to the new modules.
18. Remove the old public static runtime-directory utility from the runtime flow once the Deck sync locations module fully owns location policy.
19. Update the existing integration test to cross the new public seams instead of the removed static runtime flow, while preserving the same end-to-end behavior check.
20. Update repository documentation around the runtime flow, public modules, glossary terms, and testing expectations so the new seam layout is discoverable and the public interface is documented consistently.
21. Do a cleanup pass for naming and XML documentation on the public seams, result types, and failure types so the new shape is coherent and the mixed-style hotspots are reduced rather than moved.

## Decision Document

- Build or modify three public modules: Setup Install module, Setup Cleanup module, and Deck sync locations module.
- Remove public static modules from the runtime flow.
- The Setup Install module owns the full three-tool install flow internally and remains the only public install seam.
- The Setup Cleanup module removes only the Deck sync runtime and preserves backup data.
- The Deck sync locations module is a public seam because future callers may need runtime-only or backup-only lookups.
- The Deck sync locations module returns typed location values rather than raw strings.
- The Deck sync locations module remains pure and synchronous; it resolves locations but does not create directories.
- Progress crosses the public seam as structured progress values rather than plain strings.
- Reporter dependencies are required at the public seam; callers that do not care about progress use a no-op adapter.
- Install and cleanup keep separate failure types and result types so each public seam stays precise.
- Missing runtime during cleanup is a successful no-op.
- Partial install failure triggers best-effort rollback of the Deck sync runtime, and rollback failure is surfaced explicitly.
- The current narrow per-tool installer is renamed to Release asset install module and kept internal.
- GitHub release access becomes one generic internal port reused across all tool installs.
- Ludusavi process execution becomes one internal port with one production adapter and one test adapter family.
- The refactor preserves current install and cleanup behavior rather than introducing feature changes.
- Documentation work in this refactor covers repository docs about runtime flow, public modules, testing expectations, and glossary terms.

## Testing Decisions

- Good tests exercise external behavior through the public seam, not internal helper structure or call ordering inside the implementation unless that ordering is part of the public contract.
- This refactor will keep the existing end-to-end integration test and update it to cross the new public seams.
- Broader interface-level test expansion is deferred for later work.
- The main modules under test in this pass are the Setup Install module and Setup Cleanup module through the preserved integration scenario, with the Deck sync locations module covered indirectly through that flow and directly only if a tiny focused test is needed to keep the refactor safe.
- Prior art is the current integration-heavy runtime-flow test already present in the repository.

## Out of Scope

- Changing the user-visible install or cleanup behavior.
- Adding new runtime features, new CLI commands, or new platform support.
- A broader test expansion beyond keeping the existing integration test working against the new seam layout.
- A general repository-wide prose cleanup unrelated to the runtime flow, public modules, tests, or glossary terms.
- Reworking unrelated modules outside the runtime flow.

## Further Notes

This plan deliberately favors tiny structural commits that keep the program working at each step. The main architectural goal is to increase depth at the public seams, improve locality inside the runtime flow, and make the codebase easier to navigate without coupling the refactor to feature work.
