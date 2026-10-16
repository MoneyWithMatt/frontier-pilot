# Starting employers, company fleets and destination curation — 2026-10-15

## Outcome and evidence boundary

Four employers can be accepted from Namibia onboarding. Acceptance opens the normal application and records employment and company fleet access, not aircraft ownership. Company jobs use the existing economy demand/requirement/job/shipment/ledger and flight-tracking lifecycle.

**Only Industrial currently has live-generation destinations: Uis and Arandis.** Missionary and Safari have no simulator-verified destinations in the supplied research pool yet. Offshore has two already-resolved destination identifiers, but its Walvis Bay base lacks recorded MSFS inspection. It therefore generates no jobs until the base is approved. This is an explicit data/evidence limitation, not a claim that the other airports are missing from MSFS.

No MSFS process or Package Tool was invoked; no real Community package, personal save, simulator payload, SDK sample or prior evidence was changed. Automated synthetic flight success is not live simulator acceptance.

## Architecture found and reused

`CareerStore` previously persisted schema-1 drafts and legacy active profiles; the three employer cards were presentation-only. `EmployedCargoPrototype` seeded the Eros/Uis/Arandis SYSTEM board. `EconomyJobBoard` already consumed generic persisted recurring demands, and `EconomyRealTime` scheduled them. Player-created/freelance logistics contracts already shared the same economy. There was no separate general passenger or multi-leg mission engine to extend.

`CompanyEmployment` translates the selected employer profile into those existing recurring demands. It does not implement acceptance, cargo transfer, flight qualification, delivery or payment. The existing kernel still owns these transactions, and only `FlightAttemptTracker` produces delivery evidence. The existing system business remains present for player-operation logistics.

## Employers and exact fleets

| Career / employer | Base | Company fleet | Pool memberships | Enabled destinations initially |
| --- | --- | --- | ---: | ---: |
| Missionary / Eros Community Aviation, explicitly fictional | Eros FYWE | Cessna 152; Cessna 208 Caravan | 16 | 0 |
| Industrial / Fly Eros | Eros FYWE | Cessna 208 Caravan; Beechcraft Baron G58 | 12 | 2: FYUS, FYAR |
| Safari / Namibia Safari / Conservation (temporary) | Eros FYWE | Beechcraft Bonanza; PA-28; Savage Cub | 17 | 0 |
| Offshore / Skeleton Coast Petroleum | Walvis Bay FYWB | Cirrus Vision Jet; H125 access listed, operations disabled | 8 | 0: base inspection outstanding |

Names, bases, fleets, cargo mission categories, prototype cargo quantities/payments and preferred time envelopes are authored in embedded `CareerCatalogue.json`. Safari branding and base remain configurable. Employee aircraft access creates no owned-airframe record.

## Research import and reconciliation

Source: user-provided `frontier_namibia_destination_candidates_verified.csv`, 53 records, SHA-256 `E60B0CDD678A0E17E113D7FF2C29A864E2867DDF4697FF007D1623D5011F57AD`.

All twelve source columns were translated without changing their values into embedded `NamibiaDestinationResearch.json`. A field-for-field PowerShell comparison against the CSV passed. Runtime reads the embedded resource; it does not depend on the Desktop CSV. The older 31-record Namibia catalogue and its evidence remain unchanged.

The 53 rows describe 48 distinct candidate keys: 39 have identifiers and nine do not. Shared memberships are FYAR Arandis, FYLZ Luderitz, FYOG Oranjemund, FYSM Swakopmund and FYWB Walvis Bay, each Industrial + Offshore. They represent distinct industrial and petroleum-support work at the same airports; one shared inspection applies across those memberships. Eros is a base for three employers and an Offshore destination, rather than a duplicated research row.

The supplied `Verified` existence field and runway confidence are retained as research assertions. They do not become MSFS evidence. Missing runway measurements stay unknown; free-text runway research is displayed without converting it into invented aircraft limits.

Recognition was tested offline by exact identifier reconciliation against the existing authored Namibia catalogue's explicit `MsfsVerified` records with sourced coordinates and manual inspection evidence. FYWE, FYUS and FYAR resolve: three unique airports, four research memberships because FYAR occurs twice. Their existing evidence records Matt's 2026-09-09 inspection; their coordinates retain published OurAirports provenance. No fresh MSFS recognition test was performed.

Current SimConnect tracking reads aircraft/avatar telemetry and TITLE/ATC MODEL; it does not enumerate simulator airport facilities. Existing proximity recognition compares saved Frontier authoring geometry, not a global MSFS airport database. Consequently **unresolved means no verified, coordinate-bearing application route record**, not proven absent from the installed simulator. Neither scenery folders nor airport BGLs were scanned as a substitute for recognition.

Two identifier-less research rows have possible names in the older *unverified* catalogue: Okonjima/FYKN and Okorusu Mine/FYKR. Those codes were not silently copied into the new research. They remain investigation leads, not recognition results. Likewise the Witwater/FYGK and identifier-less Geluk records remain separate; no name alias was guessed.

## Curated airports not recognised / resolved

The following **45 unique airports** lack a verified application route resolution. All affected memberships are retained and excluded from normal generation. Blank identifiers are explicitly shown as missing. None has been proved absent from MSFS by this work.

| Identifier | Research airport name | Pool(s) |
| --- | --- | --- |
| FYAD | Aandster Airstrip | Safari |
| FYBM | B2Gold Otjikoto Mine Airport | Industrial |
| FYDN | Doro Nawas Airstrip | Safari |
| FYEN | Eenhana Airport | Missionary |
| Missing | Ehomba Airport | Missionary |
| FYEF | Epupa Falls Airstrip | Missionary |
| FYGK | Farm Whitwater East Landing Strip | Safari |
| Missing | Geluk Airstrip | Safari |
| FYGF | Grootfontein Airfield | Industrial |
| FYHV | Hartmann Valley Airstrip | Safari |
| FYHO | Hoanib Airstrip | Safari |
| FYWH | Hosea Kutako International | Offshore |
| FYKJ | Kamanjab Airport | Missionary |
| FYKA | Karibib Airport | Industrial |
| Missing | Keerweder Airstrip | Safari |
| FYKT | Keetmanshoop Airport | Offshore |
| FYLZ | Luderitz Airport | Industrial, Offshore |
| FYMW | Moewe Bay Airport | Safari |
| FYMO | Mokuti Lodge Airfield | Safari |
| FYME | Mount Etjo Airport | Safari |
| FYNP | Nepara Airfield | Missionary |
| FYOH | Okahao Airport | Missionary |
| FYKW | Okangwati Airport | Missionary |
| FYOO | Okaukuejo Airport | Safari |
| Missing | Okonjima Airport | Safari |
| Missing | Okorusu Mine Airstrip | Industrial |
| FYOE | Omega Airport | Missionary |
| FYNG | Ongava Lodge Airstrip | Safari |
| FYOP | Opuwa Airport | Missionary |
| FYOG | Oranjemund Airport | Industrial, Offshore |
| FYOS | Oshakati Airport | Missionary |
| Missing | Outapi Airport | Missionary |
| FYOJ | Outjo Airport | Missionary |
| Missing | Palmwag Airfield | Safari |
| FYRP | Rosh Pinah Airstrip | Industrial |
| FYRC | Ruacana Airport | Missionary |
| FYRU | Rundu Airport | Missionary |
| FYSF | Sesfontein Airport | Missionary |
| Missing | Sonop Airstrip | Safari |
| FYSM | Swakopmund Municipal Aerodrome | Industrial, Offshore |
| FYTM | Tsumeb Airport | Industrial |
| FYTK | Tsumkwe Airport | Missionary |
| FYTF | Twyfelfontein Airport | Safari |
| FYWB | Walvis Bay International Airport | Industrial, Offshore |
| Missing | Wolwedans Airstrip | Safari |

## Other exclusions and manual curation

Offshore FYWE and FYAR are resolved but blocked by the unverified FYWB origin. Once FYWB is approved it remains base/return-only in its own Offshore pool: the generator never creates a same-airport job. Rejected, Needs scenery and Needs investigation reviews block new offers after restart. Accepted shipments, completed history and original inspection coordinates are retained. A curated aircraft restriction with no matching enabled company type also blocks a destination.

The diagnostic window is available both before acceptance and in Career / Settings. It shows every membership, employer filter, research airport/identifier, recognition status, generation eligibility/reason, runway confidence/text, proposed work, source and review notes. It supports explicit Approved / Rejected / Needs scenery / Needs investigation reviews. Approval requires a confirmed identifier, decimal WGS84 coordinates and an inspection note; the dialog requires the player to affirm the MSFS inspection. Optional multi-selected compatible aircraft types are explicit human curation. Empty compatibility means unassessed, not proven suitable for every aircraft.

Reviews persist atomically in `destination-reviews.json` under the normal application root, separately from career data. Career reset retains them. Corrupt/future reviews are preserved and block loading rather than being replaced. Approved route coordinates/identifiers cannot subsequently move: mark an incorrect route rejected or for investigation. Original approval evidence survives later rejection. Correcting an already-approved coordinate requires a later route-version/migration feature; it is intentionally not an unsafe live edit. Catalogue revisions take effect after app restart, keeping one consistent resolver throughout a running flight session.

## Aircraft choice and compatibility

Each available company cargo offer has a company aircraft selector. Acceptance persists its selected type on the ordinary economy job in the same transaction as cargo reservation. The reserved/history cargo description retains this assignment. H125 cannot be selected. Curated compatibility, when supplied, restricts choices. Otherwise the UI explicitly leaves runway/performance suitability to the pilot; no Baron-versus-Caravan runway assumptions or invented performance calculations are made.

Temporary dispatch budgets are C152 10 kg, C208 200 kg, Baron 80 kg, Bonanza 60 kg, PA-28 40 kg, Savage Cub 10 kg and Vision Jet 40 kg. These are labelled gameplay fixtures, **not aircraft useful-load or runway-performance claims**. Industrial 160 kg supplies therefore require the Caravan, while 10/80 kg tasks may offer both aircraft. Grouped selections cannot exceed the chosen dispatch budget or mix company aircraft assignments. Existing live payload structural limits and gross/CG warnings still apply independently.

There are no guessed stock/add-on aliases. In Career / Settings, connect live tracking, select the actual supported aircraft type and confirm the displayed exact TITLE + ATC MODEL. This stores a user-confirmed implementation-to-type mapping in the career profile. Multiple stock/add-on implementations can map to the same type; a different title/model needs its own confirmation. An already-bound exact pair cannot be relabelled within that career. New positive company cargo loads check that the observed aircraft matches the reserved type. Unloading/recovery of existing cargo is not blocked by a newly imposed fleet restriction. Raw simulator identity remains in telemetry/diagnostics and existing exact-flight continuity checks are unchanged. This is explicit user classification, not automatic manufacturer/type recognition or ownership verification.

## Generation and passenger boundary

One configuration adapter selects from the employer's enabled pool, ranks up to four destinations by distance from the preferred time envelope, and supplies generic recurring demand definitions to `EconomyJobBoard`. In-envelope destinations receive two desired offers per cargo category, outside-envelope destinations one. The existing clock supplies its subsequent three-hour cadence and six-hour unaccepted expiry. Catalogue changes reconcile idempotently; disabling a demand does not delete accepted or completed history.

Planning times use WGS84 distance and a stated common 120 kt reference, not asserted aircraft cruise performance, weather, routing or actual block time. Preferred intervals: Missionary 15–45 minutes; Industrial 45–90; Safari 45–150. Offshore uses a provisional configurable 45–90 minute preference. These are biases, not distance bans; with the current northern Missionary pool, flights from Eros can remain substantially longer than the desired rhythm. More verified local communities will be needed to meet that rhythm reliably. No closer airports were invented.

Cargo categories and temporary kg quantities:

- Missionary: medical supplies 10; school supplies 100; mail/documents 5.
- Industrial: replacement components 10; tools/PPE 160; engineering samples 80.
- Safari: lodge supplies 10; conservation equipment 20; photography supplies 20.
- Offshore: urgent lightweight equipment 10; specialist equipment 20; company documents 20.

Prototype payment is configured at 2 TPC/kg. Real source inventory, reservations, cargo transfer, delivery and payment use the existing accounting. Passenger proposals remain visible research metadata, but **no passenger offers are generated** and people are not represented as consumable cargo. Multi-stop duties, passenger seating/boarding, weather, ownership and automatic aircraft spawning remain unimplemented.

H125/platform landing, offshore helipad qualification, inspection-only flights and survey-area loiter missions remain disabled/deferred. Future requirement: airport → defined offshore area → qualified continuous loiter (e.g. 30 minutes) → return, plus possible vessel/platform inspection without landing. None is represented as an ordinary airport delivery today.

## Persistence and legacy compatibility

Career schema 2 adds employment and explicit aircraft bindings. Schema-1 drafts and active legacy careers still load unchanged; acceptance is a deliberate transition from Opportunities. Legacy careers retain their old board and do not silently acquire an employer. Economy schema 8 adds nullable company-aircraft assignment; schema-7 migration preserves jobs, inventory and money. Cross-file career/economy employer disagreement fails safely. Career acceptance is durable before initial economy creation, so restart can initialize a missing economy without overwriting an existing one. Existing atomic economy transitions, completion proofs, identity rules, authoring data and reset archive scope remain in use.

## Verification

- 27 new employer-focused test cases cover all four profiles/bases/fleets, all 53 memberships/shared identifiers, unresolved exclusion, employer assignment/access, pool and cargo category isolation, H125 exclusion, aircraft constraints and exact add-on mapping, reservation/restart, legacy generation, rejection/history preservation, review corruption/immutability, reset preservation, time bias and schema-7 migration.
- A synthetic curated Missionary flight runs through the existing `FlightAttemptTracker`, qualified arrival and repeated unload, producing exactly one shipment delivery and financial payment. It does not fabricate `VerifiedDeliveryEvidence` in a job service.
- Complete solution: 660 tests passed, zero failures/skips. Release solution build: zero warnings/errors.
- Isolated WPF smoke passes all four actual employer acceptance handlers, transition into the normal gameplay shell with a Fly Eros board, the actual aircraft selector and its bound reservation command, all 53 diagnostic rows, plus existing saved-site editing and dependency-setup checks. Rendered employer, diagnostics and company-job screens were inspected.
- Loading-control WPF smoke passes payload/fuel draft preservation and zero-reset checks. Its first invocation omitted the required XAML argument; rerunning with `src/Desktop/MainWindow.xaml` passed. An initial extended UI test raised Click without invoking a command-bound button; the harness was corrected to exercise its bound command, then passed.
- Published with `scripts/Publish-Development.ps1 -CreateDesktopShortcut`: self-contained Windows x64 single-file `dist/FrontierPilot/FrontierPilot.exe`; generated binaries remain untracked. `scripts/Start-Desktop.ps1` was corrected to the existing project's actual target-framework output directory.
- Test state and flight evidence were synthetic/temporary. Live airport inspection, stock/add-on mapping, real payload operation and flight acceptance remain Matt's tests.

## Manual testing

1. Launch the refreshed Desktop shortcut. In Career / Settings choose Delete Career only when ready; review and confirm the existing reset/archive dialog. Saved site designs and installed scenery remain.
2. Create a pilot, choose Namibia, inspect the four employer cards and their fleet details. The destination catalogue is accessible before acceptance.
3. Accept Fly Eros first for the existing verified Uis/Arandis routes. Verify the employer/base/company access in Career / Settings.
4. Open Jobs, select an offer, review its complete cargo/route details, choose a company aircraft and accept/reserve it. A 160 kg tools/PPE offer should permit only the Caravan.
5. Load the corresponding stock or compatible add-on aircraft in MSFS. Connect tracking. In Career / Settings select its real company type and confirm the displayed TITLE/ATC MODEL. This does not alter MSFS or grant ownership.
6. Prepare reserved cargo in the normal Jobs/Aircraft workflow. Use the existing guarded loading, boarded verification, flight and unload flow. Mismatched company types and mixed/over-budget grouped assignments should be refused before a new positive cargo load.
7. Open Destination diagnostics / MSFS review. Filter by employer and inspect each candidate manually in MSFS. Record the actual identifier, decimal airport coordinates and simulator/version/observations in the note. Preserve unknown runway data; use compatible-type selections only where assessed.
8. Approve a destination only after inspection, or record Rejected / Needs scenery / Needs investigation. For Offshore, inspect and approve FYWB first. Restart Frontier Pilot; refresh Jobs and inspect the resulting pool. Same-airport FYWB→FYWB and all H125/platform jobs remain absent.
9. Reset through the UI and repeat with Missionary, Safari and Offshore. Before their destination/base inspections, an empty board is expected and diagnosed. Curation persists across these career resets; no config-file edits are required.
10. Report bad/missing airport identifiers with the research name and your MSFS version/scenery observation. Rejections stop new generation after restart without erasing history. Already-approved route coordinates cannot be changed through this V1 form.

## Significant files and commit

- `src/Workflow/CareerCatalogue.json`, `CareerOnboarding.cs`: profiles, acceptance, schema and compatibility mappings.
- `src/Workflow/NamibiaDestinationResearch.json`, `EmployerDestinations.cs`: lossless research import, diagnostics, resolution and explicit durable reviews.
- `src/Workflow/CompanyEmployment.cs`, `EmployedCargoJobs.cs`, `BuilderViewModel.cs`: shared board integration, fleet selection and service wiring.
- `src/Core/Economy.cs`, `EconomyJobBoard.cs`, `src/Workflow/EconomyPersistence.cs`: persisted dispatch choice, schema migration and verified-gateway destination support.
- `src/Workflow/FlightAttemptTracker.cs`, `AircraftLoadingCoordinator.cs`: additional curated route resolver and positive-load company check; existing qualification rules remain unchanged.
- `src/Desktop/CareerScreen.*`, `MainWindow.*`, `DestinationDiagnosticsWindow.cs`: functional onboarding, jobs/aircraft controls and inspection UI.
- `tests/Tests/CompanyEmploymentTests.cs`, `CareerOnboardingTests.cs`, `tests/SiteUiSmoke/Program.cs`: focused and UI regressions.
- `scripts/Start-Desktop.ps1`: corrected framework output path.
- `evidence/starting-employers/`: verification record and rendered UI evidence.

The implementation commit hash is reported in the chat completion message. Work was committed on the existing `master` branch, preserving its pre-existing eight local commits; no unrelated history was rewritten.
