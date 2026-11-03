# Feature: cargo-click-assign — visual cargo and station cards

Status: Approved
Approved by / date: Matt's attached “Simplify Cargo Loading with Click-to-Assign” brief, 2026-11-02.

## Objective
Select a consignment card, then an actual simulator station. Show blue selection and matching muted green proposed pairings in the existing dark Frontier theme, retaining the fax manifest and navigation.

## Gameplay behavior
Cargo cards show destination (including an existing route designator where provided), available/aboard mass and job reference. Simulator station cards stay visible in numeric order; physical positions are unverified and no aircraft drawing is fabricated. Multiple consignments share a station with automatic aggregate arithmetic. Change/Remove edit only transient proposed pickups. Contextual partial/split quantities remain available; confirmed aboard cargo and destination deliveries use the existing journey service. The existing shared Fleet selection is accessible above Sync; changing aircraft requires a fresh read and is disabled while a proposed/pending plan or leg needs attention.

## Acceptance criteria
- No selection is neutral; selected cargo is blue; each proposed cargo/station pair is green and labelled not aboard.
- Station inspection lists all proposed consignments and computed total, preserving current unrelated payload.
- Selection/assignment performs no simulator writes, reservations, delivery or payment.
- Confirm outside, apply seated and retained readback remain mandatory; pending plans cannot be edited.
- The current fax sheet remains on the right; local unsaved proposals are explicitly separate from durable pending and confirmed cargo.
- All existing partial delivery, multi-stop, route, stock, save and payment behavior remains in its existing service.
- Cards are native keyboard-accessible WPF buttons with text state and accessible names as well as colour.

## Non-goals
New visual theme, illustrations, invented station positions/limits, mixed passenger qualification, pay changes, persistence changes, engine troubleshooting or automatic simulator operation.

## Relevant existing systems
CargoJourneyViewModel, CargoJourneyControl, shared AircraftFleetViewModel, CargoJourneyService, CargoDispatchDocument, existing App.xaml resources. Continue the isolated PR #8 branch after inspecting its open draft and unchanged remote head; do not modify other branches or merge.

## Persistence implications
None. Per-consignment UI choices remain transient; the existing durable aggregate operation and readback govern restarts. No inferred persisted per-job physical station ownership.

## Required tests
Selection/pair notifications and actual WPF styles/buttons, multiple shared station consignments, totals/baseline, partial Change/Remove, invalid reassignment, no selection writes, outside/seated authorization, pending restart and existing multi-stop/readback/refusal/payment regressions. Full Windows BuildTest; hosted guards, both WPF programs and packaging; inspect synthetic screenshots.

## Simulator testing requirements
This card revision needs separately approved exact-artifact execution and focused live card → outside confirmation → seated apply → retained readback. Prior run-38 Caravan physical loading is historical integration evidence, not live acceptance of the new card UI. No simulator actions during implementation.

## Open design decisions
None. Display station order when layout is unverified; preserve existing gameplay and loading authority.
