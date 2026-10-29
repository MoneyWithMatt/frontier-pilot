# Handoff: offshore-career — passenger transfers

- Authority: Matt's 2026-10-29 follow-up selected passenger transfers before H125 cargo/survey. [Approved spec](../specs/offshore-passenger-transfers.md), [operation/live procedure](../offshore-passenger-transfers.md).
- Branch `codex/offshore-career`; reuse [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3) and [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2). No merge authorized.
- Last validated runtime implementation: `d1a9dfaf5d733569de6110f0fd18cb68f4cb0596`, clean tested merge `444f6220afdca35cf8d3dd22c94b1a3f776d2e97`, successful [run 10](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40855915342).
- Status: airport passenger slice implemented and automated/visual verification complete; live simulator acceptance pending. This documentation-only checkpoint follows the validated runtime. Its final CI result is recorded in task #2/PR #3. No uncommitted runtime work remains.

## Completed

No passenger runtime existed originally, only research metadata. Added typed one-passenger prototype assignments, simulated boarding, existing flight-attempt identity/distance/stationary rules and shared airborne-time/proof helpers, explicit disembarkation and atomic $800/hour PilotWage settlement. People never enter cargo inventory. Both-endpoint/type/fleet eligibility, selected physical GUID, UTC cadence/expiry with accepted-state retention, restart/failed-write/concurrent/idempotent recovery and mixed-load/pending-cargo guards are implemented. Schema 11 defaults old worlds to no passenger assignments without changing money/history. Actual-control WPF covers the complete synthetic transfer and stable row/aircraft selection.

Platform endpoint kind is prepared but explicitly unavailable, with no toggle/registry entry or guessed destinations. Future successful exact live qualification plus reviewed adapter/registry/deck rules is required before activation. H125 platform cargo and survey/inspection remain deferred. Boarding has no native passenger/payload/animation writes.

## Verification and preserved failures

[Exact source, commands, counts, run/artifact links and failure/visual follow-ups](../offshore-passenger-verification.md).

- Initial focused test: 15 pass/1 fail/0 skip; corrected an off-by-one gap expectation to unchanged ten-second semantics. Initial compile caught a cross-project internal ID helper reference, corrected through the domain API. Evidence retained locally.
- Run 8: 773 regression pass, loading WPF pass; SiteUiSmoke stack overflow in selection binding. Diagnostics downloaded/checksum matched; no development artifact. Fixed same-value/null writeback reentrancy, added a VM regression and strengthened actual selection assertions. No test/timeout/publication guard weakened.
- Local full command `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-passengers-local-3`: SDK 8.0.424, dirty `7b4f390` + repair; Release zero warnings/errors; 774/0/0. Count = 751 previous + 20 passenger + 3 cargo guard cases.
- Runs 9 and 10 passed 774/0/0, nine guard scenarios, both WPF programs, publish and both uploads. Final screenshot visual review passed after fixing row/pay clipping, selector label and completed status. Run-10 diagnostic archive checksum independently verified; clean source receipt inspected. No published application executable ran locally.
- No MSFS, Package Tool, personal save/Community changes, SDK installation, repository settings or merge. Automated verification is not simulator acceptance.

## Remaining / exact next actions

1. Inspect git status/HEAD, PR #3 and task #2. Implementation, failure repair, visual review and run-10 actions are complete. Reuse the branch/PR; never repeat external creation blindly.
2. Check documentation-checkpoint CI only if its success is not already recorded in task #2/PR #3. No further source commit is needed merely to repeat the final issue checkpoint. Fix genuine failures without weakening assertions.
3. Human review/merge remains separate. No unresolved gameplay choice blocks the implemented airport loop.
4. Before any live action, obtain exact artifact/scenario approval and use the documented disposable Windows profile or reviewed isolated harness. No general data-root switch exists. Do not reset or use personal saves as fixtures. Execute airport transfer/recovery acceptance and record redacted evidence.
5. Keep platforms unavailable until the exact qualification procedure succeeds and the subsequent reviewed adapter/registry/deck-specific rules are accepted. Defer H125 platform cargo and survey/inspection until the passenger loop is established.

GitHub connector writes were HTTP 403; configured Git authentication/API fallback `.ci-output-offshore-api/github.ps1` updates issue/PR without logging credentials. Helpers and downloaded diagnostics are ignored, not runtime features. No automatic restart/quota bypass mechanism was introduced.
