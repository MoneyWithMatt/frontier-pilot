# Feature: Career aircraft relocation recovery access

Status: Approved scope, implementation verification pending
Approved by / date: Matt requested real loading test and approved guarded preview recreation, 2026-11-04. Exposing an existing recovery action is a routine integration repair; new executable execution remains separately approved.

## Objective
Restore access to the existing fleet relocation acknowledgement from Career → Fleet → Aircraft. Live cc74814 testing found that loading correctly refuses a flagged external move, but Career Hub hides the legacy fleet tab containing the required acknowledgement.

## Gameplay behavior
Show Review external aircraft move only for a selected airframe with LocationReconciliationRequired. Reuse AircraftFleetPanel's exact existing confirmation and AircraftFleetViewModel.AcknowledgeRelocation authority. Keep stopped/fresh/type/ownership validation, uncertain position, explicit unobserved history and no flight/pay authority. Display source status when recovery refuses. This is developer/testing recovery, not normal transport or automatic relocation.

## Acceptance criteria
- Action visible only while selected aircraft needs location reconciliation.
- Existing confirmation, current selection and authoritative fleet service are reused.
- No simulator placement, cargo movement, flight proof, healthy reset or wages are created.
- Selected physical GUID and loading workspace remain unchanged.

## Non-goals
No new position adapter, tolerance changes, automatic reconciliation, save edits, or passenger visual implementation.

## Relevant existing systems
CareerHubControl, AircraftFleetPanel.ConfirmRelocation and AircraftFleetViewModel.AcknowledgeRelocation. Existing EmployerFleetTests.UnobservedCompanyMoveNeedsExplicitDeveloperRecoveryAndCannotStartObservationByBinding.

## Persistence implications
Existing fleet acknowledgement event only; no schema change. Economy/transport remains authoritative and unchanged.

## Required tests
Full BuildTest. WPF synthetic moved-aircraft state reveals action, acknowledgement hides it and retains uncertain location; reload transport and balance and compare. Preserve existing domain recovery checks. Hosted WPF/package pipeline; no local SiteUiSmoke on personal simulator configuration.

## Simulator testing requirements
New exact artifact approval before local execution. In existing stopped FYWE C152 session, review external move, confirm reconstructed 10+10 kg station plan outside, board same aircraft, apply and read retained actual stations/manifest. No flight/wage claims from this loading scenario.

## Open design decisions
None; existing recovery policy unchanged. User-facing future recovery simplification is separate.
