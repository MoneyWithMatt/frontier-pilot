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

## Tracking V4 — 2026-09-08

- **Implemented:** derive Frontier location only from saved Frontier airport definitions and authoritative aircraft coordinates. Do not query or label stock MSFS airports.
- **Implemented:** measure WGS84 aircraft position against runway rectangles, buffered taxiway segments and parking circles. Use a 250 m recognition margin separate from the 3 km infrastructure-authoring envelope.
- **Implemented:** recognise a stand on ground within its radius plus 5 m; require five continuous seconds at no more than 1 kt for Parked. Centralise these values in `AirportRecognitionOptions` for live-test tuning.
- **Implemented:** keep coordinates authoritative and persist optional airport/stand context as schema 2 while reading schema 1. Clear associations whose saved airport or stand is removed.
- **Deferred:** heading/nose-wheel alignment, brakes, engines, confirmed parking authority, ownership and spawn enforcement.
