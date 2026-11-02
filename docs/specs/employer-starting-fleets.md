# Feature: Persistent employer-owned starting fleets

Status: Approved implementation scope; live acceptance separate.
Approved by: Matt's attached implementation request, 2026-10-29.

## Objective
Joining a starting employer automatically assigns one persistent individual aircraft per catalogue fleet type, without a purchase or manual registration. Company property is separate from player assignment and personal finances.

## Gameplay behavior
The existing career acceptance provisions aircraft in the existing physical fleet. Startup repairs missing allocations idempotently. Select an assigned aircraft, load its supported type at the allocated/saved location, and confirm the exact simulator type through existing Settings. This does not spawn, teleport or qualify aircraft capabilities. Disabled types are allocated but remain disabled. Unknown historical hours/condition remain unknown; actual observations and existing qualified adapters supply updates.

Repeated acceptance of the same employer is idempotent. Different-employer changes remain unavailable under the existing career/economy policy; allocation services preserve and reuse historical company/player records for future authorized transitions. No new career-switch/reset mechanic is introduced.

## Acceptance criteria
- All four employers receive exactly their actual catalogue types, company-owned and assigned to a durable local player ID.
- Restart/retry/repair does not replace identities, registrations, hours, location, maintenance or history.
- Separate players have distinct allocation IDs/registrations; no global server uniqueness claim.
- Fleet selection/type/location authorization works with existing cargo/passenger guards and preserves unfinished capability gates.
- Existing ambiguous records are retained and require explicit in-app assignment/type reconciliation rather than duplication or guessed ownership.

## Non-goals
No purchase/net-worth entries, automatic MSFS placement, new relocation mechanic, employer-switch UI/economy migration, condition fabrication, wear/fault qualification, multiplayer server or enabling H125/platform/unsupported passenger profiles.

## Relevant existing systems
`CareerStore`, `CareerCatalogue`, `CompanyAircraft`, `PhysicalAirframe`, `AircraftFleetStore/ViewModel`, condition/exposure/location accumulators, dispatch guards, passenger transport and multi-stop cargo from PRs #3/#6. Existing architecture remains authoritative.

## Persistence implications
Career schema 3 adds a durable local player GUID; schema-2 employed profiles receive one under a cross-process lock, preserving employment and exact type bindings. Fleet schema 2 adds ownership, assignment, allocation receipt/base/type label and last tracker-qualified transport stop. Schema-1 records remain readable; ambiguous legacy data is never adopted silently. The economy/ledger and loading/attempt stores are unchanged. Acceptance saves identity/employment before fleet provisioning; restart or same-employer retry repairs the latter safely.

## Required tests
All employer definitions, restart/idempotency, 100 local player allocations, uniqueness, ownership/assignment filtering, employer transition boundary, migration/legacy ambiguity, history/maintenance/location/exposure preservation, corrupt/future preservation, type/location/disabled guards, disconnect/reconnect, cargo/passenger loading and actual onboarding/fleet WPF controls. Established full Windows verification/package pipeline.

## Simulator testing requirements
Separately approve exact artifact and isolated live scenario. See [implementation/live procedure](../employer-starting-fleets.md). Mocked identities/payload/condition observations and WPF screenshots are not live MSFS acceptance.

## Open design decisions
None blocking this scope. Active employer changes remain prohibited; future changes need coordinated economic/mission authority, not a fleet reset. FP registrations are unique local dispatch identifiers, not proof of a civil registry or native simulator tail number.
