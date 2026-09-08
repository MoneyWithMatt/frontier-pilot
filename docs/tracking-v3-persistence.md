# Tracking V3 — persistent aircraft location

## Stored state

Frontier Pilot stores tracking history at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\tracking\aircraft-state.json`.

The versioned JSON document contains an active aircraft ID and a collection of aircraft records. V3 displays and updates only the active record, but the format can retain more than one prototype identity. The opaque ID derives from the best available SimConnect type/title combination; it is not a registration, ownership ID or proof of a unique physical airframe. Transient SimConnect object IDs are not persisted.

Each record stores aircraft title, type/model, latitude, longitude, altitude in feet, true heading, ground speed, on-ground/airborne state and the UTC telemetry timestamp. This is a **last-known observation**, never a confirmed parking location. Closing the simulator in flight may preserve an airborne point.

## Write and recovery behaviour

Only validated `AircraftTelemetry` enters persistence. The first valid live observation is eligible immediately; subsequent writes are throttled to ten-second intervals. Explicit disconnect, telemetry-source shutdown and normal Frontier Pilot disposal force a final save of the newest valid observation where available.

The document is written to a new file in the managed tracking directory, flushed, and atomically moved over the prior document. Invalid telemetry is rejected before replacing saved state. Empty, oversized, malformed, future-schema or inconsistent documents are reported and preserved. **Clear saved position…** is a confirmed development action that deletes only this managed state file; it does not touch MSFS, Community, airports or generated projects.

## Live and last-known transitions

At startup the Tracking view model loads the saved aircraft independently from SimConnect. The orange marker can therefore appear while MSFS is closed. Its source badge says:

- **LIVE** for current validated aircraft telemetry;
- **LAST KNOWN — date/time** for persistence or a disconnected live session;
- **NO SAVED POSITION** when no usable history exists.

Connecting leaves the last-known marker visible until the first valid live sample. That first sample recentres once if its location differs; later telemetry respects manual pan/zoom. Losing MSFS removes the transient V2 avatar marker, preserves the newest aircraft marker, and returns to LAST KNOWN. Initialization and shutdown never generate default coordinates or a 0,0 fallback.

V2 separation remains: persistence receives `TrackingWorldState.Aircraft` only. Avatar position and current-controlled-object state stay live and transient.

## Automated coverage

Tests cover no-history loading, save/load across store instances, multi-record growth, corrupt-document preservation/reset, invalid telemetry protection, last-known to live and live to last-known transitions, connection failure, throttled writes and disconnect flush. The full suite retains existing avatar, Airport Builder, generation and installation coverage.

## Manual test

1. Launch MSFS 2024, then Frontier Pilot, and open **Tracking**.
2. Confirm the source badge says **LIVE**.
3. Taxi or fly to a recognisably different location and wait at least ten seconds.
4. Disconnect Tracking or close MSFS. Confirm the orange aircraft remains and changes to **LAST KNOWN** with a timestamp.
5. Close Frontier Pilot normally.
6. With MSFS closed, restart Frontier Pilot and open **Tracking**. An automatic connection error is acceptable, but the saved marker and LAST KNOWN badge must remain.
7. Confirm its coordinates, identity, altitude, heading and ground/air state match the final valid observation.
8. Start/load MSFS and reconnect. Confirm LIVE returns and the marker moves to current telemetry without displaying 0,0.
9. To test reset, disconnect, select **Clear saved position…**, confirm, restart without MSFS, and verify **NO SAVED POSITION**.

V3 does not enforce spawn position, infer parking, assign stands, establish ownership or distinguish multiple copies of the same aircraft model.
