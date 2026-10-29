# Offshore passenger verification — 2026-10-29

[Approved scope](specs/offshore-passenger-transfers.md), [operation and live procedure](offshore-passenger-transfers.md), [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2), [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3).

## Local checks

Pinned SDK 8.0.424 on Windows, repository `DOTNET_CLI_HOME=.tools/home` and `NUGET_PACKAGES=.tools/nuget`.

- Initial focused passenger test run: 15 passed, 1 failed, 0 skipped. A gap-test expectation was off by one second; corrected to the existing ten-second policy. `.ci-output-passengers-focused-1/passengers.trx` retained. Initial compile also caught an internal EconomyIds reference across project boundaries, corrected through a domain offer-ID method.
- Focused passenger/wage/Offshore: 46 passed. Expanded passenger/loading: 93 passed. All subsequent focused checks passed without skips.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-passengers-local-2`: dirty passenger implementation on `7545922`; Release zero warnings/errors, 773 passed / 0 failed / 0 skipped.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-passengers-local-3`: dirty binding repair on `7b4f390`; Release zero warnings/errors, **774 passed / 0 failed / 0 skipped**. Count: 751 prior + 20 passenger cases + 3 cargo-guard cases. Local TRX and receipts remain in that ignored output folder.
- `.tools/dotnet/dotnet.exe build tests/SiteUiSmoke/SiteUiSmoke.csproj -c Release`: clean compile. WPF execution remains hosted; no local dependency detector or changed published application was executed.

## Hosted failure preserved

[Run 8](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40854467605), head `7b4f390bdec8b1230cc15a12761d291a590090f1`, clean merge `c0e2f8246461d60399add9b4b5e97b567f15b2b1`: Release 0 warnings/errors, 773 tests passed, nine guard scenarios and LoadingWpfSmoke passed. SiteUiSmoke failed with exit -1073741571 / stack overflow in passenger SelectedAircraft setter -> Changed -> WPF writeback. Publish/development upload correctly remained blocked.

[Diagnostic artifact 12888138324](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40854467605/artifacts/12888138324) was downloaded through the connected GitHub tool. SHA256 matched `172ce84e9d69c93cfa5bb6c96affcb4fbdba61938ff6225d63dd58cc0bd0b4bc`; stack/log inspected at local `.ci-output-passengers-ci-run8/verified/`. Fixed same-value/null selection notifications, added a VM regression and strengthened actual row/aircraft selection checks. No assertion, timeout or publication guard was weakened.

## Successful repair run

[Run 9](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40855213721), head `21feda07de55f939a0522d9b6a8b02fb39e7c971`, clean tested merge `70662e542b170c64ae1bf5ad136f8528e5bfb334`: GitHub-hosted windows-2022, SDK 8.0.424. Full Release 0 warnings/errors; **774 passed, 0 failed/skipped**; all nine verification-guard scenarios, LoadingWpfSmoke, SiteUiSmoke, publish and both uploads passed.

Workflow commands:

```powershell
./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet
pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1
./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet
./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet
```

SiteUiSmoke drives the actual passenger reserve/board/disembark commands through synthetic flight observations, verifies row/aircraft selection through refresh, one minute produces $13.33, and repeated disembarkation is disabled. The downloaded diagnostics/TRX and clean-source receipt were inspected; TRX reports 774 executed/passed, zero failed/not-executed/errors/timeouts.

| Artifact | Link | GitHub archive SHA256 |
|---|---|---|
| Development | [12889505668](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40855213721/artifacts/12889505668) | `65955637a3e158833e96f361d3356087bc864f4a471cb05cdac36ba7d73fc711` |
| Diagnostics | [12889171490](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40855213721/artifacts/12889171490) | `d81f621e5052ec1959152be20561c36f521a08dfb318bfe88c01edd85038908c` |

Diagnostics downloaded and archive checksum independently matched; development archive not downloaded/executed. Artifacts expire after seven days. The synthetic `offshore-passenger-completed.png` was visually inspected: functional completion was correct, but route/wage clipping, a cargo-oriented selector label and a stale completed-status instruction required presentation follow-up. That follow-up wraps rows, uses a passenger-specific selector template, shows a compact wage plus full receipt below, and corrects terminal status. Its subsequent hosted result belongs in task #2/PR #3 and the handoff; this run proves the earlier repaired implementation only.

## Evidence boundary

Synthetic observations and temporary stores do not establish live MSFS acceptance. Boarding is simulated, with no native passenger/payload/animation writes. Platform kind remains unavailable; exact platform/aircraft live qualification plus reviewed adapter/registry/deck rules must precede enablement. H125 platform cargo and survey/inspection are deferred. No personal saves, Community, Package Tool, SDK installation, repository settings or merge changed. Human merge and exact artifact/live-scenario approvals remain separate.
