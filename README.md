# Frontier Pilot

A Windows x64 WPF/.NET 8 prototype for MSFS 2024: saved sites and airport authoring, aircraft/avatar tracking, career/employment, verified cargo delivery, persistent economy and physical-aircraft foundations. Some features have live acceptance; others are synthetic-tested or awaiting aircraft-specific qualification. See [current status and roadmap](ROADMAP.md).

## Build and test

Install Git, PowerShell 7.2+ and the exact .NET SDK in `global.json` (8.0.424) from Microsoft. Clone this public repository and run:

```powershell
git clone https://github.com/MoneyWithMatt/frontier-pilot.git
cd frontier-pilot
./scripts/Invoke-Verification.ps1 -DotnetPath dotnet
```

On a clean committed Windows checkout this restores dependencies, builds Release, runs the complete xUnit suite and separate WPF smoke tests, and publishes a self-contained development bundle under `.ci-output/development`. No MSFS or proprietary SDK is needed. WPF smoke needs a usable Windows desktop; use hosted CI for isolated execution. See [TESTING.md](TESTING.md) for individual stages, local `.tools` SDK use, fresh output directories and precise limitations.

## Obtain a development executable

Open [Actions -> Windows CI](https://github.com/MoneyWithMatt/frontier-pilot/actions/workflows/windows-ci.yml), select a successful run, and download `FrontierPilot-development-<commit>` under Artifacts (GitHub sign-in may be required). Artifacts expire after seven days. Extract the complete bundle: `application/FrontierPilot.exe` includes the .NET runtime. `build.json`, test results and `SHA256SUMS.txt` identify the exact tested source and files.

These are unsigned development builds, not installers or live MSFS acceptance. Downloading a changed executable does not authorize running it on the gaming PC. No automatic installation, public Release or deployment occurs.

For explicitly requested local development, `scripts/Start-Desktop.ps1` launches the Release build; `-DryRun` uses the isolated airport-builder backend. `scripts/Publish-Development.ps1` retains the usual `dist/FrontierPilot/FrontierPilot.exe` distribution. The simulator/SDK are needed for live integration and scenery compilation, not ordinary application CI. Synthetic dry-run scenery must never enter real Community.

## Project guide

- [ARCHITECTURE.md](ARCHITECTURE.md): Core, MsfsSdk, Workflow, Desktop and CLI responsibilities, storage and integration boundaries.
- [AGENTS.md](AGENTS.md): safety instructions, branch/PR conventions and escalation.
- [Feature specification](docs/templates/feature-spec.md), [task continuity](docs/task-continuity.md), and [recommended branch protection](docs/repository-protection.md).
- [Design decisions](docs/DESIGN_DECISIONS.md), [historical technical reports](docs/TECHNICAL_STATUS.md), and [new-PC/save transfer guidance](docs/new-pc-setup.md).

The solution retains the original `FrontierAirportBuilder` name. Historical FPT1/FPSC packages and evidence are preserved; installed SDK samples and unrelated Community content must remain untouched. Embedded SDK-derived template assets retain their [provenance](src/MsfsSdk/Templates/PROVENANCE.md) and accompanying license. Personal saves, credentials, generated distributions and local tool caches do not belong in Git.
