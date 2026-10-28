# Handoff: phase-1-development-foundation

- Approved specification: [PHASE-1](../specs/phase-1-foundation.md), developer-approved work packages A–G.
- Branch: `codex/phase-1-development-foundation`; PR not yet created.
- Last implementation/validation commit: baseline `53db07322ecd3d04fdc54c014cab3d6db06df4e2`.
- Local-only changes: CI/scripts, SDK pin, ignore rules, current docs and templates.
- Status: in progress.

## Completed
Inspected clean baseline and actual smoke programs. Added Windows verification stages, pinned official Actions, artifact provenance/checksums and concise documentation/templates. Preserved application code and historical evidence.

## Remaining / exact next steps
1. Run BuildTest with `.tools/dotnet/dotnet.exe` in a fresh `.ci-output-*` folder; inspect TRX.
2. Review script failure behavior, commit/push on this branch, create one PR to master and attach it to the task.
3. Validate hosted Windows build, both smoke programs and artifact; repair only scoped failures.
4. Update `docs/phase-1-verification.md` and this handoff with actual results and URLs. Never merge without approval.

## Tests and blockers
No new test results yet. No simulator or published application launched. Human input currently not required; Git metadata/network writes may require sandbox approval. Repository settings remain unchanged. No paid service or quota-resumption mechanism introduced.
