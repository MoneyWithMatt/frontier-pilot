# Namibia Location Catalogue V0

## Authored source and version

The embedded schema-2 catalogue is derived from Google Drive document **Frontier Pilot — Jobs & Career System Design Notes**, section **20. Namibia Starting Customer Network — Research Shortlist V1**, document ID `17I6WG4KdUoUWDQVb7txi3O1zUipG0B4raJQVAHdgbHU`. It is versioned independently as catalogue `0.2.0` in `src/Core/Catalogues/namibia-locations-v0.json` and remains read-only application data outside `economy-state.json`.

It contains Eros and all thirty section 20 candidates. Stable `namibia.*` IDs are the references used by mutable economy state. Identifier confidence, recommendation, MSFS verification, activation, authored economic role and proposed work remain separate concepts.

## First verified and activated records

Matt manually inspected Eros/FYWE, Uis/FYUS and Arandis/FYAR in MSFS 2024 on 2026-09-09 and confirmed that each identifier exists and that its scenery can serve the intended prototype role. Each record stores `ManualInSimulatorInspection` evidence and notes the human source. Eros remains the sole locked gateway and is not a destination customer. Uis and Arandis are the only `ActiveCustomer` records.

The three numeric positions come from the published OurAirports catalogue: Eros -22.604920, 17.079002; Uis -21.230032, 14.866888; Arandis -22.461875, 14.979322. Each record stores publisher, URL, retrieval date and a `PublishedCatalogue` source kind. These values are deliberately distinct from any future `SimulatorDerived` validation coordinates. Simulator inspection did not silently become coordinate provenance.

The other 28 locations retain null coordinates, research-candidate status and their earlier identifier confidence. The recommended subset remains Eros, Uis, Arandis, Aminuis, Leonardville, Mount Etjo and Okonjima; recommendation alone still grants neither verification nor activation.

## Validation and job boundary

Loading rejects missing or mismatched coordinate provenance, malformed source/evidence metadata, duplicate locations or identifiers and inconsistent lifecycle combinations. `MsfsVerified` requires a confirmed identifier, sourced coordinates and explicit verification evidence. `ActiveCustomer` requires prior verification.

`NamibiaJobGenerationGuard` remains the mandatory ordinary live-job boundary. Employed Cargo Jobs V0 creates offers only for records that are both verified and active. Route-time notes assume 120 knots direct from Eros and never claim runway, route, weather or aircraft suitability.

## Scope

Only Uis and Arandis are activated for the focused employed-cargo prototype. No other destination, recurring consumption, mineral production, weather rule, simulator flight lifecycle or safety verdict is introduced.
