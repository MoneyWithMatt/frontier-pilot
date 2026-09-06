# Frontier Airport Builder — Prototype 0.1

Initial toolchain proof for Frontier Pilot. The .NET solution builds and its three SDK inspection tests pass. The silent simulator launch failure is resolved. The untouched SDK 1.2.4 sample compiles its BGL but fails current package validation; a separately labelled copy with one metadata addition builds a complete package. See [the diagnosis](docs/toolchain-diagnosis.md) and [the original experiment](docs/sample-build-report.md).

## Projects

- `src/App`: temporary console entry point for inspecting an explicitly supplied SDK root. No UI implemented or framework selected.
- `src/Core`: shared SDK installation record; future airport definitions belong here.
- `src/MsfsSdk`: read-only SDK inspection. It records version and required paths, but does not certify SDK compatibility.
- `tests/Tests`: xUnit tests for SDK inspection and incomplete/invalid installations.

## Development

The repository-local .NET SDK is `.tools/dotnet/dotnet.exe`, version 8.0.424. It is excluded from Git. On another PC install .NET SDK 8.0.424 (or a compatible patch selected by `global.json`) and use `dotnet` instead.

```powershell
& .\.tools\dotnet\dotnet.exe build FrontierAirportBuilder.sln -c Release
& .\.tools\dotnet\dotnet.exe test FrontierAirportBuilder.sln -c Release
& .\.tools\dotnet\dotnet.exe run --project src/App -- 'C:\Users\mattr\Desktop\MSFS 2024 SDK'
```

Run `scripts/Test-SimpleAirport.ps1` from PowerShell 7 to copy and build a fresh untouched sample in `workspaces/`. This invokes the official tool and may launch the simulator through Steam. Each run saves its command, invocation, process observations, logs, file inventories, hashes and result under `evidence/`. It uses the project directory, project path and `-nopause`, with the correct SDK environment set only for the child process. Inspect validation errors and the generated package; exit 0 alone is insufficient proof.

SDK sample copies and compiled outputs stay in ignored disposable workspaces. Installed SDK files and synced project sources must remain unchanged. Evidence is committed, but third-party sample assets are not vendored.

Next gate: establish an updated official baseline or adopt the proven one-field compatibility migration before developing the UI or airport generator. No settlements, economy or multiplayer are in scope.
