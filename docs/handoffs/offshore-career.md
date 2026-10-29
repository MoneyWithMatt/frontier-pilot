# Handoff: offshore-career — passenger transfers

- Authority: Matt's 2026-10-29 follow-up selected passengers before H125 cargo/survey. [Approved passenger spec](../specs/offshore-passenger-transfers.md).
- Branch `codex/offshore-career`; reuse [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3) and [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2). Never merge without human approval.
- Passenger implementation `7b4f390bdec8b1230cc15a12761d291a590090f1` pushed; [run 8](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40854467605) passed 773 regression tests and loading WPF but failed SiteUiSmoke with stack overflow. Merge SHA `c0e2f8246461d60399add9b4b5e97b567f15b2b1`; no development artifact published. Current dirty follow-up fixes passenger selection binding reentrancy and strengthens actual-control selection tests. Exact new validation SHA will follow.

## Completed

Inspected the repository: there was no passenger runtime, only metadata. Implemented typed one-passenger prototype transfers, simulated boarding, existing flight-attempt identity/distance/timing rules, shared airborne-time/proof helpers, explicit disembarkation and atomic $800/hour wage. Passengers never enter cargo inventory. Added both-endpoint/type and shared fleet eligibility, explicit boarded GUID, UTC offer cadence/expiry with accepted-state retention, crash/restart/idempotency handling and mixed-load guards including pending/authorized/restored cargo. Schema 11 defaults old worlds to no passenger assignments. Added Passenger transfers UI and an actual-control synthetic WPF lifecycle scenario.

Platform endpoint kind is prepared but explicitly unavailable; no platform missions or H125 enabled. [Live airport and platform qualification procedures](../offshore-passenger-transfers.md) state separate artifact/scenario approval and isolated-data prerequisites. No general live-data-root switch or new native passenger adapter is claimed.

## Verification

- Initial focused passenger run `.ci-output-passengers-focused-1/passengers.trx`: 15 passed, 1 failed, 0 skipped. Gap test's expected elapsed time was off by one; corrected the test to the unchanged existing ten-second policy. Failure evidence retained. Initial build also found an internal `EconomyIds` use from Workflow; moved offer-ID derivation behind the passenger domain API.
- Focused passenger/wage/Offshore run: 46 passed; expanded passenger/loading run: 93 passed; 0 failed/skipped.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-passengers-local-2`: SDK 8.0.424; dirty base `7545922`; full Release 0 warnings/errors; **773 passed, 0 failed/skipped** (751 prior + 19 passenger cases + 3 loading-guard cases). Local TRX `.ci-output-passengers-local-2/tests/regression.trx`.
- `.tools/dotnet/dotnet.exe build tests/SiteUiSmoke/SiteUiSmoke.csproj -c Release` with repository caches: 0 warnings/errors. Local WPF runtime not invoked; await clean hosted runner.
- Run 8 failed SiteUiSmoke with exit -1073741571 / stack overflow in PassengerTransfersViewModel.SelectedAircraft -> Changed -> WPF writeback. Downloaded diagnostics artifact 12888138324, archive SHA256 `172ce84e9d69c93cfa5bb6c96affcb4fbdba61938ff6225d63dd58cc0bd0b4bc` matched; local `.ci-output-passengers-ci-run8/verified/SiteUiSmoke-error.log` preserves the stack. Neither smoke assertions nor timeouts were weakened. Same-value/null selection writebacks now do not emit recursive refreshes; added actual row/aircraft persistence assertions plus one VM regression (expected new total 774).
- No MSFS, Package Tool, personal saves/Community, installed SDK changes or changed published executable was run locally.

## Remaining / next actions

1. Review status/diff, commit/push this passenger slice on the existing branch, update PR #3/title and task #2. Check state first; do not duplicate external actions.
2. Complete hosted Windows regression, nine verification-guard scenarios, both WPF smoke programs (including passenger actual controls), publish/upload. Fix failures and inspect the synthetic passenger screenshot. Record exact head/merge SHA, run/artifact links and results.
3. Commit/push evidence/roadmap/handoff checkpoint; check its final CI in task #2/PR #3. Do not create an endless documentation-only rerun loop; the issue may hold the final evidence-checkpoint result.
4. Present reviewable results. No gameplay choice blocks this airport slice. Live simulator acceptance remains unperformed; follow the approved artifact/scenario and isolated-data procedure before any live action. Platform enablement requires successful empirical qualification plus reviewed adapter/registry/deck rules; the shipped Platform gate remains closed.
5. H125 platform cargo and survey/inspection remain deferred until passenger loop acceptance. No automatic restart/quota mechanism introduced.

GitHub connector writes were previously HTTP 403. Configured Git authentication/API fallback `.ci-output-offshore-api/github.ps1` successfully updates issue/PR; credentials are not logged and helper files are ignored. Do not blindly retry create actions. No merge or settings changes.
