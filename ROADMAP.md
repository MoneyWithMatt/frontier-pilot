# Frontier Pilot roadmap and current status

This index separates implementation from acceptance. Historical reports in `docs/TECHNICAL_STATUS.md`, `docs/BACKLOG.md` and `evidence/` remain intact; older dates may describe superseded behavior. See [architecture](ARCHITECTURE.md) and the linked feature reports before changing rules.

| System | Implemented / automated evidence | Live acceptance / remaining boundary |
|---|---|---|
| Site builder | Geometry, saved layouts, runway-free scenery, parking/taxiway plans, guarded generation/install; synthetic coverage | Recorded runway/parking/taxiway acceptance exists; current materials, lodge footprint and some foundation checks remain pending. No complete ATC/AI network |
| Tracking | Aircraft/avatar/current classification, persistence, Frontier recognition; fake-source tests | Aircraft V1 accepted; dual-marker/recognition/recovery procedures remain feature-specific acceptance work |
| Career/employment | Onboarding, four employers, explicit aircraft type mappings, worldwide airport lookup | Lookup accepted on recorded SDK/simulator build; employer/aircraft workflows need further playtesting |
| Offshore priority | FYWB Vision Jet cargo; both-endpoint aircraft restrictions and actual-origin planning estimates; seven new synthetic Offshore cases, 751-suite local pass | H125/platform, survey and passenger lifecycle selection pending; no new live acceptance. [Tasks/capabilities](docs/offshore-career.md), [spec](docs/specs/offshore-career.md), [handoff](docs/handoffs/offshore-career.md) |
| Economy/logistics | Authoritative ledger, recurring SYSTEM demand, player logistics, operation production and real UTC catch-up; regression tests | Prototype balance/design choices remain; accepted shipments must not expire as SYSTEM offers |
| Flight/loading/wages | Tracker-created proof, explicit simulator payload verification, partial transfer/recovery, grouped hourly wages; synthetic tests | Eros–Arandis delivery accepted 2026-09-10; later wage/payload/recovery changes need their own live procedures |
| Physical aircraft | GUIDs, binding, automatic observation, persistent location/condition, exposure/maintenance/fault contracts; 744-suite historical baseline | Native condition/time/pause and aircraft qualification pending. No enabled production fault/position adapter or maintenance facility |

Sources: [physical aircraft](docs/physical-aircraft-v1.md), [wages](docs/employed-pilot-wages-v1.md), [loading](docs/aircraft-payload-loading-v0.md), [airport lookup](docs/automatic-airport-resolution.md), [real-time economy](docs/real-time-economy.md), [site progression](docs/runway-placement-and-runway-free-sites.md).

## Development infrastructure

1. **Phase 1:** reproducible Windows build/tests/publish, GitHub-hosted CI, traceable seven-day development artifacts, accurate entry docs, feature/PR templates and durable handoff. Hosted verification and limitations belong in [Phase 1 evidence](docs/phase-1-verification.md). Repository settings require owner approval and are not changed by this work.
2. **Phase 2:** pilot one approved feature from mobile to cloud implementation, isolated branch, CI and review. Use existing subscriptions; no paid API orchestrator. Keep human merge approval.
3. **Phase 3:** isolated live-test data root, general telemetry recording/replay, restricted outbound-polling local harness. Standing approval may cover reviewed allowlisted scenarios; new executable artifacts do not inherit approval. No self-hosted Actions runner on the gaming PC.
4. **Phase 4:** bounded, durable repair/retest coordination with explicit budgets and escalation. No quota bypass or automatic resumption is claimed today.

Current product priority is completing Offshore through the staged tasks above. Passenger/platform/survey mission designs and gameplay balancing require approved specifications. Other deferred work includes qualified failure/position effects, permanent wear calibration, individual fuel/payload migration, broader live acceptance, customer signing/updating and production map service selection.
