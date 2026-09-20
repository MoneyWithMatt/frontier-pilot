# Player Operation Logistics & Job Builder V0

## Implemented boundary

Owned operations can publish import and export contracts into the existing Jobs marketplace. A contract reuses `CustomerNeed`, `TransportRequirement`, `EconomyJob`, `Shipment`, aircraft loading, `FlightAttempt`, and `VerifiedDeliveryEvidence`; there is no second mission system. Contract metadata records ownership, direction, gateway quote, cargo value, pilot payout, escrow, road fallback, and terminal state.

Publication atomically removes the contracted quantity from the origin's available inventory and records a `PlayerContractReserved` inventory audit entry. The goods remain economically reserved at the origin until transport. Active reservations are displayed separately, so inventory shows total, available and reserved quantities. Cancellation is limited to unaccepted contracts and atomically returns the cargo, closes the marketplace offer, releases escrow and retains audit/history records.

## Money and pricing

The prototype has one local player financial account and no distinct operation bank account. The operation owner therefore uses that ledger-derived balance. Active `FinancialReservation` records reduce spendable funds without pretending that escrow is a completed financial transfer. Import escrow contains the gateway purchase value plus the larger of pilot payout or configured road cost; export escrow contains the larger transport commitment. Increasing a published payout adjusts escrow only if the extra funds exist.

Eros gateway prices are configuration data behind `GatewayPriceCatalogue`: Rations sell for 0.5 TPC/kg and Raw Minerals buy for 0.8 TPC/kg. These are prototype fixtures, not dynamic market prices. Cargo purchase and mineral sale are distinct ledger transaction kinds from pilot transport payment.

The local operation owner is also the only pilot identity in V0. When the owner flies their own contract, the transport payment is an auditable self-transfer with zero net balance effect. This prevents money creation until operation and personal accounts are introduced.

## Air and road settlement

Accepted aviation contracts create the ordinary reserved shipment without reserving the already-reserved cargo again. The existing load manager and flight tracker provide physical loading and verified delivery. On delivery, destination inventory, shipment/job/requirement completion, cargo purchase or sale, pilot payment, escrow closure, and cached ledger balance are committed in the same economy aggregate transaction. Replay is idempotent.

Optional road fallback uses a deliberately isolated approximation: 1.35 times straight-line distance, 55 km/h, 0.18 TPC/km, with minimum distance/duration guards. When its persisted deadline passes, an unaccepted offer leaves the aviation marketplace, its normal shipment enters `RoadInTransit`, and road cost is recorded. Destination inventory changes only at the persisted ETA. Startup and page refresh reconcile due events exactly once. The Operations developer action advances to the next saved road event for testing; it does not advance the operation day.

## Persistence

Economy schema 6 adds `LogisticsContracts` and `FinancialReservations`. Schema 1–5 documents migrate by adding empty collections while preserving all authoritative jobs, cargo, ledger, sites and inventories. The established lock, validation, flushed temporary write and atomic replacement still protect every mutation. Future schemas and corrupt/inconsistent state remain untouched.

## Deferred decisions

V0 supports only configured Eros prices and the current local owner/pilot identity. Dynamic pricing, operation accounts, bidding, private contracts, disputes, real road routing, recurring player contracts, multiplayer identity and aviation businesses remain separate milestones.

## Verification

The focused logistics tests cover real cargo and financial reservation, over-allocation and overspending refusal, payout edits, cancellation release, ordinary marketplace acceptance, owner-flown import and export settlement, wrong-location forfeiture, road activation/arrival, restart reconciliation and idempotent replay. The complete Release suite passes 547 tests, the Release solution build has zero warnings and zero errors, and the production WPF loading-control smoke test passes. Live MSFS behavior remains a manual acceptance item.

## Manual acceptance

1. Open **Operations**, select the founded Prospecting Camp, and note its Rations, Raw Minerals and ledger balance.
2. On **Operation Detail**, click **Create logistics job**. In the dedicated creation view, choose **Import**, enter a Rations quantity that fits the camp's free capacity, choose a pilot payout, and publish. Return to the operation and confirm Eros shows the quantity as reserved and the contract shows reserved funds.
3. Open **Jobs**, accept the PLAYER OPERATION offer, then use the proven Aircraft Loading and Flight Attempt workflow from Eros to the camp. Unload all cargo after verified arrival. Confirm camp inventory rises, reservations close, cargo purchase is charged, and the self-paid transport amount creates no net money.
4. Advance prototype days if necessary to create Raw Minerals and storage room, then publish an **Export** to Eros. Fly and deliver it. Confirm camp available Raw Minerals fell at publication, Eros inventory rises only on delivery, and sale revenue appears only after completion.
5. Publish a small unaccepted job with **1 minute (development)** road fallback. Press **Process next road event (developer)** once to start road transport and again to pass its ETA. Confirm it disappears from aviation offers while in road transit and destination inventory changes only on arrival.
6. Repeat the road test while closing Frontier Pilot between start and arrival. Reopen Operations and process/refresh after the ETA; confirm one completion, one road charge and no duplicated cargo or money.

The Operation Detail UI labels threshold-generated camp offers as **Automatic camp logistics (earlier prototype)** and owner publications as **Owner-created contracts**. Job creation is a separate Operations view opened from the selected operation, keeping publication inputs away from contract management and history. That view reports the current maximum Rations import and Raw Minerals export quantities, awaits the publication transaction, and shows its success or exact refusal beside the Publish action. This prevents a threshold-generated mineral export from being mistaken for a manually published contract and makes storage-capacity refusal visible before publication.
