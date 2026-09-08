# Frontier Airport Builder — agent instructions

Read docs/TECHNICAL_STATUS.md, docs/DESIGN_DECISIONS.md and docs/BACKLOG.md before changing the pipeline. Inspect Git status and preserve pending work. This repository is nested in a ChatGPT project mirror; sources/ outside this repository is read-only. Installed SDK samples are also read-only.

Use the existing .NET 8 Core, MsfsSdk, Workflow, Desktop and CLI App projects. Do not recreate capture, geometry, XML generation or build logic in the view. No economy, careers, multiplayer or settlements in this milestone.

Never weaken avatar capture safeguards or substitute aircraft/camera data. Qualification is empirical and tied to exact SDK/simulator hashes. Preserve Microsoft's documented contradiction and the return-to-A caveat.

Do not modify FPT1 or FPSC, prior evidence or unrelated Community content. Desktop replacement requires an intact ownership receipt and matching hashes; never adopt an unknown existing package. Keep backups and fail safely if MSFS is running. Do not delete installed packages to make tests pass.

Build/test with .tools/dotnet/dotnet.exe; set DOTNET_CLI_HOME=.tools/home and NUGET_PACKAGES=.tools/nuget. Run focused tests and a solution build. Tests use temporary packages, never the real Community folder. Local UI launch uses scripts/Start-Desktop.ps1. Commit implementation and accurate evidence; distinguish automated tests from live simulator verification. No remote is configured at this time.

The normal local test distribution is a self-contained Windows x64 single-file build at dist/FrontierPilot/FrontierPilot.exe. Recreate it with scripts/Publish-Development.ps1; dist is intentionally untracked. See docs/development-distribution.md. Do not commit generated runtime binaries.

For work without the user/simulator: use the isolated dry-run backend and tests; never invoke Package Tool because it launches MSFS. Build Release if an existing Debug UI is open instead of closing a window that may hold user work. Dry-run artifacts are synthetic and must not be installed into real Community. See docs/offline-workflow.md.

Spatial policy and taxiways: read docs/taxiways-and-airport-site.md. Use Core.AirportSite for all infrastructure bounds; do not reintroduce feature-specific anchor-radius limits. Keep geography in local east/north metres and WGS84 conversions in Core. Taxiway surfaces currently share runway materials. Taxiway compilation is proven with the exact NTA1 definition after adding compiler-required `weightLimit="0"`; visual behaviour/distant parking still await Matt's MSFS acceptance. Do not describe a complete ATC/AI taxi network as implemented.

Persistence and deletion: read docs/airport-management.md. Frontier's saved JSON is the editable source of truth; packages are derived. Loading must not restore stale build/install authority. Deletion may act only on exact registered workspaces and receipt-owned packages after a fresh preview and explicit confirmation. Preserve unreadable documents and refuse unknown or changed files.

Generic scenery objects: read docs/generic-scenery-object-proof.md and docs/object-footprints-and-foundations.md. The sole current catalogue entry is the read-only Asobo `Gen_Hangar56` library model referenced by GUID; do not copy Microsoft assets. Its transformed glTF LOD0 bounds are the footprint authority. Placed objects use Core local metres, `AirportSite`, WGS84 conversion and shared layout history. Position/orientation are live accepted; do not claim foundation acceptance until Matt verifies terrain contact and blending.
