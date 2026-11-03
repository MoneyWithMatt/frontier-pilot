# Namibia exploratory careers and airport visits

## Research integration

This branch builds on Career Hub `21b1f78`, which already contains the unmerged Offshore/passenger/fleet/loading work. It does not merge or overwrite the existing PR branches or master.

The 57-record embedded `CuratedNamibiaDestinations.json` is a projection of [PR #4's dated catalogue](https://github.com/MoneyWithMatt/frontier-pilot/blob/bb9dd2cce9701d173f86482efd310ad8a5bb64c4/docs/research/starter-destinations/catalogue.json), original Git blob `d76fd3d9cd1ccd93de4c7189cd0beaf58ecf2bd2`, 2026-10-29. It retains name/reference, published coordinates, source URL/reference, runway caution, notes, career associations and shortlist priorities. Namibia airport records are selected; Cape Town, custom clinic areas and mobile/offshore destinations are excluded. Existing 53 memberships are retained and enriched by stable legacy aliases; eligible new community/lodge memberships are added. No private simulator cache or personal telemetry is copied.

Source coordinates are dated research references, not freshly verified airport or runway positions. Per-place OurAirports URLs and PR #4 operator/research notes retain provenance. Published database keys such as Leonardville NA-0114 and Dordabis NA-0014 are shown as references with unconfirmed simulator identifiers. They are not converted into fictional ICAO codes. Known manual Eros/Uis/Arandis inspection records remain separate.

## Eligibility and variety

`EmployerDestinationCatalogue` resolves a route reference independently of eligibility. Unverified published/facility references stay ResearchCandidate, with no fabricated MsfsVerificationEvidence. `CompanyEmployment` explicitly gives eligible exploratory economic locations the new ExploratoryAirport status, allowing the existing recurring-demand engine to serve them. The legacy authored-customer lifecycle is unchanged; generic research candidates are not globally activated.

Six cargo destinations are selected per employer. Authored starter priorities include Missionary Aminuis/Leonardville/Dordabis, Industrial Uis/Arandis, Safari Okonjima/Mount Etjo/Solitaire/Mokuti/Doro Nawas and existing Offshore airport work plus Windhoek/Lüderitz/Oranjemund. Other suitable references rotate deterministically using the existing persisted three-hour offer cadence. Refresh/restart within a cadence does not reroll the pool. Existing offer expiry, resources, cargo quantities and inventory rules remain authoritative. Accepted assignments are independent of subsequent board rotation.

Personnel uses the existing shared Missionary doctor/community visit and Offshore personnel policies, with four generated routes and all eligible routes available to validate existing reservations. No medical facility is invented or certified. Safari's present aircraft lack supported passenger profiles, so this slice supplies cargo there. Vision Jet physical boarding remains blocked; the Caravan requires the existing exact reviewed adapter/configuration. H125 and platform operations remain disabled. Offshore observation flight/loiter/return/payment rules are unchanged; a problem/held base prevents new acceptance. Experimental H125 diagnostics still require their previous technical base metadata, not merely a published point/player visit.

## Held data and immutable routes

The FYKW/Okangwati, FYHV/Hartmann, FYHO/Hoanib, FYNG/Ongava and FYAD/Aandster geographic conflicts remain Held even if a player marks a visit verified. Geluk/Witwater ambiguity, Nepara's unresolved current field identity, Rosh Pinah closure/identity concerns and unlocated private-strip references also remain unavailable. Missing runway dimensions/performance data alone does not impose an application loading/performance refusal.

Absent facility-list lookup alone does not reject a plausible sourced airstrip. Ambiguous identifiers, changed saved coordinates and gross (>5 NM) research/facility geographic disagreement are technical holds. Cache schema 2 retains such conflicts through later scans, including an empty scan, rather than silently clearing them. This is identity consistency, not a runway/performance limit. Resolving a Held association requires a separately reviewed reference reconciliation; neither a new lookup nor a player usability report silently moves accepted routes.

Original approved/cache coordinates and stable IDs continue to resolve accepted jobs/history. Player visit recording never edits them. Cargo flight-leg and passenger endpoint snapshots remain authoritative. The separate Core airport-authoring geometry/capture gates are untouched.

## Player reports

Normal exploratory missions never require airport verification before acceptance or flight. After successful cargo delivery or passenger disembarkation **and committed settlement**, Career shows a small optional prompt: **Yes — Verify airport**, **Report a problem**, **Skip for now**. Arrival proof or partial delivery alone is insufficient. Wages are already settled and cannot depend on the answer. Skip records one airport acknowledgement, rather than a verification visit, and suppresses further automatic interruptions for this airport across restart. Manual review remains available after completed work. Already verified airports do not prompt.

Career → Job Board → select an airport job → **Airport details / Report a simulator problem** is available before completion. Report a missing airport, incorrect location, absent/broken runway or landing area, or scenery/simulator malfunction. Supply a short note/category; aircraft and simulator/scenery version are context only. No aircraft-suitability category is offered. Manual verification is available after completed, settled work at that airport. Repeated positive answers do not add duplicate reviews.

Verification applies to airport representation across all aircraft. Frontier does not decide runway suitability from length, width, surface, terrain or performance. Historical compatible-aircraft lists at either endpoint are metadata, not dispatch filters. Legacy AircraftUnsuitable reports remain readable but never withhold jobs. Genuine unresolved simulator/airport problems withhold future jobs; a later good representation report can clear them regardless of aircraft. Technical identity/coordinate holds and company/aircraft/loading qualifications remain independent.

`airport-visits.json` schema 2 reads schema 1 without rewriting on load, retaining every observation and aircraft field. It adds bounded, idempotent per-airport prompt dismissals. The same mutex, atomic replacement, previous-file backup, corruption/future-file preservation and separate review authority remain. No economy schema change. Development never modifies personal reviews or saves.

Accepted/reserved/in-transit jobs remain after a report. No cargo/personnel disappears, no flight facts are replaced and no incomplete flight earns pay. Persisted partial passenger boarding can still use its guarded retry. No new cancellation, refund, reroute or abandonment mechanism is added; preserve impossible assignments and report the airport. Dedicated disposition and versioned route repair remain future work.

## Persistence and verification

Economy schema 15 reads prior schemas and preserves jobs, inventory, ledger, flights and aircraft ownership. It appends an explicit location status rather than mislabelling exploratory airports MSFS-verified. Cache schema 2 reads schema 1; player visits use schema 2 with schema-1 compatibility. All development tests use temporary or memory stores, not Matt's saves.

`NamibiaPlaytestingTests` covers all four pools, source identities/coordinates, held cases, deterministic variety/cadence, airport-level reports with aircraft context, restart/reset/retries, problematic-offer exclusion and preserved accepted cargo/money, technical conflicts and schema-14 migration. Existing cargo, passenger, wage and flight regressions remain required. `NamibiaAirportReviewSmoke` drives actual reserve/review/problem buttons in the Career Hub and captures synthetic screenshots. Hosted WPF and packaging are separate from real simulator acceptance. Exact results are maintained in the [handoff](handoffs/namibia-playtesting.md).

## Matt's playtest

1. Use the exact development artifact identified in the handoff. Development has not installed or launched it on your PC or changed personal saves. A normal launch uses the existing app-data career and can apply versioned save upgrades. Keep a backup for the first opt-in playtest; a unified isolated-data-root launcher is not implemented.
2. Choose a Namibia career (existing active-employer switching remains unavailable). Open Career → Job Board. Inspect several routes, distance, aircraft eligibility, manifest and destination caution.
3. Accept an exploratory airport job, load through the existing Fleet/Load Manager workflow and fly using normal tracking. Landing/unloading/disembarkation and pay still require existing proof/readback.
4. After mission completion and settlement, answer or skip the optional first-visit prompt. Report missing/broken fields manually even if the mission cannot be completed; this cannot fabricate completion or pay.
5. Check that new offers to a problematic field disappear, existing accepted work/history remains, and knowledge survives restart. Airport representation knowledge applies across aircraft; physical loading and aircraft qualification are unchanged.
6. Report any incorrect identity/coordinate association. Do not treat a facility-list hit, the map or these automated tests as proof of runway usability.

## Automated release checkpoint

Implementation `1939202748d682841d0295bd917293ace4f882a9` passed [Windows CI](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41702499937): 1,016 xUnit tests, 208 site/Career and 7 loading WPF assertions, 9 guard scenarios, historical CLI review and Windows packaging. [Download exact Windows bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41702499937/artifacts/13263234488). Screenshots and receipts remain synthetic evidence; real airport usability is Matt's playtest.

## Job Board refresh and aircraft selector

Changing the eligible company aircraft type in the Job Board does not select or transfer a physical aircraft. Repeated WPF binding writes must be idempotent. The selector repair preserves the current mission/list and existing fleet/loading checks.

Refresh work reloads current work; the existing persisted schedule generates SYSTEM offers every three real hours, with six-hour unaccepted expiry. The schedule and next update appear above the job list. New offers are shown first; still-valid older offers remain available. Accepted/loaded work never expires through board refresh. Immediate manual replacement of unaccepted offers has not been enabled without a separate explicit policy decision.