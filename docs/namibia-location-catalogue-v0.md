# Namibia Location Catalogue V0

## Authored source

The embedded schema-1 catalogue is derived from Google Drive document **Frontier Pilot — Jobs & Career System Design Notes**, section **20. Namibia Starting Customer Network — Research Shortlist V1**, document ID `17I6WG4KdUoUWDQVb7txi3O1zUipG0B4raJQVAHdgbHU`, revision recorded inside the catalogue. It is versioned independently as catalogue `0.1.0`.

The catalogue contains Windhoek Eros plus all 30 section 20 destinations: ten mining/mineral candidates, ten small-community candidates and ten safari/wilderness candidates. Each record owns a stable `namibia.*` ID, display name, category, identifier and confidence, optional coordinates, approximate direct-flight minutes, economic role, proposed jobs, research notes/sources, MSFS verification state, customer activation state and first-playable recommendation flag.

The authored JSON is embedded from `src/Core/Catalogues/namibia-locations-v0.json`. It is read-only application data and is not copied into `economy-state.json`. Mutable economy records refer to its stable IDs through `EconomicLocation.Id`.

## Status and uncertainty

Eros/FYWE is the sole locked gateway. `ConfirmedIcao` describes confidence in its real-world identifier; Eros remains `ResearchCandidate` for simulator verification and is not a destination customer.

Identifiers that section 20 explicitly calls “reported identifier” are `ReportedUnverified`: FYKO, FYIB, FYSP, FYKD, FYOK, FYGI, FYSU and FYRZ. Other destination identifiers are `PublishedUnverified`; the document presents them without the reported qualifier, but that is insufficient to claim MSFS verification or confirmed contemporary ICAO status.

Section 20 contains no numeric coordinate values and no tables. It names OurAirports Namibia data as a research source, but this implementation does not fetch or silently infer those coordinates. Every `approximateCoordinates` value is therefore `null` until a later research/verification task supplies traceable values.

All destinations remain `ResearchCandidate`. Uis, Arandis, Aminuis, Leonardville, Mount Etjo and Okonjima are `Inactive` and flagged recommended. The other 24 destinations are `DormantCandidate`. No destination is `ActiveCustomer`; recommendation is not verification.

## Validation and activation policy

Loading rejects wrong schema/catalogue identity, missing entries, duplicate stable IDs, duplicate airport identifiers, malformed values, invalid coordinates, inconsistent gateway/customer status and invalid verification/activation combinations.

Verification and activation are separate:

1. `MarkMsfsVerified` requires a confirmed identifier and supplied coordinates and leaves a destination inactive.
2. `ActivateCustomer` refuses a gateway or a destination that has not first been verified.
3. `NamibiaJobGenerationGuard.RequireEligibleDestination` refuses ordinary live job generation unless both states are present.

The guard is the explicit boundary for the future live job generator. Economy Core's clearly labelled prototype fixtures remain separate and do not bypass this rule for Namibia jobs.

## Scope

No Namibia job, inventory, balance or shipment is generated. No WPF UI, SimConnect flight lifecycle, airport verification, weather logic or aircraft-suitability rule was added. Approximate route times remain scale notes based on the document's 120-knot assumption and are not safety guidance.
