# Object footprints and foundations verification — 2026-09-08

- User acceptance entering this milestone: `Gen_Hangar56` appeared at the intended airport and orientation in MSFS 2024. Its rear floated over uneven terrain.
- Dimensions were recovered from the installed model payload, not estimated from the user's screenshot. Exact LOD results are in `model-bounds.json`.
- Focused object tests: 9 passed.
- Full Release test run: 311 passed, 0 failed, 0 skipped.
- Release solution build: succeeded with 0 warnings and 0 errors.
- Generated XML validated against SDK 1.2.4 `bglcomp.xsd`: 0 errors.
- SDK 1.2.4 Package Tool: exit 0, no builder errors, artifact validation successful, 8 layout entries.
- Compiled `scenery/frontier/airport.bgl`: 909 bytes, SHA-256 `CC7FAB29248C6E57833AF72C4F9B3FFA75CCFFA5E9DC25DC82D64F9D68B222E0`.
- The exact input, generated XML, Package Tool command/logs/result and output inventory are preserved in this directory.
- No Community package was installed or replaced during development verification.
- The self-contained Windows development distribution was republished successfully.
- Published `FrontierPilot.exe`: 165,028,514 bytes, SHA-256 `48BB9C65A867B803B6318DE8B6BCC5654EE6A6537985DFA6FE43F41961F19219`.
- The Desktop `Frontier Pilot.lnk` shortcut resolves to that published executable, which passed a local dry-run launch smoke check.

The compiled package proves the polygon attributes and structure are accepted by the installed compiler. Matt's same-building simulator retest is required to accept the true-scale footprint and visual terrain blending.
