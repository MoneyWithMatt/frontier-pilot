# Prepared draft PR — Activate Namibia exploratory careers and player airport reviews

Head: `codex/namibia-playtesting`; target: `master`; draft: true. GitHub connector creation failed with HTTP 403, Resource not accessible by integration. Check for an existing PR before creating one; nothing merged.

## Summary

All four Namibia starting careers can offer plausible curated airport work before individual live airport acceptance. The existing demand engine selects six cargo destinations on its saved cadence, retaining original resources/quantities, shared personnel, physical fleet, loading, tracker proof and hourly wage services. Career Job Board adds destination status/cautions and persistent per-aircraft player visit/problem records. Problems withhold future offers without removing accepted cargo/personnel or financial history.

57 dated PR #4 references are selectively imported with provenance and stable legacy aliases. FYKW/FYHV/FYHO/FYNG/FYAD and other identity/closure conflicts remain Held. Facility lookup is not runway acceptance; database keys are not invented ICAO codes. Reports cannot move route coordinates or grant aircraft/platform qualification. SF50 physical boarding and H125/platform restrictions remain.

Stacked integration: based on Career Hub `21b1f78`, which includes PR #10 and its unmerged dependencies. Review this feature after that commit and review dependencies before merging. Other branches/master are preserved.

## Specification

[Approved scope](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/specs/namibia-playtesting.md), [implementation/player procedure](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/namibia-playtesting.md), [handoff and failure history](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/handoffs/namibia-playtesting.md).

## Test results

Clean implementation `38a6bf76595a0dc4ee12eaebb92d59e6c9d8ab79`: [full hosted Windows CI passed](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167). 1,009 xUnit / 0 failures / 0 skips, 198 SiteUiSmoke and 7 LoadingWpfSmoke assertions, 9 simulated verification-guard scenarios, historical CLI assertions and self-contained Windows packaging. Zero build warnings/errors. Exact commands, source states and retained earlier failures are in the handoff. Synthetic screenshots were inspected, including accepted Aminuis cargo retained after a problem report, readable issue choices and visible save confirmation.

## Persistence / migrations

Economy schema 15 appends explicit ExploratoryAirport status and preserves prior jobs/inventory/ledger/flight/payload ownership. Airport cache schema 2 reads schema 1 and retains technical conflicts. Separate `airport-visits.json` schema 1 stores append-only player observations with scoped aircraft context and corruption/atomic-write safeguards. Normal runtime upgrades are versioned; development did not access or modify Matt's personal saves.

## Simulator verification and limitations

No live MSFS acceptance, actual airport usability test, scenery installation, Package Tool or new executable run on Matt's PC. Automated fixtures and WPF screens are synthetic. Safari passenger profiles, SF50 physical boarding and H125/platform operations remain restricted. Cancellation/re-routing/disposition and versioned technical route repair are separate follow-ups; incomplete flights are not paid. Active-employer switching remains unsupported.

## Development artifact

[Exact 38a6bf7 Windows bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167/artifacts/13249557417); [screenshots/logs/TRX/receipts](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41674240167/artifacts/13250126473). Build metadata/checksums identify exact source. Download is not approval for autonomous execution on the gaming PC. A subsequent documentation-only checkpoint runs the same pipeline; inspect the head check before review.

## Short playtesting checklist

- [ ] Choose a Namibia career; inspect varied mapped routes, distance, manifest, aircraft and destination caution.
- [ ] Accept supported unverified work without individual airport-approval prompts.
- [ ] Use existing physical loading, flight and completion controls; confirm ordinary hourly pay once.
- [ ] Record a visit for the actual tested aircraft; another aircraft remains unverified.
- [ ] Report a problem; future offers disappear while accepted custody/history remains.
- [ ] Restart and confirm knowledge persists; Held identities and unqualified aircraft remain unavailable.
