# Feature: Immediate company work refresh and fuel-plan recovery

Status: Approved
Approved by: Matt's explicit choice to generate fresh available jobs when clicking Refresh work; subsequent fuel-conflict screenshot on 2026-11-04.

## Objective
Make Refresh work replace available NPC-company offers immediately while preserving accepted work. Recover the reported cargo fuel conflict without discarding cargo intent or writing fuel.

## Gameplay behavior
Manual refresh retires only unaccepted generated SYSTEM cargo/passenger/observation offers for the current employer/player. Existing generation supplies fresh cargo, personnel and observation identities; held/problematic endpoints and capability gates remain. An audited sequence varies curated route selection and survives reloads. Internal refreshes, review actions and the background timer remain read/scheduled refreshes. Accepted/reserved/boarded/active/completed work, player contracts, cargo/passenger ownership, flight proof and money are preserved. The three-hour automatic clock is not reset.

Fuel at outside preview is not the write baseline: before the first payload write, current fuel and gross weight are read/persisted with the guarded intent. After a write starts, fuel mismatch still blocks confirmation. Explicit Recheck fuel outside validates identity, location, unchanged before/target station states and tank layout, requires existing walking-avatar authority, adopts current fuel/gross anchor and clears only pending readback certification. Targets and ownership intent remain. Apply still requires immediate guarded write/readback/retention and gross checks. No fuel write or airborne/economy proof is created. The primary button guides this recovery.

## Acceptance criteria
- Manual refresh changes available IDs immediately for all four starting employers and persists/retries once per command.
- Cargo loaded or accepted before refresh remains unchanged; no payment or loss of custody.
- Personnel reservations and active observations survive while offered records are replaced.
- Automatic/internal reloads never trigger manual replacement.
- Fuel drift before first write preserves current fuel; after write, explicit outside recheck and retained readback are required.
- Conflicting payload or aircraft identity cannot be adopted during fuel recovery; retry cannot double-load.

## Non-goals
No accepted-job cancellation/rerouting, new aircraft qualifications, fuel manipulation, fare/reward redesign or live simulator control.

## Relevant existing systems
CompanyEmployment, EconomyJobBoard, PassengerTransfersService, OffshoreMissionsService, CargoJourneyService and CareerJobBoardViewModel remain authoritative. No parallel mission/loading/economy subsystem.

## Persistence implications
Economy schema 16 reads prior saves and adds CompanyOfferRefreshReceipt entries with unique command ID, employer/player, sequence and time. Existing records are retained. Renewal reuses the existing cargo operation fields. No personal saves are modified during development or installation; normal runtime performs versioned upgrades when Matt opens the app.

## Required tests
Four-career refresh/restart/retry, accepted cargo preservation, personnel/observation preservation, held/problem exclusions, lost commit response, schema-15 migration/future-file preservation, actual WPF Refresh button, fuel drift/recovery/identity conflicts and duplicate mass prevention. Full Windows pipeline.

## Simulator testing requirements
Synthetic tests cannot establish live fuel/loading behaviour. Matt should keep fuel stable during recovery, walk outside near the aircraft, choose Recheck fuel outside, then board and Apply cargo. Confirm actual payload and cargo once; existing live authority gates remain. No live test is claimed.

## Open design decisions
None blocking. Immediate manual refresh explicitly approved.
## Inline fuel controls — 2026-11-04

Matt explicitly requested fuel loading beneath Aircraft loading stations. AircraftFuelControl is embedded there with the existing AircraftLoadingViewModel instance, not a second fuel implementation. It shows reported tanks, current/planned US gallons, capacity, engine/availability feedback and existing apply/keep commands. Active-career fuel writes additionally verify the selected assigned physical aircraft binding. Existing outside-avatar, stopped aircraft, engines-off, tank-capacity, operation lease and fuel readback remain. No automatic refuelling, saved-fuel restoration or fictional prices are added.
