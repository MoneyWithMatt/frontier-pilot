# Starting-employer verification

Implementation tested on 2026-10-15; final development distribution republished on 2026-10-16.

All commands used the repository .tools/dotnet/dotnet.exe, DOTNET_CLI_HOME=.tools/home and NUGET_PACKAGES=.tools/nuget.

- Focused career/catalogue/employed-cargo/economy checks: 76 passed at the initial integration checkpoint.
- Final CompanyEmploymentTests contains 27 test cases (including theories); complete solution: 660 passed, zero failed/skipped.
- Final Release solution build: zero warnings, zero errors.
- SiteUiSmoke: passes existing site/dependency checks, four employer acceptance handlers, all 53 diagnostic rows, transition to the normal company Jobs shell, Caravan-only selection for the 160 kg Industrial fixture and the bound reservation command. All gameplay stores used temporary directories or memory.
- LoadingWpfSmoke with src/Desktop/MainWindow.xaml: passed payload/fuel draft/focus preservation and zero-reset checks.
- Rendered employer cards, destination diagnostics and selected company job were inspected. Screenshots in ui/ are synthetic test runs, not simulator acceptance.
- Publish-Development.ps1 -CreateDesktopShortcut completed successfully. Windows x64 self-contained single-file output remains ignored by Git.
- Final dist/FrontierPilot/FrontierPilot.exe SHA-256: 18293578ADA46CCE1E002EA8E106BB31656E1795EF4B9B79D2801353E9EBA19B.
- CSV source SHA-256: E60B0CDD678A0E17E113D7FF2C29A864E2867DDF4697FF007D1623D5011F57AD. Field-for-field comparison of all twelve columns against embedded research JSON passed.

No Package Tool, MSFS launch/control, real Community installation, personal career reset or live payload change was performed. Synthetic flight tests exercised the existing tracker-generated evidence and atomic ledger payment; they are not live flight proof. Existing FPT1/FPSC content and prior evidence were untouched.

Initial loading-smoke invocation lacked its required XAML argument; the corrected invocation passed. The extended job-UI harness initially raised Click without executing the command-bound action; it was corrected to invoke the button's bound command and then passed.

See ../../docs/starting-employers-and-destinations.md for every unresolved airport, configuration, limitations and manual testing steps.
