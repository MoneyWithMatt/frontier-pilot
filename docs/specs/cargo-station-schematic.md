# Feature: cargo-station-schematic — aircraft outline with station boxes

Status: Approved
Approved by / date: Matt's aircraft-diagram reference and request, 2026-11-02.

## Objective
Show clickable loading-station boxes on a restrained aircraft outline, retaining Frontier's dark theme and the existing cargo workflow.

## Gameplay behavior
A generic top-down vector aircraft surrounds station buttons in simulator numeric order. Each box shows its actual station number/name, actual mass, proposed total and selection/proposal label. The outline is explicitly schematic: boxes do not claim physical compartment locations. Selected-station details retain reported limits and the list of proposed consignments. Blue selection/green pairing, keyboard buttons, partial quantities, Change/Remove, manifest and existing authorization/readback remain unchanged.

The new request supersedes the prior prohibition on an aircraft drawing for this decorative schematic only. No geometry is inferred from names/indices or the reference image. Accurate aircraft-specific layouts require separately verified positions; no layout profile is added here.

## Acceptance criteria
- Every reported station appears once in numeric order; no seats, decorative people, invented names or limits.
- Native buttons remain reachable; decoration cannot intercept clicks.
- Six- and fifteen-station synthetic layouts render without horizontal clipping; read-only stations stay disabled.
- Existing actual handler tests complete the synthetic multi-stop loading/delivery flow.

## Non-goals
No physical station-position claim, aircraft-specific geometry, loading-service change, new simulator writes, engine diagnosis, save migration or mixed passenger/cargo enablement.

## Relevant existing systems
CargoJourneyControl.xaml, CargoStationRow presentation, CargoJourneySmoke, shared application brushes and click-to-assign service flow.

## Persistence implications
None. Existing durable aggregate loading intents and readback remain authoritative.

## Required tests
Full Windows regression/build, hosted WPF/verification guards/package. WPF checks all station buttons, ordering, read-only state, layout bounds and actual pairing/Change/Remove/load/delivery handlers; capture six- and fifteen-station screenshots.

## Simulator testing requirements
No simulator acceptance claimed. A future approved exact build must repeat the parked Caravan click/confirm/apply/readback test. The preceding run-41 execution approval remains unanswered and does not approve a future schematic build. Existing simulator may have been started during the preceding request; no aircraft flight or loading performed in this slice.

## Open design decisions
None. Schematic station order is labelled; physical geometry remains unverified.

## SDK boundary
The official MSFS 2024 [payload SimVars](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/) document station count, name and weight. The current native reader supplies those values and leaves Position null. [Flight-model guidance](https://docs.flightsimulator.com/html/Samples_And_Tutorials/Tutorials/Defining_A_Flight_Model.htm) describes configuration station coordinates relative to datum; those coordinates are not inferred or newly integrated here. Documentation review is not a live aircraft observation.
