# Feature: Guided cargo loading and identity-transition recovery

Status: Approved
Approved by / date: Matt requested fixing the failed outside preview and making loading really simple, 2026-10-29/30.

## Objective
Restore a usable loading loop after the reported Black Square Caravan outside preview stopped with “Type is invalid”. Reduce routine loading to choose cargo, confirm outside, board/apply, then prepare departure.

## Gameplay behavior
2026-10-30 follow-up explicitly approved by Matt after live diagnosis: click **Load** on an individual consignment, then choose a simulator station by clicking its station card. Default to the full available amount, allow a partial amount/split, and list removable proposed pickups. No station dropdown or routine station-total typing in the main flow. Deliver is an individual consignment action. Incomplete fleet tracking identity must pause observation and recover on a subsequent complete update without bypassing type, location, object-change or persistence guards. This supersedes the earlier compartment-dropdown interaction below.

Use the existing cargo journey, payload ownership, tracker and settlement systems. One context-sensitive cargo button replaces the permanent prepare/apply toolbar. Full pickup and deliver-here shortcuts preserve through-cargo; quantities remain editable for partial operations. Select a real simulator compartment for pickups; advanced station splitting and recovery remain available in collapsed details. Preparing a plan remains read-only to MSFS; apply still requires actual seated authorization and retained readback. Departure uses one stateful button without automatic arming or invented flight proof.

## Acceptance criteria
- Two jobs load through the guided UI without manual mass arithmetic; intermediate delivery retains unrelated cargo.
- Pending plans lock quantity edits; partial failures retain immutable intent and do not double-apply.
- Missing identity cannot become a guessed/cached model or permit a write. Incomplete native identity reads receive at most three fresh attempts; disconnect/cancellation/other errors are not retried.
- Failed preview distinguishes native read failure from tracking/avatar authorization, retains useful diagnostics and cannot commit inventory or payment.
- Existing loading, recovery, wage, passenger, station and aircraft guards remain.

## Non-goals
No identity fallback, avatar substitution, new station mappings, MTOW/CG restrictions, save reset/migration, changed pay or claim of live Caravan acceptance. Do not enable mixed transport or unfinished aircraft.

## Relevant existing systems
`CargoJourneyService/ViewModel`, `CargoJourneyControl`, `AircraftLoadingCoordinator.AuthorizeCargoHandling`, `SimConnectAircraftLoading`, `FlightAircraftIdentity`, existing fleet/flight/economy stores. Compare native adapter against `checkpoint-loading-live-verified-2026-09-17` rather than assuming native loading was replaced.

## Persistence implications
No schema changes or user-data edits. Existing pending operations and cargo claims remain authoritative. Technical failure details are transient and do not qualify delivery. Original PR #7 and historical live evidence remain unchanged.

## Required tests
Identity-transition retry/refusal/cancellation, missing tracked model, both preview failure stages with unchanged economy and no writes; guided multi-destination loading/delivery, explicit station choice, pending lock, partial-write replay, full regression and actual WPF controls on hosted Windows.

## Simulator testing requirements
Exact artifact approval and reviewed isolated scenario precede agent execution. Record Caravan/add-on/MSFS/SDK versions/hashes and identity availability seated/outside. Then outside plan → board/apply → retained station/gross readback → restart → flight/delivery. Synthetic success cannot close the reported live issue.

## Open design decisions
None for this scope. Persistent missing aircraft identity is a technical blocker requiring live evidence, not permission to invent a model or bypass an authorization guard.
