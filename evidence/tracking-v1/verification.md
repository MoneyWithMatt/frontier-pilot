# Tracking V1 verification — 2026-09-08

- Repository baseline: `477cceb Render true-scale objects and add terrain foundations`.
- Mapping dependency: Mapsui.Wpf 5.1.0, MIT; OpenStreetMap tiles with visible attribution and `FrontierPilotPrototype/0.1` user-agent.
- Installed SDK 1.2.4 header confirms `SIMCONNECT_OBJECT_ID_USER_AIRCRAFT = 0`, separate from USER_AVATAR and USER_CURRENT.
- Focused aircraft telemetry/tracking tests: 13 passed.
- Full Release tests: 324 passed, 0 failed, 0 skipped.
- Full Release solution build: succeeded with 0 warnings and 0 errors.
- No MSFS or Package Tool process was launched or controlled.
- No generated project, saved airport or Community package was created, replaced or removed.
- Self-contained Windows x64 publication completed successfully at `dist/FrontierPilot/FrontierPilot.exe`.
- Published executable: 212,801,656 bytes; SHA-256 `434F103029421A3E9565101562C15AA7AD23695C4F1F2DC0DDCB6B13DDB8D790`.
- The Desktop `Frontier Pilot.lnk` shortcut resolves to that executable.
- The published executable stayed alive and responsive through a five-second `--dry-run` launch smoke check, then only that exact process was closed.

Automated verification does not establish live simulator behavior. Manual acceptance must confirm position, heading orientation, identity/units, taxi/flight update cadence, viewport behavior and disconnect recovery.
