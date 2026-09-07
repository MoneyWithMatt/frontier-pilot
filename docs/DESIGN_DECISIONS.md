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
