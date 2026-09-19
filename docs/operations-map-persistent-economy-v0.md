# Operations Map & Persistent Economy V0

## Player experience

Operations is now the world-economy home. Its Mapsui map uses the existing OpenStreetMap tile layer and draws presentation-only markers from three durable sources: the authored Namibia catalogue, saved player airport definitions, and the economy aggregate's sites and operations. Gateway, SYSTEM, player-airport and player-operation markers use different shapes and colours. A linked airport and operation share one combined marker so both concepts remain discoverable without overlapping controls.

Selecting a marker opens a factual location card. Persistent inventory is shown only for gateways and player-owned operation sites. Abstract SYSTEM candidates and customers do not acquire invented inventory. An owned-operation list remains separate from the map and opens the Operation Detail view.

The current map provider is deliberately isolated in `OperationsWorldMap`. Location selection and economic state live in the Workflow/Core projects and contain no Mapsui or tile-provider dependency. OpenStreetMap remains a prototype basemap; satellite or aviation layers require a later provider/licensing decision.

## Economic boundary

`EconomicLocation`, `ResourceType`, `InventoryBalance`, and the authoritative `InventoryLedger` remain the common model. Resources use stable IDs and unit metadata. Prospecting is still a data-defined operation archetype; resources are not defined by scenery objects.

Persistent locations in V0 are:

- a system gateway (`namibia.windhoek-eros`);
- a player-owned `OperationSite` linked to a stable saved-airport definition ID.

Other Namibia catalogue locations remain abstract SYSTEM sources or sinks. Existing SYSTEM cargo jobs continue to use their established audited supply and delivery behavior.

Eros receives one idempotent authored opening-stock transaction when Operations first initializes this milestone:

- General Supplies: target opening availability 300 kg;
- Rations: target opening availability 200 kg;
- Raw Minerals: an explicit zero balance, ready to receive camp output.

These are temporary prototype fixtures. Opening stock raises an existing balance only to the configured target and is never replenished by reopening the application. Existing `SystemGatewaySupply` can still introduce only the shortfall needed by its configured prototype job rules.

## Persistent transfer primitive

`PersistentEconomyKernel.Transfer` performs a real location-to-location inventory transfer. It validates both endpoints as persistent locations, resource identity, source availability and configured operation storage capacity. One atomic economy mutation decreases origin inventory, increases destination inventory, and appends paired `PersistentTransferOut`/`PersistentTransferIn` audit entries with deterministic request IDs. Replaying the same request is idempotent.

The Operation Detail developer expander can move 1 kg of rations from Eros to the selected camp after confirmation. It exercises this real transaction and does not create cargo, a flight, payment, or a parallel test economy. It will factually refuse when the camp is full; advance a prototype day first to create ration capacity.

## Persistence and compatibility

No new document property was required, so `economy-state.json` remains schema 5. New inventory transaction kinds serialize through the existing enum converter. Writes continue to use the cross-process lock, flushed temporary file and atomic replacement. Corrupt, future-schema, or ledger-inconsistent documents remain preserved and refused.

Existing sites, operations, jobs, shipments, deliveries, financial transactions and flight attempts are unchanged. The inventory audit validation now also checks every position touched by persistent-gateway or persistent-transfer entries, even before a player site exists.

## Deliberate limits

V0 does not add prices, markets, offline progression, player-authored jobs, production chains, workers, storage buildings, fuel economics, multiplayer, or gateway import/export simulation. Direct persistent transfer is a domain foundation and developer proof; the future Job Builder must place the same inventory movement behind transported cargo and verified delivery.
