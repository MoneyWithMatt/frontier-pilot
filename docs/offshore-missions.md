# Offshore missions and acceptance boundaries

Draft [PR #10](https://github.com/MoneyWithMatt/frontier-pilot/pull/10), `codex/offshore-missions`, starts from PR #8 `47698b1704039f12dc3875fb6e2359a12b0ac13f`, already containing PR #3/6/7. None is assumed merged. Selected PR #9 pure contact logic and read-only recording are reused; no controller or diagnostic payload writer is imported. Other branches and master are untouched.

## Player flow

Offshore employees open **Offshore operations** from Jobs. Select the company Vision Jet in Fleet, accept an observation mission, connect tracking, stop at the resolved Walvis Bay base for ten continuous seconds and Begin. Depart beyond the base area, reach the operating area, observe continuously airborne, then return and stop ten seconds. Complete settles one hourly wage and projects a fleet history event.

`Workflow/OffshoreObservationDefinitions.json` configures the first 5 NM / 300-second area using visually confirmed OL400 registry geography. This is a local harbour-area gameplay patrol, not a distant drilling-platform flight or actual equipment/incident inspection. The UI shows purpose, route, eligibility, planning estimate, area distance, observation progress, verified airborne time and blocking reasons. Observation requires at least 100 ft above the reported surface; the pilot chooses a safe altitude and remains responsible for performance.

The Personnel tab embeds the existing passenger manager. Suitable resolved Eros/Hosea Kutako airport offers request two technical specialists through the shared manifest, retaining individual declared masses. This is a mission requirement, not an aircraft capacity claim. Existing other eligible airport routes and Missionary rules remain. SF50 physical boarding remains blocked pending effective configuration and qualification.

The Platforms tab displays qualification evidence and unavailable operations. Its explicit, bounded, read-only experimental recorder watches stable contact → contact loss → stable return. It neither flies nor changes payload, career progress or money. Local JSONL diagnostics never enter the repository or normal mission economy.

| Capability | Implementation | Remaining acceptance / restriction |
|---|---|---|
| Vision Jet observation and return | Production UI, tracker, persistence, atomic hourly wage | Automated and synthetic WPF; new live round trip pending |
| Airport personnel transfers | Shared lifecycle/loading/recovery, role-based two-person offers | SF50 effective seat/station configuration unavailable; physical boarding blocked |
| Platform visit | Registry, normal outbound-plus-deck adapter, separate no-pay recorder | OL400 static-contact only; no career platform mission generated |
| Platform cargo delivery and return | Per-operation qualification contract; shared cargo manager preserved | Not wired/enabled as a career mission; ballast diagnostic insufficient |
| Platform personnel delivery/pickup | Per-operation gates; shared passenger manager preserved | H125 configuration/loading/flown completion unqualified; no offers |
| Seated models / animations | No reliable mechanism established | Unsupported; mass does not prove a visible person |
| Mixed transport / observation overlap | Existing ownership safeguards maintained | Concurrent carriage/started observation explicitly excluded pending coordinated accounting and acceptance |

## Source map and authority

- `Core/OffshoreMissions.cs`: staged records, invariants and atomic completion/wage/balance. Economy schema 14 adds a default-empty collection; older cargo, resources, passengers, jobs and ledger remain. New transaction enum value is appended.
- `Workflow/OffshoreMissionFlightMonitor.cs`: `FlightAttemptTracker.OffshoreMonitor` owns normal observation sequencing. It never manufactures cargo evidence.
- `Core/OffshoreFlightProfile.cs`: existing read-only condition transport supplies mandatory native clock/rate, running/unpaused, position, freeze and slew telemetry. Exact profile, units, timestamps and identity are checked. No native setters.
- `Workflow/OffshoreMissionsService.cs`: employer rate/resolved base, shared operation lease, existing GUID fleet/dispatch/binding/continuity guards, durable recovery and idempotent fleet history projection.
- `Core/OffshorePlatforms.cs`: authored exact platform/aircraft/operation/simulator/scenery qualification. Visual geography can locate an observation area without enabling a landing destination.
- `Workflow/PlatformArrivalMonitor.cs`: future normal-platform adapter inside `FlightAttemptTracker`. Requires its own continuous loaded-origin rest, departure, movement, route distance and airborne time before 20 seconds of deck contact. Historical or diagnostic records cannot replace that native sequence. No production mission is connected while qualification is absent.
- `Core/DeckSurfaceContact.cs`: shared pure geometry/motion predicate only. Diagnostic and normal consumers retain separate history and capability gates.
- `Workflow/OffshorePlatformDiagnosticViewModel.cs`: explicit read-only PR #9 recorder, no economy service. Normal observation rejects its diagnostic profile.
- Existing `PassengerTransfersService`, `PassengerPayloadCoordinator`, `CargoJourneyService` and native operation lease retain physical ownership/readback/recovery. No parallel passenger/loading model or guessed mass accounting.

## Recovery and wages

The career rate remains **$800 per verified airborne hour**. Consecutive valid airborne pairs earn the smaller agreeing simulation/wall interval. Ground, pause, accelerated time, slew/freeze, missing/stale/implausible samples and disconnected periods do not pay. One started observation excludes cargo/passenger carriage; accepted reservations can coexist. This interim exclusion prevents overlapping employed wages without a new pay policy.

Interruptions reset unconfirmed dwell/rest and require explicit Resume near the last recorded position or at base in the original GUID/bound aircraft. Unsafe physics or an impossible observed jump requires base recovery. Base recovery far from the last location after observation also requires airborne area presence again before qualifying a return. Historical facts and verified time remain; missing flight time is never reconstructed. Final return proof freezes time. Completion/retries/restarts settle once atomically; a later fleet-history write failure can retry without another payment. No health, wear, location, hours or spawn authority is fabricated. Existing cancellation policy is unchanged.

## Platform evidence

OL400 / Valaris 109 H01: **-22.93892875313759, 14.50461745262146**, deck **39.89600372314453 m MSL**, 8 m contact radius. Historical PR #9 run54 recorded 20.218 seconds of real static H125 Passengers contact on MSFS 1.9.21.0; water-near/below controls were rejected. Run56 recorded a fixed 5 kg Cargo 1 diagnostic/restoration. These were not tests of PR #10.

Effective scenery version remains unresolved; no genuine flown approach/departure/return or passenger handling is qualified. Every landing operation remains disabled. Promotion requires reviewed version-specific evidence, never a save flag or automatic diagnostic receipt. Normal arrival requires contact, deck elevation, stable position/speed/vertical rate/attitude and native continuity after genuine outbound flight. Hover, sea beneath deck, proximity, teleport or constrained lifts cannot qualify.

## Aircraft configuration / visible models

Read-only inspection found streamed SF50 and H125 packages, but effective configuration is archived and no readable seat/station definitions were obtained. No archive protections were bypassed. Passive aircraft models, titles or station counts do not establish the piloted configuration, capacity or limits. No guessed SF50/H125 profiles were added; the existing Caravan candidate is unchanged.

Official SDK sources: [flight_model.cfg](https://docs.flightsimulator.com/msfs2024/retail/content-configuration/cfg-files/flight_model.cfg/), [native payload variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/), [authored passengers](https://docs.flightsimulator.com/msfs2024/retail/content-configuration/careers/commercial-flights-and-passengers/), [seats](https://docs.flightsimulator.com/msfs2024/retail/content-configuration/careers/general-career-information/aircraft-seats-and-seatbelts/). Existing adapters retain documented native indexing/units; configuration station indices must not be guessed. Authored seat graphs/native mission population do not establish a reliable dynamic SimConnect seated-person control. Physical payload, seated visuals and animations need separate evidence.

## Focused live acceptance — pending

Record the hosted artifact's full source SHA and executable SHA-256, review changes and obtain exact-build and allowlisted-scenario approval under AGENTS.md. Do not launch a schema-14 candidate against personal saves. Prepare an isolated Windows profile or separately reviewed temporary-store harness using the same production service with real read-only native telemetry; the app currently has no general data-root override. Any new harness also needs artifact approval. Previous PR #9 run59 approval does not cover this build; desktop activation last failed. No automatic install, scenery edit, flight controller or physical write is included in the observation scenario.

1. **Observation:** record simulator/SDK/aircraft versions and GUID binding. At FYWB in isolated stores, accept/connect/Begin after base rest; fly normally into the broad zone at a safe altitude, observe 300 continuous valid seconds, return and stop ten seconds. Capture stages and private telemetry, mission facts and one ledger receipt matching actual eligible time. Restart before settlement and repeat Complete; money/history must not duplicate. Ground spawn, pause/rate/slew/freeze/reposition and interruption controls must not qualify. Only an actually flown and recorded loop counts as live completion.
2. **SF50 boarding:** first obtain supported effective configuration and exact aircraft/version. Review a guarded profile before any writes. In an approved stationary scenario record baseline stations/unrelated occupants/cargo/fuel; board declared passengers, read back, retry/reconnect and unload only their owned mass. Test manual edits/refusal. Keep boarding blocked until successful. Visible people are a separate optional check, never inferred from weight.
3. **H125/platform:** establish exact scenery and aircraft configuration. Read-only static/water/hover tests and constrained lifts remain diagnostic. Record a genuine outbound, stable deck contact, departure and return before promotion. Then qualify actual supply/personnel handling and once-only mission completion independently per operation. One result must not activate other aircraft/operations.

No new live simulator session, payload change, aircraft movement or personal-save migration occurred for PR #10. The full requested career remains incomplete at these physical/configuration boundaries. Automated results and exact next steps: [handoff](handoffs/offshore-missions.md).
