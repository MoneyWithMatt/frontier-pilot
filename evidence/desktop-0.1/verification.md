# Desktop 0.1 verification — 2026-09-07

Implementation built with the repository .NET SDK 8.0.424 on Windows x64.

- Solution build: success, zero warnings/errors (build.log).
- Full test suite: 196 passed, zero failed/skipped (tests.log and desktop.trx).
- New coverage: VM prerequisites, input validation, protected IDs, reciprocal display, successful workflow, invalidation after edits, capture cancellation, capture/build/install failures and retry. Temporary-folder installation tests cover own-package replacement with preserved backup, unrelated content, unknown/changed ownership, changed source, preflight/staging cancellation, simulator-start race, path escape and rollback after a locked ownership receipt causes commit failure.
- Launched the real WPF executable locally using the repository desktop runtime.
- Visually inspected initial SDK/capture sections, fixed action footer, lower input fields, runway preview, validation and final guidance at 1100 x 900.
- Clicked Check and connect with MSFS closed. The UI stayed responsive, displayed "Launch MSFS 2024 and load a flight, then check again." and left capture/build/install disabled.
- Inspected scrolling and keyboard close. window-lower.png preserves the lower section and closed-simulator message. Subsequently simplified the name validation wording to remove character-encoding jargon.
- Final build and tests rerun after progress callback guarding, same-volume refusal and wording changes.

No live avatar capture or scenery compilation/installation was repeated during this UI task. Those components retain the existing Sedona/FPT1/FPSC empirical evidence. Tests of the complete VM flow use a fake service; installer tests use disposable synthetic packages, not live scenery. This is not a claim that a full WPF-to-simulator acceptance session has happened.

No installed SDK sample, FPT1/FPSC package, unrelated Community package or historical evidence was modified. A new user-driven WPF acceptance session is recorded in docs/BACKLOG.md.
