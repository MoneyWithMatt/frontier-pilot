# Automatic airport lookup verification — 2026-10-16

All results are offline/synthetic. No live MSFS query, airport inspection, Package Tool invocation or Community modification was performed.

- Focused `SimulatorAirportLookupTests` and `CompanyEmploymentTests`: **37 passed**.
- Complete solution tests, Release: **670 passed, 0 failed, 0 skipped**.
- Release solution build: **0 warnings, 0 errors**.
- SiteUiSmoke: passed saved-site, onboarding, all four employer acceptance, destination diagnostics and dispatch checks using isolated saves. `ui/employer-destinations.png` was visually reviewed; batch-check action and recognition column are visible.
- LoadingWpfSmoke: passed payload/fuel focus and refresh preservation checks.
- `scripts/Publish-Development.ps1 -CreateDesktopShortcut`: successful self-contained Windows x64 development publish; desktop shortcut refreshed.
- Published `dist/FrontierPilot/FrontierPilot.exe` SHA256: `EDD93DA9FE277A5A500B14F85708E7E4F7861439F0E34530F0568074E086246F`. Runtime distribution remains untracked.

Ten new test cases cover complete/out-of-order native packet assembly and MSFS 2024 region-field offsets, malformed/duplicate/inconsistent packets, automatic job generation for each of four employers, missing/ambiguous identifiers, retained route coordinates after changes/absence, optional review overrides, reset persistence, and corrupt/future cache preservation.

Live acceptance still needed: load a flight anywhere, connect Frontier tracking or select **Check all airports in MSFS**, confirm completed lookup, inspect flagged rows and generated company jobs. The implementation uses the documented worldwide non-EX1 API; its response on this user's installed simulator has not been observed. Nine researched candidates have no identifier; no missing-simulator-airport results were fabricated.
