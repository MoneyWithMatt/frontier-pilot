# Technical status — 2026-09-07

## Proven pipeline
SDK 1.2.4: C:\Users\mattr\Desktop\MSFS 2024 SDK; Package Tool: Tools\bin\fspackagetool.exe. User MSFS2024_SDK points to this root; old machine-level SDK configuration is unsuitable. PackageBuilder supplies the verified root to child processes. Installed SimpleAirport remains untouched. See sample-build-report.md and toolchain-diagnosis.md.

Core owns validated definitions, WGS84 geometry, heading normalization, magnetic reciprocal numbers and guarded survey validation. MsfsSdk owns deterministic System.Xml.Linq generation, the embedded Frontier template, native read-only avatar capture, Package Tool execution, artifact validation and CommunityInstaller. App remains the console spike. Workflow adds the testable async service boundary and MVVM state machine. Desktop is the single WPF window.

PackageOrderHint CUSTOM_AIRPORT is mandatory. Tool exit zero alone is insufficient: builder diagnostics, manifest, layout and non-empty BGL must pass. MSFS must be closed before compilation; Package Tool starts its headless simulator through Steam. Cancellation does not kill unrelated simulator/Steam processes; close any residual compiler-launched simulator before retrying.

## Live survey
SDK 1.2.4 and simulator file version 1.8.16.0 are qualified by exact binary/header hashes in LiveSurveyCapture. The explicit walking-avatar proxy must match the current controlled object and differ from aircraft. Five stable seconds, finite/consistent readings, parked aircraft, upright grounded avatar and altitude agreement are mandatory. No fallback.

Sedona observations support avatar-targeted ground altitude empirically: it followed avatar elevation while aircraft ground remained fixed; object altitude agreed within about 1 cm; B repeated within 1 mm. Microsoft documents GROUND ALTITUDE as aircraft-local: retain this contradiction. Return-to-A was 66 cm from the original location, outside the original 50 cm return criterion. This is not a supported general terrain API or a whole-runway slope survey. Details: avatar-elevation-validation.md and live-survey.md.

## Preserved in-simulator proof
FPT1: original proven sample-location airport.
FPSC: Skeleton Coast Test Strip, dirt, 200 x 20 m; centre -22.58616679921284, 14.539071285657734; elevation 13.94779940508306 m; true heading 108.63324971143716; runway 12/30.
User reported perfect terrain fit, no rocks, bumps, buried or floating edges. See skeleton-coast-test.md. These packages and previous evidence are protected from Desktop installation.

## Desktop 0.1
Launch scripts/Start-Desktop.ps1. Connect in a loaded flight, capture while standing at the runway centre, enter name/identifier/dimensions/surface preset and confirm the site check. Close MSFS, generate/compile, then install. Seven authored surface choices are compiler-proven; Dry Grass and Weathered Asphalt are labelled fallbacks and all new appearances still require visual acceptance. Identifiers accept 3–8 uppercase letters/digits with a leading letter. World-map uniqueness and airport separation remain manual checks.

State/evidence: %LOCALAPPDATA%\FrontierAirportBuilder\desktop. Each capture/build has a unique directory. Package installation stages and hashes the entire package; replacement requires an intact receipt in ownership/ and unchanged installed content. Previous packages are preserved under installations/. Reparse points and cross-drive staging are refused. Community is resolved from UserCfg.opt InstalledPackagesPath/Community2024.

All capture, filesystem, compilation and installation work runs off the UI thread. Cancellation is honoured before the short install commit/rollback section. Closing a busy window requests cancellation; close it again once idle. Captured survey stays in memory while MSFS is closed; raw evidence persists, but resuming a survey after restarting the builder is not implemented.

## Verification
See ../evidence/desktop-0.1/verification.md. Existing live simulator proofs were reused; no new airport was installed in this UI implementation task. A full user-driven WPF capture/build/install acceptance run remains useful.

## Workflow hardening update — 2026-09-07
The current UI uses explicit Disconnected, ReadyToSurvey, SurveyCaptured, Validated, Built and Installed states, plus recoverable Error/Cancelled. Validate is now an explicit prerequisite to generation. Services bind generated coordinates to the captured point and restrict installation to their issued build.

Use scripts/Start-Desktop.ps1 -DryRun for a complete offline workflow with recorded Namibia frames, real XML generation, a simulated compiler and sandbox-only installation. The launcher now defaults to Release. Live mode remains the default without the flag. See offline-workflow.md for exact test steps, provenance, safety restrictions and limitations.

Verification for this update: evidence/offline-workflow/. No MSFS process was launched or controlled and no real Community package was written. The existing Debug UI was left open; Release output was used to avoid its locked binaries and preserve its in-memory work.

## Airport site and taxiways — 2026-09-08
Matt reports the preceding runway/parking/survey/build/install workflow passed live MSFS testing. Preserved its pending layout implementation in f72d384 after 255 baseline tests passed.

Implemented shared Core.AirportSite bounds (runway footprint + 3 km, within the existing 10 km geodesic limit), replacing inconsistent editor square/Core radial 500 m guards. Basic multi-point taxiways now draw in the existing canvas, reuse local coordinates and existing materials, attach to parking, support name/width editing and shared undo/redo, invalidate stale results, and reach real XML in dry run. Existing Save/Load retains these paths; no new persistence feature was added.

Release build: zero warnings/errors; 278 tests pass. Generated XML validates against installed SDK 1.2.4 XSD. Offline UI visually checked. New taxiways and distant parking are not yet simulator-proven. No Package Tool/MSFS invocation or real Community writes during this task. See [implementation, limits and exact manual steps](taxiways-and-airport-site.md), evidence/taxiways/.

## Persistent airport management — 2026-09-08

Airport definitions now persist as versioned JSON in `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\airports`. The definition includes the guarded survey archive, runway, parking and taxiways; generated MSFS packages remain derived artifacts. My Airports can reopen a saved definition, but always restores it at Survey Captured and requires confirmation, validation and a new build before install authority exists.

Delete Airport uses a two-step preview/confirmation and verifies saved revision, project ownership/inventory and installed receipt/inventory immediately before cleanup. Unknown, changed, redirected or unrelated content causes refusal. Tests cover restart, multiple airports, corrupt/tampered documents, stale writes/previews, partial outputs, rollback and unrelated-file protection. The dry-run UI was closed/reopened and visually checked. See [airport-management.md](airport-management.md) and evidence/airport-management/.

## Development distribution — 2026-09-08

The Desktop project publishes as `dist\FrontierPilot\FrontierPilot.exe`, a Windows x64 self-contained single-file application. The stable path supports a user-level Desktop or Start Menu shortcut and does not alter normal build/debug outputs. The generated runtime is intentionally ignored by Git and recreated through `scripts\Publish-Development.ps1`. See [development-distribution.md](development-distribution.md) and evidence/development-distribution/.

## Taxiway compiler regression — 2026-09-08

The first NTA1 live Package Tool build exposed that generated TaxiwayPath elements lacked compiler-required `weightLimit`. Package Tool returned zero but rejected each segment, so no BGL or layout was created; persistence's managed project path and PackageValidator's layout location were correct. The generator now emits the official-sample value `weightLimit="0"`. A fresh copy of the exact NTA1 definition passed SDK 1.2.4 Package Tool validation with a real BGL and eight-entry layout. Visual simulator acceptance remains with Matt. See evidence/taxiway-build-regression/.

## Generic scenery object proof — 2026-09-08

Matt subsequently accepted runways, parking and multi-point taxiways in MSFS 2024. The next unaccepted proof adds `PlacedSceneryObject` to Core, layout history, saved airport/layout JSON and deterministic scenery XML. The one-entry prototype catalogue maps `asobo-generic-hangar-56` to Asobo `Gen_Hangar56`, library GUID `{FD8AA200-1485-46C7-BA8F-B9B69C31E0F7}`. The GUID is present in the current streamed `fs24-asobo-modellib-airport-generic` package; installed content was inspected read-only.

The WPF layout displays a yellow hangar footprint and supports add, selection, drag, exact offset/heading edits, 15-degree rotation, delete and shared undo/redo. Objects use local metres, `AirportSite` and WGS84 conversion. XML emits terrain-snapped `SceneryObject` siblings containing a `LibraryObject` GUID reference; Microsoft assets are not copied. SDK 1.2.4 compiled a disposable object-enabled package successfully with a real BGL and no builder errors. Visual model resolution, dimensions, terrain contact and intrinsic orientation await Matt's live simulator test. See [generic-scenery-object-proof.md](generic-scenery-object-proof.md) and evidence/generic-scenery-object/.

## True-scale footprints and foundations — 2026-09-08

Matt accepted the library building's live position and orientation, while reporting that its former small generic map square was inaccurate and its rear floated over uneven terrain. The installed model's transformed LOD0 glTF bounds establish a 46.1827 × 94.4047 m footprint and its off-centre placement origin. The canvas now renders those exact metre corners, rotates them through shared local geometry, and shows foundation/blend outlines.

Each building generates a small terrain polygon from the same bounds: 2 m flat margin, forced elevation and secondary-heightmap exclusion, with 8 m feathered falloff. SDK 1.2.4 compiled this polygon and object with no errors into a 909-byte BGL. The airport survey elevation is the prototype construction datum; distant-grade structures need a future per-object elevation workflow. Live terrain effect and blending await Matt's retest. See [object-footprints-and-foundations.md](object-footprints-and-foundations.md) and evidence/object-foundations/.

## Tracking V1 — 2026-09-08

The Desktop application now has a Tracking page backed by an independent read-only aircraft telemetry boundary. Tracking V1 was subsequently accepted by Matt in MSFS. Validated latitude, longitude, altitude, true heading, ground speed, on-ground state, aircraft title and ATC model flow through a testable Workflow view model at 4 Hz.

Mapsui.Wpf 5.1.0 renders an OpenStreetMap base layer and a heading-oriented aircraft marker. Initial acquisition centres once; later marker updates preserve manual pan/zoom, and Centre on Aircraft is explicit. SimConnect quit, timeout and malformed data clear the marker and expose retry. See [tracking-v1.md](tracking-v1.md) and evidence/tracking-v1/.

## Tracking V2 — 2026-09-08

The installed SDK and current official documentation define explicit user-aircraft, user-avatar and current-controlled-object proxies. Preserved Sedona evidence already proves that current switches from aircraft object 524288 to independently moving avatar 32768 in walkaround on simulator 1.8.16.0. Tracking now requests all three proxies in synchronized 4 Hz read-only batches and exposes a player only when the explicit identities and avatar/current values agree and report on ground. Invalid or ambiguous player readings are refused while the aircraft remains tracked.

Core now represents `TrackingWorldState` with independent aircraft and optional player positions. The WPF map retains the orange aircraft and adds a blue avatar marker, independent centre controls, player telemetry/distance and collapsed two-second diagnostics. Full Release tests: 334 passed. The dual-marker integration awaits Matt's live seated/walk/return acceptance. See [tracking-v2-avatar.md](tracking-v2-avatar.md) and evidence/tracking-v2/.

## Tracking V3 — 2026-09-08

Validated aircraft telemetry now persists as versioned multi-record JSON under `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\tracking`. Writes are throttled to ten seconds and disconnect/normal shutdown forces the newest valid state. Atomic replacement preserves the prior complete document; corrupt or unsupported history is reported and preserved until explicitly cleared.

Tracking loads last-known state without MSFS. The map distinguishes LIVE, LAST KNOWN with timestamp, and no saved position. Connection loss retains the aircraft marker and removes only transient V2 player state. Persistence never receives avatar coordinates and does not claim that the aircraft was parked. See [tracking-v3-persistence.md](tracking-v3-persistence.md) and evidence/tracking-v3/.

## Desktop startup correction — 2026-09-08

The first Tracking V3 distribution crashed while WPF created the Tracking diagnostics text box: its read-only view-model property was bound with TextBox's default two-way mode. The binding is now explicitly one-way and a source-level regression test protects that contract. All 344 tests and the Release build pass; the republished self-contained executable launched responsively through the Desktop shortcut. Airport and tracking persistence were not involved and were left unchanged. See evidence/startup-binding-regression/.

## Tracking V4 — 2026-09-08

Tracking now recognises aircraft against every valid definition loaded from My Airports. Core uses WGS84 inverse geometry and the actual runway, taxiway and parking footprints; a separate 250 m operational margin avoids misusing the 3 km authoring envelope. An on-ground aircraft inside stand radius + 5 m is Near Stand, and becomes Parked after five continuous seconds at no more than 1 kt with no long sample gap.

The Tracking panel shows airport, stand, distance, state and the tuning reason. Aircraft persistence schema 2 records the optional derived Frontier context alongside authoritative coordinates and remains compatible with schema 1. Avatar state is unchanged. Full automated and simulator acceptance details: [tracking-v4-airport-recognition.md](tracking-v4-airport-recognition.md) and evidence/tracking-v4/.

## Economy Core V0 — 2026-09-09

Core now models data-driven locations (gateway, mine, community and lodge), system/player businesses, resources, inventory, customer needs, funded transport requirements, employed players, jobs, shipments and an authoritative financial ledger. Acceptance reserves source cargo once. Externally verified delivery moves the shipment, updates customer inventory and records one payment as a single validated world revision. Replay is idempotent across restarts; cached player money must equal the ledger.

Workflow originally introduced strict schema-1 JSON persistence at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\economy-state.json`; the current economy format is schema 3 with explicit schema-1/2 migration. It uses the repository's same-directory flushed temporary write and atomic replacement convention plus cross-process serialization. Existing corrupt, unsupported or inconsistent state is reported and preserved, including during initialization. Deterministic prototype data is explicitly temporary and supports mines, communities and lodges through records.

Economy Core itself contains no UI, SimConnect flight lifecycle, weather logic, flight-validity policy, economic clock or permanent balance data. The later accept-only Jobs slice composes this kernel without weakening `VerifiedDeliveryEvidence`. See [economy-core-v0.md](economy-core-v0.md).

## Namibia Location Catalogue V0 — 2026-09-09

Core embeds a strict schema-2/catalogue-0.2.0 authored data set derived from section 20 of the Google Drive design reference. It contains locked gateway Eros/FYWE and all 30 mining, community and safari candidates with deterministic `namibia.*` IDs, identifier confidence, route-time scale, roles, proposed work, notes, sources, lifecycle status and recommendation flag. The catalogue is separate from `economy-state.json`; projected economy locations retain the catalogue stable ID.

Eros, Uis and Arandis store explicit 2026-09-09 manual MSFS inspection evidence. Published OurAirports coordinates have separate provenance and are never described as simulator-derived. Uis and Arandis are the only active customers; the remaining 28 destination records retain research status and null coordinates. The recommended subset remains Eros, Uis, Arandis, Aminuis, Leonardville, Mount Etjo and Okonjima.

Pure status transitions require verification before activation. The ordinary live-job guard requires both `MsfsVerified` and `ActiveCustomer`. Tests use a conspicuous TEST ONLY candidate and do not assign invented data to a real location. See [namibia-location-catalogue-v0.md](namibia-location-catalogue-v0.md).

## Employed Cargo Jobs V0 — 2026-09-09

Catalogue 0.2.0 records Matt's manual MSFS inspection of Eros/FYWE, Uis/FYUS and Arandis/FYAR separately from published OurAirports coordinates. Eros remains the locked gateway; Uis and Arandis are the only active customers.

The WPF Jobs page exposes two temporary deterministic General Supplies offers from the Eros system employer. Acceptance uses the existing atomic Economy Core transaction, persists one reserved shipment and prevents another active acceptance. Restart restores the accepted job. At this milestone the UI had no delivery or payment operation; Flight Attempt V0 below now adds tracker-qualified, player-triggered unloading without letting the UI construct evidence. See [employed-cargo-jobs-v0.md](employed-cargo-jobs-v0.md).

## Employed Cargo Job Cycle V0 — 2026-09-10

The system employer now maintains one funded offer for each active prototype customer through generic persisted `RecurringDemandDefinition` data. Board maintenance is deterministic and idempotent at initialization, Jobs load/refresh, restart and verified delivery. Sequence IDs preserve completed history, while the UI separates available, active and recent completed work. Eros stock shortfalls are introduced only through audited `SystemGatewaySupply` inventory transactions; inventory audit remains separate from the authoritative player financial ledger. The repeatable-job schema-2 model is preserved through the current schema-3 migration, with its separate one-time aggregate upgrade for recurring definitions. See [employed-cargo-job-cycle-v0.md](employed-cargo-job-cycle-v0.md).

## Flight Attempt Observability V0 — 2026-09-10

Jobs now exposes separated live observations, persisted evidence and derived qualification diagnostics plus a significant-event timeline. Flight-attempt persistence is schema 2 with an atomic schema-1 migration; timeline events are append-only diagnostics and cannot participate in delivery qualification. Redacted JSON support bundles and plain-text summaries omit local paths and usernames. Matt completed and paid the Eros-to-Arandis lifecycle in MSFS on 2026-09-10. A follow-up fix makes `DeliveredAndPaid` a hard qualification/event boundary while still refreshing live observations; it corrects the post-delivery `OriginQualificationInterrupted` misclassification seen in that test. Uis and live recovery paths remain to be accepted. See [flight-attempt-observability-v0.md](flight-attempt-observability-v0.md).

## Flight Attempt V0 — 2026-09-10

The Jobs page now receives validated live aircraft observations from the existing Tracking boundary. A persistent `FlightAttemptTracker` qualifies simulated loading at Eros, airborne/movement/10-NM evidence and a ten-second stopped arrival at the selected Uis or Arandis coordinate. Last-known tracking state is never flight evidence; invalid samples, disconnects and gaps reset stationary timers.

Attempts persist atomically in `economy/flight-attempts.json`. Loading writes its attempt before the idempotent Economy Core InTransit transition; immutable arrival proof precedes player-triggered unload. Restart reconciliation can finish only these already-proven transitions. The tracker alone creates `VerifiedDeliveryEvidence`; Economy Core still atomically delivers inventory and records one authoritative-ledger payment. Simulated loading does not alter MSFS payload. See [flight-attempt-v0.md](flight-attempt-v0.md).

## Prospecting Camp Vertical Slice V0 — 2026-09-11

The Operations page can found one data-configured Prospecting Camp at a saved player-created airport that contains a runway and catalogue-derived Base Lodge capability. Founding requires a fresh guarded walking-avatar observation within 250 m and 150 TPC from the authoritative ledger. The atomic transaction stores the stable airport-definition link and full presence evidence, initializes audited inventories and creates one operation.

Explicit idempotent day ticks consume 10 kg Rations and produce up to 20 kg Raw Minerals within 70/140 kg capacities. Idempotent maintenance adds audited Eros resupply and camp-to-Eros mineral jobs at the configured thresholds. The proven Flight Attempt rules and thresholds are unchanged; its location resolver now uses each job's authored or player-created origin/destination. Economy schema 3 adds sites, operations and ticks with atomic schema-1/2 migration. Linked airports cannot be deleted. Automated behavior is proven; the new site presence and operation routes await live MSFS acceptance. See [prospecting-camp-vertical-slice-v0.md](prospecting-camp-vertical-slice-v0.md).

## Scenery Materials & Prototype Buildings V0 — 2026-09-10

Runway appearance is now selected through a seven-entry authored preset catalogue with stable IDs. SDK 1.2.4 Package Tool accepted native `DIRT`, `SAND`, `GRAVEL`, `GRASS`, `SHORT_GRASS` and `ASPHALT` references. Compacted Dirt, Sandy Dirt, Gravel, Green Grass and Modern Asphalt use those native identifiers. Dry Grass uses a clearly labelled `GRASS` fallback and Weathered Asphalt uses an `ASPHALT` fallback. Old definitions with no preset ID retain their original package-local procedural Dirt or sample-derived Asphalt material pipeline.

The construction catalogue defines Base Lodge, Storage Shed, Staff Accommodation, Warehouse, Workshop, Fuel Tank, Water Tank and Airfield Marker categories. Only Prototype Base Lodge is available: it intentionally reuses the proven `Gen_Hangar56` GUID, measured footprint and terrain foundation under a distinct stable placeholder ID. It contains no custom asset and no economy semantics. A generated Gravel + lodge project compiled successfully with four layout entries and no builder error. Visual preset appearance and placeholder resolution remain unverified.

Jobs now exposes a guarded developer diagnostic export and confirmation-gated reset. Reset clears only incomplete airborne/movement/arrival observations while keeping the same loaded `InTransit` shipment; completed delivery, inventory, ledger and balance are protected. See [scenery-materials-and-prototype-buildings-v0.md](scenery-materials-and-prototype-buildings-v0.md) and `evidence/scenery-materials-v0/`.

## Aircraft Payload and Loading V0 — 2026-09-15

A new Aircraft Loading page exposes payload, fuel, and factual weight/balance data through a dedicated SDK adapter. Cargo uses a persistent apply–verify–commit manifest; fuel uses bounded trusted snapshots and explicit reconnect reconciliation. All writes require fresh grounded/stationary telemetry and a fresh controlled walking-avatar observation within 100 m. Unsupported aircraft retain an explicit, never-automatic simulated mode. New flight attempts use a persisted route-sensitive maximum-origin-distance rule; schema-1/2 attempts retain 10 NM. Sandy Dirt now uses visible DIRT as a labelled fallback. Automated verification is complete; live payload/fuel and Sandy Dirt visual acceptance remain pending. See [aircraft-payload-loading-v0.md](aircraft-payload-loading-v0.md).
