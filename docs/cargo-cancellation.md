# Feature: Cargo return and free playtest cancellation

Status: Approved
Approved by: Matt, 2026-11-21. Return cargo and cancel; latest clarification: no penalty anywhere while playtesting because bugs may require cancellation.

## Objective
Recover from unwanted or unusable cargo jobs without deleting cargo records, changing personal saves by hand or disturbing other consignments.

## Gameplay behavior
In Career → Fleet → Load Manager, each supported accepted cargo job has Cancel job. At origin, loaded cargo also has Unload at origin, which leaves the job accepted and reserved for another attempt. Cancel job opens an inline explanation with Keep job and a confirmation action.

Loaded cancellation uses the existing outside confirmation → cockpit apply → retained station readback workflow. Only after successful physical removal does one economy revision cancel the job, release its reservation and update the manifest. At origin the remaining consignment returns to stock. Elsewhere only reserved stock still at origin is released; physically unloaded cargo is recorded as abandoned, never as a delivery. There is no monetary penalty anywhere during playtesting and no completion pay for the cancelled job. Already delivered quantities and previously settled unrelated wages are preserved.

An empty consignment requires a fresh stopped, bound-aircraft observation at a known location, but no payload write. Existing aircraft permissions and ownership checks remain. An interrupted unfinished cargo leg is retained as aborted without arrival credit; remaining cargo needs a new leg. Preparing a cancellation already aborts that incomplete leg; discarding the unapplied unload does not restore flight proof. Qualified historical legs remain recorded.

## Acceptance criteria
- Selected cargo can be returned at origin without unloading unrelated cargo.
- Cancellation removes only its consignment, restores only appropriate stock and awards neither completion pay nor a penalty.
- Pending unloads survive restart/failure; repeated actions cannot duplicate removal or stock release.
- Aircraft identity, unrelated payload, fuel, passengers and readback guards remain effective.
- Inline controls explain intent; committed manifest and job board refresh after successful handling.

## Non-goals
Passenger cancellation, player-company contract cancellation, rerouting, reputation penalties, aircraft transfers, automatic payment or fault resets. No changes to hourly rates or airport suitability policy.

## Relevant existing systems
`CargoJourneyService`, `CargoJourneyKernel`, `EconomyWorld`, `LocalEconomyStateStore`, `CargoJourneyViewModel` and the existing `CargoJourneyControl`. `CargoCancellationKernel` adds the atomic terminal transaction and audit rather than a second loader.

## Persistence implications
Economy schema 17 adds optional job cancellation receipts, cancellation operation/history metadata and aborted-leg timestamps. Older supported documents receive defaults through the existing validated atomic migration path. Corrupt/future saves remain preserved and refused. Receipt validation checks stock release, terminal state, location and physical unload history. No personal save migration has been performed during development.

## Required tests
Origin return, remote/full/partial/never-loaded cancellation, other cargo preservation, partial deliveries, failed writes/restart/retry, no double stock or pay, interrupted legs, identity/conflict refusal, audit tampering and schema-16 compatibility. Hosted WPF drives real controls with synthetic payload.

## Simulator testing requirements
Pending: approved exact Windows artifact; stopped bound aircraft at known origin with multiple consignments. Return/cancel one load, compare station readbacks/fuel/other consignment, restart and retry; separately cancel away from origin and inspect inventory/no payment. No flight or personal cancellation is authorised merely by compiling this feature. Existing C152 loading evidence does not qualify new cancellation/unloading behavior.

## Open design decisions
None blocking this playtest slice. Any future cancellation penalty needs a new product decision.
