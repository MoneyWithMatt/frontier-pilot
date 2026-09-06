# Read-only avatar survey diagnostic

Status: recorder implemented; controlled seated/exit/walk/look test pending Matt's participation. No survey source has been approved on the basis of API presence alone. Aircraft survey remains an unproven fallback, not an implemented automatic terrain survey.

Initial 2-second live connection check is preserved under evidence/avatar-spike/connection-check-01. The simulator returned aircraft object 524288 and avatar object 32768; current mapped to 524288. No asynchronous exceptions were received. Avatar coordinates were zero/zero in this unclassified state and cannot be accepted as a real position. CameraGet was absent from both header and DLL exports. This check establishes transport and separate object addressing only; it does not establish walking behaviour. Final build completed with zero warnings/errors.

## Supported candidates

Installed MSFS 2024 SDK 1.2.4 SimConnect.h lines 38–42 defines distinct proxy object IDs: aircraft 0, avatar 4294967168, current-controlled object 4294967169. The installed official SimvarWatcher sample also exposes USER_AVATAR and USER_CURRENT object types. These are stronger candidates than inferring avatar movement from aircraft data.

The recorder requests each documented numeric SimVar separately on each proxy at 2 Hz, so one unsupported variable does not suppress the others. It records UTC timestamps, phase labels, requested and returned object IDs, explicit units, request HRESULTs and packet IDs, asynchronous exceptions, and non-finite values. Positions and headings remain candidate object values until behaviour is verified. GROUND ALTITUDE is explicitly labelled as aircraft-local; requesting it on an avatar does not prove avatar-ground elevation. Object altitude may be above a reference point rather than the feet and must be checked before use as ground elevation.

Current official references:

- [Aircraft miscellaneous SimVars](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/): position, altitude and true heading. The diagnostic explicitly requests degrees and metres instead of relying on default units.
- [Miscellaneous SimVars](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/miscellaneous-variables/): GROUND ALTITUDE remains user-aircraft-local.
- [Camera variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/camera-variables/): camera state/substate are modes, not position or facing.
- [CameraGet](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/camera/simconnect_cameraget/): describes an add-on camera. The installed 1.2.4 header has no declaration. The recorder checks the DLL export too. It never acquires or changes a camera. Online camera documentation is newer than parts of the installed header and is not evidence of installed availability or of access to the active walking view.

## Operation

```powershell
& .tools/dotnet/dotnet.exe build spikes/AvatarProbe/AvatarProbe.csproj -c Release
& .tools/dotnet/dotnet.exe spikes/AvatarProbe/bin/Release/net8.0/AvatarProbe.dll 'C:\Users\mattr\Desktop\MSFS 2024 SDK' evidence/avatar-spike/session-01 1800
```

Use a new directory. The process only opens a SimConnect connection, defines/request data, reads replies and closes its connection. It has no SDK write, movement, event-transmission, object-creation or camera-control calls. Nothing in Community or the SDK is modified. A stop.txt file in the session directory ends recording; phase.txt changes annotate future observations. Phase labels are operator observations and must not be inferred from camera modes alone. Small asynchronous delays around phase changes should be excluded from comparisons.

Matt performs the following with the aircraft parked throughout, confirming each stage in chat so the operator can label it:

1. Seated, stationary, view held still for 10 seconds.
2. Seated, turn view left/right, then stop for 10 seconds.
3. Exit aircraft, remain stationary for 10 seconds.
4. Walk straight away for about 10 seconds, then stop.
5. Remain in place and turn view left/right, then stop.
6. Turn and walk in another direction, then stop; optionally return to the seat.

Compare coordinate changes against the stationary aircraft, current-object switching, heading changes during walking versus view-only turns, and repeated stable observations. Reliable avatar coordinates require independent movement and sensible distances; reliable facing requires establishing body/travel versus gaze semantics. Check returned IDs and exceptions at each transition. Do not treat zeros, stale values, missing replies or aliasing to the aircraft as proof.

If only a camera bearing is available, it can at most orient a runway anchored to an explicitly captured, still-valid ground fix. It cannot update that fix while walking, supply terrain height at the camera, or demonstrate a new endpoint. Combining them requires known world/true-north reference and an explicit fixed-anchor interaction. No such camera-bearing workflow is enabled until measured.

Final evidence and workflow decision will be appended after the controlled test. No airport is generated in this experiment.
