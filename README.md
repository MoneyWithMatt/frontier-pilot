# Frontier Airport Builder — Prototype 0.1

Frontier Pilot's first generated airport, **Frontier Test Strip (FPT1)**, compiles successfully with MSFS 2024 SDK 1.2.4. All 152 unit tests pass. The CLI produces one airport, one runway and two starts from an explicit airstrip definition. No WPF UI is implemented.

See [the implementation and verification report](docs/prototype-0.1.md), [the compiled package](artifacts/fpt1/frontier-airport-fpt1.zip), and [template provenance](src/MsfsSdk/Templates/PROVENANCE.md). Earlier baseline investigations remain in [the diagnosis](docs/toolchain-diagnosis.md) and [the original experiment](docs/sample-build-report.md).

## Projects

- `src/App`: CLI commands to inspect the SDK, generate an isolated project, or generate and compile it.
- `src/Core`: airstrip data/validation, heading and runway-number logic, WGS84 runway geometry.
- `src/MsfsSdk`: embedded working template, deterministic System.Xml.Linq generation, SDK invocation and package validation.
- `tests/Tests`: xUnit coverage of geometry, validation, generated XML, determinism and build-result checks.

## Development

The repository-local .NET SDK is `.tools/dotnet/dotnet.exe`, version 8.0.424. It is excluded from Git. On another PC install .NET SDK 8.0.424 (or a compatible patch selected by `global.json`) and use `dotnet` instead.

```powershell
& .\.tools\dotnet\dotnet.exe build FrontierAirportBuilder.sln -c Release
& .\.tools\dotnet\dotnet.exe test FrontierAirportBuilder.sln -c Release
& .\.tools\dotnet\dotnet.exe run --project src/App -- inspect 'C:\Users\mattr\Desktop\MSFS 2024 SDK'
& .\.tools\dotnet\dotnet.exe run --project src/App -- generate examples/frontier-test-strip.json workspaces/new-source
& .\.tools\dotnet\dotnet.exe run --project src/App -- build examples/frontier-test-strip.json workspaces/new-build
```

Run `scripts/Test-SimpleAirport.ps1` from PowerShell 7 to copy and build a fresh untouched sample in `workspaces/`. This invokes the official tool and may launch the simulator through Steam. Each run saves its command, invocation, process observations, logs, file inventories, hashes and result under `evidence/`. It uses the project directory, project path and `-nopause`, with the correct SDK environment set only for the child process. Inspect validation errors and the generated package; exit 0 alone is insufficient proof.

Use a new output directory each time. The CLI refuses existing folders and requires existing MSFS/Package Tool sessions to finish before an isolated build. The user-level MSFS2024_SDK override now points to the verified SDK root; an explicit fourth argument to `build` can select another root. Current process environment is also overridden for the child tool.

Disposable runs stay in ignored workspaces. The successful FPT1 package and evidence are committed. The minimal embedded template retains SDK-derived asphalt/thumbnail dependencies with provenance and the accompanying license; those assets are not original Frontier artwork. Installed SDK files and synced project sources remain unchanged.

FPT1 retains the original sample-location baseline and is installed separately in Community2024. The Namibia spike **Skeleton Coast Test Strip (FPSC)** now completes live walking-avatar capture, empirical automatic ground elevation, dirt airport generation, compilation and installation. Matt's visual test reported no rocks, bumps or buried/floating edges and good terrain fit. See [the result and limitations](docs/skeleton-coast-test.md), [live capture](docs/live-survey.md), and [elevation qualification](docs/avatar-elevation-validation.md). The earlier direct-coordinate API investigation remains historical evidence; the working approach samples the avatar's current location. No WPF UI, settlements, economy or multiplayer are implemented.

## Guided Windows interface
Run scripts/Start-Desktop.ps1 after building the solution. The WPF window guides you through connecting, capturing your walking-avatar runway centre, defining a strip, compiling and safely installing it. Keep the builder open while closing MSFS between capture and compilation. See docs/TECHNICAL_STATUS.md for supported versions, evidence and current limitations.

Offline regression workflow: scripts/Start-Desktop.ps1 -DryRun. Build Release first; no simulator or SDK is required in this mode. The compiler is simulated and its packages must never be copied to MSFS. See docs/offline-workflow.md for the complete procedure.
