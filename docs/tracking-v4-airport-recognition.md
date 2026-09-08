# Tracking V4 — Frontier airport and parking recognition

## Recognition model

Tracking receives the validated definitions already loaded by **My Airports**. It never scans or labels stock/MSFS airports. Core converts live WGS84 aircraft coordinates back to local east/north metres with a Vincenty inverse on the WGS84 ellipsoid, then measures the aircraft against each saved Frontier airport's actual runway rectangle, taxiway corridors and parking circles.

An aircraft is at a Frontier airport when the gap to the nearest of those infrastructure footprints is at most 250 m. This operational recognition margin is separate from the 3 km `AirportSite` authoring envelope. The closest qualifying airport wins, with identifier order as a deterministic exact-distance tie break. This is a linear scan suitable for the current small local airport library.

For an on-ground aircraft, the closest stand qualifies as **Near Stand** inside the stand's configured MSFS radius plus 5 m. **Parked** requires the same stand, on-ground state and at most 1.0 kt continuously for five seconds, with no telemetry gap longer than two seconds. Taxiing through the circle remains Near Stand. Leaving the radius, becoming airborne, exceeding the speed limit, changing stands or losing stable samples resets the timer. Heading, nose-wheel position, brakes and engine state are deliberately not required in V4.

The constants are collected in `AirportRecognitionOptions` for empirical tuning. Debug text gives the nearest airport/stand distance and the current reason.

## Persistence and lifecycle

`aircraft-state.json` now writes schema 2 and still reads schema 1. Each aircraft record retains authoritative coordinates plus optional derived `AircraftLocationContext`: airport identifier/name, stand number/name, distances, parking state, observation timestamp and reason. Outside all Frontier airports stores no association. At an airport but away from stands stores only the airport and Not at Stand.

Live airport edits, saves and deletions refresh Tracking's definition snapshot. A last-known association is cleared if its airport or stand no longer exists. Player/avatar state remains independent and transient.

## Manual MSFS test

1. Ensure the test airport and at least two stands are saved in **My Airports** and its latest scenery package is installed.
2. Launch MSFS 2024 at that airport, launch Frontier Pilot, and open **Tracking**.
3. Confirm **Frontier location** shows the saved airport while on or within roughly 250 m of its runway, taxiways or stands.
4. Taxi around the site. The airport should remain recognised; away from a stand it should show **Stand: None** and **Not at Stand**.
5. Taxi through a stand circle above 1 kt. Confirm the stand and distance appear but state remains **Near Stand**.
6. Stop on the stand. Keep ground speed at or below 1 kt for five continuous seconds; confirm state changes to **Parked**.
7. Taxi away past the stand radius plus 5 m. Confirm the stand clears and state returns to **Not at Stand**.
8. Repeat at a second stand and verify its number is selected.
9. Disconnect/close MSFS, then close Frontier Pilot. Restart Frontier Pilot without MSFS and open Tracking.
10. Confirm the orange marker is LAST KNOWN and the saved Frontier airport, stand and parking state remain clearly last-known rather than live.

Simulator acceptance remains pending. In particular, stand radii describe the scenery definition rather than exact aircraft contact geometry, and the 250 m/5 m/1 kt/5 s values may need tuning after live taxi tests.
