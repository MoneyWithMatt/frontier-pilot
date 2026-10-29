# Backlog

## Employer starting fleets — 2026-10-29

[Automatic employer-owned starting allocation](employer-starting-fleets.md), [approved scope](specs/employer-starting-fleets.md), [handoff](handoffs/employer-starting-fleets.md). Finish exact-source hosted WPF/package evidence and separately approved live type/location/condition/mission checks. Future work: explicit active-career employer/economy transition, ownership/assignment transfers and authenticated shared-world registration/allocation authority. Allocation does not enable H125/platform/passenger/configuration/fault/placement capabilities.

## Multi-stop cargo — 2026-10-29

Latest authorized implementation: [scope](specs/multistop-cargo.md), [production workflow/live procedure](multistop-cargo.md), [resume status](handoffs/multistop-cargo.md). Hosted runtime WPF/package evidence passed at 816/0/0; exact-aircraft live acceptance remains separate. Preserve PR #3. Follow-up: common mixed cargo/passenger claim accounting (not enabled), explicit disposition/cancellation, long-career archival, optional print/PDF and pilot-operated planning worksheet. None may introduce automatic MTOW/CG/performance refusal.

## Current development priority — Offshore (2026-10-29)

- Latest approved extension: [shared physical passenger transport](shared-passengers.md), [spec](specs/shared-passengers.md). Reusable multi-person carriage, native owned-station writes/readback/recovery and Missionary visit adapter implemented; existing $800/$400 hourly pay retained. Local 794-suite pass; continue exact hosted WPF/package/visual checkpoint from the handoff. Obtain exact artifact/scenario approval for isolated live Caravan qualification, establish legitimate effective Vision Jet seat/station configuration, and investigate aircraft-supported seated-model controls. No live acceptance is inferred; original simulated reports below are historical.

- [Implementation, SDK capabilities and staged tasks](offshore-career.md); [resume handoff](handoffs/offshore-career.md). Airport-cargo dispatch fixes and seven new synthetic cases implemented; [751-suite hosted pass, WPF/package evidence](offshore-career-verification.md). Continue through [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2) / [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3).
- Matt selected Offshore passenger transfers: typed airport transfer loop implemented with simulated boarding, $800/hour, endpoint/fleet guards and atomic recovery/payment. [Scope/live procedure](offshore-passenger-transfers.md). [774 hosted tests, actual-control WPF/visual review and publish passed](offshore-passenger-verification.md); airport simulator acceptance remains pending separate approval. Platforms remain unavailable. H125 platform cargo and survey/inspection are deferred until this loop is established.

## Employed wages acceptance

- V1 implemented 2026-10-17. Live-test a new Missionary flight (~30 measured minutes = ~$200), compare Industrial/Offshore rates, inspect History, and verify restart/repeated completion never pays again. Grouped cargo should earn one flight wage. See [manual procedure and migration limitations](employed-pilot-wages-v1.md).
- Tips, progression, duty/block pay and employer finances remain deferred.

## Career onboarding follow-up

- 2026-10-16: automatic worldwide MSFS airport resolution is implemented, superseding the manual-inspection prerequisite and future-enumeration item below. Live-test the batch lookup with a flight loaded anywhere; then test generated employer jobs. Resolve the nine identifierless research candidates separately. No individual airport visits are required to enable database-resolved routes. See [current workflow](automatic-airport-resolution.md).

- V1 onboarding shell and safe reset are implemented; obtain live acceptance for legacy startup, reset/restart and resumable employer exploration.
- Employer acceptance, profiles, company dispatch and curated destination diagnostics are implemented on 2026-10-15. Live-test each employer and stock/add-on mapping; inspect unresolved airports using the new review UI. Only Industrial FYUS/FYAR currently generates live work. See [the report and all 45 unresolved airports](starting-employers-and-destinations.md).
- FYWB database resolution enables Offshore airport jobs; the published 2026-10-16 lookup found FYWB and all Offshore identifiers. Aircraft/runway/scenery acceptance remains separate. Qualify H125 helipad/platform operations before enabling them. Survey zones, loiter duration and return/inspection rules require approved design; 30 minutes was illustrative.
- Add genuine passenger lifecycle and optional multi-stop duties later; never simulate people by corrupting cargo accounting. Research closer Missionary destinations to support the desired short-sector rhythm from Eros.
- Add safe versioned corrections for already-approved destination coordinates; V1 preserves those endpoints and permits rejection/investigation only. Automatic simulator facility enumeration and evidence-backed stock/add-on aliases remain future enhancements.

## Aircraft Loading V0 live acceptance

- Run the desired-state Piper PA28 procedure in [aircraft-payload-loading-v0.md](aircraft-payload-loading-v0.md); accept loading is already proven, then verify partial load/unload, correct-destination completion, wrong-location forfeiture, restart recovery and the Station 5 missing-payload reconciliation.
- Replace normalized model identity with a stable player-owned airframe ID before permanent multi-aircraft cargo/fuel continuity.
- Investigate modern `[FUEL_SYSTEM]` tank enumeration and authoritative station geometry/limits for default and third-party aircraft; retain fail-closed behavior until proven.
- Visually rebuild Sandy Dirt and confirm the DIRT fallback is visible; its pale colour hint is metadata only pending a proven SDK control.

## After Prospecting Camp V0 live acceptance

- Live-test founding from the guarded avatar at a saved Base Lodge site, then accept both camp-origin and camp-destination Flight Attempt routes.
- Decide how a permanent game clock, operation ownership/accounting, mineral valuation, wages and transport costs replace the explicit-day and combined-payment fixtures.
- Design explicit abandonment, relocation and demolition before allowing deletion of linked airports.
- Decide whether airport edits that move/remove required construction capabilities should be blocked or create an operation-attention state.

## Next acceptance test
- Run Tracking V4 at a saved Frontier airport: taxi across the operational area, pass and stop at two stands, verify Near Stand/Parked timing and distances, then restart offline and check the LAST KNOWN association. Tune only from observed results; see tracking-v4-airport-recognition.md.
- Run Tracking V2 against a loaded MSFS flight: verify In Aircraft → On Foot identity transition, stationary aircraft plus independently moving avatar, distance/facing, return to aircraft, manual viewport control and disconnect recovery. See tracking-v2-avatar.md. Tracking V1 aircraft movement was accepted by Matt.
- Run Tracking V3 persistence acceptance: move the aircraft, disconnect, verify LAST KNOWN, restart Frontier Pilot without MSFS, then reconnect and verify LIVE. See tracking-v3-persistence.md.
- Build disposable airports for the seven surface presets and visually compare texture/colour, grass behavior, merging and edges. Dry Grass and Weathered Asphalt currently use labelled fallbacks. See scenery-materials-and-prototype-buildings-v0.md.
- Rebuild a site containing the Small Base Lodge and verify `BF_TemplateHouse01` resolves as one suitably small house, has a useful heading, and sits naturally on its foundation. Measure its true model bounds and replace the provisional 10 × 8 m authoring footprint after live acceptance. `MICROSOFT_ASSET_FieldCabins_01` was rejected because it appeared as a large compound/hangar-like object.
- Run the WPF flow in a real flight: capture a new flat site, close MSFS, compile/install a new identifier, restart and inspect both starts.
- Exercise replacement of that new app-owned package and verify the prior version backup. Keep FPT1/FPSC untouched.
- Verify usability at additional Windows scaling settings.

## Prototype improvements
- Choose a production-capable tile provider/cache policy before distributing Tracking beyond limited prototype use.
- Add autosave/draft recovery beyond the implemented explicit saved-airport library; saved airports already restore qualified survey evidence and layouts after restart.
- Detect runway footprint slope/obstacles before committing to placement.
- Extend guarded surveying beyond the operation anchor before allowing a runway centre to move independently across the site map; current map placement keeps the validated anchor as runway centre and sets heading/length from an endpoint.
- Explore authoritative global airport identifier collision checks.
- Support Community storage on other drives through tested on-volume staging.
- Improve progress detail for long Package Tool runs; preserve child-process safety.
- Replace the implemented self-contained development executable/shortcut with a signed customer installer, updater and prerequisite handling when distribution work begins.

## Research

- Aircraft Condition Recorder V0 implemented 2026-10-19: run [C172, exact Dakota and Baron acceptance](aircraft-condition-v0.md), qualify fields with controlled observations, and establish component-W&T addressing/reload persistence. See also the [SDK capability matrix](aircraft-condition-feasibility.md). Individual GUID records are create-only; no penalties, maintenance economy or failure activation. All live capabilities remain unverified.
- Requalify avatar elevation after simulator/SDK changes.
- Track an officially supported ground query API; retain empirical source limitations.
- Replace the Dry Grass and Weathered Asphalt fallbacks only after a reliable licensed material source and simulator visual proof exist.

## Later product work
Economy Core, employed-cargo selection and Flight Attempt V0 now implement the first Eros-to-Uis/Arandis delivery lifecycle. Manual MSFS acceptance is next. Do not fold settlement production, player companies, multiplayer or worldwide economics into follow-up tuning.

## Economy V0 follow-up decisions
- Manually run Eros to Uis or Arandis and verify telemetry progression, connection recovery, unload, inventory and payment. Tune tolerances only from observed behavior.
- Review the temporary 120 kg / 240 TPC Uis and 100 kg / 200 TPC Arandis values after UI testing; compensation design remains open.
- Define player-facing cancellation, expiry, cargo loss and reservation-release rules. The current skip action is a prototype-only administrative retest tool.
- Define flight/location evidence and arrival conditions without adding a weather or safety verdict.
- Decide when the economic clock advances and whether anything advances while Frontier Pilot is closed.

## Completed in workflow hardening
- Explicit validation/recovery states and prerequisite gating.
- Recorded-survey offline end-to-end test mode.
- Integration tests for duplicate IDs, changed packages, reinstall, unrelated content and synthetic/live isolation.
- Technical log copy/save and stricter destination checks.

## Newly identified limitations
- Add durable install journal/recovery for power loss between directory renames.
- Reduce conservative path restrictions only with corresponding safety tests.
- Optional offline session browser/cleanup with explicit ownership and user-data protection.
- Perform the updated live WPF acceptance when the user is present; this task deliberately did not launch/control MSFS.
- Add a durable deletion journal for recovery after process or machine failure during the short cleanup transaction.

## Current acceptance priority — 2026-09-08
- Runway, parking, multi-point taxiway, persistence and Windows launch are accepted by Matt in MSFS (user report).
- The generic hangar's position and orientation are accepted. Next: test its measured footprint and generated local terrain foundation following object-footprints-and-foundations.md.
- Future taxiway work only after acceptance: vertex dragging, curve/turn-clearance handling, explicit junctions and complete ATC/AI runway graph. Not implemented in this task.
## Dependency setup acceptance — 2026-10-10

- Install both Microsoft SDK components through the new setup window on a clean PC; verify detection, persisted configuration and a live qualified connection, then compile an explicitly authorized test airport.
- Add Store configuration discovery, download resume and explicit dependency-download cleanup before customer distribution.
- Requalify newer SDK/simulator builds empirically; never select the newest Microsoft manifest version as automatically qualified.

Aircraft condition UI: implemented manual recording in Tracking. Next: run the documented C172/Dakota/Baron UI acceptance and capability tests, including reload/disconnect/close; then design automatic monitoring with explicit airframe binding and optional raw export.

## Physical aircraft V1 acceptance — 2026-10-27
Run automatic enrolment/location/restart and time/pause acceptance from physical-aircraft-v1.md. Qualify one exact aircraft and service facility, including dry-ground and elevated-platform height. Production wear/incident calibration, native fault/position adapters, vendor mappings, explicit cargo reassignment and individual fuel/payload migration remain pending. Do not enable unverified effects from synthetic test results.
