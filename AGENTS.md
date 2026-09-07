# Frontier Airport Builder — agent instructions

Read docs/TECHNICAL_STATUS.md, docs/DESIGN_DECISIONS.md and docs/BACKLOG.md before changing the pipeline. Inspect Git status and preserve pending work. This repository is nested in a ChatGPT project mirror; sources/ outside this repository is read-only. Installed SDK samples are also read-only.

Use the existing .NET 8 Core, MsfsSdk, Workflow, Desktop and CLI App projects. Do not recreate capture, geometry, XML generation or build logic in the view. No economy, careers, multiplayer or settlements in this milestone.

Never weaken avatar capture safeguards or substitute aircraft/camera data. Qualification is empirical and tied to exact SDK/simulator hashes. Preserve Microsoft's documented contradiction and the return-to-A caveat.

Do not modify FPT1 or FPSC, prior evidence or unrelated Community content. Desktop replacement requires an intact ownership receipt and matching hashes; never adopt an unknown existing package. Keep backups and fail safely if MSFS is running. Do not delete installed packages to make tests pass.

Build/test with .tools/dotnet/dotnet.exe; set DOTNET_CLI_HOME=.tools/home and NUGET_PACKAGES=.tools/nuget. Run focused tests and a solution build. Tests use temporary packages, never the real Community folder. Local UI launch uses scripts/Start-Desktop.ps1. Commit implementation and accurate evidence; distinguish automated tests from live simulator verification. No remote is configured at this time.
