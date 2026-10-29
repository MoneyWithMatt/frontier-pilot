# Feature: task 2 — shared passengers and physical payload

Status: Approved objective; implementation in draft PR #3, live acceptance pending.
Approved by/date: Matt's 2026-10-29 instruction to generalise the passenger transfer, integrate actual payload and demonstrate Missionary reuse.

## Objective

Use one passenger carriage capability for Offshore, Missionary and future independent charter/airline sources. People must affect the actual aircraft payload before boarding completes; disembarkation removes only the transfer-owned load. Mission purpose, qualified flight and settlement stay separate.

## Gameplay behavior

Reserve a group, enter one declared kilogram weight per person, qualify the existing origin stationary observation, board through an evidenced aircraft profile and verified simulator readback. Fly using existing proof/time rules, explicitly check payload after connection recovery, qualify arrival and unload. Wages remain $800/hour Offshore and $400/hour Missionary. A two-person doctor/community-liaison visit uses existing Missionary catalogue eligibility; it does not certify a clinic or provide medical gameplay. Unknown aircraft capacity never becomes a guessed seat count.

## Acceptance criteria

- Shared person/group identity, weight, reservation, boarding, carriage, arrival and disembarkation independent of employer/aircraft/pay.
- Configuration-backed seat areas, station allocation, limits and exact actual aircraft / selected physical airframe checks.
- Durable absolute targets/ownership before writes, immediate and retained readback, explicit partial/conflict/restart recovery, no duplicate mass or settlement.
- Fuel and unrelated occupants/cargo preserved; interim Frontier assignment mixing remains an explicit workflow restriction, absent from shared domain validation.
- Legacy simulated saves retain their historical behavior and never acquire invented payload ownership.
- Physical, seated-model and animation capabilities reported independently; unavailable visuals cannot block physical transport.

## Non-goals

Airline management/fares, new medical simulation, invented destination facilities, guessed stock seating, external NPC substitutes, platform activation, H125 cargo or survey/inspection. No merge or live executable/scenario approval is implied.

## Existing systems and persistence

Reuse `IAircraftLoadingSimulator`, native unit conversion, snapshots, configuration evidence, selected fleet dispatch, `FlightAttemptTracker.PassengerMonitor`, employer catalogue and atomic ledger. Economy schema 12 adds default-empty shared `PassengerTransports`; employed assignments reference the carriage and retain flight/wage fields. Cargo/loading/fleet schemas stay unchanged. See [implementation, evidence and acceptance procedure](../shared-passengers.md).

## Required tests and simulator testing

Multiple people/capacity, station/aircraft/limit compatibility, unrelated payload preservation, partial/reverted writes, pre-write manual edits, restart/read-only reconciliation, concurrent/idempotent board/unload/payment, legacy saves, both career adapters and actual WPF controls. Run full Windows regression, both WPF programs, verification guards and packaging on hosted CI. Mocked source profiles are never production qualification. Live tests require approval for the exact executable and scenario with isolated disposable data and aircraft/version/hash evidence.

## Open decisions / blockers

No unresolved gameplay choice blocks this implementation. Vision Jet effective seat/station configuration is unavailable in readable installed sources. No seated-passenger control established. Per-station maxima and planned CG are unavailable through the current native adapter; enforce all available reported/configured limits, surface missing values and require pilot aircraft-limit assessment/live qualification.
