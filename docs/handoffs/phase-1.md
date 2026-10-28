# Handoff: phase-1-development-foundation

- Approved specification: [PHASE-1](../specs/phase-1-foundation.md), developer-approved work packages A–G.
- Branch: `codex/phase-1-development-foundation`; [draft PR #1](https://github.com/MoneyWithMatt/frontier-pilot/pull/1), targeting master. Do not create a duplicate PR.
- Last hosted-validated implementation commit: `e2324eefc7d790c70d68eb741b40b4e54de60b9a`; tested merge SHA and run links in [verification](../phase-1-verification.md).
- Follow-up changes: narrow diagnostics upload fix and evidence/handoff update.
- Status: implementation delivered; follow-up CI verification and human review pending.

## Completed
Inspected clean baseline and actual smoke programs. Added Windows verification stages, pinned official Actions, artifact provenance/checksums and concise documentation/templates. Preserved application code and historical evidence.

## Remaining / exact next steps
1. Inspect git status, current HEAD and PR #1 checks before resuming; do not repeat completed external actions.
2. Confirm the latest hosted run passes and uploads both development and verification artifacts after the diagnostics path fix.
3. Review artifact metadata, hashes and TRX without executing the desktop application locally.
4. Present results for human review. Branch protection recommendations are documented, not applied. Never merge without approval.

## Tests and blockers
Local and initial hosted regression: 744 passed, zero failed/skipped; full Release builds clean. Both hosted WPF smoke programs passed, and the development artifact was uploaded. Initial separate diagnostics upload was missing; follow-up fixes its hidden-directory handling. Dirty-source publication was correctly refused locally. No simulator or published application launched locally. Human approval is required for merge, repository settings and execution of changed artifacts on the gaming PC; no design decision blocks this foundation. No paid service or quota-resumption mechanism introduced.
