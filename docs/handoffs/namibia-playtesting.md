## Startup freeze repair checkpoint — 2026-11-03

- User reports immediate startup freeze in installed 67b093a. Windows Application Hang records confirm actual hangs; Frontier Pilot was closed when inspected. No simulator input, new app launch, personal-save editing or credential changes.
- Isolated regression against e4acb53: ten loading presentation updates republished the Career Job Board 200 times (failed TRX `.ci-output-freeze-repro/freeze-repro.trx`, ignored; preserved). This confirms a refresh-storm regression, not proof that it is the only cause of Matt's observed hang.
- Implementation 7de3ca0e66772c17e923a23ccafa510bc4bbad6a: unchanged committed/list snapshots use lightweight progress notifications, staged cargo/passenger batches project after busy boundaries, airport identity index replaces repeated full geography scans and invalidates on facility-cache replacement. No gameplay, authority or schema changes. Added deterministic unchanged-collection regression and 100-update real WPF collection/selection check.
- Local exact command: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-freeze-fix-01` passed 1,014 / 0 failed / 0 skipped, zero warnings/errors. At execution, e4acb53 plus repair was dirty; subsequently committed as 7de3ca0. WPF test addition then committed with that source.
- Hosted [run 41699597542](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41699597542) passed on clean 7de3ca0, SDK 8.0.424: 1,014 / 0 failures / 0 skips, 207 SiteUiSmoke and 7 LoadingWpfSmoke assertions, 9 simulated guards, historical CLI and Windows packaging. [Development artifact](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41699597542/artifacts/13262318950), [diagnostics/screenshots/TRX](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41699597542/artifacts/13261479303). Exact hosted commands are the same BuildTest / Test-VerificationGuards / Smoke / historical CLI / Package steps recorded below.
- Local copy replaced with exact 7de3ca0 at existing Desktop shortcut after all 11 bundle hashes, metadata, prior receipt and new executable hash were checked. Prior 67b093a folder retained as backup, exact private path in local deployment receipt. Installed executable SHA256 `03F6E5CF33447AD771474157DD28358AD571FC292418BC0FE6FBA3E9BF39D47B`. No personal saves/config read or altered by replacement; no executable launch yet.
- Pending: Matt was asked for exact-build startup/navigation permission under AGENTS.md. Await that answer before opening this new executable with personal career data. Do not accept jobs, load payload, operate MSFS, terminate running user apps or restore old saves. Automated/WPF success is not yet proof of resolved startup with Matt's current career. If approved, inspect responsiveness and preserve any renewed failure before further fixes.

## Local development distribution update — 2026-11-03

Matt explicitly requested updating his local version after the tested build was identified. Installed the exact hosted `67b093a` executable (artifact 13252414736) at the existing Desktop shortcut target, after validating all 11 bundle checksums, clean win-x64 metadata and the prior deployment receipt/hash. Preserved the previous run-38 distribution in a sibling backup; the private local deployment receipt records its exact path. Post-install executable hash matches the hosted manifest and the shortcut still resolves to it. No application launch, live simulator operation, save/config access or migration performed. Older statements below that nothing was installed describe their earlier checkpoints. Future autonomous execution remains a separate scenario decision.

# Current refinement checkpoint — airport verification / pilot responsibility

- Approved scope: [airport-verification-refinement](../specs/airport-verification-refinement.md), Matt's explicit 2026-11-03 request. This supersedes the aircraft-specific review policy in the historical checkpoint below.
- Branch remains `codex/namibia-playtesting`. Implementation `67b093afb57c8bdb69a64eaea13e9fb0c2e9648f`, clean committed source. No merge, personal data access or live simulator operation.
- Implemented: airport-level representation knowledge; historical endpoint aircraft lists and AircraftUnsuitable reports are metadata only; company/type/physical loading/flight/pay/technical holds retained. Optional inline Career Yes/Report/Skip prompt projects valid committed cargo/passenger completion and settlement, including Load Manager transitions. Manual problem reports remain available before completion. Skip persists one per-airport acknowledgement, no fake visit/economy write. Manual verification requires completed/settled airport work. Existing positive reports apply across aircraft; no duplicate record conversion.
- Persistence: airport-visits schema 2 reads schema 1 without rewriting on load; retains old observations and adds idempotent dismissals under existing bounded/mutex/atomic/backup/corruption guards. Economy schema unchanged. No automatic cancellation/rerouting.
- Local verification: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-airport-refine-04` passed 1,013 / 0 failures / 0 skips, zero warnings/errors. Source at execution was 2ee42c4 plus this implementation dirty, subsequently committed as 67b093a. Run 01 retained three failures in old endpoint aircraft-exclusion assertions, updated to the explicitly approved policy; runs 02/03 passed 1,013, then the nullable dismissal warning was fixed and real passenger prompt assertions added for run 04. No skipped/weakened loading, fleet, proof or payment safeguards.
- WPF compile-only: `dotnet build tests/SiteUiSmoke/SiteUiSmoke.csproj -c Release` with the repository cache environment succeeded. No new WPF/CLI executable run locally. Full WPF and packaging use hosted Windows only.
- Hosted run: [41681372600](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600) passed on clean exact source `67b093a`, SDK 8.0.424: 1,013 / 0 failures / 0 skips; 206 SiteUiSmoke and 7 LoadingWpfSmoke assertions; 9 simulated verification guards, historical CLI review and Windows package all passed. Receipts confirm sourceDirty false. All three Namibia screenshots inspected: readable optional prompt, airport-level verified status and simulator-problem category/confirmation. These are synthetic WPF/tracker evidence, not MSFS acceptance.
- Commands: `./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet`; `pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1`; `./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet`; existing synthetic historical CLI step; `./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet`.
- [New Windows development build](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600/artifacts/13252414736), [screenshots/logs/TRX/receipts](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600/artifacts/13253915604). Metadata/checksums identify source. Nothing installed or run locally; real airport testing remains Matt's opt-in playtest.
- Exact next steps: review the committed player instructions and Windows artifact. The final documentation checkpoint changes no production code; inspect its head CI status before review. Prior scoped PR creation remains blocked by HTTP403 integration permissions; use the updated prepared description when scoped access is available, checking for an existing PR first. Do not extract credentials or bypass permissions. Leave merging to Matt.

---

# Handoff: namibia-playtesting

- Approved scope: [specification](../specs/namibia-playtesting.md), Matt's 2026-11-03 request.
- Branch: `codex/namibia-playtesting`, based on Career Hub `21b1f78f2ec9d44764450ecfffbf481a46e831bf`. Master and other feature worktrees preserved.
- Status: implementation complete; final hosted verification and screenshot review passed. Ready for Matt's opt-in playtest. Nothing merged or installed.
- PR: draft creation attempted through the scoped connector and rejected with HTTP 403, Resource not accessible by integration. Branch push works. [Prepared PR description](namibia-playtesting-pr.md). Do not extract credentials, bypass permission failures or retry the rejected shell-credential workaround. No merge authorised.
- Last validated implementation: `38a6bf76595a0dc4ee12eaebb92d59e6c9d8ab79`, clean source, [hosted run 41674240167](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167): 1,009 passed / 0 failed / 0 skipped, zero warnings/errors; both WPF programs, guards, historical CLI and packaging passed. This documentation checkpoint has no production changes.

## Completed

57 sourced PR #4 references, original membership/key preservation, exploratory-vs-technical eligibility, six-destination rotating cargo pools on the persisted clock, existing four-route personnel source, held identities and durable lookup conflicts. Inline per-aircraft player reviews in Career, independent persistent observation store, immediate future-job withholding with accepted/history preservation. Economy schema 15/cache schema 2 compatibility. No simulator/aircraft/payload qualification bypass.

## Verification and failures retained

PowerShell 7 command:

```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-namibia-10
```

Local logs/TRX are in ignored `.ci-output-namibia-*` / `.ci-output-namibia-logs`. Run 01 found a mispatched constructor call; 02 found missing source metadata on the held Nepara record; 03 found null/unconfirmed identifiers incorrectly treated as duplicate ICAOs; 04/05 exposed old approval-only fixture expectations and source/held geographic fixtures; 06 found a missing namespace import; 07/08 narrowed remaining fixture/expectation failures. Fixed, without skipping tests or weakening physical/flight/payment assertions. Run 09 passed 1,004; run 10 passed 1,005 after adding persisted-cadence coverage. Updated former FYNG ordinary-route fixtures to an unheld field; explicit held tests now cover FYNG and the other conflicts.

Local runs 11–13 passed 1,005/1,006/1,006; 14 passed 1,007; 15/16 passed 1,008. The new recovery regression proves a destination report cannot trap an already persisted partial passenger boarding intent or duplicate its mass. An explicit per-type problem cannot be cleared by a good visit in another aircraft.

Final review found the report's default aircraft could remain C152 after selecting the Caravan. Runs 17/18 each retained 1,008 passes and one new regression failure: report defaults were not synchronously refreshed on fleet selection. Fixed in `38a6bf76595a0dc4ee12eaebb92d59e6c9d8ab79`; the default now follows the selected physical airframe until the player edits aircraft, description or notes. Drafts remain intact. Local run 19 passed 1,009 / 0 / 0 with zero build warnings/errors, using the same BuildTest command with `-OutputDirectory .ci-output-namibia-19` (source `0e03af2` with the two-line CareerHub correction dirty, subsequently committed as `38a6bf7`). [Hosted failure at 0e03af2](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41673596158) is retained; [final source run](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167) passed the full pipeline. No assertions skipped or weakened.

## Final hosted evidence

- Exact source: `38a6bf76595a0dc4ee12eaebb92d59e6c9d8ab79`, sourceDirty false, SDK 8.0.424.
- [Windows CI](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167): 1,009 xUnit / 0 failures / 0 skips; 198 SiteUiSmoke and 7 LoadingWpfSmoke assertions; 9 simulated verification-guard scenarios; historical CLI assertions and packaging passed.
- Commands: `./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet`, `pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1`, `./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet`, `./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet`. Existing workflow also reviews the synthetic offshore round-trip CLI recording.
- [Windows development bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167/artifacts/13249557417); build metadata/checksums identify exact source. Download is not local execution approval.
- [Synthetic screenshots, receipts, logs and TRX](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167/artifacts/13250126473). Both `namibia-airport-verified.png` and `namibia-airport-problem.png` reviewed: readable categories, visible save result and accepted consignment retained. Existing cargo/passenger/Offshore/fleet screens also exercised.
- Earlier hosted UI failures retained: [6c03807](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41667832687), [2f355d3](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41668835084). They exposed staged Offers→Reserved snapshot selection changes; fixed with selected identity retention and null aircraft-choice protection. [c405e82 full pipeline](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41669889431) then passed; screenshot review prompted the readable-category/visible-confirmation polish.

No live MSFS operation, Package Tool, scenery installation, personal save migration, new local UI/CLI executable run or airport acceptance occurred.

## Remaining / exact next steps

1. Implementation evidence above is tied to exact 38a6bf7. Check the latest documentation-only head status before review. Do not repeat completed external actions blindly.
2. Create one draft PR to master when scoped PR-write access is available, using the prepared description. Check for an existing PR first. It is stacked on Career Hub 21b1f78 and PR #10 dependencies; review those before merging. Leave merging to Matt.
3. Screenshots and Windows artifact are linked below and have been inspected. No new executable was run locally. Real playtesting requires the exact artifact/scenario approval before autonomous PC execution.
4. Matt's real playtest uses [the procedure](../namibia-playtesting.md). Airport usability is not proven by tests. Autonomous live execution requires exact-artifact/scenario approval.

## Limitations

Safari passenger profiles, SF50 physical boarding and H125/platform missions remain restricted. Reports cannot repair technical identities or move route coordinates. Bad-airport accepted-job cancellation/re-routing/disposition remains future work; no incomplete payment/refund is invented. Active employer switching remains unsupported. GitHub PR-write credentials may still block PR creation; normal branch push and hosted Windows push verification are available.
