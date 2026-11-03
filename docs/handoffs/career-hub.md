# Handoff: career-hub

- Approved specification: [Career Hub](../career-hub.md), Matt's 2026-11-03 request.
- Branch: `codex/career-hub`; draft PR pending.
- Integration base: PR #10 head `d5d4f23b8f6fc7a13167b8f55a2398a3d157cd49`. Includes unmerged shared passenger, physical fleets and loading-manager work. Master and other worktrees untouched.
- Last validated baseline: base above plus dirty Career changes, 970/0/0 existing tests. New-test runs and final commit evidence will be recorded below.
- Status: implementation/verification in progress.

## Completed work

Five-section Career shell, actual employer/marketplace information, physical fleet and inspection-only condition workspace. Original loading controls moved once into Fleet; shared GUID context and guarded accepted-job navigation. Unified cargo/personnel/observation/platform-status board with existing Mapsui geography. Actual owned-site contract creation only. Legacy recovery preserved. No schema or simulator capability changes.

## Tests and evidence

Local Windows: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-career-<run>`.

- 01 / 02: 970 passed, 0 failed, 0 skipped; zero build warnings/errors.
- 03: 977 passed, 2 failed, 0 skipped. Caught stale board projection and incorrect test role expectation. Logs retained in ignored output.
- 04: 978 passed, 1 failed, 0 skipped. Stale projection confirmed with exact source/row states.
- 05: 978 passed, 1 failed, 0 skipped. Projection fix passed; remaining assertion incorrectly expected the word “not” in support text. Replaced with specific physical-boarding restriction and real profile refusal assertion.
- SiteUiSmoke compiles, zero warnings/errors. Execution/screenshots require hosted Windows; none claimed yet.

No live simulator execution, personal save changes, new distribution execution or simulator acceptance performed.

## Remaining work / exact next steps

1. Inspect final BuildTest result and resolve remaining failures; do not suppress assertions.
2. Commit/push the feature branch; create one draft PR to master identifying PR #10 integration base. GitHub connector previously returned 403 for metadata writes; do not extract shell credentials or retry the rejected shell REST workaround.
3. Run/inspect hosted Windows WPF and packaging checks on exact commit. Inspect screenshots, fix UI failures, preserve diagnostics. No self-hosted runner or local SiteUiSmoke against personal configuration.
4. Update this handoff and PR with exact commit, commands/counts, evidence and pending acceptance. Do not merge.
