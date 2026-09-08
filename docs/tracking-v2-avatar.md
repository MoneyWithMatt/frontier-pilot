# Tracking V2 — aircraft and walking avatar

## Decision

Outcome A is supported as a guarded prototype on the installed MSFS 2024 SDK 1.2.4 integration. Frontier Pilot requests three distinct SimConnect proxy objects in one read-only 4 Hz batch:

- `SIMCONNECT_OBJECT_ID_USER_AIRCRAFT` for the aircraft;
- `SIMCONNECT_OBJECT_ID_USER_AVATAR` for the walking avatar;
- `SIMCONNECT_OBJECT_ID_USER_CURRENT` to identify which of those the player currently controls.

The aircraft and player remain separate Core world-state fields. The player position is present only when the returned avatar object ID is nonzero, differs from the aircraft ID, matches the current-object ID, both avatar/current positions and headings agree within defensive tolerances, and both readings report on ground. Any alias, unsupported proxy, transition sample, invalid number or disagreement produces **Unknown · position refused** while valid aircraft tracking continues. Aircraft or camera coordinates are never substituted.

## Evidence classification

### Confirmed

Microsoft's current `SIMCONNECT_SIMOBJECT_TYPE` documentation defines `USER_AVATAR` as the user avatar and `USER_CURRENT` as the user regardless of whether it is the aircraft or avatar. The installed SDK 1.2.4 `SimConnect.h` lines 38–42 also defines the three corresponding object-ID proxies. Its official SimvarWatcher sample exposes both avatar and current-user object types.

The unchanged Sedona recording under `evidence/avatar-spike/sedona-01` empirically established the proxy behaviour on simulator file version 1.8.16.0: current returned aircraft object 524288 while seated, switched to avatar object 32768 outside, aircraft coordinates stayed fixed, and avatar coordinates moved independently by 18.2857 m and then 15.8888 m. The second displacement bearing and final avatar heading agreed within about 0.0012 degrees. No SimConnect exceptions occurred.

This establishes independent live aircraft and avatar latitude/longitude for that tested build. Distance is derived locally from the two WGS84 coordinates using a great-circle calculation; it is not a separate simulator value.

### Likely, with retained limits

`PLANE ALTITUDE`, `PLANE HEADING DEGREES TRUE`, and `SIM ON GROUND` returned coherent avatar-targeted values in the Sedona sessions. Heading changed during outside view turns and agreed with travel direction in the final walk, so the UI labels it **avatar facing**. It is not claimed to be independent camera gaze, body orientation under every control mode, or VR head direction.

The earlier elevation experiment found avatar-targeted `GROUND ALTITUDE` followed the avatar despite Microsoft's aircraft-local documentation, but Tracking V2 does not use that disputed value. It displays the avatar object's `PLANE ALTITUDE` only. Airport Builder's stricter, hash-qualified elevation capture remains unchanged.

### Unknown or unavailable

- Portability across future simulator/SDK updates still needs a quick live recheck. Runtime identity and consistency refusal contains failures instead of guessing.
- The currently active player-camera world position is not used. The newer Camera API describes a separately acquired add-on camera and does not establish access to the walkaround player's camera.
- Behaviour while jumping, crouching, swimming, in unusual activities, VR, or multiplayer has not been qualified. An avatar that is not on ground is deliberately refused in this prototype.
- SimConnect does not provide a direct avatar-to-aircraft distance in this implementation; Frontier Pilot derives it.

## UI and diagnostics

The Tracking map retains the orange heading-oriented aircraft marker. A blue player marker appears only in verified On Foot state and rotates with avatar facing. Re-entering the aircraft removes the separate player marker. Initial connection still centres once; later updates do not override manual pan or zoom. **Centre on Aircraft** and **Centre on Player** are explicit actions.

The side panel displays player mode, avatar position, altitude, facing and distance. The collapsed Tracking diagnostics area records SDK/simulator version, proxy/returned object IDs, mode transitions and samples every two seconds. It is intended for the seated/outside/walk/return experiment without filling the normal UI with packet-level noise.

## Manual acceptance

1. Launch MSFS 2024 and fully load into an aircraft on the ground.
2. Launch the published Frontier Pilot shortcut and open **Tracking**.
3. Confirm the orange aircraft marker and **In Aircraft** state are correct.
4. Exit into walkaround. Confirm state changes to **On Foot** and a blue player marker appears at the aircraft.
5. Stand beside the aircraft, then walk 10–100 metres away. Confirm the orange aircraft marker stays fixed, the blue marker moves, and distance increases sensibly.
6. Walk around the aircraft and turn. Confirm player position and facing update without moving the aircraft marker.
7. Pan and zoom manually while walking. Confirm updates do not recenter the map. Test both centre buttons.
8. Return and re-enter. Confirm state returns to **In Aircraft** and the separate blue marker disappears.
9. Close MSFS or disconnect. Confirm both live markers clear and the page offers a clean retry.
10. If any transition is wrong, expand **Tracking diagnostics**, copy it, and preserve the seated/outside/walk/return sequence with the simulator version shown.

Automated tests prove model separation, identity gates, invalid/airborne refusal, derived distance, view-model transitions, return behaviour, cancellation and loss handling. They do not replace this live map acceptance.

## Official references

- Microsoft MSFS 2024 [`SIMCONNECT_SIMOBJECT_TYPE`](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_SIMOBJECT_TYPE.htm).
- Microsoft [`SimConnect_RequestDataOnSimObject`](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Events_And_Data/SimConnect_RequestDataOnSimObject.htm).
- Microsoft MSFS 2024 [Aircraft Miscellaneous Variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/).
- Microsoft MSFS 2024 [Camera API](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Camera/Camera_API.htm).
