# Economy Core V0 — persistent shipment and payment integrity

## Implemented boundary

Core now contains a data-driven economic aggregate for locations, businesses, resources, inventory, customer needs, funded transport requirements, employed players, jobs, shipments and financial transactions. `EconomicLocationKind` distinguishes gateway, mine, community and lodge data; the kernel contains no industry-specific production logic.

This milestone has no WPF page, SimConnect input, real Namibia activation, weather model or flight-validity rule. `VerifiedDeliveryEvidence` is the explicit boundary a later flight/location component must satisfy. V0 validates evidence identity, route, job and shipment consistency; it does not claim that a flight occurred or decide how evidence is earned.

`EconomyPrototypeSeed` exists for tests and later isolated development only. Every location is `PrototypeOnly`, names say temporary test data, IDs are deterministic, and the `TPC` currency means Temporary Prototype Credits. Quantities and payments are fixtures, not balancing decisions. The seed is never automatically written by the current application.

## Transaction behaviour

Accepting an offered job atomically:

1. checks its funded requirement and available origin stock;
2. subtracts the allocated quantity from available origin inventory;
3. creates one reserved shipment with a deterministic ID;
4. marks the requirement reserved and the job accepted;
5. advances the world revision once.

Replaying acceptance returns the same state without reserving more cargo.

Completing a delivery atomically:

1. validates the external evidence against the accepted job, shipment and route;
2. moves the shipment to `Delivered` at the customer;
3. adds its quantity to customer inventory;
4. marks the requirement and job complete;
5. records one job-payment transaction keyed by job ID;
6. recalculates the player's cached balance from the financial ledger;
7. advances the world revision once.

The financial ledger is authoritative. Loading rejects a cached balance that differs from the sum of ledger credits and debits. Completed jobs must have one matching delivery and one matching payment. A payment without delivery, delivery without payment, mismatched evidence, or second payment is invalid. Replaying the same delivery evidence after restart returns the already committed state.

Customer demand is a target minus available customer inventory. Fleet capacity assessment is an optional, read-only dispatch recommendation. It neither creates nor removes the underlying need and explicitly makes no runway, weather or flight-safety judgment.

## Persistence

`LocalEconomyStateStore` defaults to:

`%LOCALAPPDATA%\FrontierAirportBuilder\desktop\economy\economy-state.json`

Schema 1 stores one versioned `EconomyWorld` aggregate. The structure is intended to grow as a collection-based world rather than as one hard-coded job. Each mutation is validated, serialized to a new file in the same managed directory, flushed to disk and moved over the prior document. Therefore readers see the complete prior revision or the complete next revision; shipment, inventory and payment are never separate file commits.

An in-process gate and path-derived named Windows semaphore serialize mutations. Only the dedicated managed filename is accepted. Unknown JSON members, oversized documents, corrupt JSON, unsupported schemas, broken references, duplicate IDs and inconsistent economic state fail with a preservation message. Initialization refuses to overwrite any existing document, including an unreadable one. There is deliberately no automatic reset facility yet.

## Verification

Focused tests cover deterministic system employment, the same kernel serving mine/community/lodge data, demand independent of an incompatible fleet recommendation, one-time reservation, one-time delivery and payment, authoritative-ledger validation, invalid evidence, impossible partial economic states, persistence across store instances, restart idempotency, corrupt/unsupported/inconsistent preservation and failure after the temporary file is flushed but before atomic replacement.

No simulator or UI acceptance applies to this milestone.

## Next decisions

- Choose and verify the first active Namibia customer; shortlist entries remain research candidates.
- Decide the temporary cargo mass/quantity and payment shown in the first playable job.
- Decide whether employed compensation is per-delivery pay, salary plus bonus, or another model. V0 only supports a funded payment amount.
- Define job loading, cancellation, expiry and reservation-release rules.
- Define the later component that produces `VerifiedDeliveryEvidence`; V0 must not be weakened to manufacture it.
- Decide when economic time advances. V0 has timestamps but no economic clock or offline progression.
