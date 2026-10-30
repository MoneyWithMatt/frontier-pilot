# Frontier Pilot architecture

Cargo UI now follows the [guided loading flow](docs/guided-cargo-loading.md): logical quantity shortcuts and one context-sensitive action reuse the existing journey service, with explicit compartment choice, pending-edit locks and collapsed station/recovery details. Incomplete native identity reads have bounded read-only retries; preview failures identify native versus tracking/avatar authorization stages. Missing identity remains a hard refusal; the reported live Caravan outside-preview failure still requires exact-artifact acceptance.

Current source map; historical implementation reports remain under `docs/` and `evidence/`. See [ROADMAP](ROADMAP.md) for acceptance boundaries and [TESTING](TESTING.md) for commands. The public repository integrates through `master` via reviewed pull requests.

Starting employment automatically provisions the existing physical fleet through `EmployerFleetProvisioning`, called by `CareerStore.AcceptEmployment` and startup repair. Career schema 3 supplies a durable local player GUID; fleet schema 2 separates company ownership from player assignment, with idempotent allocation/base metadata. `AircraftFleetViewModel` filters authorization, associates only an explicitly confirmed exact simulator type at the recorded location and retains existing condition/dispatch/maintenance guards. A base allocation is not observed telemetry or spawn authority. Existing tracker-qualified cargo/passenger arrivals project last-known stops idempotently; no proof or hours are invented. [Implementation/migrations/live procedure](docs/employer-starting-fleets.md).

Multi-stop cargo adds `Core/CargoJourneys`, `Workflow/CargoJourneyService` and `CargoJourneyViewModel`, composed by `BuilderViewModel` into `Desktop/CargoJourneyControl`. Explicit consignment changes are independent of aggregate station targets. Economy schema 13 atomically owns GUID manifests, pending intents, handling receipts, immutable per-leg carried snapshots, tracker flight proof and once-per-leg employed wage receipts. Legacy attempts/loading stores remain intact; no mass/GUID adoption is inferred. The common guarded payload interface and operation lease are reused from shared passengers. MTOW/CG/overall payload and prototype mass budgets are advisory for both cargo and passengers; actual station constraints and readback remain enforced. [Implementation and evidence boundaries](docs/multistop-cargo.md).

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
| `economy/economy-state.json` | Schema 13; prior ledger/jobs/inventory/passengers/operations plus aircraft cargo manifests, explicit handling intents/receipts, per-leg flight snapshots and wage receipts |
| `economy/flight-attempts.json` | Schema 5; persisted flight facts/proof and separate bounded diagnostics |
| `economy/aircraft-loading.json` | Schema 5; managed cargo manifests, intents and fuel continuity |
| `fleet/aircraft-fleet.json` | Schema 2; existing GUID/binding/condition/history plus employer ownership/player assignment/allocation base and last transport stop; schema-1 data retained, `.previous` backup |
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

`PassengerTransport` owns shared person/group identity, declared mass, reservation, boarding, carriage, arrival and disembarkation independently of employer/pay. `PassengerPayloadCoordinator` persists station ownership/absolute targets before writes through `IGuardedPassengerPayloadSimulator`, checks immediate/retained readback and preserves partial/conflicting state. A shared named lease serializes cargo/fuel/passenger operations. `IPassengerAircraftConfigurations` provides evidenced seat-area capacity; production has an exact-hash Black Square Caravan Professional 0.1.2 gear profile, not inferred stock seating. Missing structural limits/arms and unsupported visuals are explicit. Vision Jet physical boarding remains blocked until its streamed effective configuration can be reviewed; no aircraft has live passenger acceptance.

`EmployedPassengerMissions` supplies Offshore specialist/executive and Missionary doctor/community-liaison visit policies. `PassengerTransfersService` reuses both-endpoint/fleet guards and `FlightAttemptTracker.TransportMonitor` flight/time/proof rules, then commits mission completion and existing $800/$400 hourly wages atomically after verified disembarkation. It creates no cargo inventory for people. The interim cargo/passenger exclusion lives in workflow/atomic dispatch checks, not shared domain validation. Legacy simulated aboard saves retain their historical completion without invented payload ownership. Platforms remain rejected pending qualified adapter/registry. See [shared passengers and live procedure](docs/shared-passengers.md).

Three clocks are distinct: economy uses real UTC; flight wages accumulate valid aircraft observation intervals; physical-airframe exposure uses validated simulator seconds and pause/slew/continuity guards. Do not merge their semantics.

Physical GUIDs do not make title/model or transient simulator object IDs ownership proof. Production wear/fault/position qualification and maintenance facilities remain disabled/unavailable as described in [physical-aircraft-v1](docs/physical-aircraft-v1.md).

## Offline and external dependencies

`ReplayWorkflowBackend` replays historical survey frames, generates real XML and simulates compilation. Its `frontier_test_only` packages are not flyable and must never enter real Community. General flight/condition replay is later work.

Mapsui/OSM tiles are a limited prototype dependency. SDK installation downloads are interactive and separate from CI. The self-contained development executable includes .NET, not MSFS or its SDK. Existing Microsoft template provenance/EULA remain in `src/MsfsSdk/Templates`; do not copy installed assets or samples into new work.
