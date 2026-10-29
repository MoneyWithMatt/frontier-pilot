# Frontier Pilot architecture

Current source map; historical implementation reports remain under `docs/` and `evidence/`. See [ROADMAP](ROADMAP.md) for acceptance boundaries and [TESTING](TESTING.md) for commands. The public repository integrates through `master` via reviewed pull requests.

## Projects and execution

| Project | Responsibility |
|---|---|
| `src/Core` | Domain records/transitions: geography, airport layout, economy/ledger, cargo, flight evidence, physical aircraft, condition and maintenance |
| `src/MsfsSdk` | Native SimConnect transport, SDK inspection, XML generation, Package Tool adapter, package validation and receipt-owned installation |
| `src/Workflow` | Persistence, view models, flight/load/economy coordination, career/employment, fleet observation, live/dry-run builder backends |
| `src/Desktop` | Windows x64 WPF, Mapsui 5.1.0 maps, navigation, dialogs and application composition |
| `src/App` | CLI inspection, scenery generation/building, survey capture and condition recording |

Dependencies: Desktop -> Workflow -> MsfsSdk -> Core; CLI references the three libraries. These target .NET 8; Desktop targets `net8.0-windows10.0.19041`. The SDK is pinned by `global.json`. There is no Frontier server or database service.

`Desktop/App.xaml` starts `MainWindow`; `App.OnStartup` handles the career-reset restart handshake. `MainWindow` selects `--dry-run` and owns the 30-second economy refresh timer. `BuilderViewModel` composes tracking, Jobs, loading, fleet and operations. `TrackingViewModel.TelemetryChanged` forwards validated observations; last-known display data cannot qualify a delivery.

`App/Program.cs` provides `inspect`, `generate`, `build`, `capture-survey`, `record-condition` and `condition-airframe`. **`build` invokes Package Tool and can launch MSFS; ordinary CI never invokes it.** .NET building/publishing is independent of scenery compilation.

## Storage authority

Normal career data is under `%LOCALAPPDATA%/FrontierAirportBuilder/desktop`. Never point tests at it.

| Store | Current authority |
|---|---|
| `economy/economy-state.json` | Schema 11; ledger, cargo jobs/inventory, typed passenger transfers with embedded flight progress, real-time schedule, operations and maintenance charges |
| `economy/flight-attempts.json` | Schema 5; persisted flight facts/proof and separate bounded diagnostics |
| `economy/aircraft-loading.json` | Schema 5; managed cargo manifests, intents and fuel continuity |
| `fleet/aircraft-fleet.json` | Schema 1; immutable airframe GUIDs, selected binding, condition, last-known location, history; `.previous` backup |
| `tracking/aircraft-state.json` | Preserved unassigned legacy observations; not authoritative physical identity |
| Saved airports/sites | Editable JSON source of truth; scenery packages are derived artifacts |
| Career/catalogue/cache/receipts | Career and employer choices, authored routes, simulator lookup cache, verified workspace/package ownership |

See `EconomyPersistence`, `FlightAttemptPersistence`, `AircraftLoadingPersistence`, `AircraftFleetStore`, `CareerStore` and `AirportLibrary`. Stores validate schemas and preserve corrupt/future files. They use flush/replace patterns and Windows named semaphores; fleet commits also check expected revision. Cross-file operations use durable intent and idempotent reconciliation, not a global ACID transaction.

Changing `BuilderServices`' root alone does not relocate all gameplay stores: `BuilderViewModel` also derives a career root. A unified live-test data-root boundary is deferred. Existing tests inject temporary stores/dry-run services.

## Simulator boundaries

Native adapters load the installed SDK's `SimConnect.dll` dynamically; no proprietary SDK is required to compile .NET projects.

| Adapter | Behavior and boundary |
|---|---|
| `LiveAircraftTelemetry.Run/Classify` | Aircraft/avatar/current proxies, approximately 250 ms numeric requests and ten-second title/model requests; identity/value agreement, malformed-packet and disconnect guards |
| `LiveSurveyCapture.CheckConnection/Capture` | Exact SDK/simulator/header hashes plus empirical avatar stability/elevation gates; Microsoft contradiction and 66 cm return-to-A caveat retained |
| `LiveAircraftCondition.ReadAsync` | Read-only field availability and runtime events; recorded values do not certify aircraft compatibility |
| `SimConnectAircraftLoading` | Explicit payload/fuel writes with coordinator intent/readback; legacy tank view and unavailable structural station limits remain limitations |
| `LiveAirportCatalogue.ReadAsync` | Complete worldwide facility list; lookup is not runway/scenery acceptance |
| `PackageBuilder.BuildAsync`, `CommunityInstaller` | Official scenery compiler, output validation, exact receipt/hash ownership, backups and simulator-closed guards |

`FlightAttemptTracker` (including its passenger monitor) alone assembles delivery proof. Arrival requires flight facts and continuous stopped presence; explicit cargo unload/verified transfer or passenger disembarkation commits economy consequences. Current route distance policy is `RequiredDistanceForRoute` (0.5–10 NM), alongside the separate two-NM movement gate; older V0 reports describe the historical fixed ten-NM rule. Completed deliveries cannot emit more progression events.

`FlightAttemptTracker.PassengerMonitor` reuses the flight records, policy, airborne-time/proof helpers for typed Offshore airport transfers. `PassengerTransfersService` stores passenger progress and terminal disembarkation/wage atomically in the economy file; it never creates cargo shipments or inventory for people. Boarding is explicitly simulated. Shared fleet/endpoint guards apply, and pending cargo recovery blocks passenger boarding. Platform endpoint missions remain rejected until a future qualified adapter/registry is accepted. See [passenger transfer scope and live procedure](docs/offshore-passenger-transfers.md).

Three clocks are distinct: economy uses real UTC; flight wages accumulate valid aircraft observation intervals; physical-airframe exposure uses validated simulator seconds and pause/slew/continuity guards. Do not merge their semantics.

Physical GUIDs do not make title/model or transient simulator object IDs ownership proof. Production wear/fault/position qualification and maintenance facilities remain disabled/unavailable as described in [physical-aircraft-v1](docs/physical-aircraft-v1.md).

## Offline and external dependencies

`ReplayWorkflowBackend` replays historical survey frames, generates real XML and simulates compilation. Its `frontier_test_only` packages are not flyable and must never enter real Community. General flight/condition replay is later work.

Mapsui/OSM tiles are a limited prototype dependency. SDK installation downloads are interactive and separate from CI. The self-contained development executable includes .NET, not MSFS or its SDK. Existing Microsoft template provenance/EULA remain in `src/MsfsSdk/Templates`; do not copy installed assets or samples into new work.
