# Offline workflow hardening — 2026-09-07

## Implemented
The WPF window now exposes a real workflow state:
Disconnected → Ready to Survey → Survey Captured → Validated → Built → Installed.

Error and Cancelled retain the last completed state and show a recovery action. Validate checks the definition and local identifier ownership without generating files. Generation requires Validated; installation requires Built. Editing inputs invalidates validation/build results. Reconnecting discards old survey/build eligibility. Recapture also resets the site confirmation. Controls and public VM actions enforce their prerequisites; asynchronous operations disable edits and reentry.

Core validates captured-point boundaries as well as the existing five-second live frame checks. BuilderServices now binds generation to its captured survey coordinates/elevation/heading and installs only its own issued build. Build projects include a survey-reference.json linking the capture evidence.

The existing generator, Package Tool adapter and installer are reused. Native survey/compilation entry points are isolated in LiveWorkflowBackend. ReplayWorkflowBackend cannot launch Package Tool or MSFS.

## Dry run
Launch from the repository:
    .\scripts\Start-Desktop.ps1 -DryRun

The launcher defaults to the Release build; use -Configuration Debug only after building that configuration. The title and banner identify DRY RUN. Every session gets a fresh directory:
    %TEMP%\FrontierAirportBuilder-DryRuns\<GUID>\

No live Community path is read or used. A dry-run installer refuses destinations outside the fixed temporary sandbox structure. The recorded frame fixture is extracted, without changing values, from frame rows in evidence/live-survey/namibia-centre-01/observations.jsonl. The historical source remains unchanged. Replay runs SurveyCapture.Create over those frames; it does not fabricate live qualification or bypass avatar checks.

XML generation is real. Compilation is simulated because the real Package Tool launches MSFS. The synthetic airport.bgl is clearly labelled text, not usable scenery. Its manifest carries frontier_test_only=true. Live installation refuses this marker. Never copy test output into MSFS. Dry-run success does not establish new SDK compilation or simulator behaviour.

## Exact manual offline test
1. Build Release, then launch the command above. Confirm DRY RUN in the title/banner.
2. Click Check and connect. Expect Ready to Survey; generation and installation remain disabled.
3. Click Capture survey. Expect Survey Captured and the recorded Namibia coordinates, roughly 13.948 m elevation, 108.63° true facing.
4. Enter "Offline Test Strip", identifier FPDRY01, length 200, width 20, Dirt. Confirm the simulated site checkbox.
5. Click Validate. Expect Validated and reciprocal designators 12/30. Only now can you generate.
6. Click Generate and compile. Expect Built and a message explicitly describing test output.
7. Click Install airport. Expect Installed with a path under the temporary sandbox, and no flight/restart instruction for the test artifact.
8. Open Project, Package and Build log; confirm generated XML, simulated compiler log and survey-reference.json exist.
9. Change length to 240. Expect Survey Captured; install disabled. Validate, build and install again. The ownership receipt under state/ownership/ points to the preserved previous package in state/installations/.
10. Set length to NaN or use FPT1/FPSC. Validate/build must be unavailable until corrected.
11. Expand Technical log; select/copy the text or use Copy technical log. Save technical log creates a NEW file and refuses existing files. Logs include UTC timestamps, state, recovery state, paths and full exception details.

To inspect duplicate refusal in the sandbox, create a folder named frontier-airport-fpdup only beneath that session's Packages/Community2024, choose FPDUP and Validate. Expect an unowned-package error without generated files. Choose a new identifier to recover. Do not do this in the real Community folder.

## Live manual acceptance (not performed in this task)
Launch without -DryRun. Load a parked flight, check prerequisites, exit/stand/face/stabilize, capture and enter a new airport definition. Validate; close MSFS before generation and installation. Restart MSFS and inspect both starts and the entire footprint. FPT1/FPSC remain reserved and protected. This requires the qualified simulator build and the previously documented SDK setup.

## Safety and limitations
- Destination validation rejects relative/drive-root/network/device/alternate-stream/traversal/trailing-dot or trailing-space paths, reparse points, overlaps between package storage and state, missing Community2024 and cross-drive transactions. The prototype uses a conservative 240-character path limit.
- UserCfg.opt must contain exactly one InstalledPackagesPath. Identifier checks are local package ownership checks, not global airport-database uniqueness.
- Installation verifies source, stage and destination inventories; only receipt-owned unchanged packages can be replaced. It preserves backups and rolls back ordinary commit exceptions. Same-volume directory renames are used. There is a brief absence between moving the old directory and moving the new one; this is not a multi-directory atomic transaction.
- A power loss or externally interfering process during replacement may require manual recovery from the retained backup. Do not claim crash-atomic recovery or protection against concurrent malicious filesystem changes.
- Cancellation stops before installation's commit section. After that boundary, a completed install is reported as Installed even if cancellation was requested.
- Draft resume and cross-drive support remain deferred. Dry-run directories are retained for diagnosis; there is no automatic cleanup that could remove user files.
- Microsoft's aircraft-local GROUND ALTITUDE documentation contradicts the empirical avatar results. Exact-build qualification, no fallback, and the 66 cm return-to-A caveat remain intact.
