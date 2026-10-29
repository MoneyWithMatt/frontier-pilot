# Offshore career — implementation and simulator assessment

Updated 2026-10-29. Scope/authority: [feature specification](specs/offshore-career.md). Resume from [handoff](handoffs/offshore-career.md).

## Current implementation

Skeleton Coast Petroleum employs the pilot at FYWB with Vision Jet cargo access and $800 per verified airborne hour. H125 is listed but disabled. The shared company generator uses three existing prototype equipment/document cargo categories, a four-destination board limit and 120 kt planning bias. It feeds ordinary reservation, physical cargo coordination, tracker proof and ledger settlement; there is no separate Offshore mission engine.

The [published 2026-10-16 lookup](../evidence/airport-packet-padding/verification.md) found all eight Offshore memberships, including FYWB: seven possible destinations before board selection/review exclusions. Current availability depends on the user's completed cache and reviews. Earlier manual-FYWB-inspection blockers are superseded by automatic airport resolution. Airport database presence does not establish scenery or Vision Jet runway suitability.

This slice enforces authored aircraft restrictions at both base and destination. One enabled company type must satisfy both. A later exclusion disables new work while retaining reservations/history. Job details now estimate the actual origin-to-destination distance, fixing Offshore's previous Eros-based planning time. No persistence migration, pay/budget change or flight-rule change.

## Official SDK capability assessment

Research checked against Microsoft's public MSFS 2024 SDK documentation on 2026-10-29. These are documented capabilities, not new empirical qualification of the installed SDK 1.2.4 or any aircraft.

| Requirement | Documented capability | Frontier gap / decision |
|---|---|---|
| Worldwide airport discovery | Non-EX1 airport facility list covers the world, with multi-packet replies [1] | Existing packet-complete reader/cache already implements this; live published lookup exists |
| Static helipad location/geometry | Airport facility definitions expose helipad count, child coordinates/elevation, dimensions, heading and surface [2] | Existing catalogue reads airport reference points only. A separately validated detail adapter is needed; a facility record does not prove usable collision/landing behavior |
| Elevated-deck proximity | Aircraft variables expose altitude above surfaces/obstacles [3] | Current airport-radius arrival cannot identify a particular deck. Deck-specific horizontal/vertical/contact evidence needs empirical tests before authority |
| Vessel-relative stopping | Surface-relative ground speed is documented in ft/s, unlike earth-relative ground velocity in knots [3] | Zero can also occur while hovering over a moving vessel. It cannot by itself prove touchdown. Moving-platform missions need a different evidence design and are not enabled |
| Helipad flight starts | Airport XML documents helipads and separate HELIPAD start positions [4] | No new scenery/start package is generated. Package Tool/live installation requires separate approval and ownership safeguards |
| H125 payload/identity | Existing Frontier loading uses explicit identity and station/gross readback, independently of type selection | H125 title/model mapping, actual station layout, retained weights, board/exit behavior and platform loading remain unqualified |
| Area survey / inspection | Existing WGS84 telemetry can support geometric calculations | Zone authoring, duration/altitude/speed rules, return requirement, pause/disconnect handling and payout are gameplay decisions; airport delivery proof cannot substitute |
| Passenger transfer | No passenger domain lifecycle in Frontier | Requires a separately specified model. Existing cargo inventory must not stand in for people |

Inference: start with stationary, explicitly identified facilities if platform gameplay is selected; moving vessels introduce additional identity/relative-motion problems. This is a proposed sequencing decision, not authorization to create or populate platforms.

1. [SimConnect_RequestFacilitesList](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Facilities/SimConnect_RequestFacilitesList.htm).
2. [SimConnect_AddToFacilityDefinition — AIRPORT / HELIPAD](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/facilities/simconnect_addtofacilitydefinition/).
3. [Aircraft Miscellaneous Variables](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/).
4. [Airport XML Properties — Helipad / Start](https://docs.flightsimulator.com/msfs2024/flighting/content-configuration/environment/airports-and-facilities/airport-xml-properties/).

## Staged development tasks

1. Airport-cargo baseline: implemented fixes plus seven synthetic regressions; local full suite 751 passed. Hosted CI/evidence status is maintained in the handoff.
2. Mission choice/specification: pending Matt's choice of platform cargo, survey/inspection or passenger transfers. Specify only that slice; do not reinterpret the old illustrative 30-minute loiter as a requirement.
3. Adapter/recording work: implement only observations needed by the selected mission; keep optional failures isolated from current aircraft/avatar tracking. Detail packets require strict framing/completeness tests. Recorded diagnostics cannot qualify deliveries.
4. Live qualification: prepare an exact scenario with simulator/SDK hashes, aircraft identity, endpoint evidence and read/write scope. Obtain approval for the changed artifact and live scenario. Static deck and H125 compatibility must be demonstrated before activation.
5. Mission implementation: add authoritative state, restart/reconciliation, domain tests and UI through existing project boundaries. Compensation and failure/cancellation rules need an approved spec. Preserve tracker-only delivery evidence.
6. Acceptance: hosted Windows regression/WPF/artifact checks, explicit manual flight checks and human PR merge review. Synthetic tests cannot close live acceptance tasks.

## Candidate live checks (not yet authorized or executed)

For existing Vision Jet cargo: resolved FYWB and chosen airport; explicit exact aircraft mapping; origin load while on foot and boarded station/gross readback; flight, partial/full destination unload, one wage/history entry and restart. Use an approved isolated data root/scenario; never reset a personal career for testing. General isolated live-test data-root work is still deferred.

For a future H125/static platform slice: approve one actual facility and precise deck, record stationary aircraft and low hover separately, establish on-deck identity/position/contact behavior, walk/board transitions where supported, payload retention, disconnect/restart and rejection of nearby water/ground/another deck. Keep H125 disabled until evidence and gameplay authority are complete.
