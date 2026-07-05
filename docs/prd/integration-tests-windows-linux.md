# Add Windows and Linux Integration Tests

## Problem Statement

The project currently has limited integration coverage for the Deck sync runtime flow, and the existing coverage is not reliably cross-platform. The current cleanup-then-install test exercises a live-ish path that downloads real tools, runs a real Ludusavi executable, and assumes Windows-style executable names. That makes the test slow, network-dependent, and unsuitable as the default guardrail for both Windows and Linux.

As a result, contributors cannot confidently change the Setup Install module, Setup Cleanup module, Deck sync locations module, or CLI command wiring without risking platform-specific regressions. The project needs integration tests that run consistently on Windows and Linux, exercise the highest useful seams, and preserve a path for optional live verification without making regular CI flaky.

## Solution

Add hermetic-by-default integration tests for the Deck sync runtime flow on both Windows and Linux. These tests should use the real filesystem and real orchestration modules, but replace external dependencies such as GitHub release downloads and Ludusavi process execution with fakes. This keeps the tests fast, deterministic, and safe for CI while still validating the behavior users care about: install creates the Deck sync runtime, cleanup removes it without deleting backup data, Ludusavi config is seeded with the expected settings, failures roll back partial runtime state, and CLI commands return the right exit codes.

Add a Windows/Linux GitHub Actions matrix that runs the solution tests in Release configuration. Also add a skipped-by-default live smoke test gated by `DECK_SYNC_SETUP_LIVE_TESTS=1`, so maintainers can manually or separately verify real downloads and real Ludusavi behavior without making normal test runs depend on the network or upstream release availability.

## User Stories

1. As a contributor, I want integration tests to run on Windows, so that Windows-specific Deck sync runtime behavior remains protected.
2. As a contributor, I want integration tests to run on Linux, so that Linux-specific Deck sync runtime behavior remains protected.
3. As a contributor, I want default integration tests to avoid live GitHub downloads, so that CI is fast and reliable.
4. As a contributor, I want default integration tests to avoid running real Ludusavi binaries, so that test results do not depend on host-installed tools or downloaded executables.
5. As a contributor, I want integration tests to use real temporary directories, so that filesystem behavior is validated instead of mocked away.
6. As a contributor, I want install tests to exercise the Setup Install module, so that the full install orchestration is covered.
7. As a contributor, I want cleanup tests to exercise the Setup Cleanup module, so that runtime deletion behavior is covered.
8. As a contributor, I want tests to use the Deck sync locations module seam or a compatible location seam, so that runtime and backup paths can be isolated per test.
9. As a contributor, I want cleanup followed by install to be tested hermetically, so that the main local setup flow is covered without network dependencies.
10. As a contributor, I want cleanup to preserve backup data, so that tests protect users from accidental backup loss.
11. As a contributor, I want cleanup to succeed when the Deck sync runtime is missing, so that repeated cleanup remains safe.
12. As a contributor, I want install to reject an existing Deck sync runtime, so that tests protect the intended cleanup-before-install workflow.
13. As a contributor, I want install to create the Deck sync runtime, so that the resulting runtime location is observable in tests.
14. As a contributor, I want install to create the Ludusavi config directory, so that config seeding behavior is covered.
15. As a contributor, I want install to write Ludusavi config, so that the generated setup is validated.
16. As a contributor, I want Ludusavi backup and restore paths to point at the Deck sync backup location, so that tests protect backup placement.
17. As a contributor, I want Ludusavi rclone settings to point at the Deck sync runtime, so that tests protect rclone integration.
18. As a contributor, I want Windows tests to expect `.exe` tool paths, so that Windows executable resolution is covered.
19. As a contributor, I want Linux tests to expect extensionless tool paths, so that Linux executable resolution is covered.
20. As a contributor, I want path comparisons to account for platform separators, so that tests are robust across Windows and Linux.
21. As a contributor, I want install tests to assert the three expected tools are installed, so that rclone, syncthing, and Ludusavi remain part of the Deck sync runtime.
22. As a contributor, I want install tests to verify progress includes the Ludusavi config command, so that command wiring remains observable.
23. As a contributor, I want install tests to verify progress includes the Ludusavi backup command, so that backup invocation remains observable.
24. As a contributor, I want install tests to verify Ludusavi backup success is reported, so that users continue to receive meaningful progress.
25. As a contributor, I want failure-path tests to cover Ludusavi backup failure, so that rollback behavior is protected.
26. As a contributor, I want failed install to roll back the Deck sync runtime, so that partial installs do not leave broken runtime files.
27. As a contributor, I want failed install to preserve backup data, so that rollback does not delete user backup data.
28. As a contributor, I want failed install to surface a categorized Setup Install module failure, so that callers can diagnose the failed stage.
29. As a contributor, I want fake release assets to create realistic tool files, so that downstream install steps exercise expected runtime paths.
30. As a contributor, I want fake Ludusavi process results to be explicit per test, so that success and failure behavior is deterministic.
31. As a CLI user, I want `install` to call the Setup Install module, so that the command performs the expected setup work.
32. As a CLI user, I want `cleanup` to call the Setup Cleanup module, so that the command performs the expected cleanup work.
33. As a CLI user, I want successful `install` to return exit code 0, so that scripts can detect success.
34. As a CLI user, I want successful `cleanup` to return exit code 0, so that scripts can detect success.
35. As a CLI user, I want failed `install` to return exit code 1, so that scripts can detect failure.
36. As a CLI user, I want failed `cleanup` to return exit code 1, so that scripts can detect failure.
37. As a CLI user, I want command failures to print `Error: <message>`, so that failures are easy to read without a stack trace.
38. As a maintainer, I want command construction to be testable without launching a real process, so that CLI behavior can be tested hermetically.
39. As a maintainer, I want the production CLI wiring to remain unchanged for users, so that adding tests does not change command behavior.
40. As a maintainer, I want optional live smoke tests to be skipped by default, so that normal CI remains deterministic.
41. As a maintainer, I want optional live smoke tests to run when `DECK_SYNC_SETUP_LIVE_TESTS=1`, so that real tool downloads can be verified intentionally.
42. As a maintainer, I want live smoke tests to support Windows and Linux path expectations, so that manual verification is not Windows-only.
43. As a maintainer, I want GitHub Actions to run tests on `windows-latest`, so that Windows regressions are caught automatically.
44. As a maintainer, I want GitHub Actions to run tests on `ubuntu-latest`, so that Linux regressions are caught automatically.
45. As a maintainer, I want CI to run `dotnet test` against the solution in Release configuration, so that the application and integration tests build together.
46. As a maintainer, I want tests organized by behavior contract, so that future coverage can grow without creating one large test file.
47. As a maintainer, I want shared test fakes kept in test support code, so that each test focuses on external behavior.
48. As a maintainer, I want internal test seams instead of public-only test hooks, so that production APIs stay small.
49. As a maintainer, I want tests to avoid asserting implementation details, so that refactors remain safe when behavior is preserved.
50. As a future agent, I want the PRD to describe the agreed testing seams clearly, so that implementation can proceed without re-litigating the design.

## Implementation Decisions

- Default integration tests will be hermetic. They will not perform live GitHub downloads and will not run real Ludusavi binaries unless a live smoke gate is explicitly enabled.
- The Setup Install module will retain its existing public behavior while gaining internal seams for test-controlled release asset installation and Ludusavi process execution.
- The preferred install seam is one internal constructor or equivalent internal dependency seam on the Setup Install module. This keeps the production API small while allowing integration tests to replace external dependencies.
- The test project will use `InternalsVisibleTo` to access internal seams. The seams should not be made public solely for tests.
- The Setup Cleanup module can continue to be tested through its existing public module interface and a test-controlled Deck sync locations module.
- CLI command construction will be extracted into a command factory or equivalent testable composition point that accepts `ISetupInstallModule` and `ISetupCleanupModule`.
- The production entry point will continue to wire the real Deck sync locations module, Setup Install module, Setup Cleanup module, and console progress reporter.
- CLI command tests will invoke command construction directly with fake modules instead of launching the compiled executable.
- The CLI test contract is intentionally thin: `install` invokes install, `cleanup` invokes cleanup, success returns 0, and module failure returns 1 with `Error: <message>`.
- Help text, unknown-command parse failures, and full System.CommandLine behavior are not part of this pass.
- Windows/Linux behavior will be represented by shared cross-platform tests with small OS-specific assertions only where behavior truly differs.
- Executable path expectations will account for `.exe` on Windows and extensionless executable names on Linux.
- Path assertions for Ludusavi YAML should normalize separators where appropriate, matching the existing runtime behavior that writes YAML paths with forward slashes.
- The current live-ish cleanup-then-install behavior will be replaced by hermetic default tests and moved into a skipped-by-default live smoke test.
- The live smoke test will be gated by `DECK_SYNC_SETUP_LIVE_TESTS=1`.
- A GitHub Actions workflow will run the solution tests on a Windows/Linux matrix.
- The CI command will be `dotnet test` against the solution in Release configuration.
- Test organization will be split by contract: install flow tests, cleanup flow tests, CLI command tests, and shared test support.
- The existing domain language must be used throughout the implementation: Deck sync runtime, Setup Install module, Setup Cleanup module, and Deck sync locations module.

## Testing Decisions

- A good test should assert externally observable behavior: files created or removed, config content, returned result values, reported progress, command exit codes, and user-facing error messages.
- Tests should avoid asserting private implementation details such as exact helper method names, local variable names, or internal iteration order beyond observable tool installation order.
- The highest useful seam for install-flow integration tests is the Setup Install module with internal fakes for release asset installation and Ludusavi process execution.
- The highest useful seam for cleanup-flow integration tests is the Setup Cleanup module with a test-controlled Deck sync locations module.
- The highest useful seam for CLI command tests is the command factory invoked with fake `ISetupInstallModule` and `ISetupCleanupModule` implementations.
- The prior integration test already establishes useful patterns: temporary home/runtime/backup directories, a recording progress reporter, cleanup before install, assertions on Ludusavi config content, and cleanup of temporary directories after each test.
- The existing cleanup missing-runtime no-op test should be preserved conceptually, but moved into the split cleanup-flow test organization.
- Install success tests should assert that the Deck sync runtime exists, the backup directory exists, the Ludusavi config exists, the expected tools are reported, and the expected command progress appears.
- Rollback tests should force the Ludusavi backup stage to fail and assert that the Deck sync runtime is removed while the Deck sync backup location remains.
- CLI tests should cover success and failure for both `install` and `cleanup`.
- Live smoke tests should be clearly marked and skipped unless `DECK_SYNC_SETUP_LIVE_TESTS=1`.
- The normal GitHub Actions matrix should not enable live smoke tests.
- The GitHub Actions matrix should include Windows and Linux only for this pass.

## Out of Scope

- macOS integration test coverage.
- Always-on live end-to-end tests that download real release assets.
- Always-on tests that run real Ludusavi binaries.
- New CLI options or arguments.
- Help text assertions.
- Unknown command or invalid argument parse behavior.
- Changing the user-facing behavior of `install` or `cleanup`.
- Refactoring unrelated install, cleanup, or asset-selection behavior.
- Adding new package managers, build tools, or test frameworks.
- Testing every possible install failure stage in this pass.
- Simulating Windows and Linux inside a single OS run.

## Further Notes

- The agreed design keeps normal tests deterministic while preserving a manual path for real-world verification.
- The most important safety behavior to protect is rollback after partial Setup Install module failure while preserving backup data.
- The implementation should update the runtime-flow documentation if the new command factory or internal seams change the documented testing story.
- If the `ready-for-agent` issue label is used for this PRD later, no additional triage label is needed.
