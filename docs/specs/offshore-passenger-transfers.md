# Feature: OFFSHORE-PASSENGERS — airport specialist transfers

Status: Approved.
Approved by / date: Matt's 2026-10-29 follow-up selecting Offshore passenger transfers, endpoint aircraft eligibility, existing hourly pay/recovery, duplicate prevention and live-gated platforms. H125 cargo and survey/inspection are deferred.

## Objective
Provide the smallest complete passenger transfer loop for Skeleton Coast Petroleum, with separate passenger state and no cargo-inventory substitution.

## Gameplay behavior
The Passenger transfers page offers FYWB-to-eligible-airport specialist transfers alongside existing cargo work. Each transfer initially carries **one prototype passenger**, with one simulated passenger dispatch seat on the already enabled Vision Jet. This is a bounded fixture, not aircraft-certified seating capacity. It does not reduce or reinterpret the 40 kg cargo dispatch fixture.

Reserve with an eligible company aircraft, stop at the origin for ten continuous seconds, then explicitly board. Confirm the exact live TITLE/model mapping and select/bind the individual airframe using existing fleet controls. The page explicitly states that boarding is simulated: there are no native passenger animations, seat/payload writes or luggage accounting in this slice.

Fly the existing airport flight-evidence rules: same aircraft, airborne observation, later meaningful movement, route-adaptive maximum distance and ten stopped seconds at the destination. Explicit disembarkation completes the whole passenger assignment and pays $800 per contiguous verified airborne hour once. No per-person multiplier, ground/duty pay, tips or payout before completion.

Only one passenger assignment can be aboard; passenger and cargo loads cannot share a flight in this minimal slice. Multiple reservations remain possible. Pending physical cargo recovery must be resolved before passenger boarding. Finishing one transfer permits boarding another; there is no new cancellation/forfeiture design.

Offer generation uses the existing persisted UTC economy cadence and lifetime (defaults three/six hours), one deterministic passenger offer per selected route per cadence, capped by the existing offer-slot setting. At most the same four time-biased Offshore destinations are used. Refresh/restart cannot refill a completed assignment in the same cadence. Accepted/boarded passengers never expire.

## Acceptance criteria
- Both origin and destination must permit the enabled company aircraft, at generation, reservation and boarding. Accepted route snapshots never move after later catalogue changes.
- Passenger counts, reservations, boarded state, proof and completion persist separately from cargo jobs/shipments/resources/inventory.
- Exact type mapping and the shared fleet dispatch guards apply before boarding. Boarded physical airframe GUID remains explicit; changing selected airframe pauses progression.
- Interrupted/invalid/stale/nonadvancing telemetry resets transient qualification and wage continuity, retains passengers/facts and excludes disconnected/restart time. Existing airport movement/radius thresholds remain unchanged.
- Restart after qualified arrival retains proof but still needs fresh stopped destination telemetry to disembark. Retry/concurrency/failed writes cannot duplicate delivery or wage.
- Completion, flight terminal state, one existing PilotWage ledger receipt and cached balance commit in one economy replacement.
- Platforms are unavailable: the typed endpoint gate refuses Platform for generation and persisted missions. No inferred airport-radius deck acceptance, H125 enablement or guessed platform is allowed.

## Relevant existing systems
`FlightAttemptTracker` owns the passenger monitor; `FlightAttempt`, `FlightDeliveryProof`, identity/distance policy and contiguous wage/proof helpers are reused. `PilotWage`, the authoritative ledger, `LocalEconomyStateStore`, employer reviews/type mapping, shared fleet guard and existing UTC schedule are reused. Existing cargo loading/recovery has a passenger preflight guard before new/restored payload writes.

## Persistence implications
Economy schema 11 adds default-empty `PassengerTransfers`. Old worlds migrate without inventing people, changing cargo/inventory or recomputing money. Passenger flight attempts are embedded in their typed transfer and do not enter the cargo attempt file. Endpoint/type/rate/airframe snapshots and completed history persist. Career reset already archives the economy file, so its scope includes passengers. Corrupt/future/inconsistent files are preserved. No fleet/loading/attempt schema changes.

## Required tests
Lifecycle/hourly pay, endpoint/type exclusions, freshness/mapping/airframe guards, disconnected/invalid/duplicate/gapped/wrong-aircraft recovery, partial arrival timers across restart, persisted proof, failed atomic completion, concurrent/repeated commands, offer cadence/expiry and protected reservations, no cargo mutation, cargo/passenger exclusion (including pending/authorized/restored loads), old-schema/corrupt/future preservation, tamper validation and platform rejection. Hosted WPF must drive the actual passenger controls.

## Simulator testing requirements
Automated tests use supplied synthetic telemetry and isolated stores. They do not establish live passenger experience, platform geometry/contact or aircraft payload compatibility. Follow [the live acceptance procedure](../offshore-passenger-transfers.md) only after separate artifact/scenario approval. Platform qualification must succeed before a reviewed adapter/registry enables any platform mission.

## Open design decisions
None block this airport transfer slice. Multi-passenger capacity/mass, cabin/NPC integration, mixed cargo/passenger flights, cancellation, moving vessels and platform completion rules require later design/qualification. H125 platform cargo and surveys are explicitly deferred.
