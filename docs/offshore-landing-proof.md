# Offshore helicopter landing proof

## Live result — 2026-11-02

| Question | Result | Boundary |
|---|---|---|
| Actual Windows/MSFS access | PASS | Controlled running MSFS 2024 1.9.21.0 through supported Windows automation. |
| Discover an existing platform | PASS | Complete facility scan, helipad detail, simulator UI name and visual inspection of OL400 / Valaris 109. |
| H125 rests on elevated deck in normal physics | PASS, static/repositioned only | 34.36 continuous simulation seconds of rest candidates, slew off, on-ground/contact and visual deck evidence. |
| Flown FYWB → platform approach/touchdown | UNVERIFIED | No controlled outbound flight. Placement cannot satisfy this. |
| Stationary hover negative control | UNVERIFIED | Failed release contains airborne data, not controlled stationary hover. |
| Return flight / Frontier mission completion | UNVERIFIED | No return, tracker integration, credit or wages. |

This is **not a flown landing or completed Offshore mission**. The aircraft was repositioned; the first normal-physics read already reported on-ground. This establishes that the deck supports the loaded H125 and exposes distinct surface/contact telemetry. It does not prove dynamic touchdown, map skid indices or qualify production missions. The conditional experimental round trip remains deferred.

## Isolation / existing systems

Branch `codex/offshore-landing-proof`, [draft PR #9](https://github.com/MoneyWithMatt/frontier-pilot/pull/9), starts directly from master `d5ff51650789e45e46c8828893acaba071d957e7`. Never merge automatically. Unmerged passenger PR #3 and cargo PR #8 were inspected, not modified or merged. No production source, career rates, saves, payload, failures, ownership or Community changes.

`src/Workflow/FlightAttemptTracker.cs` remains the sole delivery-proof creator; airport-area arrival cannot identify a particular elevated deck. `src/MsfsSdk/LiveAirportCatalogue.cs` discovers facilities, not arbitrary scenery collision. `src/MsfsSdk/LiveAircraftCondition.cs` is diagnostic. Future experimental arrival needs a qualified specific pad, uninterrupted physical contact/rest, prior flight proof and outbound/return stages. These scripts never create economy evidence.

## Actual simulator discovery

The read-only scan received **85,717 facilities**, with **16 within 100 NM of FYWB**, then queried every nearby facility without incomplete detail replies. Actual detailed packet kinds were 28 (data) and 29 (end), resolving conditional-PICK numbering ambiguity for this SDK build. Raw replies remain private.

| Identifier | Helipad centre latitude, longitude | Reported MSL deck | Size / heading | Status |
|---|---|---|---|---|
| OL400 / Valaris 109 | -22.93892875313759, 14.50461745262146 | 39.89600372314453 m | 19 × 19 m / 180° | Visual structure and normal-physics static deck rest tested; harbour-side rig in this scenery. |
| OL617 | -22.807804942131042, 14.492918998003006 | 47.06900405883789 m | 19 × 19 m / 135° | Facility candidate only. |
| OL689 | -22.786728143692017, 14.501441717147827 | 36.900001525878906 m | 19 × 19 m / 180° | Facility candidate only. |

OL400 is **8.186 NM** from FYWB's world-list reference. Its airport reference (-22.938931100070477, 14.50408548116684, altitude 0 m) is about **54 m west of its pad centre**. Using the airport reference can place an aircraft below the structure. DevMode airport search identified **OL400 — Valaris 109**. This does not establish current real-world vessel position or exact scenery-package attribution.

FYWB altitude differs by record: world-list 96.316 m, AIRPORT detail 84.11 m, H01 helipad 82.193 m. H01 centre is -22.977764792740345, 14.64223399758339. The baseline H125 was stationary near that pad. Never substitute airport reference altitude for deck elevation.

## Live sequence and failures

Matt approved the presented diagnostic and continued experiment with **“I approve whatever you need.”** Actual aircraft TITLE was `H125 Passengers`, real object ID **540672** returned through request proxy **0**. UI: Airbus H125 / Asobo Studio / Passengers. Its advertised seat count is not verified passenger configuration.

1. Complete discovery succeeded. Two initial recordings failed because the recorder incorrectly required real object ID to equal proxy 0. Both error logs are preserved. It now binds actual identity from the explicit aircraft TITLE response and checks every later reply against it. No camera/avatar substitution or removal of identity checks. A 30-second FYWB baseline completed.
2. Entered active cockpit at FYWB H01. No engine/failure/payload controls sent. DevMode Teleport → OL400 enabled slew and placed the aircraft below the deck at the airport reference. Contact bits/on-ground in that slew state are invalid landing evidence.
3. Separate one-shot helper placed the stationary H125 at the pad centre, deck + 8 ft CG altitude. Readback: obstacle-inclusive AGL ~8.14 ft, on-ground 0, slew 1. This is placement, not hovering.
4. **Failed release:** disabling slew through the UI produced ~99 kt groundspeed and departure from the pad. Later samples show a shoreline touchdown, not a platform landing. Re-enabled slew; preserved all data. Neither trajectory nor its ground contact qualifies a mission. The cause of that transition is not fully established.
5. Bounded release helper sent SLEW_OFF **before** the single INITPOSITION write (deck + 3.5 ft, OnGround=0, airspeed=0). First readback already reported ground contact at the deck; no resolved descent/touchdown was captured. It observed normal physics for at most 35 wall seconds, then restored slew. No continuing position corrections. Independent recorder/UI confirm slew off during observation and on afterward.
6. Offline analysis found **34.3584 continuous simulation seconds**, 129 accepted rest-candidate samples. These include placement and remain diagnostic. Saved cockpit screenshot during normal physics and exterior screenshot after slew restoration. Private evidence is not uploaded to public GitHub.

Actual PC access works. The remaining limitation is **dependable continuous helicopter flight control**. The desktop interface supplies discrete inputs; no calibrated cyclic/collective/pedal controller is established. SDK collective event range/helper conversion also needs empirical calibration. Do not guess control signs/ranges or claim an unattended flight.

## Recorded static telemetry

Settled subset **10:51:42.051–10:52:10.911 UTC**: 108 samples over **28.864 simulation seconds**, same aircraft, rate 1, slew off, all on-ground. These are actual simulator readings, not fixtures.

| Field | Range |
|---|---|
| Aircraft MSL altitude | 133.20992–133.21160 ft |
| Ground/surface altitude | ~130.754358 ft / 39.854 m, ~0.042 m below facility elevation |
| Obstacle-inclusive AGL | 2.45556–2.45724 ft |
| AGL minus CG | 0.05556–0.05724 ft |
| Groundspeed | 0.000156–0.005123 kt |
| Vertical speed | -0.0000614–0.001983 ft/s |
| SIM ON GROUND / IS SLEW ACTIVE | 1 / 0 |
| Contact | Several indices 8–15 set; individual bits vary. No verified skid map. |
| Terrain AIRCRAFT AGL | ~133.23 ft, unlike obstacle-inclusive AGL ~2.457 ft. |

At 10:52:00.928 UTC: position -22.93892875958535, 14.504617428771214, essentially pad centre; rotor RPM ~406, combustion 1. Touchdown lat/lon still referenced the earlier shoreline touchdown (-22.941588472383653, 14.504482267875531). Those stored fields cannot identify this new deck rest. Surface-relative zero speed also cannot prove contact.

## Diagnostic code / authority

- `scripts/offshore_probe.py`: read-only native allowlist, complete facility scan/raw packets, fresh title-bracketed core/optional readings, hashes/time/units. Core failure stops; optional failure is explicit. No setters, control events or career access.
- `scripts/offshore_static_placement.py`: **separate mutation artifact**. Exact H125, stationary slew within 500 m of OL400, fixed pad only, one SDK INITPOSITION write; optional SLEW_OFF/ON and bounded release watchdog. Aborts on >10 m offset, >5 kt, >10° tilt or altitude outside deck -3/+15 ft; at most 35 wall seconds. No arbitrary coordinate interface, engine/fault/payload controls or flight proof. Disconnect can prevent slew restoration. Never ordinary CI or blind live retry.
- `scripts/offshore_rest_observer.py`: offline diagnostic tolerances, not production arrival rules. Requires pad/elevation, identity/time continuity, rate 1/non-slew, on-ground/contact/low motion. Pause, gap, missing data or identity change resets current interval. Repositioned/spawned aircraft can satisfy it; no touchdown, flight, mapped skid or mission authority.

Pause handling is conservative time-continuity plus UI observation, not a qualified pause-event adapter. Effective aircraft CFG/contact geometry and complete package-version hash remain unavailable in opaque streamed archives. No decryption, asset copying, installed SDK edits or scenery installation. Existing platform suffices for further tests; custom scenery is not justified yet.

## Official SDK findings — separate from live evidence

- [Facility definitions](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/facilities/simconnect_addtofacilitydefinition/): helipad children expose position, metres elevation, dimensions and heading, not collision guarantees.
- [Motion/AGL](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/), [contact](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/aircraft-simvars/aircraft-brake-and-landing-gear-variables/), [helicopter variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/helicopter-variables/): terrain/surface signals differ; numeric contacts require aircraft-specific interpretation.
- [INITPOSITION](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/structures-and-enumerations/simconnect_data_initposition/) uses feet/degrees and DWORD ground/airspeed, may reload scenery, is unsuitable for continuous user-aircraft motion. [SetDataOnSimObject](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/events-and-data/simconnect_setdataonsimobject/) documents full structure writes; HRESULT is not contact confirmation.
- [Slew events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-flight-control-events/) are diagnostic, never flight. [Legacy helicopter events](https://docs.flightsimulator.com/html/Programming_Tools/Event_IDs/Helicopter_Specific_Events.htm) need current-aircraft calibration: installed `ModelBehaviorDefs/Asobo_EX1/Generic/Shared/Helpers/Helpers.xml` collective `2 * 16384 -` conversion contradicts a simple unipolar assumption.
- [Boat helipads](https://docs.flightsimulator.com/msfs2024/flighting/content-configuration/modular-simobjects/simobjects/boats/) have moving-surface considerations; no moving-vessel test here.

## Private evidence / exact provenance

Ignored `.ci-output-offshore-live` retains raw telemetry, failures, cache hashes and requested screenshots. Logs record exact executed source hashes, which can precede later log-label edits. No personal data uploaded publicly.

| File | SHA256 |
|---|---|
| fywb-discovery-01.jsonl | `49aa5343f5dcd3e56bfbc14382f0f79b88de66f1fb780580d06f3d312fceaa91` |
| h125-preflight-03.jsonl | `01a864203383e1765523136248b6a08cacab089b58b4300cec6296c24de88ca8` |
| ol400-static-01.jsonl (failed release retained) | `5e51291ffb4e792abedc41caa3ff1710b2b2cd3a8181f1c73c5696c27e82e95b` |
| ol400-static-02.jsonl | `15dfd743ee6d313231992b58fbe622a81eeed5e340cadb9a7d2e2b9bc3a29336` |
| ol400-placement-01.jsonl | `fdde2b22036f72ef33b228aa7f03f8944721a02c71f751c7aeaa81769f0e0180` |
| ol400-placement-02.jsonl | `c60520ab0f9758a050b775952b5ebc6286c02d7447984224499c1bb25811f86c` |

Simulator version **1,9,21,0**, executable SHA256 `5cccf10e882be7aacd56e95e06ffb12986bcae08f3719879924b89cef1fbea70`; SDK DLL `e54a505be0b0a4cdc98dfb45f0c202ba9637f5d494c0919040bc89ac67d92adb`; header `3684c4b50630db2abec9815105498fa5b921c653f57423b9b5e6214a60caabea`. Executed recorder hash `e4700a1f3d482a6fbe77073722b2aad3c3259a5149e83edff0b123dc00513bad`; executed bounded-release hash `fd9405b99d230e6f153a38972713e2dc57b16fce1109b2c5a5e51df9699518cb`. H125 cache includes two readable archive hashes and an unreadable locked sound file; no complete effective aircraft-version identity.

## Automated verification

`python -m unittest discover -s tests/OffshoreProbe -v`: **21 passed / 0 failed**, no skips, no native connection. Packet/identity/allowlist/placement/watchdog/rest cases are synthetic, not simulator acceptance.

`pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-offshore-proof-02`: **744 .NET passed / 0 failed / 0 skipped**, Release zero warnings/errors, SDK 8.0.424. Dirty source `7834fec899b064f40c4d037d5055fb2767b3ec73` plus recorder/placement changes; offline observer added afterward, no .NET change. Receipt/TRX retained. 744 is master's baseline; unmerged cargo/passenger suites were not removed.

Latest implementation **`88e70d7dba94450600cdf9def01a3e318b87c583`** passed [hosted run 48](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41433936110) on clean merge **`2577cbc1a09e7e53e66936323a6526da9a22a463`**: **744 .NET / 21 Python / 9 simulated guards / both WPF programs / packaging and uploads**, no test failures/skips. [Diagnostics](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41433936110/artifacts/13134549391) SHA256 `f4f919b593bc2b81449b5ab2be0185f4c13a17b093792272378ea052ad6bb087` independently checked against clean source receipts, TRX and smoke logs. Incidental app artifact was not downloaded/executed. Earlier runs 46/47 passed older code. No local WPF or packaged Frontier app launch. Follow-up checkpoint changes documentation only; see handoff for implementation provenance.

The simulator was left on OL400 with slew restored and Escape pause menu open. No further live operation should start without a fresh state check.

## Next focused live procedure

1. Freshly inspect current simulator, source and private logs. Do not blindly repeat a failed release. Keep aircraft/object/version identity throughout.
2. Establish a concrete reviewed bounded cyclic/collective/pedal controller with ground-only range/sign/readback calibration and telemetry-loss/limit abort, or use a human pilot. This is the smallest missing capability; no further PC-access setup or purchase is needed.
3. At FYWB record controlled lift, stationary hover and landing. Contact must clear while hovering and return at actual contact; no slew, pause or reposition in qualifying intervals. Resolve H125 skid mapping where possible.
4. Fly FYWB H01 to OL400 **helipad centre**, recording approach/hover/descent/touchdown and >=20 continuous seconds at rest. Independently inspect skids/deck; retain failures. Reject airport-reference proximity, stale touchdown coordinates or zero speed alone.
5. Return to FYWB with actual flight evidence. Then implement a separate no-pay experimental round-trip state machine using tracker authority, identity/teleport/disconnect/restart guards and regression tests. Keep production H125/platform destinations disabled until separately accepted.
