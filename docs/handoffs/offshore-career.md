# Handoff: offshore-career

- Approved specification: [OFFSHORE](../specs/offshore-career.md), Matt's 2026-10-29 autonomous development request; further mission design remains pending.
- Branch: `codex/offshore-career`, based on merged Phase 1 master `d5ff51650789e45e46c8828893acaba071d957e7`; [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3), [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2). Reuse these; do not duplicate them.
- Last validated implementation commit: `961b1dcba55d2ac6c62a651854cca83b1835e08a`; tested clean merge `402392b88805d506587944d45165694076a78bda` in successful [hosted run 6](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40850041388).
- Status: airport-cargo slice ready for review; remaining career mission design awaits human input. Documentation-only evidence checkpoint follows; inspect its latest CI in task #2 / PR #3. No merge authorized.

## Completed work

Inspected implementation, current master and merged PR #1. Researched official SDK facility/deck/relative-motion capabilities. Corrected base+destination aircraft intersection and actual-origin route-time estimate. Added seven Offshore regressions, including persisted grouped partial delivery, proof/restart and exactly one wage. Published assessment: [Offshore career](../offshore-career.md).

## Tests and evidence

- `.tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --filter FullyQualifiedName~OffshoreCareerTests --logger 'trx;LogFileName=offshore.trx' --results-directory .ci-output-offshore-focused`, with repository `DOTNET_CLI_HOME`/`NUGET_PACKAGES`: 7 passed, 0 failed/skipped.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-offshore-local`: SDK 8.0.424; Release solution 0 warnings/errors; 751 passed, 0 failed/skipped. Source dirty on `d5ff51650789e45e46c8828893acaba071d957e7`; seven added cases above the 744 baseline. Local TRX `.ci-output-offshore-local/tests/regression.trx`.
- Final committed implementation: hosted 751 passed / 0 failed / 0 skipped, Release 0 warnings/errors, nine verification-guard scenarios, both WPF smoke programs, publish and both artifact uploads passed. [Exact commands/source/artifact evidence](../offshore-career-verification.md).
- No MSFS, Package Tool, local WPF/published application, Community or personal save was used. Tracker transfer tests use synthetic supplied values, not a native simulator adapter.

## Blockers and human input

Next gameplay slice needs Matt's choice: H125 platform cargo, survey/inspection with return, or separate passenger transfers. A question was sent during this task; do not assume an unanswered default is approval. Platform endpoints, loiter rules and passenger rules are unspecified. Live qualification and changed published artifact execution require approval.

GitHub connector writes returned HTTP 403. Configured Git authentication/API fallback successfully created issue #2 and draft PR #3, and the PR is attached to the task. Do not repeat creation. Ignored local `.ci-output-offshore-api/github.ps1` supports explicit metadata updates, without logging credentials; it is not a tracked runtime feature.

## Exact next steps

1. Inspect status/HEAD, task #2 and PR #3 before resuming. Preserve pending edits. Implementation/run-6 actions above are complete.
2. Check the evidence-checkpoint CI if not already recorded as successful in task #2 / PR #3. Fix genuine failures without weakening assertions; do not repeat already successful checks without an invalidating change.
3. Keep this single feature branch/PR for the coherent Offshore work. Human review/merge remains separate.
4. On mission choice, write the concrete gameplay spec and identify any remaining material decisions; continue technical work through hosted tests. Before a live check, obtain exact artifact/scenario approval.
