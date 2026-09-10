# Frontier Airport Builder — agent instructions

Read docs/TECHNICAL_STATUS.md, docs/DESIGN_DECISIONS.md and docs/BACKLOG.md before changing the pipeline. Inspect Git status and preserve pending work. This repository is nested in a ChatGPT project mirror; sources/ outside this repository is read-only. Installed SDK samples are also read-only.

Use the existing .NET 8 Core, MsfsSdk, Workflow, Desktop and CLI App projects. Do not recreate capture, geometry, XML generation or build logic in the view.

Never weaken avatar capture safeguards or substitute aircraft/camera data. Qualification is empirical and tied to exact SDK/simulator hashes. Preserve Microsoft's documented contradiction and the return-to-A caveat.

Do not modify FPT1 or FPSC, prior evidence or unrelated Community content. Desktop replacement requires an intact ownership receipt and matching hashes; never adopt an unknown existing package. Keep backups and fail safely if MSFS is running. Do not delete installed packages to make tests pass.

Build/test with .tools/dotnet/dotnet.exe; set DOTNET_CLI_HOME=.tools/home and NUGET_PACKAGES=.tools/nuget. Run focused tests and a solution build. Tests use temporary packages, never the real Community folder. Local UI launch uses scripts/Start-Desktop.ps1. Commit implementation and accurate evidence; distinguish automated tests from live simulator verification. No remote is configured at this time.

The normal local test distribution is a self-contained Windows x64 single-file build at dist/FrontierPilot/FrontierPilot.exe. Recreate it with scripts/Publish-Development.ps1; dist is intentionally untracked. See docs/development-distribution.md. Do not commit generated runtime binaries.

For work without the user/simulator: use the isolated dry-run backend and tests; never invoke Package Tool because it launches MSFS. Build Release if an existing Debug UI is open instead of closing a window that may hold user work. Dry-run artifacts are synthetic and must not be installed into real Community. See docs/offline-workflow.md.

Spatial policy and taxiways: read docs/taxiways-and-airport-site.md. Use Core.AirportSite for all infrastructure bounds; do not reintroduce feature-specific anchor-radius limits. Keep geography in local east/north metres and WGS84 conversions in Core. Taxiway surfaces currently share runway materials. Taxiway compilation is proven with the exact NTA1 definition after adding compiler-required `weightLimit="0"`; visual behaviour/distant parking still await Matt's MSFS acceptance. Do not describe a complete ATC/AI taxi network as implemented.

Persistence and deletion: read docs/airport-management.md. Frontier's saved JSON is the editable source of truth; packages are derived. Loading must not restore stale build/install authority. Deletion may act only on exact registered workspaces and receipt-owned packages after a fresh preview and explicit confirmation. Preserve unreadable documents and refuse unknown or changed files.

Scenery materials and objects: read docs/scenery-materials-and-prototype-buildings-v0.md, docs/generic-scenery-object-proof.md and docs/object-footprints-and-foundations.md. New surface choices use stable preset IDs; null preserves legacy Dirt/Asphalt output. Native surface identifiers are compiler-proven but not visually accepted. Dry Grass and Weathered Asphalt are explicit fallbacks. The Prototype Base Lodge is the read-only Asobo `Gen_Hangar56` library model under a distinct placeholder asset ID; do not copy Microsoft assets or describe it as final lodge art. Its transformed glTF LOD0 bounds remain the footprint authority. Placed objects use Core local metres, `AirportSite`, WGS84 conversion and shared layout history.

Tracking: read docs/tracking-v1.md and docs/tracking-v2-avatar.md. V1 aircraft tracking is live accepted. V2 requests explicit aircraft/avatar/current proxies and exposes a player only after object identity and synchronized-value agreement; never substitute aircraft or camera coordinates. Airport survey keeps its separate stricter elevation/hash/stability gates. Marker updates preserve manual viewport changes. Dual-marker UI acceptance remains pending. Public OpenStreetMap tiles are a limited prototype dependency, not an approved customer-scale service.

Tracking persistence: read docs/tracking-v3-persistence.md. Only validated aircraft telemetry is durable; avatar/current-object data stays transient. Saved positions are LAST KNOWN, never confirmed parked or spawn-authoritative. Preserve corrupt/future documents and use the explicit clear action; do not infer ownership or stable identity from the prototype type/title key.

Tracking location recognition: read docs/tracking-v4-airport-recognition.md. Match only saved Frontier definitions, using Core WGS84/infrastructure geometry. The 250 m airport margin and stand radius + 5 m are operational recognition policy, separate from the 3 km authoring envelope. Parked is a five-second low-speed prototype observation, not ownership, confirmed parking authority or spawn permission.

Economy Core V0: read docs/economy-core-v0.md. The ledger is authoritative for money; cargo reservation, customer inventory, delivery and payment commit as one economy revision. Preserve corrupt/future state and never initialize over it. `VerifiedDeliveryEvidence` is an external boundary, not permission to invent flight proof. Prototype seed values are temporary. Do not extend the accept-only Jobs slice into SimConnect flight lifecycle, weather, multiplayer, settlements or industry production without a later task.

Namibia catalogue and employed cargo: read docs/namibia-location-catalogue-v0.md and docs/employed-cargo-jobs-v0.md. Authored section 20 data remains separate from mutable economy state. Eros, Uis and Arandis have explicit manual MSFS inspection evidence plus separately sourced published coordinates; only Uis and Arandis are active customers. Ordinary offers must pass catalogue lifecycle guards. Jobs may accept, load and request unload, but cannot construct or modify delivery evidence.

Flight Attempt V0: read docs/flight-attempt-v0.md. Only `FlightAttemptTracker` may construct delivery evidence. Loading is simulated and never changes MSFS payload. Persist attempt facts before cross-file economy transitions, reconcile only explicitly proven state, and reset stationary timers across invalid telemetry/disconnection/gaps. Do not add cancellation, expiry, teleport enforcement, weather or recurring demand without a later task.

Flight observability: read docs/flight-attempt-observability-v0.md. Schema-2 diagnostic timeline entries are never delivery evidence and must not affect qualification. Preserve deterministic milestone IDs, bounded retention, redacted exports and the fixed Flight Attempt V0 rules.

Repeatable employed cargo: read docs/employed-cargo-job-cycle-v0.md. Board refresh is one idempotent Economy Core maintenance transaction over persisted generic demand definitions. Keep gateway inventory audit separate from the authoritative financial ledger, preserve deterministic cycle IDs and completed history, and do not infer consumption or time progression.

The developer flight reset clears only incomplete persisted observation progress and leaves the accepted shipment loaded/InTransit. It must refuse completed attempts and must never alter inventory, jobs, requirements, ledger entries or balances. It is diagnostic recovery, not cargo cancellation.
