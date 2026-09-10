# Flight Attempt Observability V0

## Purpose and boundary

This milestone makes the first live Flight Attempt V0 simulator test diagnosable. It does not change the 1.5 NM origin/destination radius, 5 kt speed ceiling, ten-second stationary duration, two-second continuity gap, two-NM post-departure movement requirement, ten-NM maximum-distance requirement, aircraft identity comparison, loading/unloading actions, delivery proof construction, payment or inventory transactions.

Diagnostic events describe what the tracker observed. They are stored separately from `FlightAttempt`, `FlightDeliveryProof` and `VerifiedDeliveryEvidence`, are never read by qualification logic, and cannot make Load or Unload available.

## Jobs display

The collapsible **Live flight evidence diagnostics** section separates:

- live observations: SimConnect state, sample freshness/time, current aircraft identity, on-ground state, ground speed and current distances;
- persisted evidence: route, job/shipment IDs, loaded aircraft identity, airborne/movement flags and maximum origin distance;
- derived diagnostics: active stationary duration, required duration and the factual reason Load or Unload is unavailable.

The event table shows UTC, event type and structured facts. It contains no route, runway, aircraft or landing safety assessment.

## Timeline and persistence

Flight-attempt persistence is schema 2. Its document contains the unchanged attempt records and a separate append-only `DiagnosticTimeline`. Schema-1 files migrate atomically to schema 2 with an empty timeline; authoritative attempt progress and delivery proof are unchanged. Invalid or future files are preserved and reported under the existing strict JSON rules.

Event IDs are a deterministic SHA-256 derivation of job, shipment, event type and a stable event key. Lifecycle milestones use one stable key, so replay and recovery cannot duplicate them. Connection cycles and qualification attempts use their observation/start time. Repeated telemetry samples do not generate events. Each shipment retains up to 256 events; if necessary, the oldest connection and qualification-interruption diagnostics are removed while lifecycle milestones and errors remain.

Recorded types cover attempt creation, connection changes, origin/arrival qualification starts and interruptions, ready states, simulated cargo loading, InTransit transition, airborne/movement/maximum-distance evidence, unload request, delivery commit/refusal, recovery and aircraft identity mismatch. Timeline write failure is displayed as a diagnostic warning and does not change qualification state.

`DeliveredAndPaid` is a hard lifecycle boundary. Valid telemetry may continue refreshing the clearly labelled live-observation values while a completed attempt remains on screen, but the tracker no longer dispatches origin or arrival qualification and cannot append flight-progression events. The terminal check uses the persisted attempt state and its completed economy job, rather than filtering a particular diagnostic message. This also covers recovery when one persisted view has reached completion before the other is reconciled.

## Support output

**Copy diagnostic summary** creates a concise plain-text snapshot. **Export support bundle** writes redacted JSON containing application version, economy/flight schema versions, configured V0 thresholds, current non-sensitive telemetry, derived status, the relevant job/requirement/shipment/payment records, active attempt, persisted evidence flags and its timeline.

The bundle omits persistence paths, working directories, Windows usernames, unrelated economy records, unrelated local files and credentials. Both actions are read-only.

## Live MSFS verification

Matt completed the Eros-to-Arandis path in MSFS on 2026-09-10. The captured diagnostics showed matching loaded/current Piper PA28-236 identity, airborne and movement evidence, 126.29 NM maximum distance from Eros, arrival 0.01 NM from Arandis, explicit unload, `DeliveryCommitted`, and the **Delivered and paid** stage. The destination inventory/payment result and replacement-offer status were visible in the application.

That test also exposed one observability defect: the first telemetry sample after delivery cleared a leftover arrival timer through the generic no-active-job path and labelled it `OriginQualificationInterrupted`. The terminal lifecycle boundary above corrects that classification without changing flight evidence or economic state. Uis, disconnect/reconnect during a flight, and a completed-attempt application restart still need live acceptance.

## Automated verification

On 2026-09-10, the terminal-lifecycle regression suite added immediate, repeated and post-restart telemetry cases. These assert that live observations continue changing, no qualification/progression event follows terminal delivery, and inventory/payment remain exactly once. The final focused/full-suite counts and published executable hash are recorded in `evidence/flight-attempt-v0/verification.md`.
