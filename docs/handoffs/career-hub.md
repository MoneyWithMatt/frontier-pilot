# Handoff: career-hub

- Approved specification: [Career Hub](../career-hub.md), Matt's 2026-11-03 request.
- Branch: `codex/career-hub`; draft PR not created (GitHub connector 403).
- Prepared PR: [description](career-hub-pr.md); [open compare](https://github.com/MoneyWithMatt/frontier-pilot/compare/master...codex/career-hub?expand=1).
- Integration base: PR #10 head `d5d4f23b8f6fc7a13167b8f55a2398a3d157cd49`. Includes unmerged passenger/fleet/loading work. Master and other worktrees untouched.
- Last validated implementation: `ea32975ea8ef9a1b1b1941aec85969053627bc98`, clean source; [all hosted checks passed](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204).
- Current checkpoint after that implementation changes documentation and one additional synthetic viewport assertion only. No production changes. Check latest branch Actions before using its newer artifact.
- Status: implemented, ready for human UI/dependency review; external PR creation blocked. Nothing merged, installed or executed on the gaming PC.

## Completed work

Five-section Career shell, actual employer/marketplace information, physical fleet and inspection-only condition. Original loading controls reparented once into Fleet; shared GUID context and guarded accepted-job navigation. Unified cargo/personnel/observation/platform-status board with existing Mapsui geography. Existing owned-site logistics creation only. Legacy recovery preserved. No schema, pay or simulator capability changes.

Selection survives immutable WPF refresh. Source mismatch cannot accept another job. Pending cargo identities remain attached to the original airframe before confirmed carriage. Cargo drafts/passenger recovery block switching through both Career and the original fleet selector. Embedded passenger handling and legacy recovery scroll at compact desktop heights. Hosted WPF found and verified the fix for read-only aircraft Run.Text writeback crashes.

## Tests and evidence

Exact local command (PowerShell 7, SDK 8.0.424):

```powershell
pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-career-14
```

Run 14: clean ea32975, 981 passed / 0 failed / 0 skipped; zero build warnings/errors. TRX/receipts remain in ignored output. Earlier 09/10 passed 980, 11–13 passed 981; initial 01/02 passed 970 existing tests. Preserved 03–05 failures caught stale projection, an incorrect test role expectation and a vague support-text assertion. Run 07 preserved a XAML property-element namespace compiler error. All fixed; no assertions skipped/weakened.

Hosted Windows commands:

```powershell
./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet
pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1
./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet
./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet
```

The workflow also runs `review-offshore-roundtrip` on the synthetic WPF recording and checks historical-only authority.

- [Implementation run 41630988204](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204): 981 xUnit / 0 failures / 0 skips; 7 LoadingWpfSmoke and 185 SiteUiSmoke assertions; 9 simulated verification guards; historical CLI and packaging passed. No build warnings/errors.
- [Synthetic screenshots, logs and receipts](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204/artifacts/13228037288). Overview, condition, employers, mapped board, cargo, passenger, owner empty state and 1024 layout inspected. Map tiles are not an asserted result.
- [Exact ea32975 development bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204/artifacts/13229275192): checksums/build metadata included. Download is not local execution approval.
- Earlier full passes: [02c0ba5](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630114192), [48b1303](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630584308).
- Preserved hosted failures: [a7aee64 compiler declaration](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41628457559), [291abdd old smoke assumed My Sites default](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41628649302), [0449232 read-only aircraft label crash](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41628982726), [832eed9 transient job selection](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41629597980). Fixed and exercised by subsequent passing pipelines.

All screenshots/flight fixtures are synthetic. No live MSFS, real payload verification, scenery installation, personal save migration or new local executable run occurred. SF50 boarding and H125/platform restrictions remain effective; visible passengers and mixed transport are not newly qualified.

## Blockers and human input

GitHub's scoped draft-PR connector returned HTTP 403, “Resource not accessible by integration.” Ordinary git push succeeded. Local browser control reports disabled in Work settings. No credentials were extracted; the previously rejected shell-credential REST workaround was not retried. No account settings/permissions were changed. The feature branch has an explicit existing hosted-Windows workflow push trigger so verification is independent of PR metadata access.

Create one draft PR to master from this branch using the prepared text, or restore a suitably scoped authorised PR-write connection. Check for an existing PR first. No gameplay decision blocks this slice.

## Remaining work / exact next steps

1. Inspect latest branch Actions for the documentation/test-only checkpoint; preserve/fix any failure. Do not run local SiteUiSmoke against personal simulator configuration. Evidence above remains tied to exact ea32975.
2. Create the draft PR when scoped access is available. It is stacked on PR #10: compare after d5d4f23 for Career review and review dependencies before any merge. Leave merging to Matt.
3. Human UI review: actual employer, GUID selection, condition, mapped board, both loading entry points and compact layouts. Real simulator use requires the exact artifact/scenario approval; this slice grants no qualification.
4. Future backend tasks remain separate: safe employer departure/switching, aviation-company authority and multiplayer hiring; SF50/H125 qualification and mixed transport acceptance.
