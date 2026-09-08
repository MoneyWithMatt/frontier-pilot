# Technical status — 2026-09-07

## Proven pipeline
SDK 1.2.4: C:\Users\mattr\Desktop\MSFS 2024 SDK; Package Tool: Tools\bin\fspackagetool.exe. User MSFS2024_SDK points to this root; old machine-level SDK configuration is unsuitable. PackageBuilder supplies the verified root to child processes. Installed SimpleAirport remains untouched. See sample-build-report.md and toolchain-diagnosis.md.

Core owns validated definitions, WGS84 geometry, heading normalization, magnetic reciprocal numbers and guarded survey validation. MsfsSdk owns deterministic System.Xml.Linq generation, the embedded Frontier template, native read-only avatar capture, Package Tool execution, artifact validation and CommunityInstaller. App remains the console spike. Workflow adds the testable async service boundary and MVVM state machine. Desktop is the single WPF window.

PackageOrderHint CUSTOM_AIRPORT is mandatory. Tool exit zero alone is insufficient: builder diagnostics, manifest, layout and non-empty BGL must pass. MSFS must be closed before compilation; Package Tool starts its headless simulator through Steam. Cancellation does not kill unrelated simulator/Steam processes; close any residual compiler-launched simulator before retrying.

## Live survey
SDK 1.2.4 and simulator file version 1.8.16.0 are qualified by exact binary/header hashes in LiveSurveyCapture. The explicit walking-avatar proxy must match the current controlled object and differ from aircraft. Five stable seconds, finite/consistent readings, parked aircraft, upright grounded avatar and altitude agreement are mandatory. No fallback.

Sedona observations support avatar-targeted ground altitude empirically: it followed avatar elevation while aircraft ground remained fixed; object altitude agreed within about 1 cm; B repeated within 1 mm. Microsoft documents GROUND ALTITUDE as aircraft-local: retain this contradiction. Return-to-A was 66 cm from the original location, outside the original 50 cm return criterion. This is not a supported general terrain API or a whole-runway slope survey. Details: avatar-elevation-validation.md and live-survey.md.

## Preserved in-simulator proof
FPT1: original proven sample-location airport.
FPSC: Skeleton Coast Test Strip, dirt, 200 x 20 m; centre -22.58616679921284, 14.539071285657734; elevation 13.94779940508306 m; true heading 108.63324971143716; runway 12/30.
User reported perfect terrain fit, no rocks, bumps, buried or floating edges. See skeleton-coast-test.md. These packages and previous evidence are protected from Desktop installation.

## Desktop 0.1
Launch scripts/Start-Desktop.ps1. Connect in a loaded flight, capture while standing at the runway centre, enter name/identifier/dimensions/surface and confirm the site check. Close MSFS, generate/compile, then install. Dirt and asphalt are the proven available surfaces. Identifiers accept 3–8 uppercase letters/digits with a leading letter. World-map uniqueness and airport separation remain manual checks.

State/evidence: %LOCALAPPDATA%\FrontierAirportBuilder\desktop. Each capture/build has a unique directory. Package installation stages and hashes the entire package; replacement requires an intact receipt in ownership/ and unchanged installed content. Previous packages are preserved under installations/. Reparse points and cross-drive staging are refused. Community is resolved from UserCfg.opt InstalledPackagesPath/Community2024.

All capture, filesystem, compilation and installation work runs off the UI thread. Cancellation is honoured before the short install commit/rollback section. Closing a busy window requests cancellation; close it again once idle. Captured survey stays in memory while MSFS is closed; raw evidence persists, but resuming a survey after restarting the builder is not implemented.

## Verification
See ../evidence/desktop-0.1/verification.md. Existing live simulator proofs were reused; no new airport was installed in this UI implementation task. A full user-driven WPF capture/build/install acceptance run remains useful.

## Workflow hardening update — 2026-09-07
The current UI uses explicit Disconnected, ReadyToSurvey, SurveyCaptured, Validated, Built and Installed states, plus recoverable Error/Cancelled. Validate is now an explicit prerequisite to generation. Services bind generated coordinates to the captured point and restrict installation to their issued build.

Use scripts/Start-Desktop.ps1 -DryRun for a complete offline workflow with recorded Namibia frames, real XML generation, a simulated compiler and sandbox-only installation. The launcher now defaults to Release. Live mode remains the default without the flag. See offline-workflow.md for exact test steps, provenance, safety restrictions and limitations.

Verification for this update: evidence/offline-workflow/. No MSFS process was launched or controlled and no real Community package was written. The existing Debug UI was left open; Release output was used to avoid its locked binaries and preserve its in-memory work.

## Airport site and taxiways — 2026-09-08
Matt reports the preceding runway/parking/survey/build/install workflow passed live MSFS testing. Preserved its pending layout implementation in f72d384 after 255 baseline tests passed.

Implemented shared Core.AirportSite bounds (runway footprint + 3 km, within the existing 10 km geodesic limit), replacing inconsistent editor square/Core radial 500 m guards. Basic multi-point taxiways now draw in the existing canvas, reuse local coordinates and existing materials, attach to parking, support name/width editing and shared undo/redo, invalidate stale results, and reach real XML in dry run. Existing Save/Load retains these paths; no new persistence feature was added.

Release build: zero warnings/errors; 278 tests pass. Generated XML validates against installed SDK 1.2.4 XSD. Offline UI visually checked. New taxiways and distant parking are not yet simulator-proven. No Package Tool/MSFS invocation or real Community writes during this task. See [implementation, limits and exact manual steps](taxiways-and-airport-site.md), evidence/taxiways/.
