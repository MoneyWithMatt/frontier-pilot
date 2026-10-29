# Handoff: offshore-career

- Approved specification: [OFFSHORE](../specs/offshore-career.md), Matt's 2026-10-29 autonomous development request; further mission design remains pending.
- Branch: `codex/offshore-career`, based on merged Phase 1 master `d5ff51650789e45e46c8828893acaba071d957e7`.
- Last validation: local dirty source based on that master; exact implementation SHA and hosted evidence to follow.
- Status: airport-cargo fixes implemented and local tests passed; preparing PR/hosted checks. No merge authorized.

## Completed work

Inspected implementation, current master and merged PR #1. Researched official SDK facility/deck/relative-motion capabilities. Corrected base+destination aircraft intersection and actual-origin route-time estimate. Added seven Offshore regressions, including persisted grouped partial delivery, proof/restart and exactly one wage. Published assessment: [Offshore career](../offshore-career.md).

## Tests and evidence

- `.tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --filter FullyQualifiedName~OffshoreCareerTests --logger 'trx;LogFileName=offshore.trx' --results-directory .ci-output-offshore-focused`, with repository `DOTNET_CLI_HOME`/`NUGET_PACKAGES`: 7 passed, 0 failed/skipped.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-offshore-local`: SDK 8.0.424; Release solution 0 warnings/errors; 751 passed, 0 failed/skipped. Source dirty on `d5ff51650789e45e46c8828893acaba071d957e7`; seven added cases above the 744 baseline. Local TRX `.ci-output-offshore-local/tests/regression.trx`.
- Hosted smoke/package checks pending. No MSFS, Package Tool, local WPF/published application, Community or personal save was used. Tracker transfer tests use synthetic supplied values, not a native simulator adapter.

## Blockers and human input

Next gameplay slice needs Matt's choice: H125 platform cargo, survey/inspection with return, or separate passenger transfers. A question was sent during this task; do not assume an unanswered default is approval. Platform endpoints, loiter rules and passenger rules are unspecified. Live qualification and changed published artifact execution require approval.

GitHub connector issue creation returned HTTP 403 (integration cannot access resource); no issue was created by that attempt. Use configured Git authentication/API fallback if available; do not duplicate a successful later action.

## Exact next steps

1. Inspect status/HEAD and this handoff before resuming. Preserve pending edits; check any existing Offshore PR/issue before creating another.
2. Commit/push this coherent slice, create draft PR targeting master and attach it; complete hosted Windows CI and fix failures without weakening assertions.
3. Record exact head/merge SHA, run URL, counts and artifacts; update roadmap/task/handoff and push evidence. Human review/merge remains separate.
4. On mission choice, write the concrete gameplay spec and identify any remaining material decisions; continue technical work through hosted tests. Before a live check, obtain exact artifact/scenario approval.
