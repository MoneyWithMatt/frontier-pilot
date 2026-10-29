# Handoff: employer-starting-fleets

- Approved scope: [spec](../specs/employer-starting-fleets.md), Matt's attached implementation instruction, 2026-10-29.
- Branch: `codex/employer-starting-fleets`, isolated worktree based on unmerged PR #6 `fbf08bc`. PR #3/#6 original branches untouched. New draft PR targeting master will be recorded when created; never merge without approval.
- Last implementation/validation: `ee8bd47` plus preceding `d8f019b`, `3a4ba5d`; clean local full Release **835/0/0**, zero warnings/errors, SDK 8.0.424, `.ci-output-fleets-04`.
- Current local-only edits: this documentation checkpoint; runtime/test implementation committed. No local UI/published executable/simulator execution.

## Completed
Automatic employer-owned/player-assigned GUID fleet allocation from all actual company types, durable local player identity and cross-process idempotency; schema-3 career/schema-2 fleet; conservative legacy reconciliation; filtered selection/type/location/disabled guards; existing cargo/passenger/condition/maintenance integration; idempotent projection of tracker-qualified arrival locations. No ledger purchase/asset value, guessed native identity, condition or hours. Production fleet/onboarding UI and synthetic WPF coverage implemented. [Full behavior/live boundaries](../employer-starting-fleets.md).

## Tests and preserved failures
Exact local command: `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-fleets-04` → **835 passed / 0 failed / 0 skipped**, clean `ee8bd47`, Release zero warnings/errors. Earlier baseline run 01 passed 816; runs 02/03 passed 835. Focused `dotnet test tests/Tests -c Release --no-restore --filter FullyQualifiedName~EmployerFleetTests` passed 19. `dotnet build tests/SiteUiSmoke -c Release` compiles without warnings/errors; hosted execution pending.

First added focused run: **13 pass / 3 fail / 0 skip**. Two assertions compared deserialized list references rather than persisted values; replaced with stronger byte-for-byte unchanged-file checks. A legacy fixture accidentally triggered the existing legacy-active onboarding protection; corrected setup to an existing employment draft before storing that legacy fleet. Repaired 16/0/0, later integration/exposure/repair coverage 19/0/0. Failed and repair TRX files remain in `.ci-output-fleets-focused`; no assertions skipped/weakened.

## Remaining / exact next steps
1. Commit/push documentation checkpoint and create/attach a separate draft PR targeting master, explicitly noting the PR #6/#3 baseline. Do not update original feature branches or merge. Inspect remote state before repeating creation.
2. Run hosted Windows CI (existing workflow): BuildTest, nine verification guards, both WPF programs, Package/uploads. Inspect failures normally, clean commit receipts, TRX and company-fleet screenshots; update PR/evidence/handoff with actual outcomes. Current local results do not substitute for hosted WPF/package acceptance.
3. Human review and separately approved exact artifact/isolated live acceptance from the procedure. No simulator testing performed. No active-employer-change mechanic exists; API boundary preserves/reuses historical allocations. Internal FP registrations/local GUIDs do not claim civil/global server enforcement. Unknown condition/hours, disabled H125, SF50 configuration/visual limits, mixed-load and platform gates remain.

No product decision blocks this slice. Live artifact/scenario approval is separate. No personal game data, Community, installed SDK sources, GitHub settings or master branch were changed. Keep ignored auth/cache/diagnostic files out of commits. A final documentation-only evidence commit may follow the runtime CI checkpoint; record the validated runtime SHA rather than a self-referential handoff SHA.
