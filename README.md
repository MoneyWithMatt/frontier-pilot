# Frontier Airport Builder — Prototype 0.1

Initial toolchain proof for Frontier Pilot. The .NET solution builds and its three SDK inspection tests pass. The untouched official airport sample has **not built successfully**. See [the experiment report](docs/sample-build-report.md).

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

Run `scripts/Test-SimpleAirport.ps1` from PowerShell 7 to copy and build a fresh sample in `workspaces/`. This invokes the official tool and may launch the simulator. Each run saves its command, invocation, logs, file inventories, hashes and result under `evidence/`. Inspect both the result and generated package; an exit code alone is insufficient proof. This machine uses Steam, so the script passes `-forcesteam`.

SDK sample copies and compiled outputs stay in ignored disposable workspaces. Installed SDK files and synced project sources must remain unchanged. Evidence is committed, but third-party sample assets are not vendored.

Next gate: diagnose the package-tool launch/build failure and obtain an intact sample package before developing the UI or airport generator. No settlements, economy or multiplayer are in scope.
