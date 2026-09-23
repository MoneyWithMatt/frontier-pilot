# Prospecting Camp Vertical Slice V0

## Boundary and terminology

This slice joins four existing systems without turning them into a full settlement game. `EconomicBusiness` remains the aviation employer/funding concept. `EconomicOperation` is a configured producer/consumer. `EconomicSite` is the small durable link between that operation and a saved Frontier Pilot airport definition. The site stores the airport definition's stable GUID and enough immutable founding/location metadata to keep the economy diagnosable; it does not copy the editable airport definition.

`OperationArchetypeCatalogue` contains the single `operation-archetype.prospecting-camp-v0` data definition. Its storage, input/output process, logistics quantities, payments and required `BaseLodge` construction capability are data. The tick and job-maintenance kernels execute that definition without mining-category branches.

All V0 values are temporary test fixtures: 150 TPC founding; 70 kg ration capacity and opening stock; 10 kg rations per explicit day; 140 kg raw-mineral capacity; 20 kg output per fully supplied day; 40 kg inbound/outbound shipments; 80 TPC frontier-support inbound payment; and 160 TPC combined outbound mineral-sale proceeds.

## Founding and presence

The Operations page lists only saved Frontier Pilot site definitions. Its service reloads the selected definition and derives its capabilities from `ConstructionObjectCatalogue`; display text is never inspected. The current `prototype-base-lodge-v0` field-cabin placeholder resolves through the catalogue to the stable `BaseLodge` capability.

`GuardedAvatarSitePresence` is the sole production constructor of `VerifiedSitePresenceEvidence`. It accepts only a valid `TrackingWorldState` in `OnFoot` mode whose current object is the explicit avatar, whose avatar differs from the aircraft, is on ground, is no more than three seconds old, and lies within the configured 250 m prototype radius of the saved airport anchor. Aircraft, camera, disconnected, last-known and refused avatar positions cannot produce evidence. The full evidence, object ID, coordinates, timestamps, radius, distance, provider and MSFS integration details are stored with the founded site.

The single atomic economy mutation revalidates the saved site snapshot, `BaseLodge` capability, presence proof and authoritative ledger balance. A runway is optional infrastructure on the same operation site. Before it is built, the site has no normal MSFS spawn and requires bush access; adding a runway later adds runway-start spawning, and adding parking provides a parking spawn. The mutation creates deterministic site/location/operation IDs, adds Rations and Raw Minerals, establishes audited opening rations, records the 150 TPC debit, updates the ledger-derived cached balance and runs idempotent logistics maintenance. Repeating founding for the same site returns the existing world and cannot charge again. A failure before atomic replacement leaves the previous complete aggregate.

## Prototype day

**Advance one prototype day** is the only clock. Each caller supplies a unique command ID. A persisted `OperationTickRecord` makes replay idempotent and advances the day exactly once. A full 10 kg ration batch is required; insufficient rations consume nothing and produce nothing. A supplied tick produces up to 20 kg of Raw Minerals, capped by the remaining 140 kg storage. Consumption and production each receive immutable inventory-audit records, and the tick stores the factual normal, supply-constrained or storage-constrained result. Refresh and restart never advance time.

For schema-3 worlds containing sites, every cached inventory position must equal its authoritative inventory-audit sum. The financial ledger remains authoritative for player balance.

## Logistics and flight lifecycle

Operation maintenance is part of the same idempotent board-maintenance path used by employed cargo. At 30 kg rations or below it creates at most one outstanding Eros-to-camp ration requirement and introduces only the exact required Eros stock through audited System Gateway Supply. At 40 kg unreserved Raw Minerals or more it creates at most one outstanding camp-to-Eros shipment. The original global one-active-shipment rule was superseded on 2026-09-21: acceptance now reserves jobs, and only explicitly selected same-route shipments share an aircraft load and flight.

The Flight Attempt thresholds and evidence rules are unchanged: 1.5 NM origin/destination radius, at most 5 kt for ten continuous seconds, trustworthy aircraft identity, persisted airborne observation, post-departure movement, 10 NM maximum origin distance and explicit unload. Coordinate resolution now accepts either an authored Namibia catalogue location or the persisted anchor of a player-created economic site. It no longer assumes Eros is every job origin. `FlightAttemptTracker` remains the only delivery-evidence producer.

Inbound delivery atomically adds Rations and one 80 TPC payment. Outbound acceptance reserves exactly 40 kg Raw Minerals; verified delivery transfers it to Eros gateway inventory and records one 160 TPC combined credit. This combined credit intentionally does not separate sale proceeds, operation revenue, pilot wage or transport cost.

## Persistence and deletion

Economy persistence schema 3 adds `Sites`, `Operations` and `OperationTicks`, plus optional site/operation audit references. Schema 1 and 2 documents migrate by atomic replacement. Missing legacy inventory-audit opening amounts are reconstructed explicitly; negative inconsistencies are refused. Existing locations, jobs, shipments, delivery evidence, flight attempts and financial transactions are preserved. Corrupt, inconsistent and future-schema documents remain untouched.

Airport deletion consults the current economy aggregate before preview and again before committing removal. A linked definition is refused with an explanation. Missing or unreadable linked airport definitions leave their economic sites intact and visible as requiring attention. Abandonment, relocation and demolition are deliberately absent.

## Manual MSFS test

1. Open **My Sites**, choose **Create new site**, capture the walking-avatar survey, add **Small Base Lodge**, then save, build and install the operation site. Leave **Build runway now** clear to establish the operation before constructing an airfield; the site will not yet appear as an MSFS airport or spawn. Adding a parking stand later also enables the runway/airport infrastructure required for a real MSFS parking spawn.
2. Complete at least one employed cargo delivery so the ledger-derived balance is 150 TPC or more. The already accepted Arandis delivery provides 200 TPC.
3. Load MSFS at the created airport. Open Frontier Pilot **Tracking**, confirm LIVE tracking, exit the aircraft and stand within 250 m of the airport anchor.
4. Open **Operations**, wait for tracking to connect, and click **Refresh operations**. Select the airport. Confirm the founding check mentions a fresh guarded walking avatar and enables **Found prospecting camp**.
5. Found it once. Confirm the balance falls by exactly 150 TPC, day is 0, Rations are 70/70 kg and Raw Minerals are 0/140 kg. Close and reopen Frontier Pilot; confirm the same camp returns without another debit.
6. Advance four prototype days. Confirm day 4, Rations 30/70 kg and Raw Minerals 80/140 kg. Refresh Jobs and confirm one ration resupply and one mineral export appear alongside the Eros employment offers.
7. At the camp, accept a 40 kg Raw Minerals job to Eros. Use the unchanged Load/flight/arrival/Unload flow. Confirm Eros receives 40 kg and the ledger records one 160 TPC credit.
8. At Eros, accept the 40 kg Rations job to the camp. Fly and explicitly unload. Confirm camp Rations return to 70 kg and one 80 TPC payment is recorded.
9. Restart Frontier Pilot and refresh both pages. Confirm the camp day/inventories, completed jobs and balance persist, and no duplicate open requirements or payments appear.
10. In My Airports, attempt to delete the linked test airport. Confirm deletion is refused because the economic operation depends on it.

The automated suite proves deterministic state behavior only. The dynamic camp-origin route, site-presence timing and both operation deliveries still require this live MSFS acceptance.
