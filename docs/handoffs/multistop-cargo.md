# Multi-stop cargo handoff

## Current checkpoint

Implementation complete for automated review in draft [PR #6](https://github.com/MoneyWithMatt/frontier-pilot/pull/6), branch `codex/multistop-cargo`, target `master`. Keep draft; do not merge. Original PR #3/#5 branches/worktrees were not changed. Base is PR #3 `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`; pinned PR #5 research copied from `98d9ace`.

Last validated runtime/source **`8b56d6f8dd5d76e50f88fdfcd3856794ed342ed6`**, [hosted run 23](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40961114914), clean tested merge **`fdda22edbebf09a85bd4729a7a8fca38dd309d9c`**: **816 passed / 0 failed / 0 skipped**, SDK 8.0.424, Release zero warnings/errors, nine guards, both WPF programs, packaging/uploads passed. Clean receipts, TRX, logs and fax screenshots independently inspected. [Verification / retained screenshots / failures](../../evidence/multistop-cargo/verification.md). This documentation checkpoint does not change runtime/test source; inspect PR #6 for its own final CI status before resuming.

## Completed

- Explicit independent job quantities/resources/routes, persistent GUID ownership and aggregate station claims; production controls prove 35→25→0 and intermediate pickup using synthetic readback. Additional jobs can be accepted in flight.
- Schema 13 durable absolute intent, seated gross expectations across retries, retained station receipts, handling history, immutable carried-leg snapshots and tracker proof. Failed native/save operations retain intent; manual mass/fuel/layout/location/identity conflicts block rather than overwrite. Legacy stores/saves preserved without inferred mass/GUID migration.
- Shared transport monitor accepts only route/flight context; cargo is never a passenger mission. No pre-loading evidence or disconnected/restart gap wages.
- Successful-delivery wage eligibility preserved: arrival records time/proof, full delivery releases each eligible unpaid leg once, including prior through-legs. Arrival alone/partial delivery do not pay. Existing career hourly rates and fixed freelance/player rewards retained; jobs on one flight cannot multiply wages.
- MTOW/overall payload/CG/company fixture budgets are advisory for cargo/passengers. Actual station/interface constraints, stock, ownership, identity/readback remain mandatory. Shared passenger retry also rejects station echoes without physical gross effect.
- Existing explicit walking-avatar outside authorization/seated verification retained. Production dry-run cannot call native cargo integration. Recover old manifests with the original Aircraft flow first.
- Production cargo/quantity/station controls, preview/recovery/actions, plan/arm leg and selectable in-flight fax manifest. Compact columns and automatic row height keep job/destination details readable.

Milestones: `a9ab547` implementation/UI/tests; `5c30dc4` architecture/spec/research/live procedure; `4f5f071` durable gross retry; `d920ca6` compact layout; `8b56d6f` delivery eligibility/shared monitor/row height/integrity coverage.

## Commands / artifacts

Local clean `8b56d6f`, passed 816/0/0:
```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-multistop-07
```
Entry point sets `.tools/home`/`.tools/nuget`. Focused: `dotnet test tests/Tests -c Release --no-restore --filter 'FullyQualifiedName~CargoJourneyTests|FullyQualifiedName~Passenger'` (65/0/0 final precommit). Compile-only `dotnet build tests/SiteUiSmoke -c Release --no-restore`. No local WPF/published executable was run.

Hosted entry point: `-Stage BuildTest`, `-Stage Smoke`, `-Stage Package`, each `-DotnetPath dotnet`; plus `pwsh -NoProfile -File scripts/Test-VerificationGuards.ps1`.

[Runtime development artifact](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40961114914/artifacts/12936670928), ZIP SHA256 `9c0fceccd48f9b1ef78260e86569e329241519cddab359fe94433ef730417abf`, not downloaded/executed. [Diagnostics](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40961114914/artifacts/12936670938), independently matched SHA256 `52d68316ab8da30c84bf5079e85df53de2708123fca29404c2ede30fd91c9c34`; all clean source receipts and 816-case TRX inspected. Seven-day retention.

Failures preserved in the verification report: initial 789/5 policy assertions; early compile plumbing/validation typo; 17/1 in-flight scenario corrected from a valid origin return to unqualified destination arrival; run-20 collapsed columns and run-22 clipped detail caught visually, fixed with bounds assertions. Run 21 superseded/cancelled, not passing package evidence. Local full runs 02/03=809, 04/05=810, 06=812, final 07=816. No skips/weakened assertions.

## Remaining / exact next action

1. Inspect current head/run/PR and task #2 before repeating external actions. This final checkpoint changes documentation/screenshots only; once its CI passes, the exact next work is human review and separately approved live acceptance below. No additional source commit is needed merely to repeat a passing status. No implementation task or unresolved product decision remains for this slice.
2. Human review/merge decision and **separately approved exact artifact/scenario live MSFS acceptance**. No live test performed. Follow [scope](../specs/multistop-cargo.md) and [focused live procedure](../multistop-cargo.md#live-simulator-acceptance). Record aircraft/add-on/config/SDK/simulator hashes and actual station/gross/fuel/job/inventory/payment results using an isolated profile/reviewed harness. No general data-root switch exists. CI success does not approve a new executable on the gaming PC.
3. Explicit limitations: one active airframe cargo journey at a time; no automatic aircraft reassignment/adoption; kilogram consignments only until mass conversion is configured. Cancellation/off-route disposition, empty repositioning wages, archival, print/PDF and the pilot-operated planning worksheet remain deferred.
4. Common cargo/passenger native interface/lease and ownership exclusions are implemented; **mixed transport remains unsupported** until coordinated accounting/recovery and simulator qualification. PR #3's SF50 configuration block, Caravan candidate-only physical support and unavailable seated-model/animation control remain. Platforms, H125 cargo and survey/inspection remain disabled/deferred. No fares or medical simulation added.

No MSFS/Package Tool, personal-save/Community/configuration writes, local app execution, repository-setting change or merge occurred. All intended source/evidence is committed/pushed on the isolated branch; inspect working-tree status before resuming.
