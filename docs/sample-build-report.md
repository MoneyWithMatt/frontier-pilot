# Untouched SimpleAirport build experiment

Date: 2026-09-06. Original attempt status: **failed — no compiled package produced**. Follow-up diagnosis resolved the launch failure and identified a separate sample metadata validation problem; see [the current diagnosis](toolchain-diagnosis.md). The historical results below are preserved.

## Installed environment

| Item | Exact value |
| --- | --- |
| MSFS 2024 SDK version | `1.2.4` (version.txt; corroborated by Windows Core and Samples registration) |
| SDK root | `C:\Users\mattr\Desktop\MSFS 2024 SDK` |
| Package tool | `C:\Users\mattr\Desktop\MSFS 2024 SDK\Tools\bin\fspackagetool.exe` |
| Official sample directory | `C:\Users\mattr\Desktop\MSFS 2024 SDK\Samples\DevmodeProjects\Sceneries\SimpleAirport` |
| Sample entry point | `C:\Users\mattr\Desktop\MSFS 2024 SDK\Samples\DevmodeProjects\Sceneries\SimpleAirport\AirportProject.xml` |
| Simulator executable | `C:\Program Files (x86)\Steam\steamapps\common\Limitless\FlightSimulator2024.exe` |
| Simulator file/product version | `1,8,14,0` |
| Older SDK (not used) | `C:\MSFS SDK`, version `0.24.3.0` |
| Initial .NET state | Runtime 8.0.15 installed, no SDK found |
| Provisioned .NET SDK | `8.0.424`, repository-local `.tools\dotnet` |

Repository root: `C:\Users\mattr\.codex\.chatgpt-projects\g-p-6a904cf3f8fc81919657c40eb26e3f77\frontier-airport-builder`.

## Procedure and evidence

The installed sample was copied byte-for-byte into a fresh disposable directory. SHA-256 inventories verified all nine input files matched before invocation. No sample XML, textures, package definitions or installed tool configuration were changed.

1. `evidence/simpleairport-20260906-122902`: sandboxed attempt, stopped by the agent after no output appeared. Exit `-1` reflects termination, not a compiler diagnosis.
2. `evidence/simpleairport-20260906-123037`: authorized execution outside the sandbox. Tool returned exit `1` after approximately two minutes. Standard output and standard error were empty. No BGL, manifest or compiled output directory was produced. The output tree contains only the copied inputs.

Each attempt preserves `command.ps1`, `invocation.json` (including tool hash and absolute arguments), input inventories before/after, `stdout.log`, `stderr.log`, `result.json`, and `output-tree.txt/json`. Both post-run installed sample inventories match their pre-run inventories.

The command invokes `fspackagetool.exe` with the copied `AirportProject.xml`, explicit disposable output and temporary directories, `-rebuild -forcesteam -nopause`. See the saved command for all exact absolute paths.

The failure is confirmed; its root cause is **not established**. Empty redirected logs are not evidence of a particular SDK or scenery error. A terminal retry of the saved command also returned exit `1`, with no package-tool diagnostic text. Enter was sent before collecting the terminal result; this does not prove the process was waiting for input. See `console-retry.log` and `console-retry-result.json`. The output tree remained unchanged. The reusable script was subsequently given closed standard input and a configurable 180-second timeout to avoid indefinite waits; those safeguards have not yet been exercised against the simulator.

## Solution verification

All four .NET 8 projects build in Release: zero warnings/errors. All three xUnit tests pass. See `evidence/dotnet-build.log`, `evidence/dotnet-tests.log` and `evidence/dotnet-tests/prototype.trx`. The App's read-only inspection of the real installation is captured in `evidence/sdk-inspection.json`.

## Decision and next experiment

The sample compilation gate is not passed. Keep UI and generated-airport work deferred. Diagnose simulator launch and console visibility using the existing copied sample before altering sample content. No package was installed into Community, and in-simulator loading has not been tested.

Official references consulted:

- [MSFS 2024 Package Tool](https://docs.flightsimulator.com/msfs2024/retail/sdk-tools/package-tool/package-tool/): invocation, output/temp paths, Steam selection and simulator dependency.
- [MSFS 2024 SimpleAirport](https://docs.flightsimulator.com/msfs2024/retail/samples-tutorials/samples/sceneries/simpleairport/): official sample entry point and expected package.
- [Microsoft .NET Windows installation](https://learn.microsoft.com/en-us/dotnet/core/install/windows): official local install script.
