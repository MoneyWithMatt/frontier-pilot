# Employed pilot wages verification — 2026-10-17

Automated/synthetic verification only; no live wage flight or personal-save mutation.

- Focused PilotWageTests + CompanyEmploymentTests + FlightAttempt tests: **92 passed**.
- Complete Release solution suite: **695 passed**, zero failed/skipped (672 existing + 23 new wage cases).
- Release solution build: **zero warnings/errors**.
- SiteUiSmoke passed isolated saved-site, onboarding, employer acceptance, destination and dispatch checks. Inspected `ui/career-employers.png`: four correct hourly rates. Inspected `ui/company-jobs.png`: `$650 / flight hour` banner, `$0` balance, no reward/compensation column for company-only offers.
- LoadingWpfSmoke passed payload/fuel draft preservation and zero-reset checks.
- Existing player/freelance logistics settlement tests, flight tracking, cargo transfer, loading and persistence tests pass in the full suite.
- Normal development distribution published through `scripts/Publish-Development.ps1 -CreateDesktopShortcut`.
- `dist/FrontierPilot/FrontierPilot.exe` SHA256: `DD8897B47FEF67F60219660C13B2BFC52F1C23EF642959EC1B6020D7BC71E098`.

Runtime binaries and personal saves are not committed. See [full report and manual procedure](../../docs/employed-pilot-wages-v1.md). The first live wage flight remains pending.
