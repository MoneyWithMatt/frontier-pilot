# Feature: offshore-landing-proof — H125 deck feasibility

Status: Approved experiment; live static deck rest observed, flown landing/mission unverified
Approved by / date: Matt's live MSFS 2024 Offshore Helicopter Landing Test instruction, 2026-11-02.

## Objective
Observe a real H125 flight/deck landing from FYWB and determine whether deck contact, stable rest and a round trip can be recognised. Prefer simulator discovery. Separate flown, repositioned/static, SDK, synthetic and unverified evidence.

## Gameplay behavior
No normal career change. Experimental mission is conditional on physical/deck telemetry qualification. H125/platform production availability and wages stay unchanged. Collect at least 20 continuous seconds at rest and negative-control hovering data; this is an observation target, not a new career rule.

## Acceptance criteria
- Establish platform identity, coordinates, deck elevation, collision and exact simulator/aircraft/version evidence.
- Record identity, lat/lon/altitude, terrain/obstacle AGL, vertical/ground/surface-relative speeds, on-ground and candidate contact signals with time/continuity.
- Proximity/hovering and stale touchdown coordinates cannot prove new deck contact.
- A flown round trip needs actual departure/arrival/return evidence; placement diagnostics are labelled separately.

## Non-goals
No global H125 enablement, career/pay/save changes, cargo branch modifications, assumed rig airport identifier, moving-vessel qualification, failure injection or third-party content purchase.

## Relevant existing systems
PR #3 passenger/platform gates and PR #8 cargo work inspected read-only. Main FlightAttemptTracker, LiveAirportCatalogue, LiveAircraftCondition and scenery/owned-package installation remain authoritative. New isolated branch from master. The recorder binds only SDK queries; it cannot control flight or grant mission proof. A separate one-shot H125/OL400 inspection helper binds only position and slew commands, with a bounded release watchdog. Offline rest candidates never create game authority.

## Persistence implications
No personal Frontier state access. Create-only JSONL outside source. No schema migration. Scenery fallback requires a separate owned package and applicable build/install approval.

## Required tests
Offline Python packet fixtures plus full Windows regression, hosted WPF, guards and packaging. Ordinary CI needs no simulator/SDK/personal configuration. Fixtures never establish live compatibility.

## Simulator testing requirements
Matt approved the presented read-only diagnostic and continued experiment with "I approve whatever you need" on 2026-11-02. The requested separate repositioning diagnostic was performed; it is not a flown mission. Record errors and unsupported fields explicitly. Any future continuous flight controller needs a concrete artifact/scenario review and ground calibration before flight. Existing simulator UI control is authorised by the user's request. No such controller is currently established.

## Open design decisions
None before physical feasibility. Reliable deck criteria follow observed signals; no prototype mission proof from unit tests.
