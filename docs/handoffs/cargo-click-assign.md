# Handoff: cargo-click-assign

## Objective / source
Approved 2026-11-02 cargo-card brief and three-state reference. Preserve Frontier's theme and existing cargo service; blue selection / green proposed pairing / actual confirmed aboard after readback. [Scope](../specs/cargo-click-assign.md). Continue isolated `codex/guided-cargo-loading`, [draft PR #8](https://github.com/MoneyWithMatt/frontier-pilot/pull/8); master/other branches untouched, no merge. Starting clean head `6aa3947e4354fd0d21800dc2a61157406d3f3817`, remote inspected, PR open/unmerged/draft. Engine investigation explicitly deferred by Matt; issue unresolved, no further simulator actions authorized by this UI task.

## Implementation
Cargo/station cards use existing App.xaml resources, native focusable buttons, accessible state text/names, persistent visibility of actual stations in numeric order, separate proposed/aboard labels, automatic mass totals, Change/Remove, contextual partial amounts and shared Fleet dropdown. Existing right-hand fax sheet is preserved with a clearly labelled local unsaved-plan appendix. No service, schema, pay or simulator authority changes. Persistent pending aggregate intent stays authoritative across restart; no invented station geometry or persistent per-consignment station association.

## Verification checkpoint
Dirty development BuildTest `.ci-output-cargo-cards-01`: existing 864/0/0, clean Release build. After five card regressions, dirty `.ci-output-cargo-cards-02`: 869/0/0, clean Release zero warnings/errors. WPF smoke project compiles; execution remains hosted. Final source checks, hosted WPF/guards/package and screenshot review pending. Local initial editing script failed Unicode encoding before any test; restored only its own changed file from clean HEAD and reran with explicit UTF-8. No user work lost or test assertions weakened.

Exact local command: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-cargo-cards-02`.

## Remaining / next
Finish source review, final clean committed full suite, hosted Windows WPF/guard/package checks, inspect all three selection screenshots and fix any failures; keep exact run/head/merge/artifact evidence in PR. Update this note when complete. Do not replace/run the local distribution, use the simulator, copy personal data or merge. New executable approval is separate from the old run-38 approval. Prior cargo loading evidence belongs in [guided handoff](guided-cargo-loading.md); this revision has no live simulator acceptance.
