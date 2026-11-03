# Feature: offshore-missions — next playable Skeleton Coast Petroleum board

Status: Approved implementation; exact executable/live scenario acceptance remains separate.
Approved by/date: Matt's “Complete Offshore Career Mission Development” request, 2026-11-03.

## Objective
Provide a usable Vision Jet observation round trip and one Offshore UI for existing shared personnel transfers and individually gated platform operations. Preserve $800 per verified airborne hour and all cargo, fleet, save and simulator guards.

## Gameplay behavior
Observation: accept, select assigned Vision Jet, connect mission telemetry, remain stopped at database/review-qualified Walvis Bay for10s, Begin, depart beyond1.5NM, reach a broad operating area after credible movement, observe continuously airborne, return and stop10s, explicitly complete. First authored area surrounds visually confirmed OL400,5NM radius/300s configurable; it is a local harbour-area patrol, not a claim of offshore-distance drilling equipment inspection. Approximate duration shown; pilot owns aircraft performance decisions.

Passenger tab reuses PassengerTransfersService/ViewModel/PassengerPayloadCoordinator and existing aircraft manager; new physical SF50/H125 boarding remains blocked without configuration/qualification. Platform registry records OL400 static evidence independently from mission activation. Supply/personnel/inspection mission families remain unavailable unless exact aircraft/operation/version qualification exists. Experimental contact diagnostics never grant normal flight proof or wages.

One started observation flight excludes aircraft cargo/passenger carriage until settled, using the same atomic world and native operation lease. Offers/reservations do not invent cargo or passenger mass. Existing hourly rates/fixed contract semantics unchanged. This interim assignment exclusion avoids double wages; mixed transport remains an explicit existing limitation.

## Acceptance criteria
- Production WPF board with acceptance, progress/area distance, begin/resume/complete, personnel manager and platform qualification reasons.
- Only actual read-only condition source with known running/unpaused, rate1, no slew/freeze, matching selected physicalGUID and exact company binding advances new objectives. Position/time gaps, implausible jumps and interruptions cannot add wages/loiter.
- Spawn in operating area cannot begin or complete. Complete observation needs continuous zone dwell; excursions reset only unconfirmed dwell. Return requires later airborne observations and fresh base rest. Final return proof freezes wage time; retries/restarts settle once.
- Economy schema14 retains all schema13 jobs, cargo, passenger, resources, ledger and flight/fleet stores. New progress/completion/wage are one atomic economy revision. No personal save access in tests/live preparation.
- Platform destination generation stays disabled; static starts/constrained diagnostics cannot qualify production destinations. Existing deck predicate reused, no unqualified platform cargo/personnel mass writes.

## Non-goals
No PR merges, unrelated branches overwritten, personal saves/Community changes, new autopilot, fares, medical simulation, guessed aircraft configuration/seat count/visible passengers, artificial MTOW/CG/performance dispatch veto or global H125 activation.

## Relevant existing systems
Integration branch starts PR8 47698b1 (contains unmerged PR3/6/7 by verified ancestry). No merge operation. Selected pure Core presence/visit and read-only recording/tests from PR9 80abb04 are reused; no flight-controller or payload diagnostic writer copied. Core.OffshoreMissions adds mission policy/atomic ledger validation, FlightAttemptTracker.OffshoreMonitor owns normal objective/flight validation, existing condition transport supplies native observations. Existing passenger and cargo ownership/recovery remain authoritative. New registry is authored qualification metadata, not editable player-save enable flags.

## Persistence implications
Schema14 default-empty OffshoreMissions; existing collections/history/money unchanged. Strict managed-file reader preserves corrupt/future saves. Started flight snapshots retain selectedGUID, identity, staged facts and verified airborne duration. Restart requires explicit recovery at base or last local area, timers restart and gaps never pay. No cancellation policy invented beyond existing supported operations. Physical airframe history projects completed mission idempotently; no location/health/hours or spawn authority inferred.

## Required tests
Full Windows regression, targeted mission lifecycle/wages/telemetry/restart/corrupt-save/operation-gate tests and synthetic WPF control flow/screenshots; existing passenger/cargo/fleet regressions retained. Required hosted WPF/package stages, no personal SDK needed for ordinary CI. Preserve failures, no skipped/weakened assertions.

## Simulator testing requirements
Separate actual SF50 observation flight, aircraft configuration/physical passenger loading and exact H125/platform operation qualification. Build artifacts and allowlisted scenario need explicit approval under AGENTS.md; current run59 approval belongs to PR9's old constrained diagnostic only. Desktop activation last failed; no simulator actions are implied by this implementation. Prepare a focused acceptance procedure and distinguish all automated/replay/live categories.

## Open design decisions

Continuation: extend the experimental platform visit to a read-only base → OL400 timed visit → base recorder, using the existing generic transport monitor inside FlightAttemptTracker and shared deck predicate. It never exports internal flight proofs into the economy or enables destinations. Persist create-only context/raw frames; offline review is historical, interrupted sessions cannot stitch dwell/flight. This is qualification tooling behind the existing restrictions, not another pay/loading system or a Jobs redesign.
No unresolved decisions block the first broad-area observation loop. H125/platform/physical SF50 restrictions depend on missing live/configuration evidence; continue independent implementation without guessing them.

Read-only installed package follow-up (2026-11-03): streamed SF50/H125 effective configuration is archived; no readable seat/station definitions obtained. No archive protections bypassed or profiles guessed. Two-specialist Windhoek offers are requested counts, not capacity claims. Physical boarding stays blocked, native seated models unsupported. [Implementation/live procedure](../offshore-missions.md).
