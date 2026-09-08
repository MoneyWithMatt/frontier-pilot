# Desktop startup binding regression — 2026-09-08

## Failure

Launching the published shortcut target reproduced an unhandled `InvalidOperationException`: WPF attempted a two-way binding to the read-only `TrackingViewModel.DiagnosticLog` property while constructing the main window. The shortcut target, working directory and executable were valid. No tracking state document existed yet, and airport data was not read or modified during diagnosis.

## Correction and verification

- The diagnostics TextBox binding now declares `Mode=OneWay` while remaining read-only.
- A regression test loads the shipped MainWindow XAML and verifies that binding contract.
- Full Release tests: 344 passed, 0 failed.
- Release solution build: succeeded with 0 warnings and 0 errors.
- `scripts/Publish-Development.ps1 -CreateDesktopShortcut` republished the Windows x64 self-contained single-file build.
- Published executable SHA-256: `CCC4E8FB0F70264C8FC47539091B047136576DEEA6BF19485B10E7A4CA88D048` (212,844,664 bytes).
- The actual Desktop shortcut launched this exact file. The resulting `Frontier Pilot · Prototype 0.1` main window was responsive.
