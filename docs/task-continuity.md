# Task continuity across sessions

Keep one small `docs/handoffs/<task-id>.md` on the feature branch, based on [the template](templates/task-handoff.md). Update before stopping, after a meaningful checkpoint, or when usage is running low. Commit/push authorized work so another machine can resume; if that is blocked, say which changes exist only locally. Never commit credentials, auth caches, private saves or personal telemetry. Link large public-safe CI evidence instead of copying it.

The recorded commit is the **last implementation/validation commit**, not the self-referential handoff commit. State dirty changes separately. Preserve the approved spec and exact next action; a conversation summary alone is insufficient. A PR body may link the handoff, but do not depend on a private chat being available.

## Resume procedure

1. Read `AGENTS.md`, the handoff, approved spec, current `ARCHITECTURE.md`/`TESTING.md` and relevant design reports.
2. Inspect `git status`, branch and HEAD. Preserve pending edits. Compare against the recorded commit; inspect later changes before acting. Do not reset another session's work.
3. Confirm existing PR and CI run status. Reuse the branch/PR; do not duplicate completed work or replay a completed external action.
4. Check whether recorded tests apply to the current code. Re-run only checks invalidated by changes or unresolved failures; a new full artifact still requires the complete pipeline on its exact commit.
5. Continue the exact next step if authorized. Escalate gameplay ambiguity, destructive actions, unavailable credentials, safety changes or missing live approval.
6. Update the handoff with actual results and remaining work.

Usage exhaustion means work stops until an authorized later session resumes it. This convention does not restart Codex, poll quota resets, bypass usage limits, or require paid APIs. The user starts/resumes the next session under their subscription. Do not promise automatic continuation.
