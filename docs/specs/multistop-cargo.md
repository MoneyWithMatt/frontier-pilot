# Feature: Multi-stop cargo logistics and dispatch manifest

Status: Approved implementation scope; simulator acceptance pending.
Approved by: Matt's explicit implementation instruction, 2026-10-29.

## Objective
Accept several jobs, choose individual consignment quantities, carry different destinations together and continue after independent deliveries/pickups. Frontier tracks logical resources/jobs; MSFS receives aggregate station mass. Pilot controls weight, balance and performance.

## Gameplay behavior
10 kg A + 25 kg B loaded at origin; deliver A, retain B; optionally collect a new accepted consignment at A; continue to B and settle each job independently. Each planned/armed leg has its own immutable carried snapshot and tracker evidence. Wages use the existing employer rate once per eligible leg after successful full delivery, never once per consignment or merely for arrival. Earlier through-legs release with the completed consignment. Freelance/player contract rewards retain their existing semantics.

The existing outside-avatar authorization and seated verification workflow remains. Explicit preview before writing; no automatic restore or adoption of manually edited mass. New jobs can be accepted in flight but their cargo cannot inherit evidence before loading. Station limits/interfaces, stock, ownership, identity and readback are hard constraints. MTOW, overall payload, CG and company prototype mass budgets are advisory.

## Acceptance criteria
- Production cargo control and live fax manifest support the concrete 35 → 25 → 0 kg journey and an intermediate pickup.
- Independent explicit quantities, resources, inventory credits, completion and idempotent settlement.
- Native immediate comparison plus retained readback, safe restart/partial-write recovery.
- Regression, WPF control interaction, packaging and separately recorded simulator acceptance.

## Non-goals
No merge; no medical simulation, airline fares, industry redesign, new platform enablement, H125 cargo, survey/inspection, automatic flight approval or performance worksheet. Mixed transport remains a coordinated follow-up until safe shared reconciliation is proven. No print/PDF requirement delays working logistics.

## Relevant existing systems
PR #5 [investigation](../research/cargo-logistics/report.md), PR #3 shared passengers, `EconomyKernel`, `FlightAttemptTracker`, payload/native adapters, GUID fleet, existing cargo recovery and ledger.

## Persistence implications
Economy schema 13 adds per-airframe consignment ownership, absolute operation intent, handling receipts, leg snapshots/proof and wage settlement. Schema 1–12 saves retain jobs, inventory, money, cargo, passenger state and history. No inferred migration of legacy mass or airframe identity. Legacy loading/attempt/fleet store schemas stay unchanged.

## Required tests
Multi-resource/multi-destination/partial quantities, pickup, intermediate independent unloading, stock, restart, disconnection, partial setter, duplicate action/payment, station refusal/clamp, manual conflicts, overweight/CG advisory behavior, mixed ownership exclusion, save compatibility and manifest consistency. Production WPF controls exercise the full base sequence using synthetic data.

## Simulator testing requirements
Use [focused live acceptance](../multistop-cargo.md#live-simulator-acceptance). Exact executable/scenario approval precedes execution. Record aircraft/add-on/config/simulator/SDK versions and hashes, real station/gross readback, seated retention and fuel/unrelated mass. Mocked tests cannot qualify aircraft behavior or visible models.

## Open design decisions
None blocking this scope. Mixed transport, cancellation/off-route disposition and empty repositioning wages are not newly activated by this slice; preserve cargo/proof and report those limitations explicitly.
