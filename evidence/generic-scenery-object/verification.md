# Generic scenery object verification — 2026-09-08

Scope: implementation and Package Tool compilation verification. The simulator was initially closed; the SDK tool was allowed to run its headless compilation process. The package was not installed and the real Community folder was not written.

## Selected base-library object evidence

- Frontier asset ID: `asobo-generic-hangar-56`
- Installed model name: `Gen_Hangar56`
- MSFS library GUID: `{FD8AA200-1485-46C7-BA8F-B9B69C31E0F7}`
- Current MSFS 2024 package inspected read-only: `%APPDATA%\Microsoft Flight Simulator 2024\Packages\StreamedPackages\fs24-asobo-modellib-airport-generic\content\minimal.fsarchive`
- Current archive SHA-256: `CA95E12F4DC728954EEE78930B1976ABAB1090FCB0A7716AEC9848FA44A12A1E`
- Readable installed generic library used to resolve name/GUID, inspected read-only: `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Official\OneStore\asobo-modellib-airport-generic\scenery\airport_generic\generic.BGL`
- Readable BGL SHA-256: `0185952D0B0D73C231DBD8C0DE65404F4B86FC32FE58664DC6D91AED93D261D3`
- The GUID byte representation occurs in the current streamed package index.

## Verification

- `dotnet test FrontierAirportBuilder.sln -c Release --no-restore`: 309 passed, 0 failed, 0 skipped.
- Release solution build: succeeded with 0 warnings and 0 errors.
- New focused tests: 7, covering local/WGS84 position, heading normalization, invalid data, deterministic XML/GUID/ground snapping, multiple instances, editing/delete/undo/redo, persistence across application instances, old layout and saved-definition compatibility, and stale-build invalidation.
- One generated object-enabled scenery XML validated against SDK 1.2.4 `Tools\bin\bglcomp.xsd`: 0 schema errors.
- SDK 1.2.4 Package Tool compiled the disposable `FPOBJ1` proof: exit 0, no builder errors, artifact validation successful, 8 `layout.json` entries, and real `scenery\frontier\airport.bgl` (659 bytes, SHA-256 `E79B5A2F79145B4204B95280EB12F81898661BE34A82C7022D71749F42C0D8B8`). The exact command, logs, inputs, generated XML and output tree are preserved in this directory.
- Self-contained win-x64 single-file publish completed through `scripts\Publish-Development.ps1 -CreateDesktopShortcut`.
- Published executable: `dist\FrontierPilot\FrontierPilot.exe`, 165,015,202 bytes, SHA-256 `7850A70E34C15B8024A0394BD00584C9DFDD89FFB7D713589D88F81DD9AB9E32`.
- Desktop shortcut target and working directory match the managed distribution.
- Published executable dry-run launch smoke check: responsive WPF main window titled `Frontier Airport Builder · Prototype 0.1 · DRY RUN`.

## Acceptance boundary

No simulator visual test or Community installation was performed. Package compilation does not establish that the user's flight resolves and displays the streamed model. The user must assess visible position, terrain contact, scale and model-forward orientation. Automated, schema and Package Tool results are not recorded as MSFS visual acceptance.
