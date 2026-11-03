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

Career → Job Board → select an airport transport job → **Visited this airport? Record your findings**.

Choose the aircraft actually tested, describe it, optionally enter MSFS/scenery version and add a note. **Mark as Verified** records a player assertion for that aircraft. **Report a Problem** requires a short note and one of missing airport, wrong location, unusable runway, scenery/terrain, unsuitable aircraft or other. No repeated confirmation is required when selecting or accepting unverified work.

`airport-visits.json` schema 1 is separate from economy, official/research data and technical reviews. It records identity/reference/route coordinates as context, tested type/description, selected airframe context where applicable, optional simulator version, UTC date, result/category and note. It is not recorded flight evidence. Observations append with idempotent IDs, mutex/atomic replacement and a previous-file backup; corrupt/future/unsafe files are preserved. Career reset retains this knowledge. A successful report immediately updates the shared catalogue and refreshes the existing job services; it does not require re-entering a mission or restarting the app.

Verified in one aircraft does not verify every aircraft. An unresolved problem report withholds new jobs for the location by default. A later explicit good visit can clear a general player-reported problem for further exploration; an Aircraft unsuitable report requires a later good report in that same aircraft type, so a C152 visit cannot clear a Caravan problem. Other aircraft remain unverified, and technical holds/aircraft configurations remain independent. Old reports remain in history.

Accepted/reserved/in-transit jobs are retained after a report; no cargo/personnel disappears, no flight facts are replaced and no incomplete flight earns pay. Existing endpoint/current-aircraft/loading/recovery guards remain active. A previously persisted partial passenger boarding intent can still use its original guarded retry; a new-job eligibility filter cannot strand existing owned payload. If a bad destination makes a job impossible, there is no new cancellation, refund, reroute or abandonment mechanism in this slice. Preserve the assignment and report the issue; use existing recovery controls where applicable. Dedicated disposition and versioned route repair need later design.

## Persistence and verification

Economy schema 15 reads prior schemas and preserves jobs, inventory, ledger, flights and aircraft ownership. It appends an explicit location status rather than mislabelling exploratory airports MSFS-verified. Cache schema 2 reads schema 1; player visits have their own schema 1. All development tests use temporary or memory stores, not Matt's saves.

`NamibiaPlaytestingTests` covers all four pools, source identities/coordinates, held cases, deterministic variety/cadence, per-aircraft reports, restart/reset/retries, problematic-offer exclusion and preserved accepted cargo/money, technical conflicts and schema-14 migration. Existing cargo, passenger, wage and flight regressions remain required. `NamibiaAirportReviewSmoke` drives actual reserve/review/problem buttons in the Career Hub and captures synthetic screenshots. Hosted WPF and packaging are separate from real simulator acceptance. Exact results are maintained in the [handoff](handoffs/namibia-playtesting.md).

## Matt's playtest

1. Use the exact development artifact identified in the handoff. Development has not installed or launched it on your PC or changed personal saves. Prefer an isolated copy/profile for the first test; normal runtime save upgrades are versioned.
2. Choose a Namibia career (existing active-employer switching remains unavailable). Open Career → Job Board. Inspect several routes, distance, aircraft eligibility, manifest and destination caution.
3. Accept an exploratory airport job, load through the existing Fleet/Load Manager workflow and fly using normal tracking. Landing/unloading/disembarkation and pay still require existing proof/readback.
4. After visiting, record the actual tested aircraft, simulator/scenery version and findings using the inline form. For a missing/bad field, report it even if the mission cannot be completed; this does not fabricate completion.
5. Check that new offers to a problematic field disappear, existing accepted work/history remains, and knowledge survives restart. Try another aircraft: the first aircraft's verification must not appear as qualification for it.
6. Report any incorrect identity/coordinate association. Do not treat a facility-list hit, the map or these automated tests as proof of runway usability.
