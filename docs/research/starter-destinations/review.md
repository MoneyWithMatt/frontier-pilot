# Starter career destination review — 29 October 2026

Status: research proposal for Matt's review. No destination is activated, aircraft cleared, mission policy changed or live simulator acceptance granted by this report.

## Decision to make

Review a smaller initial validation network before adding data to the application. The strongest immediate industrial anchors remain **Arandis and Uis**. Broaden them with real port/diamond logistics, prioritise **Dordabis, Aminuis and Leonardville** for useful Missionary services near Eros, and validate a diverse Safari group built around **Okonjima, Mount Etjo, Solitaire, Mokuti and Doro Nawas**. Offshore should retain its existing airport work, with **Hosea Kutako, Eros, Lüderitz and Oranjemund**, and consider **Cape Town** as a longer executive/marine-support extension. No twenty-platform Namibian network is supported by this evidence.

The workbook is the main review surface. Its Catalogue has one row per place; Scoring owns the four criterion inputs and the five separate flying-experience inputs; Career shortlists recalculates ranked validation priorities. CSV, JSON and map files are dated exports, not application configuration. Yellow cells are editable assumptions or scores. Suitability /100 never includes flying experience /25.

## Repository and implemented scope

Public repository: https://github.com/MoneyWithMatt/frontier-pilot. Isolated research baseline: `d5ff51650789e45e46c8828893acaba071d957e7` (`origin/master`, fetched 2026-10-29). Connected GitHub also inspected open [draft Offshore PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3), committed head `22b9e32dda56a747b7025c66761462943f34fd24`. The primary checkout had uncommitted shared-passenger changes. Those were read for context only, never copied into this branch or modified. Their claims are work in progress, not acceptance evidence.

| Career / exact employer | Implemented base | Actual fleet | Current authored time preference |
|---|---|---|---|
| Missionary / Eros Community Aviation (fictional) | Eros FYWE | Cessna 152, Cessna 208 Caravan | 15–45 min |
| Safari / Namibia Safari / Conservation (temporary) | Eros FYWE | Beechcraft Bonanza, PA-28, Savage Cub | 45–150 min |
| Industrial / Fly Eros | Eros FYWE | Cessna 208 Caravan, Beechcraft Baron G58 | 45–90 min |
| Offshore / Skeleton Coast Petroleum | Walvis Bay FYWB | Cirrus Vision Jet; H125 listed but disabled | 45–90 min |

Sources: [`CareerCatalogue.json`](../../../src/Workflow/CareerCatalogue.json), [`CompanyEmployment.cs`](../../../src/Workflow/CompanyEmployment.cs), [`EmployerDestinations.cs`](../../../src/Workflow/EmployerDestinations.cs), [current employer report](../../starting-employers-and-destinations.md). Existing research has **53 memberships / 48 unique places**, including shared airports and base/return memberships. Every original place is accounted for in this master; renamed database keys are explicit research leads, not runtime migrations.

The existing generator resolves each employer's pool, applies reviews, base and enabled-fleet compatibility, ranks by its existing common **120 kt** planning bias and selects **up to four destinations**. It supplies the existing generic recurring-demand/board/economy pipeline. In-envelope destinations receive two desired offers per category, others one. Preferences are biases, not hard distance bans. Prototype dispatch budgets are C152 10 kg, C208 200 kg, Baron 80 kg, Bonanza 60 kg, PA-28 40 kg, Cub 10 kg, Vision Jet 40 kg; they are not certified performance limits. Exact TITLE/ATC MODEL mappings, both-endpoint restrictions on the Offshore branch and physical GUID fleet/dispatch guards remain authoritative.

Existing cargo categories are medical/school/mail; industrial components/tools/PPE/samples; lodge/conservation/photography supplies; and Offshore lightweight/specialist equipment/documents. Tracker-created proof, adaptive required flight distance (0.5–10 NM) and the separate two-NM movement gate remain unchanged. No passenger, survey, cancellation, weather, production or deck rules are invented here. Offshore PR #3 adds airport specialist passenger transfers; uncommitted shared-passenger work also contains Missionary visit policies. This research proposes facilities and operational reasons that can feed those approved systems later, not a replacement engine. Safari guests remain a potential use, not an assertion that Safari passenger missions are enabled.

Reviewed architecture/testing/roadmap and technical-status/design/backlog documents, plus destination, airport-resolution, Offshore and aircraft/fleet reports. The old 31-record Namibia economy catalogue and its **Eros/Uis/Arandis manual September 9 inspection** remain separate from employer research and cached airport-list evidence. Existing resource economy uses generic authored resources and prototype player sites; identifying a real mine does not create a new mine operation or recurring ore production.

## Evidence and efficient MSFS extraction

The repository already solves efficient airport discovery with the **non-EX1 SimConnect worldwide facility list**. Its [published October 16 evidence](../../../evidence/airport-packet-padding/verification.md) records a complete 84,319-record reply, 33/39 supplied identifiers found, SDK 1.2.4 and simulator 1,9,18,0. This is historical empirical evidence, not a scan performed by this research.

A read-only inspection found the existing airport-only cache's latest scan dated **2026-10-27 10:26:44.845042 UTC**, source **SDK 1.2.4; simulator 1,9,21,0**. Its 33 matched supplied identifiers and coordinates are preserved in `sources/simulator-lookup-public.json`. This extraction contains no career state, fleet identity, personal position history, reviews, credentials or installation paths. No MSFS connection, process launch, payload write, Package Tool, scenery installation or save modification occurred.

Microsoft [documents the worldwide request](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Facilities/SimConnect_RequestFacilitesList.htm). Its [facility detail definition](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/facilities/simconnect_addtofacilitydefinition/) exposes runway and helipad child data. A future separately reviewed read-only detail recorder could batch likely fields, validate all replies and export dimensions/surfaces/headings/elevations. Existing Frontier airport-list reading supplies reference points, not those runway details. No new adapter was implemented. Exhaustive static BGL/package extraction was not attempted: it was unnecessary for this first pass and no validated extractor here establishes the effective streamed/default/add-on airport set. A folder scan alone would not prove absence.

**Confirmed in MSFS 2024** here means a geographically consistent cached airport association or retained manual evidence, with evidence type stated. It does not mean a runway/scenery/aircraft acceptance flight. A >5 NM offset flags gross research identity conflicts, not a new runtime rule. Smaller offsets also need actual runway-endpoint review. Real-world-only records remain explicitly unverified in MSFS. The six identifiers absent in the recorded response are **FYSF, FYBM, FYRP, FYTF, FYDN and FYMW**. Absence from that response is not proof that a similarly named or differently coded facility cannot exist in a different simulator/content build.

## Critical geographic and runway findings

| Research association / legacy app code | Offset to real-world field | Decision |
|---|---:|---|
| Okangwati / FYKW (OurAirports key NA-0160) | 588.6 NM | Hold association; cached point is far east in the Zambezi region |
| Hartmann Valley / FYHV | 526.4 NM | Hold; cached point is in southeastern Namibia |
| Hoanib / FYHO | 381.5 NM | Hold; cached point lies near Bitterwasser, not Hoanib |
| Ongava / FYNG | 317.6 NM | Hold; cached point lies near Betesda in southern Namibia |
| Aandster / FYAD | 148.0 NM | Hold; cached point lies well east of NamibRand |

These are inferred discrepancies from a comparison of two sourced coordinate sets. They do not establish which alternate simulator identifiers are correct. Never silently move an accepted route or overwrite its provenance. Reconcile names, geography and detail records before proposing corrections. **No live app changes or notifications were made.**

Other material findings:

- **FYGK / Geluk / Witwater:** AOPA's directory calls FYGK Geluk; OurAirports calls it Farm Whitwater East and separately lists Geluk as NA-0067. The fields are roughly 8.5 NM apart. Lodge/circuit evidence mentioning “Geluk” cannot resolve that identity automatically. Both are held.
- **Aandster:** database 900 m sand versus 2022 EMP 1,450 m and an older operator 1,500 m description. Orientations also differ. Preserve the conflict and any upgrade chronology; do not approve based on the longest figure.
- **Doro Nawas:** the same 2026 EMP says 1,550 m in prose, but its chart says 1,490 m and LDA 1,320/1,330 m. Use declared usable distances only after current validation.
- **Twyfelfontein:** operator/EMP establishes a lodge strip and flood-access purpose. Operator reports 2,200 m gravel; database roughly 1,996 m asphalt. Excellent purpose, unresolved runway representation.
- **Mokuti:** a historical operator factsheet supplies 2,200 × 30 m, 08/26, mixed concrete/gravel, daytime operation. It is much older than the current research; reconfirm usable lengths and surface.
- **Okonjima:** operator gives 1,300 m dirt/sand, 05/23, a valley between mountain ranges plus power-line/fence cautions. Elevation differs (5,430 ft operator, 5,357 ft database); no performance clearance inferred.
- **Nepara:** community clinic is real, but FYNP is absent from the current OurAirports snapshot. A historical X-Plane datum is shown only as a lead, independently of the matching MSFS cache; current real-world field/closure needs research.
- **Rosh Pinah:** legacy FYRP maps to historical field key NA-0180; X-Plane marks the code deprecated, typically due to closure. Do not approve a landing. Rosh Pinah Zinc's active mine is distinct from nearby Skorpion's care-and-maintenance operation. Oranjemund is the stronger current access alternative.
- **Otjikoto:** the operator reports the open-pit phase ended in 2025, with underground/processing activity continuing. Describe the actual phase rather than an endlessly growing open pit.
- **Tsumeb:** current 2026 reporting says copper processing is in care and maintenance while recycling investment continues. Potential maintenance/project visits are distinct from invented ongoing ore production.

All named sources and per-place links are in the catalogue and `sources/source-register.csv`. Operator statements hosted as factsheet mirrors are marked as such. OSM's Aminuis clinic feature independently supports the service association, about 1.3 NM from the airfield. OSM point links elsewhere are review links, not claims of visual inspection. No satellite scene was measured in this task; EIA charts/aerial annotations were read as published evidence, not a new photogrammetric survey.

## Scoring and aircraft boundaries

Stage A first requires an identifiable, credible recipient or operating reason. Lack of purpose makes that career pair ineligible, with no numerical suitability score. Eligible scores use exactly the requested weights: **Missionary/Safari 40/15/20/25**, **Industrial/Offshore 40/20/20/20**, each applied to 0–5 purpose/runway/access/variety and multiplied by 20. Stage B separately sums terrain, approach, runway interest, remoteness and career-network diversity, each 0–5, to /25. These are transparent editorial judgements, not measured safety probabilities. Experience scores anticipate the real geography and may overstate actual default scenery until checked.

Rubric: 0 absent/incompatible; 1 major gaps; 2 weak/limited; 3 credible with material limits; 4 strong; 5 strongest evidenced purpose/network fit. Unknown runway evidence scores 1, not an invented short runway. A purpose score of 5 means a strong case for the game's proposed operation, not evidence of an actual employer contract. Access considers the exact base and a relevant company type; long sectors are penalised for Missionary/Safari and retained where valuable. Network diversity can differ by career.

**No aircraft-specific safe-runway approvals are issued.** Unknown/conflicting length, width, slope, surface, obstacles or permissions is a hard hold on operational approval whatever the score. A known paved runway is only a candidate for an exact-aircraft AFM/POH calculation at actual mass, temperature, pressure altitude, wind and usable distances. Smaller Cub requirements do not automatically clear a Bonanza/PA-28; a Caravan-capable site is not automatically Baron-capable. No Vision Jet assignment to unmeasured gravel fields. H125 remains disabled for all operations, including hospital/airport pads and vessels. Real helipad purpose does not establish simulator deck collision/contact/identity or acceptable offshore helicopter capability.

## Flight planning assumptions

Workbook times are indicative **airborne minutes** from the actual implemented bases. Direct spherical great-circle distance uses radius 3,440.065 NM as a planning approximation to WGS84, multiplied by 1.10 for routing. Then `minutes = 60 × routed NM / planning KTAS + climb/circuit allowance`, rounded up. This differs deliberately from the runtime's unchanged common 120 kt bias.

| Type | Conservative planning cruise | Airborne allowance | Indicative extra ground time |
|---|---:|---:|---:|
| C152 | 90 kt | 8 min | 15 min |
| C208 | 155 kt | 10 min | 15 min |
| Baron | 175 kt | 10 min | 15 min |
| Bonanza | 155 kt | 10 min | 15 min |
| PA-28 | 110 kt | 8 min | 15 min |
| Savage Cub | 80 kt | 6 min | 15 min |
| Vision Jet | 220 kt; 170 kt below 60 direct NM | 15 min; 10 on short leg | 25 min |
| H125 (hypothetical only) | 110 kt | 10 min | Not an enabled operation |

These speeds are researcher assumptions below representative manufacturer maximum cruise, not validated speeds for an exact stock/add-on implementation. Manufacturer Caravan/jet/H125 sources are in the source register. Budget about ±20% airborne uncertainty, more for weather/routing. Ground time and clinic/lodge transfers are separate. No reserve-fuel, range, IFR, alternate, cross-border or overwater permission is inferred from time. A 340 NM manufacturer H125 maximum range, for example, is not a safe offshore return radius. Cape Town is a longer airport executive option requiring exact-aircraft reserves and cross-border review; a slow piston type is not silently substituted for the implemented Offshore fleet.

## Ranked career validation shortlists

Only purpose-eligible **Priority validation / Extended network** entries appear below; held identities, unresolved closure and speculative facilities remain visible on Scoring. These are recommendations to validate, not approval to generate jobs. The workbook also contains all other relevant aircraft times; a reference type is shown here for comparability. Ties retain catalogue order.

### Missionary

19 purpose-eligible assessments; 17 ranked validation candidates. Reference aircraft: Cessna 208 Caravan.

| Rank | Destination / key | Suitability /100 | Flying /25 | Airborne min | Recommendation |
|---:|---|---:|---:|---:|---|
| 1 | Aminuis Airstrip / FYAM | 88 | 18 | 70 | Priority validation |
| 2 | Leonardville Airport / NA-0114 | 88 | 17 | 57 | Priority validation |
| 3 | Kamanjab Airport / FYKJ | 84 | 18 | 106 | Priority validation |
| 4 | Dordabis Carpet Airport / NA-0014 | 83 | 19 | 26 | Priority validation |
| 5 | Sesfontein Airport / FYSF | 80 | 22 | 132 | Priority validation |
| 6 | Gobabis Airport / FYGB | 80 | 14 | 55 | Priority validation |
| 7 | Opuwa Airport / FYOP | 76 | 19 | 150 | Priority validation |
| 8 | Tsumkwe Airport / FYTK | 76 | 20 | 122 | Priority validation |
| 9 | Outjo Airport / FYOJ | 76 | 14 | 79 | Priority validation |
| 10 | Rundu Airport / FYRU | 73 | 14 | 145 | Extended network |
| 11 | Oshakati Airport / FYOS | 73 | 11 | 138 | Extended network |
| 12 | Eenhana Airport / FYEN | 68 | 11 | 143 | Extended network |
| 13 | Epupa Falls Airstrip. / FYEF | 67 | 23 | 181 | Extended network |
| 14 | Ehomba Airport / NA-0169 | 67 | 21 | 163 | Extended network |
| 15 | Omega Airport / FYOE | 67 | 20 | 180 | Extended network |
| 16 | Outapi Airport / NA-0171 | 59 | 14 | 148 | Extended network |
| 17 | Okahao Airport / FYOH | 59 | 14 | 140 | Extended network |

**Why the leaders work:** Aminuis has a documented clinic and three school types in a rural settlement; Leonardville independently has a clinic and schools. They offer believable supplies/visiting-personnel work without inventing a need from an isolated runway. Kamanjab adds an actual health centre and more interesting Kunene approaches. Dordabis is especially valuable even at rank four: its named clinic/community and approximately **26-minute C208 / 36-minute C152** leg provide the short-sector rhythm missing from most northern candidates. Its private field still needs permission, geometry and simulator identity. Outjo and Gobabis provide credible referral/service hubs with easier road access, rather than being mislabelled isolated villages.

Sesfontein and Opuwo have excellent service purposes but are extended Eros flights. Tsumkwe is about 122 minutes in the C208 under these assumptions, borderline rather than comfortably inside two hours. Northern Epupa/Ehomba/Omega have stronger remoteness but are not a routine short Missionary network from Eros. Do not move the base or introduce staging missions to hide this gap without design approval.

### Safari

25 purpose-eligible assessments; 17 ranked validation candidates. Reference aircraft: Beechcraft Bonanza.

| Rank | Destination / key | Suitability /100 | Flying /25 | Airborne min | Recommendation |
|---:|---|---:|---:|---:|---|
| 1 | Doro Nawas Airstrip / FYDN | 94 | 21 | 97 | Priority validation |
| 2 | Okonjima Airport / FYKN | 94 | 21 | 56 | Priority validation |
| 3 | Solitaire Airport / FYSO | 94 | 20 | 52 | Priority validation |
| 4 | Twyfelfontein Airport / FYTF | 91 | 20 | 94 | Priority validation |
| 5 | Mokuti Lodge Airport / FYMO | 90 | 16 | 107 | Priority validation |
| 6 | Wolwedans Airport / FYWD | 88 | 22 | 79 | Priority validation |
| 7 | Okaukuejo Airport / FYOO | 88 | 17 | 103 | Priority validation |
| 8 | Mount Etjo Airport / FYME | 88 | 20 | 54 | Priority validation |
| 9 | Damaraland Wilderness Camp Airstrip / FYDC | 88 | 22 | 100 | Priority validation |
| 10 | Palmwag Airport / NA-0168 | 84 | 22 | 113 | Priority validation |
| 11 | Kerweder Airport / NA-0030 | 83 | 22 | 76 | Priority validation |
| 12 | Etendeka Airstrip / FYEA | 82 | 23 | 114 | Priority validation |
| 13 | Halali Airport / FYHI | 71 | 17 | 103 | Priority validation |
| 14 | Namutoni Airport / FYNA | 71 | 17 | 108 | Priority validation |
| 15 | Helmeringhausen Airport / FYHH | 69 | 19 | 94 | Priority validation |
| 16 | Moewe Bay Airport / FYMW | 67 | 22 | 143 | Priority validation |
| 17 | Tsumkwe Airport / FYTK | 67 | 20 | 122 | Extended network |

**Why the leaders work:** Doro Nawas is an actual lodge and charter hub with documented infrastructure, but missing simulator identity and internal runway conflicts prevent immediate approval. Okonjima combines verifiable lodge/conservation purpose, operator runway evidence, mountain-valley approaches and a short Eros sector. Solitaire offers a useful closer desert-tourism stop with an operator-described field and shuttle. Twyfelfontein adds a real lodge, heritage destination and flood-access reason, with surface/length conflict retained. Mokuti adds eastern Etosha wildlife tourism and explicit Onguma transfers. Mount Etjo is a good shorter sector with an actual lodge; missing geometry remains a hold on aircraft clearance.

Use NamibRand/Wolwedans, Damaraland and Etosha to balance the network rather than selecting twenty Etosha-like fields. PA-28 and especially Savage Cub times are much longer than Bonanza times. Hoanib, Hartmann Valley, Ongava and Aandster would add excellent variety after their code associations are resolved; they are withheld from the recommended table despite attractive scores. Sonop/Westies are real-facility research leads without sufficient current landing identification. Guest transport requires appropriate passenger feature approval; current lodge supplies/conservation work is a legitimate initial fit.

### Industrial

11 purpose-eligible assessments; 7 ranked validation candidates. Reference aircraft: Cessna 208 Caravan.

| Rank | Destination / key | Suitability /100 | Flying /25 | Airborne min | Recommendation |
|---:|---|---:|---:|---:|---|
| 1 | Walvis Bay International Airport / FYWB | 100 | 16 | 69 | Priority validation |
| 2 | Arandis Airport / FYAR | 96 | 16 | 60 | Priority validation |
| 3 | Luderitz Airport / FYLZ | 92 | 20 | 123 | Priority validation |
| 4 | Uis Mine Airport / FYUS | 84 | 21 | 74 | Priority validation |
| 5 | B2Gold Otjikoto Mine Airport / FYBM | 84 | 17 | 77 | Priority validation |
| 6 | Oranjemund Airport / FYOG | 84 | 19 | 164 | Priority validation |
| 7 | Swakopmund Municipal Aerodrome / FYSM | 76 | 17 | 70 | Priority validation |

**Why the leaders work:** Walvis Bay offers a substantial port/repair/logistics purpose and runway evidence; Arandis preserves the proven prototype anchor near Rössing. Lüderitz adds a distinct fishing/mining/offshore supply port and rugged coastal environment. Uis preserves the existing tin-mining destination and Brandberg scenery. Otjikoto adds a gold operation, but absent simulator identifier and missing runway geometry make validation necessary. Oranjemund provides a documented diamond/crew chain on the southern coast, substantially longer than the central routes. Swakopmund offers service-company access rather than a fictional runway inside a uranium mine.

Rosh Pinah is held for field/closure reconciliation; Okorusu for operating status and airfield access; Karibib for military/civil permission and geometry; Tsumeb for current industrial phase. Grootfontein has no specific industrial customer established in this pass, so it fails the purpose gate. Do not delete any original research/history based on these recommendations. Industrial consignments remain aircraft-scale urgent components/samples, not bulk ore or an automatically created economic production chain.

### Offshore

7 purpose-eligible assessments; 5 ranked validation candidates. Reference aircraft: Cirrus Vision Jet.

| Rank | Destination / key | Suitability /100 | Flying /25 | Airborne min | Recommendation |
|---:|---|---:|---:|---:|---|
| 1 | Hosea Kutako International Airport / FYWH | 100 | 13 | 63 | Priority validation |
| 2 | Luderitz Airport / FYLZ | 96 | 20 | 83 | Priority validation |
| 3 | Eros Airport / FYWE | 96 | 15 | 56 | Priority validation |
| 4 | Oranjemund Airport / FYOG | 88 | 19 | 121 | Priority validation |
| 5 | Cape Town International Airport / FACT | 88 | 17 | 223 | Extended network |

**Why the leaders work:** Hosea Kutako is a useful international/business gateway to Windhoek petroleum offices; Eros serves the same city's closer local-office role through a different airport. Lüderitz has a documented maritime logistics base, and Oranjemund participates in an explicit fixed-wing-to-helicopter marine crew chain. Cape Town is a credible longer marine-support/executive extension: the Debmarine EIA documents Cape Town–Oranjemund crew flights. The proposed Walvis Bay leg is an inference for game design, not a claim the existing employer flies that route.

Walvis Bay is the actual starting base, not a same-airport destination. Swakopmund remains a research reserve: it is close to the base and no specific Offshore client/transfer chain was established, although coastal executive work is plausible. Arandis' uranium purpose does not automatically pass an Offshore purpose gate. Keetmanshoop needs a named executive/logistics recipient. Benguela Gem has real helicopter crew-change purpose but is a moving vessel, not a qualified static pad. Kudu is a development area with no operator-listed installed production facility; Venus is a proposed development. Neither is a current approved landing destination. No platform coordinates or helipad identifiers have been invented, and neither executive scope nor future helicopter mission variety is restricted beyond the actual implemented fleet/gates.

## Network balance and gaps

| Career | Evidence-backed breadth | Main gap / recommended response |
|---|---|---|
| Missionary | Named health/education/community services across central/eastern Namibia and the north | Geographic breadth is sufficient for a research library, but only a handful fit a comfortable direct Eros two-hour C208 leg; the C152 network is smaller. Prioritise Dordabis, Leonardville, Aminuis, Gobabis, Outjo and Kamanjab verification. No claim of twenty strong short missions. |
| Safari | Enough real tourism purposes for a roughly twenty-place research network | Only seventeen currently ranked validation candidates; identity, runway data and current access limit readiness. Validate a balanced initial 8–12 rather than activate the whole pool. |
| Industrial | Seven recommended validation destinations plus four eligible holds and one purpose rejection | More than enough for the existing four-destination board, below a twenty-field strong library. Deepen actual mine-airstrip data and operating phase before adding generic towns. |
| Offshore | Four strong existing airport-transfer purposes plus Cape Town extension; one coastal reserve and one deferred vessel | Airport loop has useful coverage. No qualified platform/helipad network is established. Do not pad the list with speculative fields or unsuitable H125 missions. |

Interesting overlap is operational, not just geographic: **Lüderitz and Oranjemund** legitimately serve Industrial and Offshore airport work; **Tsumkwe** supports community supplies and a distinct conservancy/tourism purpose. **Walvis Bay** is Industrial destination plus Offshore base. Arandis remains Industrial while its Offshore association is held. The old shared memberships are preserved in the master even where a career recommendation changes.

The time ranges, unknown/known dimensions, mixed surfaces, plateau density altitude, mountain valleys, inland sandveld, Etosha woodland, dunes, fog coast and long southern airport sectors create genuine diversity. Weather and challenging approaches are research considerations only; no weather mission mechanic or real-world flight clearance is added.

## Potential custom airstrip phase

Three separately mapped **clinic service areas**—Etanga, Otjondeka and Oruvandjei—have a concrete medical-logistics need supported by the Namibia Institute of Pathology's 2025 specimen-delivery RFI and MHSS facility list. Its proposed drone service is evidence of a logistics problem, not proof that a staffed fixed-wing airstrip is necessary or viable. Published clinic coordinates are not runway sites. The RFI's Okangwati latitude conflicts markedly with the documented settlement and is deliberately not used.

For each area first locate existing MSFS/real airfields and determine practical road transfer. Only then assess flat terrain, runway slope/obstacles, drainage, land/conservation restrictions, community consent and the ground connection. A basic Frontier-generated strip might improve some community access and network atmosphere, but cannot solve long distance from Eros. No claim that runway absence was established, no site survey, no scenery build/install, and no construction proposal at a lodge which already has an unidentified strip. Existing field identity fixes take priority over custom construction.

Frontier's basic runway/scenery workflow and some historical compilation/placement acceptance exist; they do not prove a new site, procedural terrain grading, every surface, a complete taxi/ATC network or safe spawning. Retain avatar survey/hash/elevation, AirportSite geometry, receipt-owned install and simulator-closed safeguards for any later separately approved build.

## Proposed integration handoff — no implementation

1. Matt selects a modest validation set and the career memberships/aircraft to investigate. Preserve this research snapshot and existing application records; no roadmap or game-vision change is implied.
2. Reconcile the five gross code/coordinate conflicts and FYGK/Geluk first. Record current simulator/content version, matching geography and airport detail geometry. Verify a small representative sample of surfaces/terrain visually; a world-list check can cover the remaining identified fields without visiting each one.
3. Add only approved research facts through the existing authored catalogue/review architecture (`NamibiaDestinationResearch.json`, `EmployerDestinationCatalogue`, `SimulatorAirportStore`, `CareerCatalogue.json`). The current research loader asserts exactly 53 memberships; an approved later catalogue extension must deliberately update that validation and tests. Do not bypass it by copying this JSON into runtime resources.
4. Preserve stable location/demand IDs, provenance, accepted-job endpoints, reservations, completed history and receipts. Existing review coordinates are immutable. Code/coordinate corrections need a separately specified route version/migration policy rather than silent rewrites. No save/schema migration is proposed by this research itself.
5. Supply approved compatible-aircraft sets at both endpoints and actual-base planning metadata through `CompanyEmployment`; retain the generic recurring-demand/board adapter, existing four-destination selection, exact aircraft mapping and tracker-only delivery evidence. Aircraft-specific times here are review metadata; changing runtime preferences/time bias or ensuring rotation across all twenty destinations is a separate design choice.
6. For passenger opportunities reuse the approved shared passenger / career adapters when merged and qualified. Do not put people into cargo resources. For decks retain separate registry, exact static/mobile facility identity, horizontal/vertical/contact proof, fuel/range/aircraft compatibility and human approval; no airport-radius shortcut or H125 enablement.
7. Later integration tests should cover catalogue uniqueness, identity/geography conflicts, missing dimensions, both-endpoint fleet restrictions, no same-base job, retained exclusions and accepted-history immutability; run the existing hosted Windows build/regression/UI/package pipeline. Exact-aircraft/airport and deck live acceptance remains separate. This research changes no application, generator, tests, official roadmap or Offshore files.

## Deliverables and reproducibility

- `outputs/starter-destinations-20261029/starter-destination-catalogue.xlsx`: five linked review sheets, formula-based scores/times/rankings, filters/frozen identifiers and explicit source columns.
- `catalogue.json` here: one destination master plus normalised career assessments and source keys; this is research data, not a runtime schema.
- `outputs/starter-destinations-20261029/catalogue.csv` and `career-scores.csv`: portable dated data.
- `outputs/starter-destinations-20261029/destinations.geojson`: WGS84 points, career-to-base proposed relationships, custom clinic points and conflicting simulator points/links. Unknown-location facilities/mobile vessels are omitted from geospatial coordinates and retained in the catalogue.
- `outputs/starter-destinations-20261029/destination-map.html`: offline clickable geographic schematic, career filter, confidence colours, conflict layer and evidence links. It loads no external tile/API/CDN. A geospatial tool can render the GeoJSON over a preferred map. The schematic itself does not depict terrain or satellite imagery.
- `sources/`: dated public-data subsets, airport-only cached matches, source register and SHA256 provenance. OurAirports is public domain and explicitly disclaims accuracy; operator/current official material takes precedence where actually reconciled.

No paid service, account provisioning, private telemetry or proprietary asset has been added. The primary Offshore checkout remains untouched. Rebuild the outputs with bundled Python `tools/curate.py`, bundled Node `tools/workbook.mjs`, then Python `tools/report.py`; the first two work from the saved country subset when worldwide scratch downloads are absent. The workbook builder needs the documented bundled artifact-tool node_modules junction, which is ignored by Git.
