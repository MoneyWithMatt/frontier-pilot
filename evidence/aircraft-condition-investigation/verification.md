# Aircraft-condition investigation — verification

Date: 2026-10-19. Baseline commit: `33be077`. Initial `git status --short`: empty. Report: [aircraft-condition-feasibility.md](../../docs/aircraft-condition-feasibility.md).

## Scope

Documentation and offline investigation only. No production code, simulator settings, aircraft state, personal saves, installed SDK samples, Community packages, FPT1/FPSC or prior evidence were changed. No simulator/Package Tool was launched. No failure, repair, payload or fuel command was sent. No live condition capability is claimed.

`Get-Process` inspection found zero processes with names matching `*FlightSimulator*`. The installed SDK's `version.txt` reports **1.2.4**. No simulator build was measured in a live handshake during this investigation.

## Experiment E1: native loading and export resolution

In the local PowerShell process, loaded the installed x64 `SimConnect.dll` using .NET `NativeLibrary.Load`, resolved the following exports with `GetExport`, then freed the library in `finally`. All resolved to nonzero addresses:

- `SimConnect_Open`
- `SimConnect_AddToDataDefinition`
- `SimConnect_RequestDataOnSimObject`
- `SimConnect_GetNextDispatch`
- `SimConnect_SetDataOnSimObject`
- `SimConnect_MapClientEventToSimEvent`
- `SimConnect_TransmitClientEvent`
- `SimConnect_TransmitClientEvent_EX1`
- `SimConnect_RequestClientData`
- `SimConnect_EnumerateInputEvents`

**No exported function was invoked.** This proves local native binary loadability and symbol presence only. It does not establish variable availability, parameter syntax, successful connection, event effects or aircraft compatibility.

Reproduction (PowerShell 7 / .NET with NativeLibrary; read-only, no simulator connection):

```powershell
$conditionDll = 'C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll'
$conditionHandle = [System.Runtime.InteropServices.NativeLibrary]::Load($conditionDll)
try {
    foreach ($conditionExport in @(
        'SimConnect_Open', 'SimConnect_AddToDataDefinition',
        'SimConnect_RequestDataOnSimObject', 'SimConnect_GetNextDispatch',
        'SimConnect_SetDataOnSimObject', 'SimConnect_MapClientEventToSimEvent',
        'SimConnect_TransmitClientEvent', 'SimConnect_TransmitClientEvent_EX1',
        'SimConnect_RequestClientData', 'SimConnect_EnumerateInputEvents')) {
        $conditionAddress = [System.Runtime.InteropServices.NativeLibrary]::GetExport(
            $conditionHandle, $conditionExport)
        '{0}: {1}' -f $conditionExport, ($conditionAddress -ne [IntPtr]::Zero)
    }
} finally {
    [System.Runtime.InteropServices.NativeLibrary]::Free($conditionHandle)
}
```

## Installed-file identity

SHA-256, read directly from the installed files:

| File relative to `C:\MSFS 2024 SDK` | SHA-256 |
|---|---|
| `SimConnect SDK/lib/SimConnect.dll` | `E54A505BE0B0A4CDC98DFB45F0C202BA9637F5D494C0919040BC89AC67D92ADB` |
| `SimConnect SDK/include/SimConnect.h` | `3684C4B50630DB2ABEC9815105498FA5B921C653F57423B9B5E6214A60CAABEA` |
| `WASM/include/MSFS/MSFS_Vars.h` | `284E23582C49B6778FCD7AADE80C2048F60D8C21B8F3685ECE0B1E9C28387F5F` |

These hashes identify inspected files; they are not a new survey or condition qualification allowlist.

## Static inspection S1: SDK and application

- Installed `SimConnect.h:920` declares the ordinary AddToDataDefinition API. The header also declares event transmission, ClientData and Input Event operations.
- `MSFS_Vars.h` declares `fsVarsAircraftVarGet` with `FsVarParamArray`. No WASM bridge was built or installed.
- Installed SimvarWatcher `textsimvars.cs` lists engine failure/damage, Hobbs, touchdown and W&T variables. Its request UI appends one index and passes a datum string to AddToDataDefinition. Catalogue inclusion is not runtime support proof. Its older list also includes names for which no current documented general-purpose contract was established (`FAILURE ACTIVE`, `FAILURE VALUE`); they were not promoted into the recommendation.
- Application source review confirmed only six position/movement numeric fields in live tracking. Loading separately reads combustion and writes only payload/fuel. No condition monitoring or failure command implementation was found in the inspected integration.

## Documentation check D1: setter icons

Read official retail table HTML with `Invoke-WebRequest` because the text view omits graphical yes/no marks. Parsed table rows and inspected their last cell: `checkmark_circle_red` is the non-settable cross; `checkmark_stem` is the writable tick. No source HTML was copied into the repository.

Results: `ENG FAILED`, `GENERAL ENG FAILED`, engine damage percentage, oil leaked percentage, Hobbs, left/right brake temperature and remaining weight, flap speed damage and gear speed damage are marked non-settable. `GENERAL ENG COMBUSTION` is marked settable. W&T health/weakness/failure/aggregate values are non-settable; preflight `CHECK PROGRESSION` is settable. These are documentation observations, not attempted simulator writes.

## Automated regression/build verification

Environment for both commands:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools/nuget'
```

```powershell
& .tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --filter 'FullyQualifiedName~TrackingTests|FullyQualifiedName~FlightAttemptTests|FullyQualifiedName~AircraftLoading' --nologo
& .tools/dotnet/dotnet.exe build FrontierAirportBuilder.sln -c Release --nologo
```

Results: **77 passed, 0 failed, 0 skipped**; solution build **0 warnings, 0 errors**. Tests use existing isolated fixtures; no new condition-behaviour test was fabricated. An earlier invocation used the nonexistent path `tests/Tests/Tests.csproj` and exited with MSB1009 before testing; the corrected command above ran successfully. No full-suite or live simulator acceptance is claimed.

No runtime change was made, so the untracked development distribution was not republished. The next live experiment and acceptance criteria are specified in the report.
