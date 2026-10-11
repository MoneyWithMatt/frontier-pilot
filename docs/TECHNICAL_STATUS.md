# Technical status — 2026-09-07

## Dependency setup — 2026-10-10

Native SDK installation verified — 2026-10-11: the user completed Samples setup; the MSI log records Samples 1.2.4 success/exit 0, the SimpleAirport project exists under `C:\MSFS 2024 SDK\Samples`, and the user's actual recheck screenshot reports all required SDK components verified. Core and Samples now share the SDK root. Live simulator connection, survey qualification and airport compilation on the new PC remain unverified.

Installer command correction — 2026-10-11: the user's Windows Installer help screenshot exposed invalid SDK property quoting. Launch now quotes the property value using MSI syntax and trims the trailing SDK separator. Release solution build and 634 tests pass; actual msiexec accepted the production command against a synthetic invalid package (1620), without installing anything. Native Samples installation and live airport compilation remain pending.

Samples follow-up — 2026-10-11: Windows Installer logs show Samples failed before successful Core installation, rather than installed elsewhere. Samples now requires a verified Core root, receives fresh SDK environment and explicit root arguments, supports reusing previous downloaded Samples installers, and records verbose installation logs. Actual Core hashes and four cached Microsoft MSI identities/signatures pass; 628 tests and WPF smoke pass. Native Samples installation and live simulator acceptance remain pending.

Recheck follow-up: new-PC inspection found SDK Core installed but Samples absent. Detection now retains and displays incomplete SDK roots, identifies missing Samples and confirms each check with a timestamp. Regression and actual-button WPF checks pass; 627 solution tests pass. The current user's open application is preserved during distribution replacement.

Added a setup window available before career activation: SDK discovery, Community path detection, pinned Microsoft Core/Samples 1.2.4 downloads with cancellation, external CAB preservation, signature-gated interactive installation, and saving a verified SDK configuration. Existing exact SDK/simulator capture safeguards remain unchanged. Missing SDK paths now give setup guidance. Release build and 626 tests pass; WPF setup smoke and real Core download/signature verification pass. Actual SDK installation and live simulator acceptance remain pending. See [dependency-setup.md](dependency-setup.md).

## Career Onboarding V1 — 2026-10-04

Implemented New Career/name/region/employer exploration, draft persistence, legacy-career detection, and confirmed restart-safe career reset. Namibia offers all three planned employers as Coming Soon. Mapsui/Operations map is reused. New drafts do not initialize gameplay services or seed an economy. Verification: 22 focused tests and all 616 solution tests pass; WPF smoke covers onboarding and existing site editing. See [career-onboarding-v1.md](career-onboarding-v1.md) for reset scope and manual tests.

## Return-leg boarded loading retry — 2026-09-21

The return import remained PendingApplication with 10 kg already reported in its baggage station. Each retry took the already-loaded gross weight as a fresh baseline and expected another 10 kg increase from writing the same target. Authorized boarded application now calculates its physical delta from live managed-station weights. When targets already match but the recorded gross expectation does not, the explicit retry persists its intent, removes only the authorized managed allocation to its recorded baseline, verifies both station and gross removal, then reapplies and verifies the target. Unrelated seats are preserved. No automatic restoration or economic mutation occurs on refresh; failed weight verification still prevents commitment. Pending target weights no longer appear as additional Other payload in the editor.

Verification: 69 focused loading tests and all 562 solution tests passed. Regressions cover restart after an applied-but-uncommitted load, changed unrelated pilot weight, and refusal when station writes do not affect gross weight. Live PA-28 retest remains required.

## Player-contract physical-unload settlement correction — 2026-09-21

Replaying copies of the live two-contract save through the real economy/attempt/loading stores reproduced destination failure: `Completed job cargo and payment records are inconsistent.` The earlier UI changes did not resolve this economic rejection. Physical delivery completed the job/shipment at the persisted flight proof's ReadyUtc, but SettleAir posted the contract payment at the later unload-command time. The immutable completion consistency check correctly rejected the entire transaction. Air settlement now uses the shipment's authoritative DeliveredUtc; the cargo/inventory audit retains the actual unload-command timestamp. No validation was weakened, no schema changed and no live save was edited. The same copied-save replay then succeeded.

Returning an InTransit shipment to origin also exposed a missing confirmation boundary: the domain requires forfeiture after departure, but the coordinator exempted origin from its warning. The coordinator now asks for confirmation before any manifest/simulator mutation in that case, preserving the existing domain policy. Pre-departure origin returns remain unchanged. Regression tests cover delayed grouped import/export physical delivery, local-store restart and replay, self-fulfilment balance, and confirmation before origin-return mutation. Live MSFS retest remains required.

Verification: 77 focused loading/logistics tests and all 560 solution tests passed. Release build: zero warnings/errors. Self-contained Windows application republished and Desktop shortcut refreshed.

## PA-28 exit-time payload reset at grouped destination — 2026-09-21

The first live two-contract flight proved that both 10 kg shipments, their separate attempts and arrival evidence reached `ReadyToUnload`. The PA-28 then reset its managed Station 5 payload to the recorded zero baseline when the player exited. The economy correctly kept 20 kg aboard, but choosing restoration changed reconciliation from `CargoAppearsMissing` to `RestoreAvailable`; the load UI consequently prioritized the post-boarding restoration command and misclassified an explicit zero-cargo draft as another restoration.

A changed player cargo draft now takes priority over restoration actions. For a committed manifest whose payload is physically missing, both `CargoAppearsMissing` and an authorized `RestoreAvailable` state use the observed baseline when constructing a zero/partial unload plan. Setting cargo to zero at a verified destination therefore verifies that MSFS is already at baseline and commits the preserved delivery evidence, inventory transfer and separate shipment settlements without first re-adding payload. The authoritative manifest and economy cargo remain unchanged until that explicit action.

The first release of that correction still left a UI/coordinator gap: station rows held the zero draft, but the coordinator could retain its previous 20 kg plan until the primary button ran, while the button's state was evaluated before that submission. Valid typed edits and **Set cargo to zero** now publish the authoritative plan immediately. The primary action therefore sees the same plan shown in the editor and can enter the durable apply/verify/settle path.

Release verification: all 557 tests pass and the Release build completes with zero warnings and errors. The self-contained desktop application and Desktop shortcut were republished on 2026-09-21.

## Player-contract manifest identifier fix — 2026-09-21

The first live grouped load exposed an identifier composition defect before any MSFS write: player-created jobs and shipments legitimately use the full 64-character economy ID limit, while aircraft loading prefixed the derived 64-character flight-attempt ID with `manifest-` and produced an invalid 73-character identifier. Manifest IDs now use the same bounded deterministic derivation as the economy domain. Existing manifest IDs and saved gameplay state are unchanged. A regression using maximum-length player-contract job and shipment IDs verifies that the pending manifest persists before the simulator write. Release verification: 554 tests pass, the WPF loading-control smoke test passes, and the Release build has zero warnings and errors.

## Reserved multi-job cargo flights — 2026-09-21

Job acceptance now reserves contracts without immediately selecting a single active flight. The Jobs page separates Available, Reserved and History views. Several reserved jobs with identical origin and destination can be selected for one aircraft load; a return contract remains reserved until deliberately selected at its origin.

The economy keeps each requirement, job, shipment, financial reservation and settlement independent. The loading manifest schema is version 5 and stores an aggregate physical station allocation with immutable per-shipment cargo legs. The flight tracker creates one persisted attempt and delivery proof per shipment, synchronizes their observations across the shared flight, and commits grouped departure and delivery changes atomically. Existing single-job manifests migrate to one leg. Release verification was subsequently superseded by the manifest identifier fix above. Live MSFS grouped-loading acceptance remains outstanding.

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

On 2026-09-24 the layout canvas gained a georeferenced Mapsui/OpenStreetMap base layer. No-runway sites now show only their validated anchor and authored objects. **Place runway** uses a map click to set heading and full length from the fixed surveyed centre; this intentionally does not claim unsurveyed terrain elevation. Saved sites open with explicit edit-state guidance and continue to revise the same persistent site ID. Automated coverage proves map-driven runway geometry persists and an edit increments the existing site's revision rather than creating a second definition.

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

The construction catalogue defines Base Lodge, Storage Shed, Staff Accommodation, Warehouse, Workshop, Fuel Tank, Water Tank and Airfield Marker categories. Only Prototype Base Lodge is available. Its stable Frontier asset ID resolves to the installed base-scenery `BF_TemplateHouse01` GUID `{0286FC19-4558-4228-A480-2CD5690A2022}` at normal scale. This replaces `MICROSOFT_ASSET_FieldCabins_01`, rejected in live testing because it rendered as a large compound/hangar-like object. The temporary 10 × 8 m footprint and the replacement's appearance remain to be accepted live. It contains no custom asset and no economy semantics.

Jobs now exposes a guarded developer diagnostic export and confirmation-gated reset. Reset clears only incomplete airborne/movement/arrival observations while keeping the same loaded `InTransit` shipment; completed delivery, inventory, ledger and balance are protected. See [scenery-materials-and-prototype-buildings-v0.md](scenery-materials-and-prototype-buildings-v0.md) and `evidence/scenery-materials-v0/`.

## Aircraft Payload and Loading V0 — 2026-09-15

A new Aircraft Loading page exposes payload, fuel, and factual weight/balance data through a dedicated SDK adapter. Cargo uses a persistent apply–verify–commit manifest; fuel uses bounded trusted snapshots and explicit reconnect reconciliation. All writes require fresh grounded/stationary telemetry and a fresh controlled walking-avatar observation within 100 m. Unsupported aircraft retain an explicit, never-automatic simulated mode. New flight attempts use a persisted route-sensitive maximum-origin-distance rule; schema-1/2 attempts retain 10 NM. Sandy Dirt now uses visible DIRT as a labelled fallback. Automated verification is complete; live payload/fuel and Sandy Dirt visual acceptance remain pending. See [aircraft-payload-loading-v0.md](aircraft-payload-loading-v0.md).

## Aircraft-loading live freshness correction — 2026-09-15

Apply, simulated load, unload, restore and fuel actions now acquire a new simulator snapshot at the action boundary. A displayed table may age while the player reviews it, but the write never relies on that stale sample. Cargo application additionally compares the newly read station weights with the planned baseline and refuses if payload changed after planning.


## Manual aircraft-loading editor — 2026-09-15

Live testing proved the PA28 payload write/read-back path. The original editable DataGrid column did not reliably commit typed station values. Aircraft Loading now uses explicit two-way slider and numeric controls per payload station and fuel tank, with a fixed-cargo allocation summary and current/planned weight review. Automatic distribution was removed so allocation remains a player decision.


## Loading editor lifecycle repair

Manual editing now retains row instances and draft text across loading/flight-status refreshes. Fuel plans no longer reset to live quantities during refresh. Numeric fields are widened; competing slider bindings and the simulated-loading creation button are removed. Existing simulated manifests remain readable/unloadable. Empty, negative, or out-of-range text cannot submit the previous valid value.

Loading eligibility subscribes to flight qualification changes. UI readiness may use an aged inspection table, but the actual Apply action still obtains and validates fresh simulator data, current avatar presence, aircraft identity and origin qualification before writing. No qualification threshold changed.

Verification: 490 solution tests, Release build, and the Windows WPF control smoke test passed. Run `.tools/dotnet/dotnet.exe run --project tests/LoadingWpfSmoke -c Release -- src/Desktop/MainWindow.xaml` for synthetic control tests without MSFS. This exercises the actual production templates, focus, fractional entry, refresh retention and zero reset. Live aircraft application remains a user acceptance step.

Manual retest: accept a reserved job, qualify stopped at its origin, exit nearby, Read live aircraft, enter allocations totaling the shown mass, wait through refresh, then Apply and verify. Check the displayed reason if disabled. For fuel, engines must be off; enter a different tank quantity and apply, then check simulator read-back. No simulated loading button should be present.

## Prototype mission skip recovery — 2026-09-16

Aircraft Loading now provides a confirmation-gated **Skip incomplete prototype mission** action for retesting. It applies only to a committed, incomplete shipment. For an MSFS-integrated manifest it first persists a removal intent, subtracts exactly the Frontier allocation from the current station weights, and requires read-back verification. It then atomically marks the economy requirement, job and shipment `Cancelled`, returns the reserved quantity to the origin through a `ShipmentReservationReleased` inventory-audit entry, and runs the normal idempotent board maintenance to create one replacement offer. No delivery evidence, destination inventory, financial transaction or payment is created. Completed jobs and terminal manifests remain immutable. This is an explicit prototype administration tool, not the eventual gameplay cancellation design.

The first live skip exposed binary/unit-conversion drift: a requested 20 kg station read back as `19.999999999999993 kg`, and pre-tolerance subtraction incorrectly reported insufficient weight. Cargo removal now uses the manifest's existing 0.25 kg verification tolerance and clamps only tolerance-scale negative results to zero; larger shortages still refuse. The committed-load UI also separates live non-Frontier payload from the manifest allocation so station rows and weight totals do not count loaded cargo twice.

## Loading tracking recovery
The payload snapshot reader can succeed while continuous Tracking is disconnected. Aircraft Loading now shows Tracking status and its exact error, offers Reconnect tracking, and retries connection on Read live aircraft. A complete allocation no longer says READY TO APPLY unless live qualification also permits loading. No avatar/flight checks were bypassed. The screenshot proves World was unavailable, but the underlying simulator connection failure requires its Tracking error to diagnose. 491 tests and Release build passed; update staged while the user application is open.

## Loaded-shipment investigation — 2026-09-16
Read-only inspection found Arandis shipment 000002 already InTransit, with a committed MSFS-integrated manifest verified on 15 September: 20 kg in each of five stations. Latest reconciliation reported CargoAppearsMissing. This explains why new-load Apply remained disabled despite a complete new draft. Refresh and telemetry status paths alternately displayed generic Ready/telemetry messages. Both now prioritize the committed-manifest explanation. Loaded allocations are shown as read-only, and existing explicit Restore manifest cargo is the recovery path. Inspection did not modify user saves or MSFS. Missing cargo no longer advances the persisted restoration reference position. Automated regression reproduces missing committed cargo and repeated status updates; 492 tests passed. Actual avatar/restoration eligibility remains live-session dependent.

## Desired-state aircraft loading — 2026-09-16

MSFS-integrated loading remains the accepted physical path, but station edits now express the complete desired Frontier-managed state. Partial loads and unloads use one durable apply/read-back transaction while retaining each station's inferred non-Frontier baseline. Economy schema 5 records loaded, delivered and forfeited quantities plus idempotent cargo transfers; aircraft-loading schema 2 records desired and prior managed state for crash recovery. Loading places the shipment in `Loaded`; proven departure changes it to `InTransit` without altering the accepted Flight Attempt thresholds.

The live Station 5 failure was traced to a genuine simulator/manifest mismatch: the committed manifest expected 100 kg above a zero baseline while the current simulator station had already returned to zero. The legacy unload path tried to subtract 100 kg again. A desired zero state now recognises the live baseline and commits the corresponding cargo consequence only after verified read-back. Mismatch diagnostics include station, baseline, managed quantity, expected target and observed value. Correct-destination partial unload updates inventory without payment; complete delivery pays once. A confirmed wrong-location unload forfeits the shipment and cannot pay. Terminal jobs no longer supply stale cargo to the load manager.

Live retesting then exposed an editor refresh defect: changing an allocation to zero was immediately replaced by the committed manifest quantity whenever telemetry refreshed while no calculated plan was stored. The station editor now owns a dirty draft that survives telemetry, qualification and read-back refreshes until Apply succeeds or the active aircraft/shipment changes. The summary separates total job cargo, cargo physically aboard, cargo currently available at the aircraft's location, requested aboard cargo and delivered cargo. Loading remains limited to cargo physically available at the current origin; unloading remains physically selectable anywhere, with existing delivery or forfeiture consequences applied only after simulator verification.

The first successful desired-state unload at Eros exposed a second eligibility issue. Economy correctly returned all 120 kg to the reserved origin pool and the manifest correctly recorded 0 kg aboard, but the tracker both cleared the already-proven stationary qualification and only permitted additional loading from `Loaded`, not `Reserved`. Apply therefore remained disabled even though the aircraft never moved. Continuous valid origin presence now remains qualified across partial transfers, and both Reserved and partially Loaded shipments may load again up to the quantity available at that origin. A restart or real telemetry gap still requires a fresh ten-second qualification; the UI shows that factual countdown instead of a generic manifest message.

A further live retest exposed a circular command gate: the button required coordinator approval of the desired plan, but the typed plan was only submitted to the coordinator after the button was clicked. Valid changed drafts can now invoke Apply; the coordinator still performs every fresh telemetry, avatar, identity, origin and stationary check before any simulator write. The existing attempt telemetry branch also now re-evaluates loading qualification for a shipment returned completely to `Reserved`, rather than only for a partially `Loaded` shipment. Focused loading/flight tests (80), the complete 515-test suite, Release build and WPF loading-control smoke test pass. The self-contained desktop application was republished on 2026-09-16.

The next PA28 live check proved that an immediate SimConnect read-back was insufficient: Station 5 accepted 120 kg and returned it immediately, then later reverted to 0 kg. MSFS showed 380 lb payload and 2,406 lb gross instead of the expected 644.6 lb and 2,670.6 lb, an exact 264.6 lb/120 kg shortfall. Payload application and explicit restoration now require a second read after a configurable three-second retention delay before economy inventory is committed. Reconciliation checks only Frontier-managed stations, so entering the pilot seat cannot turn missing Station 5 cargo into an ambiguous whole-aircraft mismatch. The UI distinguishes persistent **Shipment cargo** from **Detected in MSFS** and never restores missing payload automatically. The exact reset trigger (elapsed time versus entering this third-party PA28) remains a live-test question. All 519 solution tests, the Release build and WPF loading-control smoke test pass; the self-contained desktop build was republished.

The follow-up screenshots resolved that question for the tested PA28: an outside station write could remain visible to SimConnect, and a later run even reported the corresponding SimConnect total-weight change, but the station reset when the player boarded while the aircraft EFB had never incorporated the cargo. Aircraft-loading schema 3 therefore adds a bounded outside authorization followed by mandatory post-boarding verification for every positive MSFS cargo load. The outside step still proves avatar presence, location, aircraft identity and ground state, but it cannot commit a new shipment. After boarding the same stationary aircraft within five minutes and 1.5 NM, the player explicitly completes verification. If the aircraft retained the station and expected total weight through boarding, Frontier Pilot verifies without another write. If boarding cleared it, Frontier Pilot writes the authorized target while seated. Both paths require retained station values and the expected total-aircraft-weight result before a new economy cargo transfer is committed. It never writes automatically on boarding. This is a compatibility experiment for aircraft that rebuild payload state on cockpit entry; live PA28 acceptance remains pending.

The seated PA28 retest succeeded against standard simulator state: reported gross weight increased from 1,091.343 kg (2,406 lb) to 1,211.343 kg (2,670.6 lb), exactly the 120 kg manifest allocation, and Station 5 retained 120 kg. The MSFS EFB continued to display its earlier 2,406 lb planning value. No documented SDK mechanism was found to refresh that EFB display from an external SimConnect payload write, so Frontier Pilot treats station readback plus simulator-reported total weight as the factual integration result and records the EFB as a display compatibility limitation. A related UI defect that counted a newly occupied unmanaged Pilot station as Frontier cargo was corrected; detected cargo now includes only manifest-managed allocations and clamps each to its recorded Frontier amount.

The Weight & Balance tab now provides a dedicated direct-MSFS diagnostic view for the Duke forward/rear test. It reports `TOTAL WEIGHT`, `CG PERCENT`, `CG FWD LIMIT`, `CG AFT LIMIT` and every dynamically enumerated `PAYLOAD STATION NAME/WEIGHT` with a timestamp and manual refresh. Raw station readings remain separate from the editable manifest-adjusted payload view. The cargo transaction path is unchanged. Automated tests cover direct values, dynamic station metadata, precision, unavailable fields and the raw-versus-managed distinction; Duke behavior remains a live acceptance item.

The Duke live test then exposed a stale-intent recovery case. The authoritative economy still held the Arandis shipment as Reserved with zero loaded quantity, but a failed 90 kg payload application had left a `CargoLoaded` attempt and pending manifest. After the Duke EFB cleared its stations, ordinary desired-state inference tried to subtract that uncommitted allocation from a lower live station value and correctly refused the resulting negative baseline. Aircraft-loading schema 4 adds an explicit `ResetAtOrigin` checkpoint. Setting every Frontier allocation to zero now restores only the stations touched by an uncommitted intent to their recorded pre-intent baselines, preserves unrelated live payload, verifies MSFS, and removes the stale evidence-free attempt. It leaves the shipment reservation, inventory audit, financial ledger and payment untouched, then permits the same shipment to qualify and load again. Committed cargo and any attempt with flight or delivery evidence remain protected.

Live acceptance on 2026-09-17 completed a full Black Square Turbine Duke Eros-to-Arandis flight: 100 kg loaded, flight evidence recorded, payload removed, destination inventory delivered, and one 200 TPC payment committed. The following PA28 video confirmed that aircraft still clears its standard payload station when transitioning from the cockpit to walkaround. `SimConnectAircraftLoading` reads `USER_AIRCRAFT`, independently of the avatar/current-object proxies used by tracking, so the zero reading is an aircraft/add-on state reset rather than Frontier Pilot reading the avatar. The persistent manifest remains authoritative and the existing explicit restore plus post-boarding verification recovers the load without duplicating cargo or payment. Automatic restoration remains prohibited. Source and published binaries are preserved under tag `checkpoint-loading-live-verified-2026-09-17`, with an offline checkpoint in Matt's Documents folder.

The Aircraft Loading payload workflow is now presented through one context-sensitive primary action. Outside the aircraft it validates access and persists the exact plan without writing MSFS payload; after boarding the same control performs the single physical write, retained read-back, and economy commit. Reducing allocations, including zeroing all managed cargo, uses the same control. Explicit missing-manifest restoration also authorizes outside and writes only after boarding. Permanent recovery and post-boarding toolbar buttons were removed; refresh remains visible, while factual blocking reasons remain in the panel.

## Operations Map & Persistent Economy V0 — 2026-09-19

Operations is now the economic-world homepage. A Mapsui/OpenStreetMap regional view projects authored Namibia catalogue locations, saved player airports, and founded player operations into clickable, visually distinct markers. Linked airports and operations use one combined marker; its card exposes both concepts and the operation-detail route. Owned operations remain separately listed. The detail view shows only simulation-backed identity, status, prototype day, audited inventory/capacity, configured production and consumption, current constraints, last tick and pending logistics.

The existing generic `ResourceType` + per-location `InventoryBalance` + authoritative `InventoryLedger` model now includes persistent gateway opening stock and paired atomic location transfers. Eros is seeded once to temporary configurable targets of 300 kg General Supplies and 200 kg Rations, with an explicit Raw Minerals position. Restart never replenishes depleted opening stock. A confirmed developer action moves 1 kg of rations from Eros to a selected camp through the real capacity-checked, idempotent transfer path. Abstract SYSTEM locations remain inventory-free in Operations even when they participate in SYSTEM jobs. This milestone originally used economy schema 5; Player Operation Logistics later migrated it to schema 6. See [operations-map-persistent-economy-v0.md](operations-map-persistent-economy-v0.md).

## Player Operation Logistics & Job Builder V0 — 2026-09-19

Owned operations can publish real import/export contracts into the existing Jobs marketplace. Publication atomically reserves authoritative origin inventory and ledger-derived funds; acceptance, aircraft loading, Flight Attempt evidence and delivery reuse the proven aviation path. Separate purchase/sale, pilot-payment and road-cost ledger entries preserve the distinction between goods and transport. An optional persisted road fallback moves the same shipment through a deterministic deadline and ETA, with restart-safe exactly-once settlement. Economy schema 6 adds contract and escrow records. See [player-operation-logistics-job-builder-v0.md](player-operation-logistics-job-builder-v0.md).


## Independent runway placement — 2026-10-03

Runway drawing now uses two endpoints, with independent persisted centre offsets and editable heading/length/width. The survey anchor and existing scenery remain fixed. Runway-free sites remain scenery-only, with no airport search or parking spawn. Explicit transparent runway surfaces are supported but still flatten terrain; no no-terraform guarantee or automatic hidden runway is made. See [runway-placement-and-runway-free-sites.md](runway-placement-and-runway-free-sites.md) for MAT1 build evidence and manual verification.
