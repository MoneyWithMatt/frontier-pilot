# Physical aircraft, automatic observation and maintenance — implementation V1

27 October 2026. Implements the independent persistent foundation and testable domain models from the supplied proposal, with persistent location added and no vendor-specific policy. This is not a claim that production failure injection, workshop facilities or automatic placement are qualified.

## Available in the desktop

Aircraft now opens with Fleet, alongside the existing Payload/Fuel/Weight & Balance tools. Enrol the currently loaded aircraft explicitly with a registration and declared package/version. Existing company type access remains separate; map the exact loaded type in Career / Settings before enrolling a company aircraft. No aircraft, position, lifetime hours or health is seeded as player data.

Each airframe has an immutable GUID, operator, company type, explicit livery bindings and independently saved location, components, history and dispatch associations. Registration can change without changing the GUID. Adding a livery to the selected airframe is an explicit action, not a new aircraft. Engine count is pinned from the first observation; different reported configurations refuse further condition frames. Package version is currently player-declared, not auto-detected.

Selecting a matched physical airframe starts a single automatic observation owner. No Record button is required. Unselected/mismatched connections remain observation-only. An object-ID change during a session requires explicit binding confirmation, even with the same title/model. Multiple identical airframes require deliberate selection; title or registration never proves which physical instance is loaded. The manual JSONL recorder remains an optional isolated diagnostic connection and does not feed exposure or wear a second time. Raw JSONL is not continually written by automatic monitoring.

The overview shows source-labelled hours (lifetime Unknown; observed since enrolment), cycles, current observation status and each airframe's last-known location. The fleet map uses the existing Mapsui/OSM presentation with saved positions, no fabricated markers and no repeated forced recenter. Condition, maintenance requirements/quote preview and durable history are separate tabs. Explicit baseline declaration can initialize unknown Frontier estimates once; it neither invents historical hours nor changes MSFS. It refuses existing health, vendor authority, active faults or work.

## Persistence and migration

`<career root>/fleet/aircraft-fleet.json` schema 1 is the physical-airframe authority. An atomic replacement contains condition, location, counters, faults, work and event receipts in the same revision. Cross-process semaphore and expected revisions prevent competing writers from overwriting one another. A `.previous` snapshot is retained. Corrupt, unsupported or missing-primary-with-backup documents block writes; they are never replaced with healthy defaults. History prefixes, removed component instances and fault identities cannot be deleted through normal commits. Restore an inspected backup explicitly with the application closed; there is no automatic recovery over damaged data.

The old `tracking/aircraft-state.json` remains preserved and readable as unassigned legacy history. The desktop's `FleetTrackingProjection` stops further type/title-based persistence and projects the selected physical record to Tracking at initialization. It never assigns legacy coordinates to a new GUID. The legacy clear action refuses to delete fleet data. Existing CLI V0 identity documents are not silently imported or assigned; their files remain untouched. Fleet registration is career-scoped and case-insensitively unique.

Automatic checkpoints are bounded to ten seconds, with immediate session and meaningful location-transition saves and graceful shutdown flush. An abrupt exit can lose up to the last checkpoint interval of counters; startup marks an open session interrupted/uncertain. Graceful shutdown cancels and joins condition observation and waits for in-flight maintenance reconciliation. Career reset archives the exact fleet file and prior snapshot alongside existing career files; diagnostics remain separate.

## Location and dispatch

Validated Tracking aircraft observations feed only the selected binding. Initial continuity is observed for ten seconds; known pause, stopped simulator and slew state inhibit updates. Latest observed position retains latitude, longitude, altitude and heading. Unqualified ground observations are labelled Last known; neither the ground flag nor legacy stand recognition proves safe parking. Separate qualified-location logic requires sustained low-speed dry contact, context and height qualification, retaining full elevated altitude. Water, loading and unqualified heights do not generate a stationary placement. No production height/surface adapter is enabled yet, so normal live recordings cannot presently establish spawn-authoritative parking or workshop access.

Gaps/reset/object changes break continuity. Reconnection far from a saved point and large continuous jumps are recorded as external relocation requiring acknowledgement before new dispatch. Acknowledgement is a history action only: it never moves cargo or creates flight/payment evidence. Unknown airborne termination becomes Interrupted flight. Confirmed crash assessments can ground an airframe without inventing component percentages.

New positive cargo-loading/dispatch actions check the selected individual airframe, company type, availability and fresh saved ground position within the existing 1.5 NM operational loading radius of each origin. New dispatch associations are durably recorded before continuing existing payload verification. Another identical airframe cannot claim that same associated job. Accepted shipments, unloading, inventory and payment remain under their existing services. Reassignment of a failed/aborted association is intentionally blocked pending explicit recovery support, not silently transferred. Legacy fuel/payload manifests retain their existing authority and identity format; this milestone does not migrate fuel or cargo between individual airframes.

Return to aircraft shows saved coordinates, altitude, heading and uncertainty, and guides manual MSFS loading. It does not teleport or restore an airborne snapshot. `AircraftPlacementService` supplies a tested, one-attempt journal for qualified adapters, with readiness/terrain/platform and readback contracts. No production positioning adapter is registered.

## Exposure, condition and incident models

The standard read-only profile adds SIMULATION TIME and SURFACE TYPE; the native source subscribes to Sim, Pause_EX1 and PositionChanged. Unknown event state blocks exposure; pause flags include active pause. Engine combustion is used for engine-running exposure/starts, never generic Hobbs as assumed lifetime history. Counters are provisional observations; aircraft-specific semantics still require acceptance.

`AircraftExposureAccumulator` integrates validated simulator seconds, not wall time. Pause/loading/slew, duplicate/backward timestamps, excessive gaps, session changes and discontinuities break continuity. Engine-running time, starts and airborne time are separate. Fresh airborne-to-qualified-ground episodes consolidate bounces and exclude water; latched touchdown values cannot create cycles on connect.

Components have immutable instance IDs, individual authority, unknown or declared health, operating exposure, persisted cumulative hazard/threshold and model version. Replacement archives the removed instance and creates a new one. Frontier, vendor and unknown authorities are distinct; unknown/vendor components do not receive Frontier wear. The generic versioned wear/risk engine is tested, but no aircraft-specific wear coefficient or operating limit is shipped as qualified. Persisted thresholds prevent reconnect rerolls; deterministic fault IDs and integration prevent duplicate threshold faults. Risk integration stops while its fault is unresolved; explicit repair renews the persisted risk lifecycle. Random failures and automatic simulator effects default disabled in fleet state.

`AircraftIncidentAccumulator` accepts only fresh contact evidence plus a qualified aircraft-specific inspection threshold. It consolidates a landing episode's worst contact, excludes water and stale initial touchdown, and creates an inspection requirement rather than a guessed percentage. Crash assessment requires explicit qualified evidence. Exposed-parts aggregates are never allocated to components. Generic incident logic has synthetic coverage; production crash and hard-landing mappings remain evidence-gated. Direct lightning, hail, corrosion and parts logistics are deferred.

`IAircraftOperationalAdapter` is the exact-aircraft/connection qualification boundary for additional field semantics, dry-ground/platform height, wear profiles and normalized samples. The generic transport profile is not such a certificate. There is no default-aircraft or third-party whitelist inferred from the existence of SDK variables.

## Maintenance and economy

Inspection, service, repair, overhaul and replacement have distinct tested transitions. Inspection/service do not repair faults or infer unknown health. Repair acts on the ordered fault set; overhaul restores model health/life; replacement creates a new component while preserving removed history. Unsupported external repairs remain AwaitingSimulatorRepair. Paying is never sufficient to prove a simulator repair.

Work orders persist explicit payer/payee, quote version, facility, component instance, selected faults and real UTC due time. Centralized `MaintenancePrototypePolicy` quotes are labelled prototype credits, not real-world prices. Booking requires qualified stationary dry-ground location at an enabled facility and engines stopped. The shipped facility list is deliberately empty until a real authored facility and location adapter are accepted: quote preview works, booking is disabled, and no player is charged during acceptance. This is a functioning domain/settlement implementation, not yet an enabled workshop gameplay loop.

Economy schema 10 adds maintenance charge receipts and a dedicated authoritative ledger kind. Player payment checks unreserved funds; SYSTEM employer maintenance uses an explicit employer-funding abstraction and never debits the player. Non-SYSTEM business treasury support is not invented. Durable fleet intent precedes the idempotent economy debit, then fleet settlement/completion. A retry after either commit boundary cannot pay or repair twice. The existing desktop UTC refresh reconciles due work; no second simulator-time maintenance clock is introduced. Actual fault repairs need external verification before completion.

## Simulator commands

Observation/recording remains read-only. `AircraftFaultSynchronization` is separate from the source and accepts only allowlisted component-matched explicit set operations supplied by a qualified adapter. Intent and the attempted marker are persisted before a write; readback and operational effect must both agree. Unknown, conflicting or interrupted outcomes remain visible and cannot blindly retry. Adapters are injected in isolated tests only: there is no native failure-writing implementation registered, and no live failure command was sent. Apply/clear, retained effect across initialization and vendor interference still require exact package/version acceptance.

## Delivery status

| Area | Status |
|---|---|
| Physical IDs, registration/livery selection, location/history, automatic checkpoints, overview/map | Implemented; live acceptance pending |
| Existing tracking migration | Legacy preserved, unassigned; physical fleet authoritative |
| Engine/airborne exposure | Implemented; native pause/time behavior not yet live-verified |
| Qualified stationary/elevated location and landing episodes | Implemented model/adapter contract; no production qualification |
| Wear, deterministic risk, inspections and component replacement | Implemented and synthetic-tested; aircraft calibration not enabled |
| Work orders, UTC recovery, ledger settlement | Implemented and synthetic-tested; facilities/booking not enabled |
| Fault set/clear and position return journals | Implemented and synthetic-tested; production adapters unavailable |
| Automatic native wear restoration, fault enforcement, automatic placement | Evidence-gated; unavailable in shipped desktop |
| Weather damage, logistics, cargo reassignment, complete individual fuel/payload migration | Deferred, no simulated substitutes |

## Shortest simulator acceptance

1. Launch the normal rebuilt application. Load an aircraft in MSFS; open Aircraft / Fleet (Tracking connects through the existing navigation lifecycle).
2. For company dispatch, confirm its supported type in Career / Settings. Enter a real registration and exact installed package/version; Enrol loaded aircraft. Do not declare a healthy baseline unless you intend that explicit assumption.
3. Wait 15 seconds. Verify automatic observation status, discovered components, last-known position/map and observed counters. Record is optional. Pause, active-pause and slew must not accrue exposure; compare time at 1x/2x.
4. Close/reopen normally. Select the same GUID/registration; location and component IDs must persist. Enrol another registration for the same model and verify independence. Changing the original registration or binding a livery must retain its history.
5. Disconnect in flight and reconnect: expect Interrupted flight/last known, never a safe spawn. Change loaded object and confirm observation-only until explicitly bound. Review an external relocation without moving cargo.
6. Test C172, the exact installed Dakota and Baron separately. Do not inject failures, purchase maintenance or request positioning: no production adapter/facility is enabled. Next acceptance milestone is qualification of time/pause, dry-ground/platform height and one aircraft/facility pair.

## References and verification

- Existing [SDK investigation](aircraft-condition-feasibility.md), [recorder V0](aircraft-condition-v0.md) and [manual recorder UI](aircraft-condition-recording-ui.md) remain historical evidence.
- [MSFS 2024 environment variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/Environment_Variables.htm): simulation time is exposed through SimConnect; rate is not wall time.
- [System-event subscriptions](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/general/simconnect_subscribetosystemevent/): Pause_EX1 includes active/player pause and returns current state. Read-only subscription is not fault injection.
- [Aircraft miscellaneous variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_Misc_Variables.htm): SURFACE TYPE includes Water; aircraft ground contact alone cannot certify a platform landing.

Automated results and precise limitations: [verification](../evidence/physical-aircraft-v1/verification.md). No live simulator compatibility is claimed.
