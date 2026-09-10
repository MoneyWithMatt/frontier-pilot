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

## Support output

**Copy diagnostic summary** creates a concise plain-text snapshot. **Export support bundle** writes redacted JSON containing application version, economy/flight schema versions, configured V0 thresholds, current non-sensitive telemetry, derived status, the relevant job/requirement/shipment/payment records, active attempt, persisted evidence flags and its timeline.

The bundle omits persistence paths, working directories, Windows usernames, unrelated economy records, unrelated local files and credentials. Both actions are read-only.

## Live MSFS verification still required

Flight Attempt V0 remains unverified in MSFS. During the test, expand diagnostics before loading and confirm connection/sample data, the ten-second origin timer, the loaded identity and each evidence milestone. After landing, confirm the arrival timer reaches ten seconds, **Ready to unload** appears, and one explicit Unload produces **Delivery committed**. If anything differs, export the support bundle before resetting or changing state.

## Automated verification

On 2026-09-10, 23 focused Flight Attempt tests passed. The complete Release suite passed 439 tests. The Release solution build completed with zero warnings and zero errors, after which the self-contained Windows x64 application was republished. These results prove deterministic software behavior only; SimConnect timing and the full live flight remain pending simulator verification.
