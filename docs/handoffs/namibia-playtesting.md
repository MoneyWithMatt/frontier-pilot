# Handoff: namibia-playtesting

- Approved scope: [specification](../specs/namibia-playtesting.md), Matt's 2026-11-03 request.
- Branch: `codex/namibia-playtesting`, based on Career Hub `21b1f78f2ec9d44764450ecfffbf481a46e831bf`. Master and other feature worktrees preserved.
- Status: implementation complete, hosted WPF/package verification pending.
- PR: pending scoped connector attempt after commit. Previous Career task's connector returned 403; do not extract credentials or retry the rejected shell-credential workaround. No merge authorised.
- Last validation: dirty implementation at base above, local run 10: 1,005 passed / 0 failed / 0 skipped, zero build warnings/errors. New SiteUiSmoke code compiles; not run locally.

## Completed

57 sourced PR #4 references, original membership/key preservation, exploratory-vs-technical eligibility, six-destination rotating cargo pools on the persisted clock, existing four-route personnel source, held identities and durable lookup conflicts. Inline per-aircraft player reviews in Career, independent persistent observation store, immediate future-job withholding with accepted/history preservation. Economy schema 15/cache schema 2 compatibility. No simulator/aircraft/payload qualification bypass.

## Verification and failures retained

PowerShell 7 command:

```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-namibia-10
```

Local logs/TRX are in ignored `.ci-output-namibia-*` / `.ci-output-namibia-logs`. Run 01 found a mispatched constructor call; 02 found missing source metadata on the held Nepara record; 03 found null/unconfirmed identifiers incorrectly treated as duplicate ICAOs; 04/05 exposed old approval-only fixture expectations and source/held geographic fixtures; 06 found a missing namespace import; 07/08 narrowed remaining fixture/expectation failures. Fixed, without skipping tests or weakening physical/flight/payment assertions. Run 09 passed 1,004; run 10 passed 1,005 after adding persisted-cadence coverage. Updated former FYNG ordinary-route fixtures to an unheld field; explicit held tests now cover FYNG and the other conflicts.

No live MSFS operation, Package Tool, scenery installation, personal save migration, new local UI/CLI executable run or airport acceptance occurred.

## Remaining / exact next steps

1. Commit/push the coherent implementation and this scope. Create one draft PR to master; identify Career Hub/PR #10 integration ancestry. No automatic merges. Inspect before repeating external actions.
2. Inspect hosted Windows CI on the exact commit. Both WPF programs, verification guards, historical CLI check and packaging are required. Fix any failure and retain diagnostics. Do not run SiteUiSmoke against personal simulator configuration.
3. Download/inspect synthetic airport verified/problem screenshots and publish traceable Windows artifact/evidence links. Update this handoff with exact source commit/counts and remaining limitations.
4. Matt's real playtest uses [the procedure](../namibia-playtesting.md). Airport usability is not proven by tests. Autonomous live execution requires exact-artifact/scenario approval.

## Limitations

Safari passenger profiles, SF50 physical boarding and H125/platform missions remain restricted. Reports cannot repair technical identities or move route coordinates. Bad-airport accepted-job cancellation/re-routing/disposition remains future work; no incomplete payment/refund is invented. Active employer switching remains unsupported. GitHub PR-write credentials may still block PR creation; normal branch push and hosted Windows push verification are available.
