# Development distribution verification — 2026-09-08

- Target: Windows x64, .NET 8 self-contained, single-file WPF application.
- Stable output: `dist\FrontierPilot\FrontierPilot.exe` (generated locally and intentionally excluded from Git).
- Desktop shortcut: `%USERPROFILE%\Desktop\Frontier Pilot.lnk`.
- Shortcut working directory: `dist\FrontierPilot`; target verified against the stable executable.
- Existing user-level `MSFS2024_SDK`: `C:\Users\mattr\Desktop\MSFS 2024 SDK\`.
- Full automated suite: 300 passed, zero failed or skipped (`tests.trx`, `test-output.txt`).
- Release solution build: succeeded with zero warnings and zero errors (`build-output.txt`).
- Publish: succeeded through the checked-in publish script (`publish-output.txt`).
- Published inventory: one file, `FrontierPilot.exe`, 165,000,866 bytes.
- SHA-256: `DC3D3D9AF036046E60A907688C86F90E1205E7D03BC79D3966A11965B9424993`.
- Launch: the executable opened `Frontier Airport Builder · Prototype 0.1`. A separate launch through the Desktop shortcut resolved to the same executable and closed cleanly.
- No MSFS process, Package Tool invocation or Community-folder write is part of this verification.
