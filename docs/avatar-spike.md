# Read-only avatar survey diagnostic

Status: controlled test completed with Matt on the Sedona ramp, 6 September 2026. Avatar world position and true facing are supported for the prototype survey capture workflow in this tested configuration. Ground-elevation portability remains a separate validation gate. Aircraft survey remains a fallback candidate, not an implemented automatic terrain survey.

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

## Controlled Sedona result

The recorder completed and closed its connection cleanly, with zero SimConnect exceptions. Raw timestamped observations are losslessly preserved in `evidence/avatar-spike/sedona-01/observations.zip`; extract observations.jsonl alongside analysis.json to rerun the analysis. The uncompressed working file is ignored by Git. `scripts/Analyze-AvatarProbe.py` uses GeographicLib for the displacement bearings; analysis.json records its input SHA256. Phase summaries include circular heading offsets to avoid treating north crossings as full rotations.

| Test | Aircraft | Avatar / current object |
| --- | --- | --- |
| Seated view turns | Heading about 316.01 degrees, fixed | Avatar heading also fixed; current points to aircraft |
| Exit and stand | Object 524288 remains parked | Avatar 32768 has separate coordinates; current switches to avatar |
| First walk | Zero coordinate displacement | 18.2857 m displacement; Matt first sidestepped around the wing, so displacement bearing is not a facing check |
| Outside view turns | Coordinates and heading effectively fixed | Latitude, longitude and altitude exactly fixed; heading changes with view turns |
| Turn right and second walk | Zero coordinate displacement | 15.8888 m at 42.024641 degrees true; final heading 42.023462 degrees, difference -0.001179 degrees |

These observations establish an independently moving avatar position and a usable walking facing/yaw value. They do not establish independent camera world position, camera pitch, or whether head/body separation is exposed in other controls or VR modes. Outside, view turning changed avatar yaw; inside, looking around did not change avatar yaw. Therefore label the survey input **avatar facing**, not a universal camera gaze direction. The final direction-of-travel comparison is evidence for this session, not an accuracy guarantee for every aircraft or control mode.

Avatar object altitude remained stable while standing and changed slightly with movement on the ramp. At the final point it was 1458.279663 m. The avatar-targeted GROUND ALTITUDE returned 1458.280467 m (about 0.8 mm higher), while aircraft-targeted ground altitude remained 1458.261134 m. This suggests object-local ground values on this simulator build, contrary to the official aircraft-local note. Record the discrepancy rather than rewriting the documented contract. An independent sloping-ground/step test is still needed before using this value as a robust airport terrain elevation. Sub-millimetre agreement between two simulator values is not evidence of real-world accuracy.

## Survey workflow now selected

Use the explicit avatar proxy while walking; require the current object to match it and remain distinct from the aircraft. Stop, face the intended runway direction, and capture a stationary sample. `scripts/Capture-AvatarSurvey.py` exports latitude, longitude, avatar object altitude and true facing from an explicitly selected recorded phase. It requires at least five seconds, at least ten finite values per required field, on-ground state, position stability within 5 cm per axis and heading stability within one degree. It refuses overwrites. It does not change MSFS or generate airport XML.

The first point is preserved in `evidence/avatar-spike/sedona-01/survey-point.json`: latitude 34.84966717418079, longitude -111.78953721758243, facing 42.023461977 degrees true. This is a prototype survey point with `suitableForAirportGeneration: false` until altitude semantics are validated. Successful stationary capture and rejection of seated and view-turning phases are recorded in capture-checks.json.

```powershell
python scripts/Capture-AvatarSurvey.py evidence/avatar-spike/sedona-01 second-walk-done evidence/avatar-spike/sedona-01/new-survey-point.json
```

A camera-bearing/fixed-ground-anchor workaround is unnecessary for this tested walking mode. If avatar data are unavailable or fail these checks, reject capture and retain aircraft-based surveying as the fallback to investigate; never silently substitute aircraft or camera coordinates. No airport or UI was built, no scenery installation changed, and no movement was commanded by the diagnostic.
