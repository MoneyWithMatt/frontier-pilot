# Career Onboarding V1

Implemented 2026-10-04. This is the onboarding shell; employer acceptance and employed gameplay remain future work.

## Existing architecture and integration

There was no career/profile file. The local economy save owns funds, ledger, jobs, resource reservations, operations and persistent inventory. Flight attempts, aircraft loading manifests/fuel, and last-known aircraft tracking are separate saves. MainWindow previously initialized these services on every launch, including a new installation.

`CareerStore` loads before gameplay services initialize. Legacy gameplay files without a profile classify the installation as an existing active prototype career, with display name `Pilot`; gameplay files remain untouched. A clean installation or draft shows CareerScreen without initializing the economy, world clock or flight services. Injected-model/dry-run tests bypass this gate.

`career.json` schema 1 stores stage, pilot name and optional region ID. It has no employer assignment. Only legacy migration can produce the existing active state; onboarding cannot activate a career. Continue and Back save resumable drafts atomically. Keystrokes remain UI state until navigation. Invalid/future profile files fail closed and remain preserved, with reset available.

## Flow and catalogue

New Career → validated pilot name → region map → Namibia employment map/details. Back preserves name and region. All three employers say **COMING SOON**, with no acceptance action. Eros groups its two employers at one marker; Walvis Bay has the third. The list allows inspection independently of marker selection.

`CareerCatalogue.json` is an embedded presentation catalogue with stable region, base and employer IDs and validated references. It contains Namibia, the three requested employers, aircraft descriptions and work descriptions. It adds no job generation, destination pools, aircraft restrictions, spawning or offshore mechanics. CareerScreen reuses the Operations Mapsui/OpenStreetMap component and attribution. Tiles require the same network/cache availability as existing maps.

Eros coordinates reuse the Namibia catalogue. The approximate Walvis Bay airport marker uses the location published by the [Namibian Ministry of Home Affairs](https://mha.gov.na/rooikop/walvisbay-airport). This marker creates no economic location or gameplay airport.

## Delete Career and exact reset scope

Available in Career / Settings and on onboarding screens. Confirmation defaults to No and describes the destructive reset. Busy operations and other running Frontier Pilot instances prevent reset. No simulator payload is modified.

Confirmation writes a durable `career-reset.json` intent and restarts the app. The replacement process waits for the old process to exit, then resets before constructing gameplay services. A failed automatic relaunch closes the old app and requests manual relaunch, preventing old caches from overwriting reset data.

Only these primary files under `%LOCALAPPDATA%/FrontierAirportBuilder/desktop` are removed from active use:

- `career.json`: pilot and onboarding progress.
- `economy/economy-state.json`: money/ledger, jobs, reservations, operations, inventories and world progression.
- `economy/flight-attempts.json`: flight attempts/evidence and their saved diagnostics.
- `economy/aircraft-loading.json`: cargo manifests, pending changes and saved fuel state.
- `tracking/aircraft-state.json`: last-known aircraft state.

Files move intact to `career-archives/<reset-id>/` for recovery/audit. A clean New Career profile prevents legacy migration. Interrupted moves and interruption after writing the clean profile replay safely. Filesystem links, invalid intents, unsupported profiles and archive conflicts are rejected without blindly overwriting data. Reset does not scan/delete arbitrary JSON files.

SDK/environment configuration, standalone diagnostics, unknown files/backups, saved `airports/` authoring definitions, build workspaces, installed Community scenery and ownership/install receipts remain. These are authoring/install assets: retaining them prevents loss of designs and orphaning installed scenery. Economic site/operation ownership is reset. Reset is not an MSFS/scenery uninstall or permanent privacy erasure.

## Verification and manual checks

Focused tests cover legacy/no-career startup, pilot validation, catalogue, draft/back navigation, inability to activate employment, cancellation, reset allowlist, unrelated-file preservation, future/corrupt profiles and reset interruption/replay. WPF smoke exercises real buttons, name binding, region/employer selection and Back using isolated saves.

Release verification: 22 focused career tests and all 616 solution tests pass. Release build has zero warnings/errors. Site/onboarding UI smoke and loading-control WPF smoke pass. The established self-contained win-x64 publish and Desktop shortcut refresh completed successfully. Live career deletion was deliberately not performed during development; reset tests use temporary directories.

1. Launch an existing save: normal gameplay and Career / Settings should appear.
2. Cancel Delete Career and verify nothing changes. Confirm only when ready to reset gameplay.
3. After restart, choose New Career. Check invalid names cannot continue; enter a valid name.
4. Select Namibia; inspect both Eros employers and Skeleton Coast Petroleum. All remain Coming Soon.
5. Use Back, close/reopen: saved stage/name/region resume without an active employer or seeded economy.
6. Reset a draft; welcome returns while settings, designs and installed scenery remain.

Until employer acceptance is implemented, newly created careers deliberately cannot enter normal gameplay. No changes to loading, fuel, delivery or economy settlement are included.
