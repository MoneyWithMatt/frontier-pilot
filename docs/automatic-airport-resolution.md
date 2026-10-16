# Automatic employer airport resolution — 2026-10-16

The research catalogue is implemented without requiring individual airport visits or approval. On the first live tracking connection in an employed career, Frontier requests the worldwide airport list from MSFS 2024. The Curated destinations window also has **Check all airports in MSFS**, available before accepting employment. Load a flight anywhere; there is no need to travel to Namibia first.

Found identifiers supply simulator coordinates and enable eligible employer jobs immediately. The existing shared generator, company fleet rules, accounting and flight evidence remain unchanged. Unknown runway data stays unknown. Database presence is not visual acceptance or proof that a particular aircraft can use a runway.

## Recognition and diagnostic boundaries

- Uses the non-EX1 `SimConnect_RequestFacilitiesList` airport request. Microsoft documents this as worldwide; EX1 is limited to the reality bubble and would incorrectly exclude remote destinations. [Official API documentation](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Facilities/SimConnect_RequestFacilitesList.htm).
- The installed MSFS 2024 SDK header defines a 28-byte packet header and packed 36-byte airport entries, including `Ident[9]`, `Region[3]`, latitude, longitude and altitude. Offline packet tests cover this layout and require every packet before returning a successful result.
- Exact identifiers only. A completed scan can report **Not found in MSFS airport catalogue** or **Ambiguous identifier**. Timeout, disconnection, malformed replies and simulator absence are lookup failures, never evidence of missing airports. Previous completed results survive failure.
- The separate `simulator-airports.json` cache records scan time, SDK/simulator versions and returned coordinates. It survives career reset. Malformed/future files are preserved. This adds no career/economy schema migration.
- Initial coordinates remain stable for accepted jobs. Later absence or coordinate changes block new offers while retaining old route resolution. Existing manually inspected coordinates retain their original provenance.
- Manual Rejected / Needs scenery / Needs investigation reviews continue to block new jobs. They are optional curation, not a mandatory checklist. Manual review changes still require restart; automatic lookup results apply immediately.
- The existing `MsfsVerified` lifecycle value is used for resolved employer locations with the explicit evidence method `SimConnectAirportCatalogue`. This means database resolution in this context; it does **not** mean `ManualInSimulatorInspection`. Original authored catalogue evidence is unchanged.

## Expected pool availability

Actual enabled counts depend on the simulator response, local reviews and base availability. If all supplied identifiers resolve uniquely, with no review exclusions:

| Employer category | Potential destinations (excluding its own base) |
| --- | ---: |
| Missionary | 14 |
| Industrial | 11 |
| Safari | 11 |
| Offshore | 7 |

All 53 memberships / 48 unique candidates remain visible. There are 39 unique supplied identifiers. Five shared Industrial/Offshore airports remain legitimate memberships. The nine candidates without identifiers cannot be queried automatically: **Ehomba, Outapi, Okorusu, Keerweder, Zannier Sonop, Wolwedans, Palmwag, Geluk and Okonjima**. They remain flagged and excluded until their identifiers and locations can be established; none were guessed.

**No live scan was performed during development.** There is consequently no new factual list of airports missing from the user's MSFS installation. The previous report's 45 unresolved records describe the old app-catalogue baseline, not missing MSFS airports. This change supersedes its manual-approval prerequisite.

## Manual test

1. Start the rebuilt development copy and load any flight in MSFS 2024.
2. Accept a Namibia employer and connect Tracking. The career panel displays lookup progress and the count requiring attention.
3. Open Curated destinations. Alternatively, use **Check all airports in MSFS** here before accepting an employer or to retry a failed lookup.
4. Filter by employer; inspect recognition, job eligibility and runway notes. Found routes feed the normal board immediately. No career reset is needed for an existing employer save.
5. Fly the jobs you want. Record bad scenery or runway suitability as an optional review; no database editing is needed.

First live acceptance remains pending. H125/platform, passenger and survey missions remain deferred as before.
