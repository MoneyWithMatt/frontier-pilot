# Feature: OFFSHORE — career completion

Status: Approved technical investigation/airport-cargo hardening; passenger transfer follow-up approved in [OFFSHORE-PASSENGERS](offshore-passenger-transfers.md).
Approved by / date: Matt's 2026-10-29 instruction to prioritize Offshore, investigate capabilities, implement/test autonomously and maintain GitHub continuity. Material gameplay changes and live simulator testing require further input.

## Objective
Complete Skeleton Coast Petroleum's career through the existing economy, flight-evidence, loading and fleet systems. First establish the implemented airport-cargo path, then select and specify the next mission lifecycle.

## Gameplay behavior
Current approved behavior: FYWB base, Vision Jet airport cargo, $800 per verified airborne hour paid on successful delivery, existing prototype cargo/dispatch budgets. H125 remains disabled. No passenger or survey mission lifecycle exists.

This slice fixes two defects: base aircraft restrictions now participate in route eligibility/aircraft choices, and job planning time uses the actual origin instead of an Eros reference. Empty aircraft restrictions still mean unassessed, not certified suitability. Disjoint endpoint restrictions block new offers and acceptance. Existing reservations/history remain intact.

## Acceptance criteria
- Offshore jobs originate at resolved FYWB, never FYWB to itself; only enabled company aircraft can be selected.
- New offers require a common enabled company type satisfying both endpoint reviews and the existing dispatch budget.
- Job details calculate direct time from the job origin at the existing 120 kt planning reference, explicitly labelled as an estimate.
- Later missing, ambiguous or moved FYWB lookup blocks new offers but preserves accepted endpoints, assignments and money.
- A synthetic grouped Offshore flight supports partial unloading, persisted proof/restart, and exactly one $800/hour wage.
- Full regression, Release build and GitHub-hosted Windows smoke/package CI pass without simulator dependencies.

## Non-goals
No new platforms, inferred H125 compatibility, mission payouts, passenger-as-cargo substitution, survey timers, scenery installs, native fault/position writes, gameplay thresholds or merge.

## Relevant existing systems
`CompanyEmployment`, `EmployerDestinationCatalogue`, `EmployedCargoJobsService`, `FlightAttemptTracker`, existing payload coordinator, `SimulatorAirportStore`, `PilotWage` and local economy/attempt stores. See [capability assessment and staged tasks](../offshore-career.md).

## Persistence implications
No schema change. Origin aircraft restrictions are a derived diagnostic property. Previously recorded reviews/cache coordinates retain their existing authority; blocked routes do not cancel accepted cargo or rewrite historical endpoints.

## Required tests
Offshore dispatch/assignment, endpoint intersection/disjoint sets, base exclusion and acceptance refusal, missing/ambiguous/moved base after acceptance, route-time display, persisted grouped transfer/partial delivery/restart/wage idempotency. Existing complete suite and hosted WPF smoke remain required.

## Simulator testing requirements
No simulator required for this implementation slice. Synthetic transfer tests call the real tracker/economy boundary with supplied values; they do not test native payload writes, H125 behavior or live flight acceptance. Follow the separately approved live procedure before activating platform work or executing changed published artifacts locally.

## Open design decisions
Matt selected passenger transfers in the 2026-10-29 follow-up; see the approved linked spec. H125 platform cargo and survey/inspection are deferred. Platform activation requires live qualification. The historical example of a 30-minute loiter is not an approved requirement.
