# Multi-stop cargo logistics

Implementation on `codex/multistop-cargo`, isolated from PR #3 and PR #5. This branch starts from PR #3 `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`, includes the pinned PR #5 research, and targets `master` in a new draft PR. It does not modify or merge either original branch.

## How to use

1. Accept jobs in Jobs; another acceptance does not require an empty aircraft. Select/bind the physical aircraft in Fleet. Open **Cargo dispatch / manifest** from Reserved jobs or the main tab.
2. Read aircraft. Rows show cargo available here and cargo aboard, job/resource, destination and status. Set **Desired aboard kg** per consignment; unchanged through-cargo remains aboard. The loader never infers logical allocation from shipment order.
3. Set desired Frontier mass per station, or select a writable station and use **Suggest station mass**. Suggestions only edit a plan; they do not write MSFS. Review distribution. Simulator-reported maxima remain enforced; absent maxima show “Not reported.” No certified station capacity is invented.
4. Exit near the aircraft using the existing explicit walking-avatar guard. Prepare preview; the printout clearly separates proposed targets from committed cargo. Board the same aircraft and Apply within five minutes. Immediate and three-second retention readback precede economic commit. Renew outside authorization for the same targets if it expires. Discard is allowed only when all stations still match the saved baseline and no leg was armed.
5. Select the next known stop, plan it, remain stopped for ten continuous tracking seconds, then arm before departure. Fly normally. Arrival keeps the existing departure, movement, route-adaptive distance, identity, continuity and ten-second stopped requirements.
6. At A, read and reduce only A's consignment; suggest/review station mass, prepare outside and apply seated. B stays aboard. Accept/load another available job at A through the same procedure. Plan/arm the next leg to B. The read-only printout remains accessible in flight and updates after commits.

The logical current location is a stopped committed handling location / last settled leg; it is not a claim of current parking while airborne. Fuel is never written by this service. Station mass attributed to unrelated payload is retained.

## Source map and authority

- `Core/CargoJourneys.cs`: airframe GUID manifests, explicit consignment placements/changes, station claims, operations/receipts, immutable leg snapshots and leg settlement/validation.
- `Workflow/CargoJourneyService.cs`: durable intent → guarded native write → retained readback → verified intent → atomic consignment/inventory/completion commit. The `FlightAttemptTracker` partial alone constructs consignment delivery evidence from qualified leg proof. The existing tracker monitor supplies the common airborne-time and flight qualification rules; its temporary transport context is never persisted as a passenger or paid by passenger rules.
- `Core/Economy.cs`: existing shipment reservation/load/transit/partial delivery/payment semantics. Requests name explicit jobs and the verified owning manifest. Managed jobs cannot use legacy physical transfer/completion/diagnostic skip paths.
- `Workflow/CargoJourneyViewModel.cs`, `Desktop/CargoJourneyControl.xaml`: available/aboard selection, station distribution, preview/actions, flight controls and selectable live dispatch text on off-white monospaced ruled paper.
- `BuilderViewModel`: production composition uses actual GUID selection, company/fleet identity checks, known catalogue/site coordinates, ambiguous-location refusal and existing outside/seated guards. No new destination catalogue entries or facilities are invented.
- `IGuardedAircraftPayloadSimulator`: shared native comparison/write interface with a compatibility bridge to the PR #3 adapter. Shared named operation lease serializes cargo/passenger/fuel operations. Native one-based station IDs and kilogram/pound conversion remain in the existing adapter.

Schema 13 retains ownership/intents/legs in the same atomic economy document as shipments, inventory and financial receipts. A verified simulator write that outlives a failed save can be retried using absolute targets, never additive mass. A partial setter may already have applied stations: only saved before/target values are accepted; conflicting manual mass, station layout, fuel, location or aircraft identity blocks recovery. No guessed rollback or automatic restoration. Legacy saves are not adopted by title/model; finish/recover them using the existing Aircraft workflow first. Cargo transfer histories and legacy stores are preserved.

## Wages and pilot authority

An employed leg records its employer/rate and eligible contiguous airborne ticks once. Qualified arrival atomically produces one deterministic `CargoLegWage` receipt and balance update. Destination unloading settles individual jobs against that receipt, including several jobs delivered together or partial deliveries. Repeating commands or restarting cannot create another receipt. A later pickup appears only in a subsequent leg snapshot. Existing rates remain Missionary/Safari $400, Industrial $650, Offshore $800 per hour. Freelance and player logistics use their existing full-delivery fixed contract semantics.

MTOW/gross maximum, overall recommended payload, CG envelope, company fixture mass budgets, runway/performance/environment assessments do not refuse a technically permitted load. Available gross/CG information is advisory. Identity, ownership, station/interface limits, resource quantity and physical readback remain mandatory. Gross **delta**, when reported, checks whether a write affected mass; it is distinct from rejecting weight above a maximum.

## SDK evidence and limitations

Official [station configuration](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/CFG_Files/flight_model_cfg.htm) defines zero-based CFG stations and weights in pounds; SimVar adapter indices remain one-based. [Shared legacy SimVar reference](https://docs.flightsimulator.com/html/Programming_Tools/SimVars/Aircraft_SimVars/Aircraft_System_Variables.htm) describe station mass rather than job/resource identity. [Aircraft Loads](https://docs.flightsimulator.com/msfs2024/flighting/content-configuration/careers/general-career-information/aircraft-loads/) distinguishes visual load nodes from weight distribution. These are documentation findings, not live evidence of any installed aircraft's setter, EFB synchronization or visible people. PR #5's pinned [sources](research/cargo-logistics/sources.md) remain historical evidence, not a mutable implementation specification.

New cargo uses discovered writable payload stations; it does not assume a Caravan index or load capacity. Individual add-ons may reject/reset writes; readback failure preserves intent. No aircraft has received live acceptance for this new multi-stop flow. PR #3 passenger aircraft/configuration restrictions remain, including the unavailable effective SF50 profile and unsupported seated visual/animation control. Cargo and passenger coordinators reject concurrent Frontier ownership/pending intents; the shared model/interface supports a later reviewed mixed ownership adapter, but mixed transport is **not supported or live accepted** here.

No new cancellation/abandoned-cargo economy, empty repositioning pay, printer/PDF exporter, performance worksheet, passenger visuals, platform activation or automatic safe-flight decision is included. The manifest is selectable/copyable text, available in flight. Histories retain dispatch references and stops. Schema/file size limits remain fail-safe; archival for very long careers is a later task.

## Live simulator acceptance

**Pending, not performed.** Obtain approval for the exact hosted artifact SHA and isolated scenario before running it. No broad local execution permission or personal-save migration is inferred from build/test permission. Use a reviewed disposable Windows user/profile/harness; the app has no general test-data-root switch.

1. Record source/head and tested merge SHA, executable SHA256, MSFS/SDK hashes, exact aircraft/add-on/version/config hashes, selected airframe GUID/type mapping, known airport catalogue/review evidence and actual station IDs/names/limits. Confirm whether seated payload writes affect real gross mass and survive aircraft entry.
2. Prepare two actual accepted jobs at a common verified origin: A10 kg, B25 kg. Use a reviewed isolated fixture if the live board does not offer those quantities/routes; do not alter personal inventory or claim synthetic origins/facilities as verified. Record unrelated occupants/cargo, each station, fuel and gross baseline.
3. Select a suitable writable station/distribution; preview shows unchanged committed mass. Authorize outside, enter aircraft, apply. Confirm retained +35 kg station and gross change, unchanged unrelated payload/fuel and manifest. Above-MTOW/CG synthetic checks do not authorize an unsafe real flight; pilot plans operation.
4. Plan/arm origin→A, fly and stop ten seconds. Observe one leg wage at the existing rate, never two wages for two jobs. Unload only A10: confirm physical −10 kg, B25 aboard, inventory +10 at A, only A completed and receipt count unchanged.
5. Accept another available A-origin job, load only its quantity, verify aggregate increase and independent identity. Plan/arm A→B. Confirm departure evidence starts after loading. At B unload B25; verify through-cargo remains, correct destination inventory, only B completed. Deliver C at its own destination if different.
6. Repeat a command; close/reopen after safe parking; disconnect/reconnect during observed flight and between preview/apply; verify no duplicate mass/payment and no gap wages. Use isolated fault-injection only for partial writes, clamping and changed aircraft/stations. Manual payload edits must block rather than overwrite. Record exact before/targets/actual and error status.
7. Test passenger boarding with active/pending cargo and cargo with active/pending passengers: clear explicit interim limitation, unchanged other mass. Separately follow PR #3 passenger acceptance; no visible-person/animation claim follows from cargo success.

Evidence table per aircraft: baseline/station/retained/gross/fuel observations, save/reconnect/retry results, job/inventory/ledger receipts, UI screenshots, issue links and pass/fail/unverified. A screenshot, replay or test fixture alone cannot mark this procedure accepted.

## Automated verification

`CargoJourneyTests` covers the base sequence, intermediate pickup, multiple resources sharing a station, partial load/delivery, local JSON restart, partial setter/retry, idempotency, independent inventories/rewards, one wage per leg, gaps/restart, manual conflict, limits/clamping, actual overweight/CG-warning writes, passenger ownership exclusion, authorization renewal/discard, layout/airframe mismatch, legacy guards and schema-12 compatibility. `SiteUiSmoke/CargoJourneySmoke.cs` exercises real quantity cell editing and production-bound read/suggest/prepare/apply/plan/arm controls, with 35→25→0 and fax screenshots. Synthetic tests do not qualify actual MSFS behavior.

Exact commands/results, failures, hosted receipts and remaining steps belong in the [handoff](handoffs/multistop-cargo.md). Do not overwrite failed verification directories or infer live acceptance from a green build.
