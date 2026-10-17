# Employed pilot hourly wages V1 — 2026-10-17

## Existing architecture and implementation

Previously, the recurring-demand generator assigned a fixed payment to each company job, based on a prototype cargo weight multiplier. Both `CompleteDelivery` and the physical cargo-transfer completion branch settled this fixed requirement amount through `FinancialTransactionKind.JobPayment`. Delivery, customer inventory, job state, payment and cached balance were committed in one validated economy revision. The ledger, not the cached balance, was authoritative.

Those transaction paths remain authoritative. `PilotWagePayments` now selects hourly wages for configured employer work; freelance/player logistics contracts retain their existing settlement path and contract reward. Company demand definitions no longer calculate a cargo-based payment; their legacy payment field is zero for new offers. Old requirements and completed payments retain their original data.

## Configuration and pay

`CareerCatalogue.json` is the source of employer rates. `CareerEmployer.HourlyWage` also supplies the onboarding and career/settings wage text. Company setup resolves existing career employer IDs and updates the existing business metadata with `PilotHourlyRate`. This is policy metadata, not a bank account: no employer funds, revenue, profitability or costs are simulated.

| Employer | Airborne hourly rate |
| --- | ---: |
| Eros Community Aviation — Missionary | $400 |
| Namibia Safari / Conservation (temporary) | $400 |
| Fly Eros — Industrial | $650 |
| Skeleton Coast Petroleum — Offshore | $800 |

`amount = Round(verifiedAirborneTicks / decimal(TimeSpan.TicksPerHour) × hourlyRate, 2, AwayFromZero)`.

No whole-hour, minute or second rounding occurs before payment. Final amounts use cents, with midpoint values rounded away from zero. Examples: 30 minutes at $400/$650/$800 earns $200/$325/$400; 47 minutes at $650 earns $509.17. The timer uses real UTC observation intervals supplied by existing validated telemetry, not simulator-calendar acceleration or offer/acceptance wall-clock duration.

## Authoritative duration

`FlightAttemptTracker` remains the only application producer of delivery evidence. `FlightAttempt` now persists `VerifiedAirborneTicks`, `WageObservedUtc` and a shared `WageFlightId` alongside the existing flight facts.

An interval counts only when both ends are valid airborne samples for the loaded aircraft, timestamps advance, and the gap is no more than the existing two-second continuity limit. Existing aircraft identity, discontinuity, route and terminal-state safeguards execute before wage accumulation. Ground, taxi, loading/unloading, accepted-but-not-loaded, invalid telemetry, disconnect and restart gaps do not accrue time. The boundary samples crossing takeoff/landing are conservatively excluded; precision inside the airborne run is retained in .NET ticks.

The continuation sample is transient, so restart cannot bridge an unobserved gap. The persisted observation watermark prevents replaying old intervals. Duration is saved through the existing flight-attempt persistence path; it is frozen after arrival proof and completion. Developer reset clears duration along with incomplete flight observations, without changing cargo or money. No second flight timer or independent flight-history store was introduced.

## Completion, ledger and duplicate prevention

Wages are paid at successful delivery, including simulated unloading and verified physical cargo unloading. The saved attempt supplies wage evidence; single-attempt completion explicitly saves the proof/duration before the economy transition. Partial unloads do not pay until the shipment is fully completed.

`FinancialTransactionKind.PilotWage` distinguishes wages from freelance revenue. Each entry preserves employer ID/name, payment job, delivery evidence ID, flight reference, verified airborne ticks, hourly rate, amount, currency key and timestamp. The existing transaction timestamp follows verified completion-proof time. Jobs → History displays the calculation, timestamp and flight/payment-job references.

Jobs sharing the existing flight batch share one wage identity and duration. The first successful completed shipment creates the wage transaction; subsequent completions reference the same immutable receipt. Wages are therefore paid once for the flown time, not multiplied by shipment count. A deterministic wage transaction ID, unique flight receipt, completed-job validation and existing delivery/command idempotency prevent repeat payment. Repeated completed batch actions may be refused by the existing guard; they cannot add money.

The money is a controlled system source. There are no employer accounts or balances to debit, and no wage-to-cargo-value calculation. Ledger-derived player balance is still checked against its transactions.

## Presentation and dollars

- All four employer cards and their selected details show the configured hourly rate.
- Career / Settings and the Jobs banner show the same wage.
- Company-only Available/Reserved lists hide the compensation column. When freelance contracts are also present, those rows retain their dollar rewards; company rows show an hourly rate, never a job price.
- Current flight shows recorded airborne time. History shows the wage calculation; historical fixed-price completions retain their actual old payment amount.
- `DollarDisplay` provides invariant dollar formatting with grouping and meaningful cents: `$400`, `$2,500`, `$509.17`.
- Balances, logistics payout labels, cargo purchase/sale quotes, reserved-funds summaries and founding costs/errors now use dollars.

The internal `TPC` currency key, old fixture property names and historic records remain unchanged to avoid a needless redenomination migration. They are not shown as the player-facing currency. This is a display denomination change: 2,500 remains 2,500, with no exchange-rate conversion. Diagnostic/raw JSON exports still contain internal keys.

## Existing saves and migrations

Economy schema **9** adds nullable wage policy/receipts; flight-attempt schema **5** adds duration/checkpoint/flight identity. Existing migration readers validate and atomically rewrite supported older documents. Corrupt/future data is preserved and refused. Existing careers, aircraft/loading records, airports, inventories, jobs and ledger amounts are not reset.

**Old attempts have zero accumulated duration because older records did not measure it.** No duration is inferred from takeoff-to-arrival timestamps. An active attempt can accrue new valid intervals after upgrade; an already-qualified old attempt has no retroactive airborne wage. A new flight is recommended for the acceptance test. Previously completed fixed payments are never recalculated or paid again.

## Verification

23 new cases in `PilotWageTests` cover four configured rates and shared UI values; prorated examples including 47 minutes; real tracker 30-minute completions at $400/$650/$800; ground/invalid/wrong-aircraft/non-advancing/gap/disconnection/restart exclusions; fractional seconds; no pre-delivery wage; grouped physical delivery and duplicate refusal; persistent completion recovery; legacy numerical balances; old attempt migration without invented duration; presentation distinction; and ledger audit tampering. Existing migration assertions and the company delivery test were updated for the intentional new schema/compensation behaviour.

Focused wage/company/flight tests: **92 passed**. Complete solution: **695 passed**, zero failed/skipped. Release solution build: zero warnings/errors. Isolated SiteUiSmoke and LoadingWpfSmoke passed. Onboarding and company-only Jobs screenshots were inspected. No live MSFS wage flight was performed; personal saves and simulator state were not altered during verification.

Normal self-contained Windows x64 distribution rebuilt with `scripts/Publish-Development.ps1 -CreateDesktopShortcut`, at `dist/FrontierPilot/FrontierPilot.exe`. Generated binaries remain untracked. See [verification evidence](../evidence/employed-pilot-wages/verification.md).

## Manual test

1. Open Frontier Pilot from the normal desktop shortcut. Existing careers work; a fresh/reset career is optional.
2. Choose Missionary and verify `$400 / flight hour` on its card, in settings and on Jobs.
3. Run the airport lookup if necessary. Verify company-only offers have no per-job reward column.
4. Accept work, select/confirm an appropriate company aircraft and load cargo normally.
5. Taxi and wait: recorded airborne time must remain zero. Fly about 30 minutes with Frontier connected; the current flight shows its measured duration.
6. Complete normal arrival and unloading. Check roughly $200 credited, based on measured duration, and the calculation/reference in Jobs → History.
7. Restart/reconnect/revisit the completed job. The balance must not increase again.
8. Repeat with Fly Eros ($650/hr) or Offshore ($800/hr). Airport-based Offshore operations only; H125 remains disabled.
9. Optional: carry two compatible company shipments on one route; confirm one flight wage, not double pay. Check a freelance/player contract still shows and settles its agreed reward.

## Significant files and deferred scope

Core: `PilotWages.cs`, `Economy.cs`, `FlightAttempt.cs`, and the founding-cost display in `ProspectingCamp.cs`.

Workflow: `CareerCatalogue.json`, `CareerOnboarding.cs`, `CompanyEmployment.cs`, `FlightAttemptTracker.cs`, `FlightAttemptPersistence.cs`, `EconomyPersistence.cs`, `EmployedCargoJobs.cs`, `ProspectingOperations.cs`.

Desktop: `CareerScreen.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`. Tests: `PilotWageTests.cs`, `CompanyEmploymentTests.cs`, `FlightAttemptTests.cs`.

Safari tips, profiles, progression, bonuses/penalties, duty/block pay, minimum pay, player-company employment, NPC finances, costs, maintenance, ownership/markets, financing, insurance and taxes remain unimplemented. No passenger or generator redesign was introduced.
