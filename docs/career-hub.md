# Career Hub

Status: Implemented on the isolated feature branch; automated verification passed, human review pending.
Approved by: Matt's “Build the Career Hub UI” request, 2026-11-03.

## Objective

Give pilots one Career workspace for employer information, physical aircraft, loading and work. Preserve existing transport, flight proof and economy authority.

## Gameplay behavior

Career replaces the separate Jobs, Aircraft, passenger, dispatch and Offshore navigation entries. Overview is the default. Fleet, Find Work, Job Board and Create Jobs remain within this workspace. Navigation is session state; ordinary refreshes retain the section, selected job and aircraft GUID.

Overview and employer listings use the actual catalogue/profile. Company initials are labelled placeholders. Assigned base and simulator-observed last-known location are separate. No balances, historical hours, health or simulator compatibility are invented. Existing hourly employment wages are unchanged.

Fleet exposes authorised existing physical records and inspection-only maintenance. It moves the original CargoJourneyControl, PassengerTransfersControl and aircraft loading/recovery view into the selected aircraft workspace; it does not construct another loader or native connection. The same controls open from accepted jobs. Reservation navigation chooses a recorded GUID, otherwise the current eligible airframe or a unique eligible record; ambiguity requires an explicit Fleet choice. Opening a workspace never binds/transfers a reservation. Pending cargo edits and passenger payload ownership/recovery block switching, including the original loader's aircraft selector.

The unified Job Board projects existing cargo, personnel and Offshore observation sources. Accept delegates to their existing services. Routes use resolved coordinates and committed ordered cargo legs; observation shows base–area–base and its configured radius. Missing coordinates have an explicit unavailable-distance state. Maps use the existing Mapsui/OSM prototype integration; map tiles are not flight proof. Platform qualification listings cannot be accepted as jobs. Original Offshore commands and restricted diagnostics remain accessible. Existing legacy job controls remain under Aircraft systems / legacy recovery.

Find Work browses all four catalogue employers. Active employment switching remains unavailable: backend policy for leaving accepted work, payload, wages and company airframes is required first. Create Jobs permits only existing player-owned site logistics through the established inventory/escrow service. NPC employees cannot publish employer contracts. Aviation-company ownership, manager authorisation, passenger orders and multiplayer employment/publication remain future systems.

## Acceptance criteria

- Display actual employer, wage, assigned fleet and active work.
- Preserve GUID selection and reuse the same loading control instances from both entry points.
- Inspect condition without repairing/resetting it.
- Display known routes/areas, select and accept supported work, and retain selection on refresh.
- Keep unqualified SF50 boarding and H125/platform operations restricted.
- Preserve original legacy recovery and existing cargo/passenger ownership safeguards.
- Browse employers without changing employment. Publish only authorised owned-site logistics.
- Verify ordinary desktop layouts with hosted synthetic WPF screenshots.

## Non-goals

No flight/pay/loading rewrite, simulator qualification, new maintenance actions, employer-switch transaction, new freelance/company business simulation, personal save migration, scenery installation or live simulator control.

## Relevant existing systems

`CareerHubViewModel` composes `CareerOverviewViewModel`, `CareerFleetViewModel`, `CareerJobBoardViewModel`, `CareerWorkViewModel`, `CareerOwnerJobsViewModel` and transient `CareerNavigation`. `BuilderViewModel` supplies its existing services and read-only economy snapshot function. `MainWindow` reparents the original controls once. `CareerHubControl` hosts them; `CareerJobMap` uses Mapsui. Existing `EmployedCargoJobsViewModel`, `PassengerTransfersViewModel`, `OffshoreMissionsViewModel`, `AircraftFleetViewModel`, `CargoJourneyViewModel` and `ProspectingOperationsViewModel` retain authority.

## Persistence implications

No schema/version/migration changes. Career refresh reads the existing economy store. Existing services alone accept/reserve/settle/persist work. Physical selection still uses the fleet store's original guarded setter. Navigation is transient, not a new personal preference/save document.

## Required tests

`CareerHubTests`: catalogue/pay, read-only refresh and GUID persistence, passenger reservation once, loading context, source-selection mismatch, platform gating, observation geography, owner permissions, transient selection clearing and cargo-draft selection guards. Existing full regression covers cargo/passenger recovery and settlement. `SiteUiSmoke/CareerHubSmoke.cs`: actual navigation and controls, acceptance, original workspace identity, map update, capability states, visible selection through immutable refresh, reachable passenger actions and 1280×900 / 1024×768 layouts. Existing SiteUiSmoke cargo acceptance now exercises Career Job Board rather than a hidden legacy tab. `DesktopBindingTests` guards read-only aircraft label bindings, after hosted WPF found an actual Run.Text writeback crash.

Hosted Windows verification for implementation `ea32975ea8ef9a1b1b1941aec85969053627bc98`: [981 regression tests, WPF, historical CLI review and packaging passed](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204). [Synthetic screenshots and logs](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204/artifacts/13228037288). Subsequent documentation/test-only validation is visible in branch Actions history. No personal save or live MSFS testing occurred.

## Simulator testing requirements

None for presentation acceptance. Hosted WPF uses isolated synthetic stores. No screenshots or test fixtures qualify physical payload or flown missions. Subsequent live acceptance requires approval for the exact build/scenario; existing SF50/H125/platform limitations remain authoritative.

## Open design decisions

None blocking this UI slice. Future employer departures, company authority, freelance terms and multiplayer contracts require their own designs before activation.
