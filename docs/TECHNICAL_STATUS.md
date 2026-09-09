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
Launch scripts/Start-Desktop.ps1. Connect in a loaded flight, capture while standing at the runway centre, enter name/identifier/dimensions/surface and confirm the site check. Close MSFS, generate/compile, then install. Dirt and asphalt are the proven available surfaces. Identifiers accept 3–8 uppercase letters/digits with a leading letter. World-map uniqueness and airport separation remain manual checks.

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

Workflow adds strict schema-1 JSON persistence at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\economy-state.json`, using the repository's same-directory flushed temporary write and atomic replacement convention plus cross-process serialization. Existing corrupt, unsupported or inconsistent state is reported and preserved, including during initialization. Deterministic prototype data is explicitly temporary, supports mines/communities/lodges through records and is not automatically created or connected to the application.

There is no Jobs UI, Namibia activation, SimConnect flight lifecycle, weather logic, flight-validity policy, economic clock or permanent balance data. `VerifiedDeliveryEvidence` is the boundary for a later component. See [economy-core-v0.md](economy-core-v0.md).
