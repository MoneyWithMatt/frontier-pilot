# Aircraft Condition Recorder V0 verification

Date: 2026-10-19. Baseline: `33be077`. The preceding investigation's four staged documentation files were present at task start and preserved. No approved repository-local `user.name` or `user.email` is configured; no identity was invented. Changes remain available for review/staging; see final Git status for commit state.

## Implemented scope

Core condition/identity/profile contracts, standard candidate catalogue, isolated read-only native source and bounded packet correlation, Workflow create-only individual-aircraft documents and streaming export, existing CLI commands and launcher, automated tests, architecture/compatibility/restoration/future-failure design. No Desktop UI logic changes, gameplay service changes, simulator writes or maintenance rules.

## Automated results

Used repository `.tools/dotnet/dotnet.exe`, `DOTNET_CLI_HOME=.tools/home`, `NUGET_PACKAGES=.tools/nuget`, Release configuration.

```powershell
& .tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --filter 'FullyQualifiedName~AircraftConditionTests' --nologo
& .tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --nologo
& .tools/dotnet/dotnet.exe build FrontierAirportBuilder.sln -c Release --nologo
```

- New focused suite: **24 passed, 0 failed, 0 skipped**.
- Complete test suite: **719 passed, 0 failed, 0 skipped**.
- Final Release solution build: **0 warnings, 0 errors**.
- Existing tracking, avatar safeguards, flight-attempt, cargo/loading and economy tests remain in the full passing suite. No real Community packages are used by these tests.

The tests cover invalid/NaN/out-of-range failure readings, received-but-unverified zero values, generic multi-engine catalogue generation, independent airframe GUIDs for identical models, unknown initial condition, create-only persistence, preservation of corrupt/future/unknown documents, component fault/estimate persistence, duplicate components, unsupported-variable isolation, timeout nulls, late/duplicate request isolation, truncated/misidentified native payloads, timestamped export, no selected-airframe mutation, output overwrite refusal, identity changes/mismatches, cancellation footer and rejection of stale or estimated observations.

Native packet tests use synthetic byte buffers and Workflow tests use a synthetic source. They prove local validation and routing, not live SimConnect variable semantics. Static inspection of the native source confirms only read/request/close functions are bound; it has no event transmission, SetDataOnSimObject, arbitrary RPN execution or bridge installation.

## CLI smoke experiments

Used a unique `FrontierConditionSmoke-<GUID>` directory in the system temporary directory; synthetic artifacts were retained there, not added to source control.

1. `condition-airframe` with explicit confirmation created a synthetic GUID-backed record: exit **0**.
2. Repeated creation at the same path: exit **1**; SHA-256 comparison confirmed original bytes preserved.
3. With no `FlightSimulator2024` process present, `record-condition` returned exit **1** with guidance to load a flight. Its partial JSONL contained a valid read-only/unverified session header and an `error` end record with **0 frames** and `IOException`. No simulator was launched or contacted by that failure path.

The CLI has no synthetic mode masquerading as live support. Tests inject their source explicitly and label it synthetic.

## Packaging

`scripts/Publish-Development.ps1` completed successfully. The self-contained Windows x64 single-file development executable was recreated at `dist/FrontierPilot/FrontierPilot.exe`; it remains untracked. The new recorder is invoked through the existing CLI project/launcher, not through a new Desktop page. No existing user window was closed. No generated binaries were staged.

## Aircraft inspection and live boundary

Read only the installed package-root configuration and relevant streamed-package directory inventory. Found `fs24-microsoft-aircraft-pa28-236-dakota`, `fs24-asobo-aircraft-baron-g58`, `fs24-asobo-aircraft-c172sp-as1000` and `fs24-asobo-aircraft-c172sp-classic`. Targeted searches found no accessible manifest/aircraft.cfg in these cache directories, so version and runtime identity are explicitly pending. No package files were modified or copied.

**No MSFS live recording, failure activation, repair, maintenance, payload/fuel write or native condition write was performed.** No accepted capability profile was created. No Package Tool, Community install or personal-save operation occurred. All live field semantics, external component-W&T addressing, coexistence performance, reload persistence and add-on compatibility remain unverified. Follow [the live test procedure](../../docs/aircraft-condition-v0.md).
