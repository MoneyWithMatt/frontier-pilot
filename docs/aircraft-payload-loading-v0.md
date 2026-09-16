# Aircraft Payload and Loading V0

Status: MSFS-integrated loading was accepted by Matt on 2026-09-15. Desired-state partial loading/unloading and the Station 5 correction are automated-test verified as of 2026-09-16 and require the manual checks below.

## SDK 1.2.4 findings

The installed SDK at `C:\Users\mattr\Desktop\MSFS 2024 SDK` was inspected read-only. `PAYLOAD STATION COUNT`, `PAYLOAD STATION NAME:n`, and writable `PAYLOAD STATION WEIGHT:n` expose up to 15 indexed stations; payload writes use pounds and are converted explicitly to the economy's canonical kilograms. MSFS does not expose dependable authoritative station maxima, arms, or a maximum-payload value for every default and third-party aircraft. Missing limits remain labelled unavailable and are never inferred from default weights.

`EMPTY WEIGHT`, `MAX GROSS WEIGHT`, `TOTAL WEIGHT`, `CG PERCENT`, `CG FWD LIMIT`, and `CG AFT LIMIT` provide factual values when the aircraft publishes them. The desired-state path refuses a reported station, maximum-payload, or maximum-gross violation. Planned CG remains unavailable when the simulator does not expose sufficient station geometry; current simulator CG and published limits remain visible.

Fuel uses reported tank quantity/capacity, `FUEL WEIGHT PER GALLON`, and indexed engine-combustion state. Writes use US gallons and require every detected engine to be off. Modern or third-party fuel systems may not enumerate through the eleven legacy named tank SimVars; unknown engine or tank data fails closed.

## Desired-state cargo architecture

The Aircraft Loading page is a general aircraft state editor. A nonterminal accepted shipment can make reserved cargo available at its origin, but the mission does not own the station editor. Terminal Completed, Cancelled, Forfeited, or skipped jobs no longer present cargo as available. A nonterminal manifest can still show Frontier-managed cargo physically aboard after a job forfeiture.

Each station plan stores its inferred non-Frontier baseline and desired Frontier allocation. The target is always `baseline + desired Frontier cargo`; changing 100 kg to 40 kg is a verified -60 kg physical delta and never removes the baseline. Desired cargo may range from zero through the shipment quantity remaining after verified destination deliveries. Cargo can be loaded in multiple verified steps, such as 0 -> 60 -> 100 kg. Additional cargo is permitted only at the shipment origin and never beyond actual reserved availability.

At the verified destination, a negative delta adds only the physically removed quantity to destination inventory. Partial delivery is recorded without payment. When the complete shipment quantity has been delivered, the tracker-supplied immutable evidence completes the existing atomic inventory/job/payment transaction exactly once. Away from the origin and destination, the UI requires an explicit forfeiture confirmation before any write. Confirmation physically applies the requested reduction, records its coordinates, terminates the job and shipment without payment, and prevents later completion. Unloaded reserved cargo that never left the origin is released through an inventory-audit transaction.

The economy format is schema 5. It adds explicit loaded, delivered and forfeited shipment quantities plus an idempotent cargo-transfer log. Existing schema 1-4 states migrate atomically: historical Loaded/InTransit quantities remain aboard, historical Delivered quantities remain delivered, all identifiers and ledger entries are preserved, and corrupt/future documents remain untouched. Aircraft-loading storage is schema 2 and adds shipment totals, delivered quantities, desired-state intent and transfer-command recovery data.

## Apply, verification and recovery

An operation follows `fresh MSFS read -> compute delta -> validate location/access/limits -> confirmation if destructive -> persist intent -> write -> read back -> persist simulator-applied state -> atomic economy transfer -> commit manifest`. The persisted prior and desired cargo quantities distinguish simulator state from economy state during recovery. Replaying a simulator-applied intent reuses the same command, recognises station targets already present, and cannot add cargo, inventory, or payment twice.

The live Station 5 failure was not caused by a station-index or kilogram/pound conversion error. The saved manifest recorded a verified 100 kg target on Station 5 with a 0 kg non-Frontier baseline. A later MSFS read reported Station 5 back at 0 kg and reconciliation correctly classified the cargo as missing. The former dedicated unload code nevertheless subtracted the saved 100 kg from the live 0 kg, causing “Station 5 no longer contains enough weight.” The desired-state path treats that live 0 kg as the physical baseline for a requested zero state, verifies that nothing needs subtracting, and then commits the inventory consequence. Future mismatches report station index, inferred baseline, managed allocation, expected target and observed read-back.

The dedicated mission-specific unload button is no longer the primary UI. The player types the desired Frontier cargo per station and chooses **Apply desired state and verify**. Fuel remains on its own tab. Automatic distribution and new simulated loading were removed from the UI; existing simulated manifests remain readable and retain their explicit compatibility recovery path.

Typed station values are editor drafts. Live telemetry and qualification refreshes update factual aircraft readings without replacing an unfinished draft with the committed manifest. The summary displays total job cargo, cargo available to load at the current location, cargo aboard, the requested aboard amount and cargo already delivered. At the origin, loading moves quantity from available to aboard; at the destination, unloading moves quantity from aboard to delivered. Cargo can be physically unloaded elsewhere only through the explicit forfeiture confirmation.

## Other retained corrections

New flight attempts persist `min(10, max(0.5, directRouteDistanceNm * 0.5))`. Existing schema-1/schema-2 attempts retain their historical 10 NM requirement. Loading records `Loaded`; InTransit begins on proven departure, while all airborne, movement, arrival, identity and stationary thresholds remain unchanged.

Sandy Dirt emits the visible native `DIRT` fallback rather than `SAND`. It remains clearly labelled temporary and affects an airport only after the preset is selected and rebuilt.

## Manual MSFS test

1. Start MSFS with the previously proven Piper PA28 and launch Frontier Pilot from the Desktop shortcut. Accept an Eros job, park within 1.5 NM of its origin, remain at or below 5 kt for ten seconds, exit into walkaround, and stay within 100 m.
2. Open **Aircraft Loading**, select **Read live aircraft**, and note the non-Frontier station weights. Enter 60 kg total across any reported writable stations and apply. Confirm MSFS payload and gross weight rise by 60 kg.
3. Add the remaining shipment cargo without changing the original 60 kg unintentionally. Apply and confirm the total managed cargo equals the shipment quantity. Restart Frontier Pilot and confirm the manifest returns without another payload write.
4. Before departure, reduce one station and apply. Confirm only that Frontier delta leaves and the reserved remainder remains available at the origin. Restore the desired full load manually.
5. Fly the existing evidence-qualified route. At the destination, stop and exit nearby. Reduce the desired cargo by part of the shipment and apply; confirm payload falls by that amount, destination inventory records only the partial amount, and there is no payment.
6. Set all Frontier station values to zero and apply. Confirm remaining cargo leaves MSFS, the job completes, and exactly one payment is recorded. Reapply/restart and confirm neither inventory nor payment repeats.
7. On a disposable job, request an unload away from both endpoints. Cancel the warning and confirm nothing changes. Repeat, explicitly confirm forfeiture, and verify the requested physical delta occurs, the job cannot later complete, and no payment appears.
8. Reproduce the old Station 5 condition only if convenient: load Station 5, let MSFS return that station to its non-Frontier baseline, refresh until the manifest says cargo is missing, then request zero. Confirm it reconciles without attempting a negative station weight and displays expected/observed detail on any mismatch.
9. With engines off, set one fuel tank within reported capacity and verify read-back. Start an engine and confirm fuel Apply is blocked. No fuel purchase or fuel economy is implied in V0.

Do not claim the desired-state unload path or third-party-aircraft behavior is accepted in MSFS until these checks are completed.
