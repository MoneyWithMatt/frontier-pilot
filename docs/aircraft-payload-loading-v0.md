# Aircraft Payload and Loading V0

Status: implemented and automated-test verified on 2026-09-15. The SimConnect write path has not yet been accepted in MSFS or on the Piper PA28 used for the successful Arandis/prospecting tests.

## SDK 1.2.4 findings

The installed SDK at `C:\Users\mattr\Desktop\MSFS 2024 SDK` was inspected read-only. Its native SimConnect header exports `SimConnect_SetDataOnSimObject`; the official SetData sample demonstrates client-data-definition writes. The installed SimvarWatcher metadata and current SDK reference establish:

- `PAYLOAD STATION COUNT` enumerates 0–15 indexed stations. `PAYLOAD STATION NAME:n` and writable `PAYLOAD STATION WEIGHT:n` expose the label and weight in pounds.
- No SimVar exposes authoritative per-station structural maximums, station arms/positions, or an authoritative maximum-payload value. Default station weights are not limits. Aircraft configuration may contain station geometry, but it is not a dependable external interface for encrypted or third-party aircraft.
- `EMPTY WEIGHT`, `MAX GROSS WEIGHT`, `TOTAL WEIGHT`, `CG PERCENT`, `CG FWD LIMIT`, and `CG AFT LIMIT` provide factual simulator values. Maximum gross is advisory in this workflow and is never treated as maximum payload.
- `FUEL WEIGHT PER GALLON` supplies the conversion. The adapter exposes the eleven documented legacy named tanks and their capacity/quantity SimVars. Modern `[FUEL_SYSTEM]` component variables exist, but no dependable external name enumeration was established in SDK 1.2.4, so complex modern or third-party tank layouts may be incomplete.
- `NUMBER OF ENGINES` and indexed `GENERAL ENG COMBUSTION:n` cover up to four installed engines. Fuel writes fail closed if engine state is absent or unknown.
- Writes use pounds for payload and US gallons for fuel, followed by a fresh read and comparison. Core/economy cargo remains kilograms. UI displays kilograms and pounds; fuel shows US gallons and a mass only when simulator density is available.

Structural station and total-payload limits are hard only when an authoritative value is present. Maximum gross and reported CG-envelope exceedances are warnings that do not disable Apply. Planned CG remains unavailable because SimConnect does not expose the moments needed for an honest calculation; the post-write simulator CG is shown.

## Architecture and persistence

`Core/AircraftLoading.cs` owns units, aircraft snapshots, plans, hard-limit validation, manifests and fuel snapshots. `MsfsSdk/SimConnectAircraftLoading.cs` is the only native payload/fuel adapter. `Workflow/AircraftLoadingCoordinator.cs` owns access checks and the recoverable external-write sequence; the WPF view model never writes SimConnect or Economy state itself.

`%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\aircraft-loading.json` is a strict schema-1 document, protected by the existing managed-path, cross-process lock, flushed temporary write, atomic replacement, unknown-member refusal, and corrupt/future-file preservation conventions. It contains immutable manifest identity/allocation plus forward-only application/unload progress, last verified station readings and position, reconciliation state, and tank-by-tank fuel snapshots.

Cargo uses `pending intent -> simulator write -> read-back -> simulator-applied record -> existing idempotent LoadCargo/InTransit transaction -> committed manifest`. Restart can continue a pending intent without adding weights if the targets are already present. Unload persists its targets before removing exactly the Frontier allocation from the current station weight; this preserves identifiable unrelated payload added later. Delivery/payment runs only after removal verifies. If a crash occurs after verified removal but before delivery commit, the saved unload target is reused and is not subtracted again. Existing simulated-loading InTransit attempts acquire an explicit compatibility manifest.

Fallback is never automatic. The player must choose **Use simulated loading** when an aircraft cannot support a verified write. A separate confirmed compatibility unload is available after a supported removal fails; it retains all existing verified-arrival and idempotent economy rules while saying MSFS payload was unchanged.

Cargo manifests remain authoritative across app/MSFS/Windows restart. Reconnection compares identity, last position and station weights. Matching weights reconcile without writing; missing or indeterminate cargo is reported. Restore requires an explicit click, matching normalized TITLE/ATC MODEL, fresh on-ground <=5 kt telemetry, a fresh explicit controlled-avatar observation within 100 m, and position within 1.5 NM of the saved position.

Fuel checkpoints only during an uninterrupted validated matching-aircraft session after a meaningful change or bounded interval. Disconnect freezes the last trusted snapshot. The first reconnect reading cannot replace it; the player chooses Restore saved fuel, Keep simulator fuel, or leaves reconciliation pending. Restore additionally requires every engine confirmed off and an identical tank layout. V0 identity is a normalized model identity, not a unique airframe. Two aircraft of the same model cannot be distinguished reliably; permanent continuity requires a player-owned airframe ID, potentially linked to a verified registration.

Support bundles and copied summaries include manifest mode/state, allocations, targets/read-backs, tolerance, W&B warnings, tank snapshots, identity/position comparison and reconciliation reasons. They omit storage paths, usernames and credentials and cannot change gameplay or delivery evidence.

## Flight and scenery corrections

New attempts persist `min(10, max(0.5, directRouteDistanceNm * 0.5))`. A 6.54 NM route therefore requires 3.27 NM. Existing schema-1/schema-2 attempts migrate with their historical 10 NM rule. Airborne, post-departure movement, grounded arrival and ten-second endpoint qualification are unchanged. Diagnostics display actual and required maximum-origin distance.

Sandy Dirt now emits native `DIRT`, because live testing showed native `SAND` compiled but was visually absent. The preset retains a pale authored colour hint, but SDK runway XML has no proven colour-multiplier control, so the visible DIRT appearance is the explicit temporary fallback. Existing airports change only when the preset is selected and the airport is rebuilt.

## Manual MSFS test

1. Update the app using the normal Desktop shortcut after the publish reported in the implementation commit. Start MSFS in the Piper PA28 used for Arandis/prospecting tests, accept a Reserved job, park within 1.5 NM of its origin, stop at <=5 kt for 10 seconds, exit into walkaround and remain within 100 m.
2. In Jobs choose **Prepare aircraft**, then **Read live aircraft**. Confirm station names/weights, tanks, engine states, weight, CG and limits. Record any item shown as unavailable rather than assuming it exists.
3. Allocate the exact shipment mass across at least two stations. Verify the planned targets equal baseline plus Frontier allocation. If the aircraft reports a station limit, try exceeding it and confirm Apply blocks. If limits are unavailable, confirm that limitation is explicit.
4. Review Weight & Balance. Where the aircraft makes it practical, create an above-maximum-gross or outside-reported-CG plan and confirm it warns but remains selectable. Do not use that plan for flight unless you independently choose to.
5. Apply cargo. Confirm MSFS station values and total aircraft weight increase by the shipment mass within 0.25 kg read-back tolerance. Restart Frontier Pilot, read the aircraft again, and confirm the committed manifest returns without adding cargo a second time.
6. With an engine running, confirm fuel Apply is disabled and cargo still shows a caution rather than a hard block. Shut every engine down, adjust one tank within capacity, apply, and confirm the read-back. Restart/reconnect and exercise Keep simulator fuel or Restore saved fuel; confirm neither happens automatically.
7. Fly normally. For the 6.54 NM prospecting route, confirm diagnostics require 3.27 NM rather than 10 NM, while airborne and movement evidence remain required.
8. At Ready to unload, stop at the configured destination and exit nearby. Use Aircraft Loading **Unload at verified destination**. Confirm the exact Frontier allocation leaves the stations, unrelated added payload remains, delivery inventory changes, and payment records once. Repeat/restart and confirm neither weight nor payment changes again.
9. For a deliberately unsupported aircraft/write failure, confirm the app explains the failed verification and requires explicit **Use simulated loading**; it must never change modes silently.
10. Rebuild a disposable airport after selecting Sandy Dirt and confirm the runway is visibly DIRT-coloured. Preserve existing airports unless intentionally rebuilt.

Do not claim third-party aircraft, modern custom fuel-system enumeration, planned CG, or the live SimConnect write/read-back path as accepted until these checks pass.

## Live freshness correction

Every mutating action obtains a fresh simulator read immediately before validating access and writing. Cargo Apply compares this read against the plan baseline; if another source changed payload after planning, it refuses and requires a new review rather than applying an outdated target.


## Manual loading interface acceptance correction

The initial DataGrid editor was replaced after live testing showed typed allocations did not reliably commit. Each station and tank now has a dedicated two-way slider and numeric field. Automatic distribution has been removed. A live summary shows assigned and remaining job cargo, while current/planned weights and warnings remain visible during the player's allocation decision.

