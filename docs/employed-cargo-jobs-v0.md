# Employed Cargo Jobs V0 — Eros selection and acceptance

## Player-visible slice

The WPF application now has a Jobs tab. It displays the two deterministic system-employment offers originating at Eros: General Supplies for Uis and Arandis. A selected row shows the authored customer purpose, published position, reported direct-flight-time scale and manual MSFS verification method. The screen makes no flight-safety or aircraft-suitability claim.

The prototype values are configurable through `EmployedCargoPrototypeOptions`: 120 kg / 240 TPC for Uis and 100 kg / 200 TPC for Arandis. `TPC` means Temporary Prototype Credits. These figures are test inputs, not permanent game balance. The later repeatable-cycle milestone replaced the original 500 kg opening fixture with audited just-in-time gateway funding.

## Economy and transaction path

`EmployedCargoPrototype` creates one generic system cargo employer at Eros, one employed player, one General Supplies resource and a funded need/requirement/job for each verified active customer. Catalogue stable IDs are used throughout. Offer creation filters through the catalogue lifecycle state; it does not inspect aircraft or weather.

The UI depends on `IEmployedCargoJobs`, whose public operations are load and accept. `EmployedCargoJobsService` delegates acceptance to the existing `EconomyService`/`EconomyKernel` transaction. Accepting subtracts source stock, creates the reserved shipment and changes requirement/job state in one persisted economy revision. The kernel and employee-facing service prevent a different second job while the player has an accepted shipment. Replaying the same acceptance is idempotent.

The accepted job and shipment reload from `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\economy-state.json`. Existing atomic-write, strict validation and corrupt/unsupported-data preservation remain unchanged. An existing economy world with an unsupported identity is reported and preserved rather than replaced.

## Deliberate boundary

Acceptance itself cannot update destination inventory, create a financial transaction or pay the player. Flight Attempt V0 now places those changes behind a tracker-owned `VerifiedDeliveryEvidence` and an explicit Unload action. The Jobs view model cannot manufacture evidence. The ledger remains authoritative and empty until verified unloading.

No weather or aircraft-suitability filtering, cancellation, expiry, recurring demand, production, offline progression or Namibia-wide generation was added.

## Verification

Automated tests cover catalogue provenance/lifecycle validation, eligible-only offer generation, exact deterministic Uis/Arandis offers, one-time cargo reservation, second-job refusal, persistence/restart restoration, the narrow UI boundary and absence of payment/customer inventory changes. This milestone has no simulator flight-completion acceptance because that lifecycle is intentionally absent.

## Decisions before flight-attempt tracking

- Define the exact conditions and observations that begin and end a flight attempt.
- Decide how a reserved shipment enters `InTransit`, including reconnect and app-restart behaviour.
- Define the component allowed to issue `VerifiedDeliveryEvidence` and its origin/destination tolerances.
- Decide interruption, abandonment and reservation-release rules before adding cancellation.
- Review temporary masses and payments after the first selection/acceptance usability test; do not treat them as balance commitments.
