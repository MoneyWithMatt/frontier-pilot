# Handoff: namibia-playtesting

- Approved scope: [specification](../specs/namibia-playtesting.md), Matt's 2026-11-03 request.
- Branch: `codex/namibia-playtesting`, based on Career Hub `21b1f78f2ec9d44764450ecfffbf481a46e831bf`. Master and other feature worktrees preserved.
- Status: implementation complete; full hosted verification and screenshot review passed. Ready for Matt's opt-in playtest. Nothing merged or installed.
- PR: draft creation attempted through the scoped connector and rejected with HTTP 403, Resource not accessible by integration. Branch push works. [Prepared PR description](namibia-playtesting-pr.md). Do not extract credentials, bypass permission failures or retry the rejected shell-credential workaround. No merge authorised.
- Last validated implementation: `009b0f260c856ae9ab8f613b1421efefd7171295`, clean source, [hosted run 41671407695](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41671407695): 1,008 passed / 0 failed / 0 skipped, zero warnings/errors; both WPF programs, guards, historical CLI and packaging passed. This documentation checkpoint has no production changes.

## Completed

57 sourced PR #4 references, original membership/key preservation, exploratory-vs-technical eligibility, six-destination rotating cargo pools on the persisted clock, existing four-route personnel source, held identities and durable lookup conflicts. Inline per-aircraft player reviews in Career, independent persistent observation store, immediate future-job withholding with accepted/history preservation. Economy schema 15/cache schema 2 compatibility. No simulator/aircraft/payload qualification bypass.

## Verification and failures retained

PowerShell 7 command:

```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-namibia-10
```

Local logs/TRX are in ignored `.ci-output-namibia-*` / `.ci-output-namibia-logs`. Run 01 found a mispatched constructor call; 02 found missing source metadata on the held Nepara record; 03 found null/unconfirmed identifiers incorrectly treated as duplicate ICAOs; 04/05 exposed old approval-only fixture expectations and source/held geographic fixtures; 06 found a missing namespace import; 07/08 narrowed remaining fixture/expectation failures. Fixed, without skipping tests or weakening physical/flight/payment assertions. Run 09 passed 1,004; run 10 passed 1,005 after adding persisted-cadence coverage. Updated former FYNG ordinary-route fixtures to an unheld field; explicit held tests now cover FYNG and the other conflicts.

Local runs 11–13 passed 1,005/1,006/1,006; 14 passed 1,007; 15/16 passed 1,008. The new recovery regression proves a destination report cannot trap an already persisted partial passenger boarding intent or duplicate its mass. An explicit per-type problem cannot be cleared by a good visit in another aircraft.

## Final hosted evidence

- Exact source: `009b0f260c856ae9ab8f613b1421efefd7171295`, sourceDirty false, SDK 8.0.424.
- [Windows CI](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41671407695): 1,008 xUnit / 0 failures / 0 skips; 198 SiteUiSmoke and 7 LoadingWpfSmoke assertions; 9 simulated verification-guard scenarios; historical CLI assertions and packaging passed.
- Commands: `./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet`, `pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1`, `./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet`, `./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet`. Existing workflow also reviews the synthetic offshore round-trip CLI recording.
- [Windows development bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41671407695/artifacts/13249051668); build metadata/checksums identify exact source. Download is not local execution approval.
- [Synthetic screenshots, receipts, logs and TRX](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41671407695/artifacts/13249061550). Both `namibia-airport-verified.png` and `namibia-airport-problem.png` reviewed: readable categories, visible save result and accepted consignment retained. Existing cargo/passenger/Offshore/fleet screens also exercised.
- Earlier hosted UI failures retained: [6c03807](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41667832687), [2f355d3](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41668835084). They exposed staged Offers→Reserved snapshot selection changes; fixed with selected identity retention and null aircraft-choice protection. [c405e82 full pipeline](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41669889431) then passed; screenshot review prompted the readable-category/visible-confirmation polish.

No live MSFS operation, Package Tool, scenery installation, personal save migration, new local UI/CLI executable run or airport acceptance occurred.

## Remaining / exact next steps

1. Check the branch's latest documentation-only CI result; implementation evidence above is tied to exact 009b0f2. Changes are committed/pushed. Do not repeat completed external actions blindly.
2. Create one draft PR to master when scoped PR-write access is available, using the prepared description. Check for an existing PR first. It is stacked on Career Hub 21b1f78 and PR #10 dependencies; review those before merging. Leave merging to Matt.
3. Screenshots and Windows artifact are linked below and have been inspected. No new executable was run locally. Real playtesting requires the exact artifact/scenario approval before autonomous PC execution.
4. Matt's real playtest uses [the procedure](../namibia-playtesting.md). Airport usability is not proven by tests. Autonomous live execution requires exact-artifact/scenario approval.

## Limitations

Safari passenger profiles, SF50 physical boarding and H125/platform missions remain restricted. Reports cannot repair technical identities or move route coordinates. Bad-airport accepted-job cancellation/re-routing/disposition remains future work; no incomplete payment/refund is invented. Active employer switching remains unsupported. GitHub PR-write credentials may still block PR creation; normal branch push and hosted Windows push verification are available.
