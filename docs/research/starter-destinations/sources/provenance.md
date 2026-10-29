# Source snapshot provenance

Research retrieval date: **2026-10-29**. Current use and limitations are described per destination in the catalogue; links and evidence roles are in `source-register.csv`. Published reports retain their historical dates, even when retrieved now.

## OurAirports

Public data entry point: https://ourairports.com/data/ . Public domain, with no guarantee of accuracy/fitness. Airport presence in this dataset does not establish MSFS presence, current landing permission or exact-aircraft performance.

Downloads made with PowerShell `Invoke-WebRequest -Uri <URL> -OutFile <local file>`:

| URL | Bytes | SHA256 of original download |
|---|---:|---|
| https://davidmegginson.github.io/ourairports-data/airports.csv | 12,752,187 | `f508467c19122ea549200daa215ea477eae2dc5375f339fd4e8226736c5981ea` |
| https://davidmegginson.github.io/ourairports-data/runways.csv | 3,972,202 | `ca6ad4f9e86ee5c59085979301a327ba3ab897e83c0aa5357fe8dc351b6f9feb` |

Committed subsets preserve all columns, filtering airports to `iso_country == NA` plus `FACT`, and runways to those airport keys. The full worldwide scratch downloads are ignored; the research tools reproduce outputs from the subsets without network access. `SHA256SUMS.txt` identifies the exact committed source files. Feet-to-metres conversion is 0.3048; unknown dimensions stay null. Closed runway records are excluded, and the longest recorded open runway is the numerical reference, with operator conflicts retained separately. This does not establish takeoff/landing distances available.

## Simulator cache

`simulator-lookup-public.json` was extracted read-only from the pre-existing local airport catalogue cache. It retains schema, latest Scan object (33 supplied-identifier matches), time and source, and an explanatory note. No career/economy/fleet, reviews, telemetry history or private path is included.

- Observed: `2026-10-27T10:26:44.845042+00:00`.
- Source: `SDK 1.2.4; simulator 1,9,21,0; SimConnect worldwide airport list`.
- This task made **no live simulator request**. The committed [October 16 report](../../../../evidence/airport-packet-padding/verification.md) is the earlier empirical complete-sequence evidence; the current cache is a later recorded result, not a new packet qualification.
- Latitude/longitude differences are independent research calculations against sourced field points. Five gross discrepancies are withheld; no accepted coordinates or runtime cache was edited.

## External purpose evidence

Government/authority/primary operator pages support facilities and operating purpose. Lodge factsheet mirrors, historical flying circuits, secondary reports and another simulator's closure/datum leads have narrower roles and are identified in notes. Operator runway descriptions do not silently override a conflicting dataset or become MSFS observations. Clinic locations, lodge positions, licence areas and moving vessels are never substituted for airport/helipad coordinates.

OSM clinic evidence was cross-referenced through https://mapcarta.com/W1080747309 and its linked https://www.openstreetmap.org/way/1080747309 . Other per-row OSM links are supplied for future map review; no claim that all were visually inspected or that satellite imagery was measured. The offline schematic and GeoJSON contain sourced points and proposed relationships, not an invented base map.
