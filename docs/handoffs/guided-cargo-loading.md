# Handoff: guided-cargo-loading

Approved scope: [spec](../specs/guided-cargo-loading.md), Matt requested a fix and a really simple loader after the failed outside Caravan preview. Branch `codex/guided-cargo-loading`, isolated worktree from PR #7 `b481d9824450d1004e687bbf3316b0bbd0ae42d8` (includes unmerged PRs #6/#3); original branches unchanged. No new PR created yet; inspect remote before creation. Never merge without approval.

## State
Guided production UI and strict identity-transition read retry/refusal, stage-specific preview diagnostics, pending-edit locks, full pickup/deliver-here shortcuts, automatic aggregate station arithmetic and stateful departure implemented. No schema/pay/authority change or user data writes. [Findings, limits, live procedure](../guided-cargo-loading.md). Exact live source of the missing field is unconfirmed; persistent absence is not solved by inventing an identity.

## Verification
Dirty development `.ci-output-guided-01` passed 836/0/0 while additions were in progress; not final source evidence. Dirty `.ci-output-guided-02` passed 845/0/0, Release zero warnings/errors, SDK 8.0.424. A further native-versus-tracking failure-stage case was then added. Exact command: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-guided-02`. Compile-only `dotnet build tests/SiteUiSmoke -c Release` passed; no local WPF/runtime/simulator execution.

## Next
1. Commit reviewed source/docs; run clean final BuildTest, create/attach one draft PR to master and run hosted Windows full regression, nine guards, both WPF tests, package/uploads. Inspect screenshots and receipts, fix any real failures without weakening assertions. Keep exact source/dirty status/counts/failures/artifacts in the PR and update this checkpoint after runtime validation.
2. Prepare exact artifact for separately approved isolated live acceptance. Do not call the reported Caravan loading issue live-fixed from mocked tests. No agent execution of a new artifact is approved by the previous PR #7 installation request.
3. Preserve both September live-tested source checkpoint and prior local distribution backup. Do not roll back new-schema saves, change simulator/Community settings or reset the career.
