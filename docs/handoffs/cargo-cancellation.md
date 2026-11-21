# Cargo cancellation handoff

## Objective and authority
Implement Matt's approved origin return and cancellation, with latest clarification: no penalty anywhere during playtesting. See [specification](../cargo-cancellation.md).

## Branch / scope
`codex/cargo-cancellation`, based on `d7332f7` of `codex/namibia-playtesting`. Stacked on existing Career Hub, cargo, passenger and Namibia development; do not merge dependencies or master automatically. Personal save untouched by feature work. Previously reported Dordabis missing remains recorded separately; existing C152 consignments remain aboard in the installed older build.

## Completed
Atomic stock/cancellation receipt, existing physical unload/recovery integration, origin-return action, inline cancellation preview and abort history for interrupted cargo legs. Schema 17 compatibility; regression and hosted WPF coverage added. Cargo/passenger mutual exclusion remains enforced. Player-company contracts and passenger cancellation remain outside scope.

## Validation checkpoint
Implementation uncommitted during development; exact tested commit/run to be recorded after hosted Windows verification. Local BuildTest early run 02: 1035 passed / 0 failed / 0 skipped. Later additional coverage and UI changes invalidate this as final evidence.

Preserved development failures: run 01 test null assigned to nonnullable fake failure counter; run 03 test used nonexistent inventory enum; run 04 migration test incorrectly expected byte-identical schema-16 JSON despite existing atomic migration behavior. Separate WPF compilation caught incorrect catalogue property. Fixes retain production assertions and check semantic save preservation. Output retained in untracked `.ci-output-cancel-*` paths; no personal data included.

## Remaining / exact next steps
1. Finish full regression and compile WPF smoke; review diff and commit.
2. Push feature branch; run hosted Windows CI build/xUnit/guards/WPF/package. Inspect results/screenshots and fix any failures.
3. Create draft PR to master through scoped GitHub connector; previous sessions saw 403 for this mutation. Do not bypass credentials or change repository settings. If unavailable, preserve review-ready body and compare link.
4. Record exact commit, CI evidence and development artifact. Local installation/execution requires exact artifact approval under AGENTS.md; do not launch new binaries or cancel personal cargo automatically.
5. After approval, conduct stopped-aircraft live unload/cancellation acceptance. Synthetic tests never prove MSFS unloading.
