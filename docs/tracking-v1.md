# Tracking V1 — live aircraft map

## Scope and boundary

Tracking V1 is a read-only proof from MSFS 2024 SimConnect to a moving marker in Frontier Pilot. `AircraftTelemetry` explicitly describes the user aircraft. It is not a generic player-position type and is not reused for the walking avatar or camera.

`LiveAircraftTelemetry` loads the configured SDK's `SimConnect.dll`, opens a read-only connection and requests data for `SIMCONNECT_OBJECT_ID_USER_AIRCRAFT` (`0`). The installed SDK 1.2.4 header separately defines user-aircraft, user-avatar and current-controlled-object proxies; Tracking uses only the aircraft proxy.

Numeric observations are requested every 250 ms:

- `PLANE LATITUDE` in degrees;
- `PLANE LONGITUDE` in degrees;
- `PLANE ALTITUDE` in feet;
- `PLANE HEADING DEGREES TRUE` in degrees;
- `SIM ON GROUND` as a boolean;
- `GROUND VELOCITY` in knots.

`TITLE` and `ATC MODEL` are fixed-length string observations refreshed every ten seconds. All numeric readings must be finite and within defensive physical/range limits before they reach the view model or map.

The telemetry service closes SimConnect when the user disconnects, leaves Frontier Pilot, or cancellation/error unwinds the session. Missing simulator processes, failed handshakes, malformed data, native exceptions, simulator quit messages and five seconds without observations become clear, retryable Tracking errors.

## Map

The WPF page uses Mapsui.Wpf 5.1.0 (MIT) and its native Skia renderer. The base layer is OpenStreetMap, so no paid service or API key is required. The page displays the required OpenStreetMap attribution and supplies a Frontier Pilot user-agent.

Coordinates are projected from WGS84 to Web Mercator only in the map view. The displayed telemetry retains the original values. At the Web Mercator polar limit, the marker is clamped to the nearest representable map latitude.

The orange triangular marker points along true aircraft heading. The map centres and selects a useful zoom only when the first valid sample arrives. Subsequent position/heading samples update the marker without moving the viewport, so manual panning and zooming remain under the user's control. **Centre on Aircraft** recentres without changing the current zoom.

Maps need an internet connection for uncached OpenStreetMap tiles. Public OSM tiles are appropriate for this limited prototype; customer distribution needs a reviewed tile provider/cache strategy rather than assuming unrestricted public-tile capacity.

## Architecture

- Core: `AircraftTelemetry` validation and `IAircraftTelemetrySource` boundary.
- MsfsSdk: native read-only `LiveAircraftTelemetry` implementation.
- Workflow: `TrackingViewModel`, state, formatting, cancellation and recovery.
- Desktop: Mapsui projection/rendering in `AircraftTrackingMap` and Tracking tab composition.

The Airport Builder continues to use its existing `LiveSurveyCapture`; its qualified avatar rules and evidence are unchanged. Opening Tracking does not change Builder workflow state, files, saved airports, packages or Community content. Tracking is deliberately disabled when the app is launched with the Airport Builder `--dry-run` switch.

## Manual acceptance test

1. Launch MSFS 2024 and fully load a flight.
2. Launch Frontier Pilot from the existing Desktop shortcut.
3. Open **Tracking**. It connects automatically; use **Connect** to retry after an error.
4. Confirm the orange aircraft marker is at the aircraft's real location and points along its true heading.
5. Compare the latitude, longitude, altitude, heading, ground speed, ground/air state and aircraft identity with the simulator.
6. Taxi and confirm marker position and heading update several times per second.
7. Fly a short distance. Use **Centre on Aircraft** if the aircraft leaves the current viewport.
8. Pan and zoom manually, then keep moving. Confirm telemetry moves the marker without taking control of the viewport.
9. Close MSFS or select **Disconnect**. Confirm Frontier Pilot clears the live marker and shows a disconnected/error state without closing.
10. Reopen the Airport Builder and confirm its existing saved airports and workflow still operate normally.

This implementation has automated and local launch coverage but has not yet received live telemetry from MSFS during this task. Matt's manual test is the acceptance authority for the SimConnect values and moving map.

## References

- Microsoft MSFS 2024 [Aircraft Miscellaneous Variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/).
- Microsoft [SimConnect_RequestDataOnSimObject](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Events_And_Data/SimConnect_RequestDataOnSimObject.htm).
- [Mapsui 5.1.0](https://github.com/Mapsui/Mapsui/tree/5.1.0) source and WPF samples.
- [OpenStreetMap copyright and attribution](https://www.openstreetmap.org/copyright).
