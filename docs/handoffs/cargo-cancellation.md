# Cargo cancellation handoff

## Objective and authority
Matt approved origin unloading and job cancellation. His latest decision supersedes an away-from-origin penalty: **no financial penalty anywhere during playtesting**, because bugs may require cancellation. See [approved specification](../cargo-cancellation.md).

## Branch / source
`codex/cargo-cancellation`, based on `d7332f7` of `codex/namibia-playtesting`. Stacked on existing Career Hub/cargo/passenger/fleet/Namibia development; do not merge dependencies or master automatically. Last validated implementation: **4d1f64f5a820316d231887bbf013e9cc2c1cb11b**. A subsequent documentation-only handoff commit does not change that artifact.

## Completed
- Unload at origin leaves job accepted and cargo reserved; unrelated consignments remain aboard.
- Inline Cancel job / Keep job preview scrolls its confirmation into view.
- Loaded cancellation reuses outside confirmation, cockpit apply, durable intent and retained station readback before terminal economy commit.
- At origin, remaining cargo returns to stock. Elsewhere, origin reservation remainder is released and physically unloaded cargo is abandoned without delivery credit or completion pay. No penalty.
- Cancellation is idempotent across failed writes/restarts. Interrupted unfinished legs are retained as aborted, with no arrival credit; start a new leg for other cargo. Preparing cancellation already aborts an unfinished leg; discarding its unapplied plan does not restore proof.
- Schema 17 adds optional audit/operation/history metadata and aborted timestamps. Older supported documents migrate through existing validation and atomic replacement. Passenger/mixed-load, identity, fuel and unrelated-payload safeguards remain intact.
- Passenger and player-company contract cancellation remain outside this slice.

## Verification — exact implementation
[Hosted Windows CI 44875880218](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875880218), clean commit **4d1f64f**, SDK **8.0.424**, Windows hosted runner:
- Release build and xUnit: **1,040 passed / 0 failed / 0 skipped**.
- SiteUiSmoke: **236 PASS assertions**; LoadingWpfSmoke: **7 PASS assertions**. Includes actual return/cancel/keep/apply controls and viewport visibility.
- **9 simulated verification guard scenarios**, synthetic Offshore historical CLI check, self-contained win-x64 Package: passed.
- [Verification artifact 14695743606](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875880218/artifacts/14695743606). Inspected `cargo-cancellation-preview.png` and completion screenshot. These are hosted synthetic WPF, not personal-PC or MSFS evidence.

Commands: local `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-cancel-05` (1,040 / 0 / 0); `dotnet build tests/SiteUiSmoke/SiteUiSmoke.csproj -c Release` with documented caches (0 warnings/errors). Hosted workflow runs verification BuildTest, `scripts/Test-VerificationGuards.ps1`, Smoke, synthetic CLI review and Package. Exact logs/TRX/receipts are in the verification artifact.

Earlier implementation 0f1587e also passed [run 44875574081](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875574081): 1,040 / 0 / 0, 235 SiteUiSmoke and 7 LoadingWpfSmoke assertions. Subsequent UI scrolling added one viewport assertion. Preserved local development failures: run 01 assigned null to nonnullable synthetic failure counter; run 03 used a nonexistent test enum; run 04 incorrectly expected byte-identical JSON from the existing schema migration; first WPF compilation used an incorrect catalogue property. Fixed tests check semantic save preservation and retain production guards. Logs remain untracked in `.ci-output-cancel-05`; no personal data committed.

## Development build — not installed or executed
[Artifact 14695932886](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875880218/artifacts/14695932886), exact source **4d1f64f**. Downloaded to untracked `.ci-output-cancel-05/final-development` and verified **all 11 SHA256SUMS entries**.
- ZIP SHA256: `5136E5F7A6B720080F2C395A8025A2C6F23DCF98096102140244C9CC0B71E70D`
- `application/FrontierPilot.exe` SHA256: `A112068A06BF3094120D65AADFC3D366C1D65210540FECA3444092A82844583E`
- Unsigned development build; `build.json` explicitly says simulator verification not performed.

## PR / external blocker
Scoped GitHub `create_pull_request` returned **403 Resource not accessible by integration**. Branch pushed; [prepared PR description](cargo-cancellation-pr-body.md). [Create draft PR targeting master](https://github.com/MoneyWithMatt/frontier-pilot/compare/master...codex/cargo-cancellation?expand=1). No credentials extracted, alternate authenticated API used, repository settings changed or merge performed.

## Personal state and exact next steps
No feature changes installed, new local executable launched, personal save migrated or cargo cancelled. Existing installed C152 loading build and aboard consignments remain intact. Prior Dordabis missing report and manual-airstrip candidate are separate from this implementation.

1. Obtain exact artifact approval per AGENTS.md before replacing/running the local distribution. Proposed stopped-aircraft scenario: return/cancel the two Dordabis consignments at Eros, retaining the unrelated mail load; compare payload, fuel, stock, history and no payment. No flight, fuel write, repair, aircraft transfer or scenery installation.
2. Preserve the existing deployment receipt/hash checks and backup when installing approved artifact 14695932886; refuse mismatched/unowned distribution files. Do not run local WPF smoke against personal simulator configuration.
3. Conduct live MSFS origin unload/retry/restart acceptance. Separately obtain/confirm a remote cancellation scenario. Synthetic tests do not qualify physical unloading.
4. Create the draft PR when scoped connector permission is available. Leave merging to Matt.
