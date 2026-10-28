# Build and testing

## Windows prerequisites

Windows x64, Git, PowerShell 7.2+ and **.NET SDK 8.0.424**, pinned without roll-forward in `global.json`. Install the SDK from Microsoft's official distribution, or use GitHub's pinned `setup-dotnet` action. No MSFS installation, SDK, Community directory, private package source or personal save is needed for regression/build/publish.

Existing developer checkouts use `.tools/dotnet/dotnet.exe`. Fresh machines can use the official SDK on PATH. The verification entry point prefers the repository SDK when present and supports `-DotnetPath dotnet` explicitly. It sets `DOTNET_CLI_HOME=.tools/home` and `NUGET_PACKAGES=.tools/nuget`; caches are optional and untracked.

## Entry points

From the repository root, run on an inspected branch:

```powershell
# Main regression: restore, complete Release solution build, all xUnit cases.
./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-local

# Separate synthetic UI tests; requires a usable Windows desktop session.
./scripts/Invoke-Verification.ps1 -Stage Smoke -OutputDirectory .ci-output-local

# Clean committed source and both passing receipts are required for a bundle.
./scripts/Invoke-Verification.ps1 -Stage Package -OutputDirectory .ci-output-local

# Clean Windows machine / CI: all three stages, exact SDK installed on PATH.
./scripts/Invoke-Verification.ps1 -DotnetPath dotnet
```

Choose a fresh output directory for each full run. Evidence is not overwritten. A dirty-tree BuildTest/Smoke run is useful during development, but cannot be packaged as a commit-traceable build. Commit reviewed changes, then run all stages again for a bundle. Do not edit source between stages. SDK pin changes require explicit dependency review.

Equivalent focused commands with the repository SDK:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/home"
$env:NUGET_PACKAGES = "$PWD/.tools/nuget"
& ./.tools/dotnet/dotnet.exe restore FrontierAirportBuilder.sln
& ./.tools/dotnet/dotnet.exe build FrontierAirportBuilder.sln -c Release --no-restore
& ./.tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FlightAttemptTests
```

Run the complete suite after focused checks. Do not add a count assertion: the historical **744 passed** baseline is recorded in [physical-aircraft verification](evidence/physical-aircraft-v1/verification.md); the current run's TRX is authoritative. Explain legitimate additions/removals and any failures. No coverage percentage is claimed merely because coverlet is referenced.

## Test inventory and evidence levels

| Category | Examples | Platform/dependency | What it proves |
|---|---|---|---|
| Pure domain | `GeodesyTests`, `HeadingsTests`, `EconomyTests`, `PilotWageTests`, parts of physical-aircraft tests | .NET, synthetic values | Domain rules for supplied inputs |
| Persistence/recovery | `AircraftPersistenceTests`, `PersistentEconomyTests`, `FlightAttemptTests`, `AircraftLoadingTests`, `PhysicalAircraftTests` | Temporary JSON stores; named semaphores require Windows | Schema guards, idempotency, recovery and revision behavior |
| Windows/filesystem safeguards | `AirportManagementTests`, `OfflineWorkflowTests`, `BuilderWorkflowTests`, installation tests | Temporary packages/receipts; fake SDK fixtures | Ownership/path validation, dry-run isolation; no real installation |
| Transport/classification units | `TrackingTests`, `AircraftConditionTests`, `SimulatorAirportLookupTests`, `SurveyCaptureTests` | Fake sources/packets or historical frames | Parsing/classification and guarded behavior, not live compatibility |
| XAML inspection | `DesktopBindingTests` | XML parser, copied XAML | Specific declaration checks, not WPF execution |
| WPF smoke | `tests/LoadingWpfSmoke`, `tests/SiteUiSmoke` | Windows WPF, usable desktop; synthetic stores | Bindings/focus, navigation/rendering and prerequisite UI |
| Live simulator | Procedures in `docs/tracking-*`, `docs/aircraft-payload-loading-v0.md`, `docs/physical-aircraft-v1.md` | Qualified MSFS/SDK/aircraft; explicit authorization | Exact integration behavior observed in that environment |
| Manual acceptance | Scenery/material inspection, flight/wage usability, aircraft-specific conditions | Developer and simulator | Visual/gameplay acceptance of the tested version only |

Categories overlap within classes; they are an inventory, not new xUnit filters. All main-suite tests run on Windows. A Linux-safe subset has not been qualified; do not claim the full `net8.0` suite is portable. Main tests reference Core/MsfsSdk/Workflow, not WPF. The two smoke projects are outside the solution and are built/run separately.

Smoke programs have 120-second per-process limits; failures and timeouts fail CI. LoadingWpfSmoke needs keyboard focus; a locked/noninteractive session may fail. SiteUiSmoke uses Mapsui and opens the dependency detector, which can read machine-specific SDK/package configuration; use a clean hosted runner for this check, not an unreviewed local session with personal simulator configuration. No smoke action installs SDKs or packages. Screenshots are synthetic; tiles are not an asserted result. Hosted behavior must be demonstrated, not assumed. No test is silently skipped or marked continue-on-error.

## CI and artifacts

`.github/workflows/windows-ci.yml` runs on PRs to `master`, pushes to `master`, and manual dispatch. The named `windows-2022` image is a maintained image label, not an immutable VM snapshot. Actions are pinned to immutable commits. No dependency cache is required; the full solution is restored on each fresh hosted VM.

Required steps: restore/build/xUnit -> separate WPF smoke -> self-contained win-x64 publish -> development upload. Any earlier failure prevents development upload. Diagnostics upload uses `always()` and cannot turn the job green. Obsolete runs for the same PR/ref are cancelled. Job timeout is 25 minutes; artifacts expire after seven days. No `pull_request_target`, self-hosted runner, secret, paid API, automatic release or deployment is configured.

Open the PR's **Windows CI** check, or repository **Actions -> Windows CI -> run**. A successful run has `FrontierPilot-development-<commit>`; download/extract it. `application/FrontierPilot.exe` and accompanying files form the distribution. `build.json` records the actual checked-out SHA, UTC build time, SDK and run URL. PR builds test GitHub's merge commit, not only the contributor head; the manifest identifies that exact commit. `SHA256SUMS.txt` covers every bundled file except itself. TRX and success receipts are included. `verification-<commit>` contains logs, receipts, TRX and synthetic screenshots, including diagnostics from failed runs. Artifact retrieval may require GitHub sign-in.

Verify downloaded files against `SHA256SUMS.txt` with PowerShell `Get-FileHash -Algorithm SHA256`. Checksums identify contents; they are not signing or permission to execute. New/changed executables do not inherit local execution approval. Do not install a candidate or overwrite the normal local distribution automatically.

For an explicitly requested local distribution, the existing `scripts/Publish-Development.ps1` still produces `dist/FrontierPilot/FrontierPilot.exe`. CI uses the same publish profile but a separate fresh output folder; it never runs that executable or creates shortcuts. Local UI launch remains `scripts/Start-Desktop.ps1` (use `-DryRun` for airport-builder offline checks).

## Failure reporting

Report command, exact commit/dirty status, runner/SDK, test totals, failures/skips, TRX/log location and evidence level. Distinguish infrastructure blockage from assertions. Never infer live MSFS acceptance from synthetic tests, UI screenshots or a successful publish. Never invoke `App build`, Package Tool or install scripts to make ordinary CI pass.
