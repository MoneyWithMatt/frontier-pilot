# Offshore helicopter landing proof

## Current result
**UNVERIFIED**: no H125 flight, deck contact or round trip performed. Platform location/identity not yet established. Existing MSFS UI has loaded the selected H125/FYWB H01 ground scene to **Ready to fly**; left at that preflight screen while the new read-only probe's exact execution approval is pending. No new telemetry probe or Frontier application executed live.

## Inspection
Isolated `codex/offshore-landing-proof` from origin/master `d5ff51650789e45e46c8828893acaba071d957e7`. Cargo PR #8 branch and reference attachments untouched. Open PR #3 inspected: shared passengers and platform/H125 gates are unmerged; no successful deck qualification. No merge of unrelated branches.

`Workflow/FlightAttemptTracker` requires continuous on-ground/low-speed arrival, route movement and tracker-created proof, but airport-area arrival cannot identify a particular elevated deck. `LiveAirportCatalogue` reads complete worldwide airport packets, not arbitrary 3D scenery. `LiveAircraftCondition` is a separate read-only diagnostic whose output never grants delivery evidence. Scenery compilation does not establish elevated collision.

## Actual desktop observations
Supported `@oai/sky` Windows automation can control the user's real MSFS window, title version **1.9.21.0**. Entered main menu, Free Flight and Helicopter catalogue; Airbus H125 / Asobo Studio / Passengers is available. Availability is not loaded-aircraft or flown evidence. Initial single clicks/Return did not advance Start; a fresh double-click did. No controller bindings/settings changed.

Read-only StreamedPackages inventory includes H125, worldwide oil-rig, oil-rig travelbook, worldwide helipad and offshore boat packages. Some entries are empty cache folders; H125/helipad content includes opaque streamed archives. No archives decrypted, assets copied or packages changed. Package names establish no coordinates or collision near FYWB.

## Diagnostic artifact
`scripts/offshore_probe.py discover --sdk <root> --output <new.jsonl> --radius-nm 100` reads the complete worldwide list, finds unique FYWB from the actual response, records nearby identifiers/regions/elevations, then queries airport/helipad children. It retains wire replies and incomplete/unparsed responses. Later facility reply IDs differ around the SDK conditional PICK entry; packet type and shape are recorded for empirical review, never game authority. Timeouts do not imply absence.

`record --sdk <root> --output <new.jsonl> --seconds 120` uses aircraft proxy 0, bracketed title and fresh numeric reads. Optional fields are individually probed; core/identity failure stops the log. Contact indices 0..19 are candidates, not a verified H125 skid map. Source/DLL hashes, timestamp, units, simulation time/rate/slew and raw motion/contact data are retained. Pause/menu status still needs independent observation. No setter/event-control bindings, placement, career access or automatic landing qualification.

## SDK findings — not live qualification
- [AIRPORT/HELIPAD facility definitions](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/facilities/simconnect_addtofacilitydefinition/): helipad count, coordinates/elevation, dimensions, heading and surface. No collision guarantee.
- [Aircraft motion variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/): terrain and obstacle-inclusive AGL differ; surface-relative speed may also be zero during a hover. Proximity/zero speed alone cannot establish touchdown.
- [Contact variables](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/aircraft-simvars/aircraft-brake-and-landing-gear-variables/), [numeric indices](https://docs.flightsimulator.com/html/Programming_Tools/SimVars/Aircraft_SimVars/Aircraft_Brake_Landing_Gear_Variables.htm?agt=index) and [helicopter variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Helicopter_Variables.htm): candidates require aircraft-specific readback and physical interpretation.
- [Boat attached helipads](https://docs.flightsimulator.com/msfs2024/flighting/content-configuration/modular-simobjects/simobjects/boats/) have separate moving-platform configuration. No moving-vessel qualification here.
- [Helipad XML](https://docs.flightsimulator.com/msfs2024/flighting/content-configuration/environment/airports-and-facilities/airport-xml-properties/) and [INITPOSITION](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/structures-and-enumerations/simconnect_data_initposition/): placement/spawn is separate from physical flight/contact. No position writes used.

## Remaining
Approve exact diagnostic, load H125 at FYWB and discover candidates. Record runway baseline, platform visual identity, hover/contact/rest and actual flight if practical. Discrete desktop inputs are not a continuous joystick interface; a separately reviewed bounded control harness may be needed. Custom scenery remains a conditional fallback, not yet justified/built. Experimental round-trip mission is deferred until physical landing/detection works; normal production gates remain disabled.

## Automated evidence
Diagnostic implementation `fdf5f3f89995039957968c635abeb8a565982e5b`; source script SHA256 `9f2ce5a0c7b9c0d2a89bc4cf2671fbbd440eeed46e464bc8492f75f7ac4acacb`.

Local full BuildTest on dirty base `d5ff516` plus this probe/docs: **744 .NET passed / 0 failed / 0 skipped**, Release zero warnings/errors, SDK 8.0.424. Ten offline Python packet/allowlist tests passed separately. The 744 count is master's baseline; this branch intentionally does not include the unmerged cargo/passenger suites.

Commands: `python -m unittest discover -s tests/OffshoreProbe -v`; `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-offshore-proof-01`.

[Hosted Windows run 46](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41424653817), clean merge `604b8cffd3eff56a589a34e117606d141200519e` of head `fdf5f3f`, passed **744/0/0**, ten Python cases, nine simulated guards, both WPF programs and packaging/uploads. [Diagnostics](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41424653817/artifacts/13131321939) downloaded; archive SHA256 `e6d05e3f15e4da464bdb0a8d8b1ff1d79a5cd944bd2669a362f61d2fcc9a109a`, clean regression/smoke/package receipts and TRX independently verified. The [incidental app bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41424653817/artifacts/13130992860) was not downloaded/executed; it is not needed for this source probe. No automated result establishes live landing acceptance.

[Draft PR #9](https://github.com/MoneyWithMatt/frontier-pilot/pull/9) remains unmerged. New probe execution approval is pending. No packet test failures or assertion weakening. A combined documentation-edit/build shell request was rejected by execution review before running; edits were safely completed through apply_patch and the ordinary BuildTest command ran separately. SDK managed reflection inspection also failed to load its native dependency; no simulator action came from it.
