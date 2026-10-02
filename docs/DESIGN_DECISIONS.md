# Design decisions

- **Implemented:** retain the CLI and add Desktop plus platform-neutral Workflow. WPF code-behind only constructs the view model and handles safe close; testable services own operations.
- **Implemented:** use existing avatar capture unchanged in substance, with a separate read-only prerequisite handshake. Strict qualification and no fallback take priority over accepting a questionable survey.
- **Empirical prototype decision:** Sedona elevation results are sufficient for this exact simulator build. Microsoft's aircraft-local documentation contradicts observed avatar targeting. Preserve the 66 cm return-to-A caveat; invalidate qualification when binaries change.
- **Implemented:** retain deterministic template generation and CUSTOM_AIRPORT ordering. Dirt and asphalt only, because these have compiled; FPSC dirt was visually verified.
- **Implemented:** validate definition and local ownership before project creation. Global airport identifier uniqueness is not discoverable from local package names; require a world-map check and do not claim automatic global uniqueness.
- **Implemented:** explicit build then install. MSFS must be closed at both transitions. Hashes and build validation gate installation. Only app-owned unchanged packages can be replaced, with backup/rollback. FPT1/FPSC are reserved proof IDs.
- **Prototype restriction:** staging and Community must share a drive; redirected folders are refused. A broader installer needs a separately tested transaction design.
- **Deferred:** persistent draft/resume, wider simulator qualification, whole-footprint elevation checks, and polished customer distribution without SDK setup.
- **Not in scope:** economy, careers, multiplayer and settlement interfaces.

## Workflow hardening decisions
- **Implemented:** explicit validation state between captured survey and build. Failures retain a named recovery state; edits and reconnect invalidate downstream artifacts.
- **Implemented:** dry run is selected at launch, never silently substituted after a live failure. Live/replay backends are separate; only live backend can invoke SimConnect or Package Tool.
- **Implemented:** dry run reuses recorded frames and Core validation, real generation and safe installation. Compilation alone is simulated and marked; live installer refuses synthetic packages.
- **Implemented:** dry-run destination is app-selected temporary storage, never a caller-selected Community path. Installation remains same-volume stage/hash/backup/rename/verify with rollback on ordinary exceptions.
- **Limit:** multiple renames are not one atomic transaction. Power-loss recovery is a future improvement; backups are retained and no cleanup deletes content.
- **Implemented:** copying/saving technical diagnostics includes full exception text. Save uses create-new semantics and never overwrites a prior log.

## Shared site and taxiway decisions — 2026-09-08
- **Implemented:** one runway-oriented Core.AirportSite envelope replaces all three parking-era 500 m guards. Its 3 km runway-footprint expansion and shared 10 km solver ceiling constrain full infrastructure footprints, independent of screen coordinates. These are prototype safety policies, not SDK limits.
- **Implemented:** taxiways are ordered local east/north points with width and name. Parking endpoint references follow moved stands. Deleting a stand detaches the endpoint at its current location; undo restores the reference. A common history restores both feature types together.
- **Implemented:** paths share the selected runway material (Dirt/Asphalt) to reuse the existing material pipeline. No per-path surface library or apron is introduced. Generation emits deterministic SDK-ordered points/parking/names/paths. Existing capture and installation service boundaries remain unchanged.
- **Prototype limitation:** physical runway overlap only; a full ATC/AI RUNWAY/HOLD_SHORT graph, automatic intersection splitting and curve geometry are not included. Native ground following is expected, not newly empirically proven. Schema validity is not simulator acceptance.
- **Preserved:** existing same-survey layout export/import now retains taxiways; no new airport catalogue, persistence or survey-resume workflow.
- **Pending:** Matt's new live test of distant parking, segment visibility, joins and terrain fit. See taxiways-and-airport-site.md.

## Georeferenced site editing — 2026-09-24
- **Implemented:** the site editor now places its existing metre-accurate authoring overlay over the same Mapsui/OpenStreetMap provider already used elsewhere in Frontier Pilot. Tile pixels remain view-only; saved survey coordinates and local metre offsets remain authoritative.
- **Implemented:** a site without airfield infrastructure renders an operation anchor and placed objects, with no implied runway footprint. Runway creation is a map action: the surveyed anchor remains its validated centre and the selected endpoint determines true heading and full length.
- **Implemented:** My Sites opens an explicit saved-site editing state. The page identifies the active saved site and explains that Save site revisions that identity, while New site begins a different location.
- **Constraint:** V0 runway placement retains the guarded survey point as its centre because only that point has validated elevation. Moving the runway centre independently requires a future multi-point terrain/elevation survey rather than deriving authority from map imagery.

## Airport management decisions — 2026-09-08
- **Implemented:** Frontier's versioned local JSON definition is the editable source of truth. Generated projects and Community packages are derived outputs.
- **Implemented:** saved survey evidence remains subject to the same dry-run/live boundary and exact live SDK/simulator qualification. Loading never invents aircraft or camera fallback data.
- **Implemented:** optimistic revisions protect saves from another application instance. Unknown schema fields, future versions and corrupt documents are preserved and reported rather than rewritten.
- **Implemented:** historical build/install labels are informational. Opening an airport restores Survey Captured and requires a fresh validation/build before install.
- **Implemented:** permanent administrative deletion is a narrowly scoped operation over registered, verified artifacts. It does not model future selling, closing or abandonment gameplay.
- **Limit:** project cleanup is exact-inventory conservative; user-added files cause refusal. Recovery handles ordinary failures, while durable crash journaling remains future work.

## Development launch decision — 2026-09-08
- **Implemented:** publish Windows x64 self-contained and single-file to a stable repository-local `dist\FrontierPilot` path. This avoids a machine-wide .NET runtime dependency while preserving ordinary source builds.
- **Implemented:** a user-level shortcut points to the stable path. Republishing stages a complete output before replacing the previous distribution; it does not touch airport data.
- **Deferred:** signed installer, updater, SDK bootstrap/distribution, uninstall/repair and release channels belong to customer distribution work.

## Taxiway compiler correction — 2026-09-08
- **Implemented:** emit `weightLimit="0"` on every TaxiwayPath. This matches the installed official sample, permits all aircraft weights and satisfies the stricter SDK 1.2.4 BGL compiler.
- **Implemented:** keep missing layout/BGL validation strict, while surfacing the first current Package Tool error so a downstream missing-layout message is not mistaken for the root cause.

## Generic scenery object proof — 2026-09-08
- **Implemented:** represent placed models with a Frontier asset ID, instance number, local east/north offsets and true heading. Reuse `AirportSite`, WGS84 conversion, editor history and workflow invalidation rather than adding object-specific geography or build state.
- **Implemented:** start with a one-entry catalogue mapping `asobo-generic-hangar-56` to the installed Asobo generic-airport `Gen_Hangar56` library GUID. Keep the saved model keyed by the Frontier ID so later owned assets can use the same placement type.
- **Implemented:** emit a ground-snapped `SceneryObject`/`LibraryObject` reference at scale 1. Do not copy, modify or redistribute the base model.
- **Acceptance limit:** automated tests and local package inspection establish generation and likely availability; only Matt's MSFS test can establish visible resolution, terrain contact, apparent scale and the model's intrinsic forward axis.

## True-scale footprints and foundations — 2026-09-08
- **Implemented:** store model-local minimum/maximum right and forward bounds, not a centred preview size. Transforming the four corners preserves the MSFS placement origin and makes canvas, selection, site validation and terrain generation share one geometry source.
- **Evidence:** derive `Gen_Hangar56` bounds from transformed accessor data in the installed LOD0 glTF payload. Do not infer size from screenshots. Preserve the source BGL hash and all LOD measurements.
- **Implemented:** associate optional `TerrainFoundation` metadata with an asset. The current hangar uses a 2 m flat margin and 8 m falloff; generation enables polygon flattening and forced elevation and excludes secondary-heightmap roughness.
- **Prototype datum:** use the guarded airport survey ground elevation for foundations near the airport construction datum. Refuse to imply knowledge of terrain at distant object positions; per-object elevation capture is future work.
- **Acceptance limit:** compiler success does not prove visual terrain behaviour. Matt must confirm the building sits naturally and the feathered edge is acceptable.

## Tracking V1 — 2026-09-08

- **Implemented:** model aircraft telemetry separately from the avatar survey. Tracking requests `SIMCONNECT_OBJECT_ID_USER_AIRCRAFT` only and has no aircraft/avatar/camera fallback.
- **Implemented:** poll six numeric SimVars at 4 Hz and fixed-size identity strings every ten seconds. Reject non-finite, out-of-range, malformed or stale observations and expose connection loss as a retryable state.
- **Implemented:** use Mapsui.Wpf 5.1.0 with OpenStreetMap for a native WPF, key-free prototype map. Centre once at acquisition; marker updates never override later user pan/zoom.
- **Prototype dependency:** online tiles use the public OpenStreetMap service with visible attribution and a distinct user-agent. Review a production tile provider and cache policy before customer-scale distribution.
- **Acceptance limit:** automated tests prove state, validation and failure handling. Matt's live simulator test must confirm aircraft identity, units, heading orientation and marker movement.

## Tracking V2 — 2026-09-08

- **Confirmed mechanism:** use the SDK's explicit `USER_AIRCRAFT`, `USER_AVATAR` and `USER_CURRENT` proxies. Microsoft documents the object types, the installed header defines the proxies, and preserved Sedona evidence demonstrates the current-object switch and independent avatar movement.
- **Implemented:** keep aircraft and optional player positions separate in Core. Populate player only when current identity equals a distinct avatar and their synchronized values agree; otherwise retain aircraft and refuse player state.
- **Implemented:** derive distance from independent WGS84 coordinates. Label heading avatar facing because the evidence does not establish universal camera gaze or body semantics.
- **Preserved:** Airport Builder survey retains its stricter five-second stability/elevation/hash qualification. Tracking does not reuse or weaken elevation capture and does not use disputed avatar-targeted `GROUND ALTITUDE`.
- **Acceptance limit:** prior evidence proves the underlying proxies on the recorded build. The new continuous dual-marker UI still requires Matt's live seated/walk/return test.

## Tracking V3 — 2026-09-08

- **Implemented:** persist validated aircraft telemetry independently from transient player/avatar state. A saved observation is last known and never implies parked, owned or spawn-authoritative.
- **Implemented:** use a versioned document with an active ID and aircraft-record collection. Prototype IDs derive from available type/title while acknowledging that they cannot distinguish identical airframes.
- **Implemented:** update memory at telemetry rate, write at most every ten seconds, and force the latest valid sample on disconnect/shutdown. Use flushed temporary-file replacement; preserve and report corrupt or future documents.
- **Implemented:** retain the saved marker through startup, connection attempts and loss. LIVE/LAST KNOWN/NO SAVED POSITION are explicit; the first live fix may recenter once and later updates preserve manual viewport control.
- **Deferred:** parking legitimacy, engine/brake rules, stand association, ownership, stable airframe identity and spawn enforcement.

## Economy Core V0 — 2026-09-09

- **Implemented:** represent locations, customers and resources as data. Gateway, mine, community and lodge share the same inventory, need, requirement, job and shipment machinery; there is no mining-specific kernel.
- **Implemented:** treat the financial transaction ledger as the authority for player money. A cached player balance is permitted only in the same atomic aggregate and loading rejects any disagreement with the ledger.
- **Implemented:** accept reserves source inventory once. Delivery evidence commits shipment state, customer inventory, requirement/job completion, one payment and the recalculated balance as one revision. Replays are idempotent.
- **Implemented:** store one versioned world aggregate through a dedicated managed local file with flushed same-directory temporary output, atomic replacement and cross-process serialization. Existing corrupt, unsupported or inconsistent state is preserved and blocks initialization/mutation.
- **Boundary:** `VerifiedDeliveryEvidence` is supplied by a future flight/location component. Economy V0 validates references and route consistency but contains no flight-validity, airport, SimConnect or weather rule.
- **Prototype only:** deterministic seed IDs, quantities, `TPC` currency and payments are labelled test fixtures. They do not activate Namibia locations or establish balance rules.

## Namibia Location Catalogue V0 — 2026-09-09

- **Implemented:** keep versioned authored Namibia data embedded and read-only, separate from mutable economy persistence. Economy locations reuse stable catalogue IDs.
- **Implemented:** model identifier confidence, MSFS verification, customer activation and first-playable recommendation separately. Recommendation never implies verification or activation.
- **Implemented:** preserve Eros/FYWE as the sole locked gateway and all thirty section 20 candidates. Manual inspection now verifies Eros, Uis and Arandis; only Uis and Arandis are activated for the first employed-cargo slice.
- **Implemented:** require both `MsfsVerified` and `ActiveCustomer` at the mandatory future live-job-generation guard. Verification requires supplied coordinates and a confirmed identifier; activation is a separate transition.
- **Source handling:** section 20 contains no numeric values. The three activated-slice coordinates were separately researched from OurAirports and carry explicit published-source provenance; they remain distinct from manual MSFS inspection evidence and any future simulator-derived coordinates.

## Employed Cargo Jobs V0 — 2026-09-09

- **Implemented:** record manual MSFS inspection evidence independently from published coordinate provenance. A simulator verification flag does not claim that published coordinates were measured in MSFS.
- **Implemented:** activate only Uis and Arandis; Eros stays the locked system gateway. Eligibility remains a catalogue lifecycle decision and contains no aircraft or weather judgment.
- **Prototype only:** seed one configurable General Supplies offer per active destination with temporary kilogram and TPC values. No recurring need or mineral output exists.
- **Implemented, superseded in part:** keep the employee UI behind a load/accept-only boundary. Acceptance uses Economy Core's atomic reservation. As of 2026-09-21, several jobs may be reserved; only explicitly selected same-route shipments share an aircraft load and verified flight. Delivery, customer inventory and ledger payment remain unreachable without tracker-created evidence.
- **Superseded by Flight Attempt V0:** the flight-attempt and InTransit portions were deferred here; cancellation/release, expiry and permanent balance remain deferred.

## Flight Attempt V0 — 2026-09-10

- **Implemented:** consume only validated live aircraft samples emitted by Tracking; never use last-known aircraft or avatar state as flight evidence.
- **Implemented:** persist load, aircraft identity, airborne observation, later movement, maximum origin distance and qualified arrival as explicit attempt facts. Stationary timers remain transient and reset across telemetry gaps.
- **Implemented:** use normalized SimConnect title/model to reject clearly different aircraft. This is a conservative prototype fingerprint, not stable ownership identity.
- **Implemented:** write attempt/proof before their related economy mutation and reconcile only idempotent state already proven by those files. Economy completion remains one atomic inventory/job/payment revision.
- **Player action:** qualified landing enables Unload; landing never automatically pays or delivers.
- **Prototype policy:** 1.5 NM endpoint radius, 5 kt, ten seconds, 2 NM meaningful movement and 10 NM maximum departure distance are configurable test rules, not balance or safety judgments.
- **Deferred:** cancellation, reservation release, expiry, payload stations, weather, anti-teleport enforcement and recurring demand.

## Scenery Materials & Prototype Buildings V0 — 2026-09-10

- **Implemented:** save a stable optional runway preset ID while retaining the old `RunwaySurface` value as physical/backward-compatible data. A null preset deliberately selects the old package-local Dirt or Asphalt generator so loading and saving old airports does not silently alter their appearance.
- **Implemented:** use compiler-proven SDK-native surface names where available. Factual compiler acceptance and visual acceptance are separate. Dry Grass and Weathered Asphalt remain explicit fallbacks because no reliably identified native appearance variant was established.
- **Implemented:** keep colour adjustment, ground merging, tiling, falloff and additional material-layer metadata in the preset model. V0 emits only established values and never invents texture art or unknown GUIDs.
- **Implemented:** catalogue construction categories independently from placed instances and economy. Only the Base Lodge category resolves to a placeable V0 placeholder under a stable Frontier asset ID. The initially proven `Gen_Hangar56` and later `FieldCabins_01` visuals were both rejected; the current mapping is the base-scenery `BF_TemplateHouse01` temporary house.
- **Developer recovery:** resetting an incomplete flight means clearing flight-observation progress only. Cargo remains loaded/InTransit; this is not cancellation or inventory rollback. Completed delivery and all economic records are immutable through this tool.
- **Prototype retest recovery:** a separate, confirmation-gated skip action may archive one incomplete shipment. Verified MSFS manifest cargo must be removed first; the economy then records explicit cancelled states and an audited reservation release, creates no payment, and replenishes the board once. This is intentionally an administrative retest mechanism and does not define future cancellation, failure, loss or refund gameplay.

## Tracking V4 — 2026-09-08

- **Implemented:** derive Frontier location only from saved Frontier airport definitions and authoritative aircraft coordinates. Do not query or label stock MSFS airports.
- **Implemented:** measure WGS84 aircraft position against runway rectangles, buffered taxiway segments and parking circles. Use a 250 m recognition margin separate from the 3 km infrastructure-authoring envelope.
- **Implemented:** recognise a stand on ground within its radius plus 5 m; require five continuous seconds at no more than 1 kt for Parked. Centralise these values in `AirportRecognitionOptions` for live-test tuning.
- **Implemented:** keep coordinates authoritative and persist optional airport/stand context as schema 2 while reading schema 1. Clear associations whose saved airport or stand is removed.
- **Deferred:** heading/nose-wheel alignment, brakes, engines, confirmed parking authority, ownership and spawn enforcement.
## Repeatable system employment — 2026-09-10

- Continuing prototype demand is a new system-funded requirement after fulfilment. It does not claim inventory consumption or offline economic activity.
- Recurring demand is generic persisted configuration keyed by stable location/resource IDs; customer category does not select special kernel logic.
- One outstanding requirement per configured Uis/Arandis board slot prevents an accepted job from spawning its replacement before delivery.
- Gateway stock enters only as the exact unfunded shortfall for a missing offer and receives a separate inventory audit record. It never touches the player financial ledger.
- Historical jobs, requirements, shipments and ledger payments are immutable. Deterministic cycle suffixes derive from retained history.
- Flight Attempt V0 remains the exclusive producer of delivery evidence and its qualification policy remains unchanged pending simulator testing.
## Flight attempt observability — 2026-09-10

- Diagnostic timeline events are persisted beside attempts but are not evidence and are never inputs to qualification.
- Stable lifecycle keys deduplicate milestones across telemetry replay and restart; observation timestamps distinguish real connection and qualification cycles.
- Timeline retention is bounded per shipment by coalescing older connection/interruption entries before any lifecycle milestone or error.
- The support bundle selects only the relevant economy records and omits all local paths, usernames and unrelated files.
- `DeliveredAndPaid` is a terminal tracker boundary. Aircraft samples may refresh live diagnostics, but no origin/arrival qualification dispatch or flight-progression event is permitted after the persisted attempt or its economy job is terminal.

## Prospecting Camp vertical slice — 2026-09-11

- Aviation businesses, physical sites and economic operations are separate records. A site links to a stable airport-definition GUID and does not copy the editable airport document.
- Prospecting is the first data-authored operation archetype. The shared kernel consumes configured input, creates configured output, applies capacity and maintains configured logistics without mining-specific branches.
- Only a fresh, nearby, on-ground position from the explicit controlled avatar proxy may create `VerifiedSitePresenceEvidence`. The evidence is persisted; aircraft, camera and last-known positions are excluded.
- Founding, its 150 TPC ledger debit, initial audited stock and the operation record are one idempotent economy revision. Day ticks use persisted command receipts and never follow wall-clock time.
- Inbound support payment and outbound combined mineral-sale proceeds are temporary abstractions. They do not establish permanent wages, operation accounts, prices or ownership rules.
- Flight Attempt V0 thresholds remain fixed. Dynamic economic-site coordinates extend its route resolver without changing how a flight qualifies.
- A linked airport cannot be administratively deleted until a future explicit abandonment/demolition design exists. Missing airport definitions never erase their economic site.
- Flight Attempt V0 thresholds, state transitions, identity policy and transaction entry points remain fixed pending live MSFS verification.

## Aircraft loading authority and continuity — 2026-09-15

- Economy shipment plus committed manifest is authoritative for Frontier cargo; simulator payload is reconciled external state and never creates delivery evidence.
- A simulator write and Economy transaction are bridged with durable intent, read-back verification, and forward-only recovery rather than pretending they are atomic.
- Live fuel is authoritative only during one uninterrupted validated session. After disconnect, the saved snapshot freezes until the player explicitly restores it or keeps simulator fuel.
- Structural station/tank limits are hard when authoritative. Maximum gross and CG envelope are advisory warnings. Unknown limits are reported and never derived from defaults.
- TITLE/ATC MODEL is a conservative compatibility identity, not a unique airframe. Automatic cargo/fuel restoration is forbidden.
- Payload/fuel integration failure may use the existing simulated lifecycle only after an explicit player choice.

## Player-authored load distribution — 2026-09-15

Frontier Pilot does not automatically balance job cargo. Each station receives an explicit player-entered allocation; the application validates exact cargo mass and available authoritative limits, then reports factual weight/CG consequences. This preserves player judgment for the future Performance Calculator rather than embedding an unexplained optimiser in loading.


## Manual-only new cargo loading

At Matt's request, remove the UI action that starts a new simulated cargo load. Preserve existing simulated shipments and their recovery/unload semantics. All newly initiated UI loading uses explicit station allocation and simulator verification. Editing refreshes must preserve draft text and focus; an invalid draft blocks submission rather than using a hidden previous value.

## Desired-state aircraft loading — 2026-09-16

- The load manager expresses the requested physical aircraft state. Loading and unloading are positive and negative deltas through the same verified Apply operation.
- Reserved cargo is real inventory. Partial loading leaves the remainder reserved at origin; partial verified destination unloading adds only the removed quantity to destination inventory.
- Payment remains separate from physical payload manipulation and occurs only when the complete shipment has arrived with tracker-created evidence.
- Unloading active shipment cargo away from its route requires explicit confirmation and forfeits the shipment without payment. This is a terminal prototype outcome, not the future cancellation/loss design.
- Simulator station state is external and reconcilable. The persisted manifest and economy transfer log remain authoritative for managed cargo and exactly-once consequences.

## Outside authorization with seated physical application — 2026-09-16

- Guarded avatar presence remains the authority for permitting ground handling. Every positive MSFS cargo load creates a short-lived post-boarding verification authorization because a third-party aircraft may accept an outside station value and then rebuild or discard it when cockpit control resumes.
- Boarding never writes payload automatically. The player must invoke the separate seated action in the same aircraft, on the ground, stationary, within 1.5 NM and within five minutes of authorization.
- A payload-station read-back is insufficient evidence of physical loading. If station and total weight survive boarding they are verified without another write; otherwise the seated compatibility path reapplies the authorized target. In either case, reported total aircraft weight must match the authorized result within 1 kg before any new economy cargo transfer is committed.

## Player-operation logistics contracts — 2026-09-19

- Player-created logistics contracts extend the normal requirement/job/shipment lifecycle. They do not form a parallel mission system.
- Cargo and transport are distinct: real origin stock is reserved at publication, while pilot payout, gateway purchase/sale and road cost use separate financial concepts.
- Until operation accounts exist, the local player's ledger funds their owned operation. Owner-flown pilot payout is a zero-net self-transfer so fulfillment cannot create money.
- Road fallback uses a replaceable deterministic approximation and persisted event times. It is an economic fallback rather than teleportation or a road-network simulation.

## Operation-site infrastructure progression — 2026-09-23

- **Implemented:** a new Frontier operation begins as a saved site with its operation building; a runway is optional infrastructure on that same site, not a separate site or operation category. Prospecting Camp founding requires the Base Lodge and guarded walking-avatar presence, but does not require a runway.
- **Jobs versus spawn:** economic jobs use the site's location and do not require a runway or parking. `TaxiwayParking` belongs to an MSFS `Airport`, and an MSFS airport requires at least one runway. Before the runway is built there is no normal MSFS spawn. A runway adds runway-start spawning; parking adds a ramp/parking spawn.
- **Implemented:** generation before airfield construction omits `Airport`, `Runway`, taxiway and parking XML while retaining terrain-snapped scenery objects. Enabling the runway later and rebuilding upgrades the same saved site. Disabling an existing runway clears incompatible parking and taxiways after the player chooses that change.
- **Prototype visual:** the Base Lodge uses the installed base-scenery `BF_TemplateHouse01` library object at normal scale. Live testing rejected `MICROSOFT_ASSET_FieldCabins_01` because it appeared as a large compound/hangar-like structure. The replacement is deliberately a temporary standalone-house fallback; its 10 × 8 m authoring footprint remains unverified pending live measurement and dedicated Frontier lodge artwork.
