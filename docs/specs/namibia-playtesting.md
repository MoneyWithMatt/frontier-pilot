# Feature: Namibia destination playtesting

Status: Approved.
Approved by / date: Matt's “Activate Namibia Career Destinations for Playtesting” request, 2026-11-03.

## Objective

Make all existing Namibia starter careers usable now, with broad curated airport choices and player-led reports. Individual live airport acceptance is no longer a prerequisite for exploratory airport offers.

## Gameplay behavior

Keep suitability, technical identity and player verification separate. Import sourced Namibia airport references from PR #4; retain all original memberships/stable keys. Plausible, located airports may offer unverified work. Held identities and coordinate/closure conflicts cannot. A facility-list match is not runway acceptance.

Use existing employer demand, cargo resources/quantities, personnel policies, physical aircraft, loaders, flight proof and wages. Offer six cargo destinations with authored starter priorities and seeded variety, on the persisted offer clock. Personnel generation uses four routes through the existing Missionary/Offshore adapter. Accepted routes remain eligible independently of rotation. No Safari passenger capability, SF50 loading or H125/platform permission is invented.

Career → Job Board displays destination knowledge and cautions. An inline review form records a visit for a named aircraft and optional simulator/scenery version. Mark as Verified is an explicit player assertion, not technical qualification. Report a Problem requires a category/note and withholds new offers by default. Reports cannot move route coordinates or complete/pay/cancel missions. Accepted cargo, passenger ownership and prior wages remain intact.

## Acceptance criteria

- Each career offers varied work without personal inspection records or a simulator lookup.
- Aminuis, Leonardville and Dordabis are available Missionary references; researched lodge/industry/airport pools remain career-appropriate.
- FYKW/FYHV/FYHO/FYNG/FYAD, Geluk/Witwater and other unresolved identities remain Held.
- OurAirports NA-xxxx keys remain published references, never invented ICAO identifiers.
- Per-aircraft reports survive restart/reset; problem reports preserve accepted work and financial history.
- Existing loading, flight proof, once-only settlement, hourly rates and recovery tests pass.
- Actual WPF controls and Windows packaging pass hosted CI; no automated test claims airport usability.

## Non-goals

No live airport acceptance, scenery installation, speculative clinics/airstrips, passenger aircraft qualification, platform activation, new fares, cancellation/re-routing economy or company-management system.

## Relevant existing systems

`EmployerDestinationCatalogue`, PR #4 research, `CompanyEmployment`, `EconomyJobBoard`, `EmployedCargoJobsService`, `PassengerTransfersService`, Career Hub, shared loading and `FlightAttemptTracker`. See [implementation and evidence boundaries](../namibia-playtesting.md).

## Persistence implications

Economy schema 15 appends an explicit ExploratoryAirport economic status; older saves retain their jobs/inventory/ledger/ownership. Simulator cache schema 2 retains technical conflicts and reads schema 1. Separate `airport-visits.json` schema 1 stores append-only player reports; corrupt/future files are preserved. Existing reviewed/cache route coordinates are not rewritten. Development uses temporary/synthetic stores only.

## Required tests

Expanded pools/associations, dated source coordinates, held identities, seeded rotation on the persisted clock, per-aircraft visits/restart/reset/retries, problematic-offer exclusion and accepted state preservation, lookup conflicts, schema upgrades, existing flight/loading/wage regression, actual review buttons and map knowledge display.

## Simulator testing requirements

Matt will assess real fields while playing. Synthetic tests prove software behavior, not that a runway exists or suits a particular aircraft. New executable/scenario approval is required before any autonomous local live test. No personal saves are used by development automation.

## Open design decisions

None blocking this slice. Explicit disposition/cancellation and versioned route-coordinate repair remain separate future work; incomplete flights are not paid.
