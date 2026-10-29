# Persistent employer-owned starting fleets

This isolated feature builds on draft PR #6 `fbf08bc` (which includes PR #3). Neither original branch was modified or merged.

## Player flow and supported allocation

Accept employment normally. The application creates company-owned aircraft automatically in the existing physical fleet, with a persistent local player assignment. Open Aircraft / Fleet, select an assigned aircraft and follow its loading instructions. No purchase or registration is required. Unknown historical condition/hours are explicit, not a zero-hour/healthy assumption.

Actual `CareerCatalogue.json` fleets:

| Employer | Allocated types |
|---|---|
| Eros Community Aviation | C152, C208 Caravan |
| Fly Eros | C208 Caravan, Baron G58 |
| Skeleton Coast Petroleum | Vision Jet, H125 (mission capability disabled) |
| Namibia Safari / Conservation (temporary) | Bonanza, PA-28, Savage Cub |

An allocation does not grant new aircraft compatibility. Load the selected enabled type in MSFS at its assigned base/saved location. Existing Career / Settings confirmation records the **exact** title/model/type mapping; the fleet can then associate that matched telemetry with the explicitly selected allocation at its location. No aircraft-name alias guessing, default simulator identity, native tail change, purchase or simulator placement occurs. Subsequent object/livery changes retain explicit binding confirmation. The assigned FP registration uses the whole GUID and is an internal dispatch identifier, not a claim of legal registration or an observed MSFS tail number.

The fleet screen lists only currently authorized company/player records, with type/registration, allocated base or observed location, condition, hours, unavailable-type explanation and selected-job aircraft eligibility. Legacy/personal enrolment is hidden for employed pilots. Exact package/livery confirmation remains available when needed; it is distinct from creating a company aircraft.

## Architecture and persistence

- `PhysicalAirframe` remains the sole aircraft aggregate. `OwnerEntityId` and `AssignedPlayerId` are separate from `Operator`, with a stable company/player/type `AllocationKey`, authored `AllocatedBase`, readable type label and last tracker-qualified transport stop. Future unassigned company assets fit the same schema. Current commits refuse silent ownership/assignment replacement; a future transfer workflow must provide explicit authority/history.
- `EmployerFleetProvisioning.Provision/Ensure` derives types from the actual employer definition, reuses existing valid company/player/type records and creates only missing ones. GUID-backed registrations are collision-checked within the store. Fleet revision checks/cross-process serialization prevent duplicate concurrent startup; no in-memory identity counter.
- `CareerStore` schema 3 adds a durable local player GUID. A named profile lock protects generation/acceptance. Employment/identity save first, fleet allocation second; startup and repeated **same-employer** acceptance repair a failed second stage. Schema-2 profiles retain employment/bindings. Economy's existing prototype player account remains scoped to its existing career root; old job/player/ledger IDs are not rewritten.
- `AircraftFleetStore` schema 2 keeps history, condition/components, engine/airborne exposure, faults, work orders, bindings, positions and GUIDs. Old schema-1 fleets remain readable. Migration is non-destructive and normal atomic commit/backup/revision guards apply. Future/corrupt data remains preserved.
- `AircraftFleetViewModel` applies current employer/player authorization to listing, selection, binding and dispatch. Initial base is an authored allocation, stored **without manufactured telemetry/altitude/session**. A first confirmed type at that base is associated using real telemetry; arbitrary remote selection cannot move it. Existing tracking/condition observation, runtime continuity and qualification gates remain.
- `CargoJourneyService.WorldObserved` exposes already validated economy snapshots to `Fleet.ReconcileTransportArrivals`. Existing tracker proof for GUID cargo/passenger missions supplies a last-known arrival position and a named last transport stop. Projection is idempotent and preserves newer telemetry; it never creates delivery proof, observed hours, wages, parked/terrain qualification or spawn authority. It also reconciles persisted proof after reconnect/restart. Tracking's saved-Frontier infrastructure recognition is unchanged.

Allocation creates **no financial ledger entry or player asset value**. Company maintenance retains its existing company payer; facilities/repair authority are not enabled by provisioning. Historical airframe hours stay unknown; observed counters start at zero since allocation. Existing exposure/condition adapters update only from supported evidence. No healthy baseline, engine count, vendor condition, fault repair or lifetime hours are invented.

## Legacy reconciliation and employment boundaries

An old `Operator` label alone is not ownership/player evidence. A legacy company-labelled or known company-owned/unassigned record matching a type blocks a duplicate for that type. An untyped company record conservatively blocks automatic allocations it could represent. Fleet UI presents preserved candidates: explicitly confirm the existing record and company type. Its GUID, registration, components, history, maintenance and observed location/hours remain intact, an assignment event is appended, and genuinely missing types are provisioned. Multiple valid assigned records are preserved and reported, never deleted.

Selecting a different employer/player authorization exposes only that allocation; other records remain unchanged. Returning through the allocation API reuses them. **The active career UI still prohibits changing employer**, because the existing economy and accepted missions are employer-bound. This task does not pretend to implement that migration. Deleting/resetting a career retains its existing explicit archival policy and is not an employer-switch tool.

The existing external relocation acknowledgement is labelled **Developer recovery**. It never creates flight/delivery/pay evidence or moves cargo. Normal fleet selection does not reposition MSFS or erase the stored location. Actual saved positions are last known, not confirmed parking or safe spawn points. Native condition/contact, fault and positioning adapters retain their qualification gates.

## Local identity and future shared persistence

Local player GUIDs, physical GUIDs, scoped allocation keys and stored registration checks support separate allocations for many players; automated coverage provisions 100 players into one test fleet. There is no shared-world server, civil registration allocator or global ownership enforcement. Local JSON remains bounded by the existing file-size safeguards (fleet validation allows 10,000 records; histories can reach the file bound sooner). Shared persistence will need authenticated player identities, server-owned company/registration uniqueness, transactional allocation/transfer authorization, authoritative locks and migration of scoped legacy economy accounts. A GUID does not certify that a loaded MSFS object is a globally owned aircraft.

## Automated verification

`EmployerFleetTests` adds 19 cases: all four acceptance flows; one-per-type allocation; byte-stable idempotent startup/same-employer retry; unique player/aircraft/registrations including 100 local players; ownership filtering and employer return at the allocation boundary; schema migration and explicit legacy reconciliation; unknown condition and evidence-driven exposure; maintenance/location/history persistence; corrupt/future preservation; concurrent startup; type/location/disabled/disconnect guards; actual cargo/passenger loading with the allocated GUID; idempotent tracker-arrival projection; and interrupted acceptance repair.

`SiteUiSmoke.EmployerFleetSmoke` follows the actual employment acceptance button for all four employers, reads the persisted fleet, opens the production fleet panel, selects an assigned GUID, confirms company property/unknown history and hidden registration controls, and captures synthetic screenshots. Full regression, WPF and packaging use the established Windows scripts. [Handoff](handoffs/employer-starting-fleets.md) records exact commits, commands, counts, failures and hosted evidence.

## Focused live MSFS acceptance

**Not performed.** Approval must identify the exact CI artifact/source/executable and isolated scenario before local execution. Use an approved disposable Windows profile or reviewed harness; the application has no general live test-root switch. Never test against personal saves by default.

1. Record source/merge and artifact/executable SHA256, MSFS/SDK versions/hashes, aircraft/add-on/version/config hashes and isolated data-root procedure. Keep prior live qualification separate.
2. Create a new career, accept each company in separate isolated profiles, inspect one record per actual type, distinct GUID/FP registration, employer ownership, player assignment and allocated base. H125 must remain disabled; no registration/purchase control required.
3. Select an enabled airframe. Load a wrong simulator type and a wrong location: expect observation-only and no binding/dispatch. Load the correct type at base, confirm its exact type in Settings, observe automatic association and fresh saved location. No tail-number or simulator-position write should occur.
4. Select an eligible cargo job, load/fly/deliver using the existing payload/flight gates. Inspect the same GUID's observations, last-known destination and named tracker stop, ledger and hourly settlement. Repeat with a supported passenger configuration; SF50 passenger configuration/visual limits remain as in PR #3, not bypassed by fleet allocation. Mixed cargo/passenger carriage remains blocked.
5. Close/reopen and repeat acceptance of the same employer: same GUID, registration, component instances, exposure, condition/history/maintenance/location; no duplicate records. Disconnect/reconnect and change object/livery: no guessed association or gap hours. Native pause/time/exposure behavior requires its own exact-aircraft evidence; unsupported condition remains unknown.
6. Test a preserved legacy company fixture: no duplicate, explicit assignment/type confirmation, original history unchanged. Try selecting another employer/player's record: unavailable. Do not reset a real career to simulate employer switching.

Record pass/fail/unverified per aircraft, before/after fleet JSON receipts, real simulator type/object observations, location/condition/exposure and mission outcomes. Synthetic payload/condition/WPF checks are not simulator acceptance. Platforms/H125/surveys, unsupported passenger configurations/visuals, automatic relocation, new ownership transfers and active employer changes remain deferred.
