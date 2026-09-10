# Employed Cargo Job Cycle V0

## Player loop

The Eros system employer maintains one available General Supplies assignment for each active prototype customer: Uis (120 kg / 240 TPC) and Arandis (100 kg / 200 TPC). These are temporary fixtures, not balancing commitments. The Jobs page separates available work, the single accepted or InTransit job, and the twelve most recently completed jobs. All completed records remain in the authoritative economy document.

Completing and unloading a shipment through Flight Attempt V0 makes the player eligible to accept another offer. The existing flight qualification rules and `VerifiedDeliveryEvidence` boundary are unchanged and remain pending manual MSFS acceptance.

## Recurring demand and deterministic IDs

`RecurringDemandDefinition` is persisted configuration rather than mining logic. It names a need, origin, destination, resource, preferred quantity, temporary payment, desired offer count, system funder, employer, player and enabled state. The same kernel accepts Mine, Community and Lodge destinations. A destination must still be an `ActiveCustomer`; authored catalogue verification and activation remain the earlier live-generation gate.

Board maintenance counts outstanding Open or Reserved requirements for each definition and creates only a missing slot. An accepted shipment therefore does not cause premature replacement. Fulfilment creates the next system-funded transport requirement as an explicit temporary abstraction for continuing customer demand; no consumption or elapsed economic time is inferred.

Cycle one retains the V0 IDs (`job-uis-supplies-v0`, for example). Later IDs append a six-digit sequence such as `job-uis-supplies-v0-000002`. Requirements and planned shipment IDs use the same cycle. Offered jobs carry the immutable planned shipment ID; the existing acceptance transaction materializes the Shipment record. Sequence selection derives from persisted history, so refresh and restart reproduce the same result without random IDs.

## Gateway supply and audit

Eros gateway stock is separate from Uis and Arandis customer inventories. `EconomyJobBoard` calculates stock already promised to available offers and introduces only the shortfall required to fund a missing offer. Each addition creates an immutable `SystemGatewaySupply` inventory transaction. This models goods reaching the gateway through economic activity outside the current playable simulation. It does not represent manufacturing, imports, market prices or unlimited unaudited stock.

Acceptance records a negative `ShipmentReserved` inventory transaction and verified delivery records a positive `DeliveryReceived` transaction. This inventory audit ledger is separate from the financial ledger. It cannot alter player money. The financial ledger remains the sole balance authority and the cached balance continues to be checked against it.

This feature introduced economy schema 2. The current schema 3 preserves it through an explicit schema-1/2 migration. Missing legacy audit amounts receive clearly labelled `LegacyOpeningBalance` checkpoints without changing quantities or historical records. The first Jobs load attaches recurring definitions to a pre-cycle employed-cargo aggregate in one economy revision. Writes use the existing flushed temporary-file and atomic-replacement path; migration failure leaves the earlier document intact. Corrupt, inconsistent and unsupported documents are still preserved and reported.

## Transaction and concurrency behavior

Initial creation, page open/restart and **Refresh jobs** all run the same idempotent maintenance operation. A successful delivery invokes maintenance inside the existing atomic economy revision after cargo, requirement, job, payment and balance changes are prepared. Replaying delivery exits through the existing evidence idempotency path and cannot create another payment or replacement.

Local store mutations remain protected by an in-process gate and a path-derived cross-process semaphore. Contending Frontier Pilot instances wait for the current economy mutation, then reload and reevaluate the board. Only the first sees a missing slot.

## Deferred decisions

- What causes customer inventory consumption and when it occurs.
- Whether later demand is event-driven, time-driven or market-driven.
- Reservation release, cancellation and expiry policy.
- Funding limits, prices and permanent progression balance.
- Player businesses, production, trading and additional destinations.

No economic clock, offline progression, weather decision, route-safety claim or change to Flight Attempt V0 was added.

## Automated verification

On 2026-09-10, the focused economy/employed-cargo set passed 24 tests. The complete Release suite passed 430 tests, and the Release solution build completed with zero warnings and zero errors. The self-contained Windows x64 distribution was then republished to `dist/FrontierPilot/FrontierPilot.exe`. These checks do not constitute manual MSFS acceptance of Flight Attempt V0.
