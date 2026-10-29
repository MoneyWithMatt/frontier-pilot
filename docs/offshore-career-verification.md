# Offshore airport-cargo verification — 2026-10-29

Scope: [specification](specs/offshore-career.md). Draft [PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3), task [#2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2).

## Source and local results

- Base: merged master `d5ff51650789e45e46c8828893acaba071d957e7`.
- Implementation/test commit: `961b1dcba55d2ac6c62a651854cca83b1835e08a`, clean and pushed before hosted testing.
- Local Windows, pinned SDK 8.0.424, repository `DOTNET_CLI_HOME=.tools/home`, `NUGET_PACKAGES=.tools/nuget`.
- `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-offshore-local`: dirty implementation on the base commit; Release solution 0 warnings/errors, 751 passed / 0 failed / 0 skipped. TRX `.ci-output-offshore-local/tests/regression.trx` and `regression.json` retain that source status.
- `.tools/dotnet/dotnet.exe test tests/Tests/FrontierAirportBuilder.Tests.csproj -c Release --filter FullyQualifiedName~OffshoreCareerTests --logger 'trx;LogFileName=offshore.trx' --results-directory .ci-output-offshore-focused`: 7 passed / 0 failed / 0 skipped. Repeated after strengthening partial inventory and retained-reservation qualification assertions; both passed. Hosted CI below validates the final committed tests in full.
- 744 previous cases + 7 new Offshore cases = 751. No tests/assertions disabled or skipped.

## Hosted Windows result

[Windows CI run 6](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40850041388), job `123366958251`, succeeded. GitHub-hosted `windows-2022`, image `20261025.346.1`, Windows Server 2022, SDK 8.0.424. Tested clean PR merge `402392b88805d506587944d45165694076a78bda` (implementation head `961b1dcba55d2ac6c62a651854cca83b1835e08a` into the base above).

Commands from the workflow:

```powershell
./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet
pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1
./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet
./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet
```

Release solution: 0 warnings/errors; **751 passed, 0 failed, 0 skipped**. All nine simulated verification-guard scenarios passed. LoadingWpfSmoke and SiteUiSmoke passed, including synthetic onboarding acceptance for Skeleton Coast Petroleum. These smoke checks do not exercise a live Offshore flight or qualify the new planning text visually. Self-contained win-x64 publish and both artifact uploads passed.

| Artifact | Download | GitHub-reported archive SHA-256 |
|---|---|---|
| Development for tested merge | [12887265383](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40850041388/artifacts/12887265383) | `f41b6e2f012ee4f66db90bae2b93c4010e7e31877c01fe187f548b2ecc3f2bf1` |
| Diagnostics/TRX/screenshots | [12886652084](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40850041388/artifacts/12886652084) | `1e8823feaca6fd09f0a9eab6149643d898b42d010517c397b4b02436e2f1741e` |

Artifacts expire after seven days (these expire 2026-11-05); links may require sign-in. Metadata/logs were inspected; these archives were not downloaded, independently checksum-verified or executed locally in this task. The later documentation-only checkpoint triggers a fresh CI run; its status is recorded in task #2 / PR #3. The implementation SHA above remains the last validated implementation, not a self-referential handoff commit.

## Evidence limits and remaining acceptance

All new airport coordinates, telemetry and transfers were synthetic in temporary stores. The grouped-transfer test uses the real tracker/economy persistence boundaries but supplies verified-adapter inputs directly; it does not prove SimConnect payload writes or aircraft retention. The new route-time assertion checks displayed string content, not a screenshot/usability acceptance. Existing historical airport lookup proves database presence on its recorded build, not new scenery acceptance.

No MSFS, Package Tool, local WPF/published application, Community changes, personal-save reads/migrations, SDK installer, repository settings or merge occurred. H125/platform, passenger and survey work remain unimplemented pending mission design and any required exact live qualification.
