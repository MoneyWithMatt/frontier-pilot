# Feature: offshore-platform-payload-diagnostic — fixed OL400 ballast cycle

Status: Approved fixed scenario executed successfully on OL400 / H125, 2026-11-02. This is diagnostic acceptance only.
Approved by / date: Matt's continued autonomous offshore proof request, 2026-11-02. Matt separately approved exact run56 artifact and the 5 kg physical scenario after the explicit approval request; see execution receipt in offshore-platform-presence-v1.md.

## Objective
Prove actual H125 payload can change while stationary on the known platform, without requiring a human helicopter flight or inventing mission evidence.

## Gameplay behavior
No production UI/career change. A CLI diagnostic adds and removes 5 kg test ballast on OL400, using actual station index 6 / Cargo 1 inspected in MSFS. It is not a delivered resource or passenger.

## Acceptance criteria
- Actual H125 Passengers / H125, inspected nine-station layout, OL400 H01 at -22.93892875313759, 14.50461745262146, deck 39.89600372314453 m MSL.
- Twenty continuous seconds of fresh unpaused deck presence before each operation.
- Reviewed Cargo 1 baseline 99.7903214 kg (tolerance 0.05 kg), target baseline + 5 kg, then original baseline. All other stations preserved.
- Immediate and three-second retained readback; total-weight change consistent with +/-5 kg and observed normal fuel burn. No fuel write.
- Intent durable before writes, failure explicitly unresolved. No automatic recovery or repeat command.

## Non-goals
Flown legs, departure qualification, production jobs, rewards, physical GUID assignment, passenger capacity/models, shared mixed loads, generic aircraft/station setter UI. No changes to PR #3 or cargo-loader branches.

## Relevant existing systems
Core.ExperimentalPlatformPresence; Workflow.ExperimentalPlatformPayloadCycle; existing IAircraftLoadingSimulator / SimConnectAircraftLoading; App CLI. Existing native setter units/conversion are reused, not guessed.

## Persistence implications
Create-only private diagnostic JSONL. No save migration or economy/flight/store access. Unresolved journal cannot authorise removal: fresh actual-state inspection and a separately reviewed recovery action are required. Manual changes cannot be safely distinguished from identical values; no concurrent changes permitted.

## Required tests
Synthetic nominal cycle; partial/refused/reset writes; conflicts; disconnect; duplicate prevention; wrong aircraft/layout/baseline; unrelated payload preservation; gross warnings advisory. Existing Windows regression, WPF smoke, guards and packaging remain required.

## Simulator testing requirements
Download/hash exact successful hosted CLI, obtain artifact/scenario approval, freshly inspect PC/aircraft and ensure no competing loader. Resume through supported UI, execute once with a new private journal, review both physical deltas and retained state, pause again. Stop on any mismatch; do not improvise rollback. Native start is not a flight. Existing adapter has a non-atomic check/write and no same-session aircraft pin: controlled diagnostic only, not production ownership acceptance.

## Open design decisions
None for this fixed diagnostic. Integration with actual mission lifecycle remains later work; no new wage or flight-proof policy is introduced.
