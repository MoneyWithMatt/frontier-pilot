# Flight Attempt V0 — Eros cargo delivery lifecycle

## Implemented loop

An accepted Uis or Arandis shipment can now progress through Reserved, simulated loading at Eros, InTransit flight evidence, qualified arrival, explicit unloading, Delivered and paid. The Jobs page shows the current stage, factual blocking reason, distances from origin and destination, shipment state and ledger-derived balance.

Opening either Jobs or Tracking starts the existing read-only SimConnect tracking connection. `BuilderViewModel` forwards only `TrackingViewModel.TelemetryChanged` aircraft samples, which have already passed the existing aircraft telemetry validation. Last-known persisted tracking positions are never forwarded as live flight evidence.

## Rules

`FlightAttemptTrackerOptions` contains the temporary prototype policy:

- origin and destination radius: 1.5 nautical miles from the published catalogue coordinate;
- loading and arrival maximum ground speed: 5 knots;
- continuous stationary time: 10 seconds;
- meaningful post-airborne movement: 2 nautical miles from Eros;
- required maximum observed distance from Eros: 10 nautical miles;
- maximum gap within a continuous timer: 2 seconds.

Loading requires a reserved accepted shipment, valid connected telemetry, on-ground state, the origin radius, the speed threshold, ten continuous seconds and a usable aircraft identity. Loading is simulated in Frontier Pilot: V0 does not alter MSFS payload stations.

The tracker persists the load position/time, route, job, shipment and normalized SimConnect `TITLE` plus `ATC MODEL`. Empty/unknown identity refuses loading. A later sample with a clearly different title or model cannot advance evidence. These fields are adequate for detecting an obvious aircraft change in this prototype, but they are not a unique or ownership-grade airframe identifier; an MSFS aircraft/livery title change may conservatively require diagnosis.

After loading, the tracker requires a persisted airborne observation, a later persisted observation at least 2 NM from Eros, a recorded maximum distance of at least 10 NM, and then arrival within 1.5 NM of the configured destination. Arrival must be on ground at no more than 5 knots for ten continuous seconds. Invalid telemetry, disconnection, a non-advancing timestamp or a gap longer than two seconds resets only the current stationary timer. It does not erase persisted flight facts or change cargo.

Once arrival qualifies, the tracker stores immutable `FlightDeliveryProof` and enables Unload. It does not complete the job automatically. Clicking Unload asks the tracker to construct `VerifiedDeliveryEvidence`; the Jobs view model cannot supply or modify evidence. Economy Core then commits shipment delivery, requirement/job completion, destination inventory, one ledger payment and the ledger-validated cached balance in one economy revision.

## Persistence and recovery

Economy state remains at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\economy-state.json`.

Flight attempts use strict schema-1 JSON at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\flight-attempts.json`. The document supports multiple attempt records keyed by deterministic job/shipment IDs. Writes use the same managed-path, same-directory temporary file, flush, atomic replacement, in-process gate and path-derived named semaphore conventions as Economy Core. Corrupt, unsupported or inconsistent data is reported and preserved.

Loading writes the attempt before moving the shipment to InTransit. If the process stops between these files, startup reconciliation completes the idempotent InTransit transition. Qualified arrival proof is written before explicit unload. If the process stops around unloading, the immutable proof and Economy Core's idempotent evidence/payment IDs allow startup and another unload to converge without duplicating cargo or money.

## Manual MSFS procedure

1. Start MSFS 2024 and load the aircraft you intend to use at Eros/FYWE.
2. Start Frontier Pilot and open Jobs. Wait for the tracking connection.
3. Select either the Uis/FYUS or Arandis/FYAR offer and click **Accept selected job**.
4. Keep the aircraft on the ground within 1.5 NM of Eros and at 5 kt or less for ten uninterrupted seconds. The stage should become **Ready to load**.
5. Click **Load cargo**. This records simulated cargo loading only; no MSFS payload station changes.
6. Take off in the same aircraft. Continue until Frontier Pilot has seen an airborne sample, later movement away from Eros and at least 10 NM maximum distance. The page should progress through **Departed** and **En route**.
7. Land at the selected destination. Stop within 1.5 NM of its published coordinate at 5 kt or less for ten uninterrupted seconds.
8. Confirm **Ready to unload**, then click **Unload cargo** once.
9. Confirm **Delivered and paid**, a Delivered shipment, destination inventory increase and the new TPC balance. Restart Frontier Pilot and confirm the completed state and balance remain.

These checks validate lifecycle behavior, not runway, route, aircraft or weather safety. MSFS continues to own weather and flight operation.

## Deferred

V0 has no cancellation, expiry, reservation release, payload manipulation, anti-teleport system, weather evaluation, recurring demand, offline economic time, player business or additional destination activation.

## Guarded developer diagnostic

The Jobs page can export a read-only JSON snapshot of the current tracker status, flight attempt and economy aggregate. Its reset action requires an explicit Windows confirmation and applies only to an incomplete attempt whose accepted shipment is already `InTransit`. Reset clears airborne, movement, maximum-distance and arrival-proof observations and returns the attempt to `CargoLoaded`; the original load facts and aircraft identity remain.

This does not release, cancel or reload cargo. It writes only `flight-attempts.json` and leaves the economy aggregate byte-for-byte unchanged. A delivered attempt is refused, so completed inventory, requirement/job state, ledger payment and balance cannot be changed through the diagnostic.
