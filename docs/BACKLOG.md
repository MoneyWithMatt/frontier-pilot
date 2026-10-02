# Backlog

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
- Place the Small Base Lodge placeholder and verify the `MICROSOFT_ASSET_FieldCabins_01` model resolves, resembles a suitable small house/cabin, and sits naturally on its foundation. Measure its true model bounds and replace the provisional 8 × 6 m authoring footprint after live acceptance.
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
