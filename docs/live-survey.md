# Live capture-survey prototype

The CLI now connects directly to MSFS for an explicit walking-avatar ground point. The previous offline diagnostic remains preserved. No UI or aircraft movement is involved.

```powershell
& .tools/dotnet/dotnet.exe src/App/bin/Release/net8.0/FrontierAirportBuilder.App.dll capture-survey evidence/live-survey/new-point 'C:\Users\mattr\Desktop\MSFS 2024 SDK'
```

MSFS must already be running, with the aircraft parked and the user outside, standing upright and facing the desired direction. Use a new directory for every attempt. Each attempt saves result.json, and after binary checks metadata.json plus timestamped read-only observations. Success adds survey.json. Refusal returns nonzero and never produces a survey point. Ctrl+C cancels and closes the connection. The command waits at most 60 seconds for a valid window.

The explicit SDK proxies are aircraft 0, avatar 4294967168 and current 4294967169. Each round issues new request IDs, reads one grouped numeric definition per proxy and checks the returned real IDs. Missing, duplicate, stale or malformed replies and SDK exceptions refuse immediately. Replies in a round must arrive within 250 ms of each other. A successful window spans at least five seconds and 16 complete rounds with no gap over one second. Constant coordinates are permitted for a stationary avatar; freshness comes from distinct live requests and replies, not numerical movement.

Across the window the current object must equal the avatar and differ from the aircraft. All numeric values must be finite and in supported ranges. Avatar/current/aircraft must report on-ground, and the avatar must not be crouched. Avatar position must stay within 5 cm of the window start, heading within one degree circularly, and ground elevation within 3 cm. Object altitude must be within 5 cm of avatar ground; their offset may vary at most 3 cm and must agree with altitude-above-ground within 2 cm. Current-object readings must match avatar readings within 2 cm and 0.1 degree. Aircraft position/ground must remain stable. Motion resets the opportunity for a stable window; nothing falls back to aircraft or camera data.

## Empirical scope

The command is bound to SDK 1.2.4, its checked SimConnect header/DLL hashes and the tested MSFS executable hash (1.8.16.0). Updates refuse until revalidated. See [Sedona acceptance and caveats](avatar-elevation-validation.md), including Microsoft's contrary aircraft-local documentation and the 66 cm A revisit accepted by Matt on 7 September. This is an empirical prototype source, not a cross-version SDK guarantee or a real-world survey.

A single stationary capture cannot independently rediscover whether the ground value follows the avatar across terrain; that qualification comes from the preserved multi-height Sedona calibration. Runtime identity, offset, current-object comparison and binary checks guard its use. Capture does not certify the whole runway footprint as flat or free of airports/obstacles. Site review remains a separate step.

The saved AltitudeMeters uses avatar-targeted GROUND ALTITUDE; AvatarObjectAltitudeMeters and ObjectGroundOffsetMeters remain separate evidence. MagneticVariationDegrees is the raw MAGVAR observation, not silently an east-positive model value; verify its convention before creating the airstrip definition.

## Implementation verification

169 unit tests pass, including five-second minimum, identity/alias refusal, airborne/crouch refusal, non-finite and inconsistent altitude refusal, stale responses at the core boundary, movement and circular heading behaviour. Build has zero warnings/errors. The first runtime attempt correctly refused because MSFS was closed; successful live capture in Namibia remains pending.

Dirt generation has a separate deterministic material ID and DIRT physical surface with a tan diffuse material and ground merging. It contains no asphalt texture references. The canonical FPT1 template and installed package remain untouched. This new material is compilation/visual-test pending until the Namibia package is built; unit tests establish XML structure only.
