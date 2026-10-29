# Multi-stop cargo implementation handoff

## Objective / authority
Matt explicitly authorized full implementation following PR #5, on a separate branch and draft PR targeting master. [Approved scope](../specs/multistop-cargo.md); [implementation, limitations and live procedure](../multistop-cargo.md). No merge. No local UI/published executable or simulator execution authorized by test/build permission.

## Branch and implementation
`codex/multistop-cargo`, isolated managed worktree. Base PR #3 `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`; copied PR #5 research from `98d9ace`. Original branches/worktrees are preserved. Implementation milestone: `a9ab547` (production/core/tests). Documentation checkpoint follows. PR/CI links will be recorded after they exist.

Implemented production selection/loader/manifest, schema-13 consignment/airframe ownership, durable absolute targets, immediate guarded native adapter reuse plus retained readback, partial/restart reconciliation, independent resources/destinations, intermediate pickup and leg proof/wage receipts. Legacy loading/attempt/fleet schemas stay intact. Core/production guards block legacy commands against managed cargo and unsafe mixed passenger ownership. Pilot policy removes app gross/overall-payload/CG/prototype-budget loading refusal while enforcing actual station/readback/integrity constraints.

## Verification checkpoint
Last validated source: base `f7df3ff` **plus dirty implementation**, local `.ci-output-multistop-03` full Release solution build and xUnit: **809 passed / 0 failed / 0 skipped**, zero build warnings/errors, SDK **8.0.424**. Later UI navigation/documentation edits and WPF smoke source require final exact-commit hosted validation. New WPF smoke compiled with zero warnings/errors; not run locally. No packaging receipt or live acceptance claimed yet.

Exact local commands (worktree root; SDK comes from existing developer checkout):
```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-multistop-03
# .tools/home and .tools/nuget set by entry point.
# Focused tests use dotnet test tests/Tests -c Release --no-restore --filter FullyQualifiedName~CargoJourneyTests.
# Compile-only WPF smoke: dotnet build tests/SiteUiSmoke -c Release.
```

Preserved failures: `.ci-output-multistop-01` built cleanly but had **789 pass / 5 fail / 0 skip** because existing assertions enforced the explicitly superseded mass policy. Replaced them with positive advisory behavior while retaining identity/station rejection coverage. Initial new test compilation found target-typed `new` ambiguity in params calls and a nonexistent helper; fixed production-independent test plumbing. Focused 11-case run passed before four more cases were added; full later 809 run passed. `.ci-output-multistop-02` and `-03` each passed 809/0/0. No weakened checks/skips or lost failure directory.

Additional local checkpoint `.ci-output-multistop-04`: **810/0/0**, zero warnings/errors, after adding failed-economic-replacement recovery. Final focused 16-case cargo run and WPF smoke compile passed. Implementation commit `a9ab547` includes later station-readback receipts/origin/arrival validation and will receive exact-commit hosted checks.

## Remaining / exact next actions
1. Commit implementation milestone and updated documentation. Run final local regression after source review.
2. Push only this new branch; create/attach new draft PR to master, explicitly noting included PR #3 baseline and unchanged original PRs. Run hosted Windows CI using established workflow: BuildTest, nine guard scenarios, both WPF programs, Package and uploads. Review failures and fix normally. Inspect exact clean commit receipts, TRX and fax screenshots; preserve links/failures. Update this handoff/PR and task #2 with the actual outcome.
3. No live simulator action without exact artifact/scenario approval. Follow the focused live procedure and record aircraft/SDK/simulator/config hashes. No aircraft has live acceptance for this new flow.
4. Mixed transport remains disabled until shared accounting/recovery and live qualification; SF50 passenger config remains blocked as documented in PR #3. Platforms, H125 cargo, survey/inspection, aircraft safety/performance worksheet, fares/medical simulation and print/PDF remain deferred. Existing saves are preserved; no personal data, installed packages or simulator were touched.
