# Feature: cargo logistics and dispatch manifest investigation

Status: Approved investigation only; proposed implementation is not approved by this document.
Authority: Matt's cargo-loading, multi-job logistics and manifest investigation request, 2026-10-29.

## Objective

Establish what the existing cargo/passenger systems already support, compare them with independent multi-stop consignments, and recommend the smallest safe upgrade route and a practical printed dispatch-sheet interface.

## Gameplay behavior

No behavior changes in this task. The requested future policy leaves MTOW, CG and operational performance decisions to the pilot; only actual simulator/interface constraints should block physical loading. Preserve identity, inventory, ownership, readback and once-only settlement integrity. Record current conflicting gates without modifying ongoing Offshore development.

## Acceptance criteria

- Report current code, models, methods, tests, persistence and official SDK evidence.
- Classify intended behaviors as supported, partial, unsupported or unverified.
- Explain logical consignments versus aggregate simulator mass and multi-stop/payment/recovery risks.
- Supply a static fax-style manifest study, incremental stages and genuine product decisions.
- Distinguish existing automated evidence, source deductions and live acceptance.

## Non-goals

No production/test-code changes, gameplay implementation, merge, simulator/package operation, airline system or performance worksheet implementation. Do not change PR #3 or import PR #4's destination research.

## Existing systems / persistence

Inspect current `master` and development PR #3; use the existing economy, shipment, transfer, loading, tracker, fleet, wage and shared passenger systems. No save/schema migration in this task. Add research documents and a handoff on a separate branch targeting `master`.

## Verification / simulator requirements

Run existing regression against a clean isolated development snapshot, validate source references and mock-up arithmetic/rendering. Ordinary hosted Windows CI may validate the documentation PR's unchanged master runtime. No new gameplay assertion is implemented. Future stages specify tests and exact aircraft-specific live acceptance separately.

## Open design decisions

See report section F: multi-leg wage settlement timing, cargo custody/cancellation outside destination, and outstation work generation. The pilot-responsibility policy and use of existing logical shipments are already settled; do not ask those again.
