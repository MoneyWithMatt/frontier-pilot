# Feature: Airport-level verification and pilot responsibility

Status: Approved
Approved by / date: Matt's explicit refinement request, 2026-11-03.

## Objective
Offer plausible curated airport missions without deciding aircraft/runway performance for the pilot. Ask about simulator representation only after a successful mission and settlement.

## Gameplay behavior
Airport verification is shared across aircraft. Aircraft identity in an observation is context, never airport eligibility. Historical endpoint aircraft lists and AircraftUnsuitable reports cannot prevent offers or dispatch. Company fleet/type identity, physical loading, flight proof, once-only wages and technical coordinate holds remain authoritative.

After an airport cargo delivery or personnel disembarkation is completed and settled, Career shows one optional inline first-visit prompt: Yes — Verify airport / Report a problem / Skip for now. No popup, acceptance gate or economy mutation. Already verified, technically held or already reported problematic airports do not prompt. Skip persists one airport acknowledgement, does not create a verification record, and suppresses repeat interruptions; manual reporting/verification after completed work remains available. Existing completed records may supply a missed prompt after restart.

Manual simulator-problem reporting remains available before completion. Categories describe missing/broken airport representation, incorrect location and simulator/scenery malfunction, never aircraft performance. Reports withhold new offers while preserving accepted custody, history and wages. No automatic cancellation or rerouting.

## Acceptance criteria
- Unverified destinations generate normal supported work.
- Runway dimensions, surface, terrain, historical aircraft lists and unsuitable-aircraft reports cannot exclude company types.
- No prompt at acceptance, loading, arrival proof or partial delivery; prompt only from valid committed completion/ledger state.
- Yes, problem and skip do not touch economy or flight evidence; repeated answers cannot duplicate pay or reviews.
- Airport-level verification, skip acknowledgements and technical holds survive restart safely.

## Non-goals
No mission cancellation/rerouting, new physical aircraft capabilities, flight controller, performance approval or personal-save/live simulator operation.

## Relevant existing systems
EmployerDestinationCatalogue, CompanyAircraft, AirportVisitStore, CareerHub composition and authoritative cargo/passenger/ledger services.

## Persistence implications
Airport visits schema 2 reads schema 1 without rewriting on load, preserving every observation and aircraft metadata. Adds idempotent per-airport prompt dismissals in the same atomic, bounded document. Old AircraftUnsuitable entries remain readable but advisory only. Economy schemas and balances unchanged. Corrupt/future records preserved. No personal data migration during development.

## Required tests
Airport-level compatibility, legacy review migration, optional prompt timing and all answers, duplicate prevention, unchanged payments, manual reports/accepted custody, WPF actual controls and full Windows pipeline.

## Simulator testing requirements
No live simulator required to verify review timing/persistence. Synthetic tracker/WPF tests cannot establish airport usability or physical payload compatibility. Matt's actual visits provide that separate evidence.

## Open design decisions
None blocking; pilot responsibility and post-completion optional reporting explicitly approved.
