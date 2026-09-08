# Backlog

## Next acceptance test
- Run the WPF flow in a real flight: capture a new flat site, close MSFS, compile/install a new identifier, restart and inspect both starts.
- Exercise replacement of that new app-owned package and verify the prior version backup. Keep FPT1/FPSC untouched.
- Verify usability at additional Windows scaling settings.

## Prototype improvements
- Persist and explicitly resume drafts/surveys after app restart, with qualification checks and provenance.
- Detect runway footprint slope/obstacles before committing to placement.
- Explore authoritative global airport identifier collision checks.
- Support Community storage on other drives through tested on-volume staging.
- Improve progress detail for long Package Tool runs; preserve child-process safety.
- Package a convenient distributable launcher; reduce SDK setup burden.

## Research
- Requalify avatar elevation after simulator/SDK changes.
- Track an officially supported ground query API; retain empirical source limitations.
- Expand surfaces only after compile and simulator visual proof.

## Later product work
Economy, careers, settlements and multiplayer require separate design decisions and are outside Airport Builder 0.1.

## Completed in workflow hardening
- Explicit validation/recovery states and prerequisite gating.
- Recorded-survey offline end-to-end test mode.
- Integration tests for duplicate IDs, changed packages, reinstall, unrelated content and synthetic/live isolation.
- Technical log copy/save and stricter destination checks.

## Newly identified limitations
- Add durable install journal/recovery for power loss between directory renames.
- Reduce conservative path restrictions only with corresponding safety tests.
- Optional offline session browser/cleanup with explicit ownership and user-data protection.
- Perform the updated live WPF acceptance when the user is present; this task deliberately did not launch/control MSFS.

## Current acceptance priority — 2026-09-08
- Previous runway/parking/full workflow accepted by Matt in MSFS (user report).
- Next: test parking at East 1200 m / North 200 m and a three-segment taxiway, following taxiways-and-airport-site.md. Record live Package Tool result, parking selector/spawn, visible segments/surface/width, joins and terrain fit. New features have automated/schema/UI checks only.
- Future taxiway work only after acceptance: vertex dragging, curve/turn-clearance handling, explicit junctions and complete ATC/AI runway graph. Not implemented in this task.
