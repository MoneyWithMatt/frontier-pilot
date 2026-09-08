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
