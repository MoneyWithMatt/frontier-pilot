# Prepared draft PR — Namibia exploratory careers and optional airport reviews

Head: `codex/namibia-playtesting`; target: `master`; draft: true. Prior scoped connector creation failed with HTTP403, Resource not accessible by integration. Check for an existing PR before creating one; nothing merged.

## Summary

All four Namibia starting careers offer suitable curated airport work without individual live airport acceptance. Six-destination rotating cargo pools, existing passenger sources and Career mapped dispatch reuse the existing fleet, loading, economy, flight proof and hourly wages.

Airport verification now records MSFS representation at airport level. Runway size, surface, terrain and aircraft performance are pilot decisions. Historical compatible-aircraft lists and AircraftUnsuitable observations remain readable but cannot exclude jobs. Known identity/geography conflicts remain Held. Exact company/type identity, physical payload, SF50/H125/platform restrictions and flight/payment safeguards remain.

After committed cargo delivery or personnel disembarkation and settlement, Career presents an optional inline Yes — Verify airport / Report a problem / Skip for now prompt. Skip persists an airport acknowledgement only. Verified airports do not repeatedly prompt. Manual simulator-problem reporting remains available before completion. Genuine problems withhold future offers without touching accepted cargo/passengers, completed history or wages. No cancellation/rerouting.

Stacked integration: based on Career Hub `21b1f78`, which includes PR #10 and its unmerged dependencies. Review dependencies before merging. Other branches/master are preserved.

## Specification

[Original approved scope](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/specs/namibia-playtesting.md), [superseding pilot-responsibility refinement](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/specs/airport-verification-refinement.md), [playtest procedure](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/namibia-playtesting.md), [exact evidence/handoff](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/namibia-playtesting/docs/handoffs/namibia-playtesting.md).

## Test results

Implementation `67b093afb57c8bdb69a64eaea13e9fb0c2e9648f`: local BuildTest passed 1,013 / 0 failures / 0 skips, zero build warnings/errors. [Hosted Windows run 41681372600 passed](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600): 1,013 tests / 0 failures / 0 skips, 206 SiteUiSmoke and 7 LoadingWpfSmoke assertions, 9 guards, historical CLI review and packaging; exact source clean. Three synthetic screenshots inspected, including the optional first-visit prompt. Tests exercise actual synthetic tracker payment followed by all three answers, real passenger disembarkation timing, airport-level legacy compatibility, dismissal persistence and retained accepted work. No assertions skipped/weakened to bypass physical/proof/pay safeguards.

## Persistence / migrations

Economy schema 15 and airport cache schema 2 remain from the original slice. Airport visits schema 2 reads schema 1 unchanged on load, preserving every observation and aircraft metadata; adds idempotent per-airport prompt dismissals under existing bounded atomic/mutex/backup/corrupt-file guards. Skip does not create a review. Existing positive reports become airport-level through compatibility logic, without duplicating them. Development has not accessed personal data.

## Simulator verification and limitations

No live MSFS acceptance or actual airport-usability test. WPF/synthetic tracker checks prove software behavior only. Safari passenger profiles, SF50 physical boarding and H125/platform operations remain restricted. Cancellation/re-routing/disposition and technical coordinate repair remain separate follow-ups. No incomplete flight payment. Active employer switching remains unsupported.

## Development artifact

[New Windows build at 67b093a](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600/artifacts/13252414736); [screenshots/logs/TRX/receipts](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41681372600/artifacts/13253915604). Metadata/checksums identify exact source. Download is not approval for autonomous execution on the gaming PC.

## Short playtesting checklist

- [ ] Inspect varied mapped routes, aircraft/manifest and destination caution; accept unverified work without verification prompts.
- [ ] Use existing physical loading, flight and completion controls; hourly pay settles once.
- [ ] Optional first-visit prompt appears after completion/payment; Yes records airport representation across aircraft.
- [ ] Skip preserves all mission/economic state, persists across restart and does not pretend the airport is verified.
- [ ] Manual problem report works before completion and removes future offers while preserving accepted custody/history/pay.
- [ ] Held identities and unqualified loading/platform operations remain blocked; runway/performance decisions remain with the pilot.
## Startup freeze repair follow-up

Installed 67b093a produced Windows application hangs. Isolated regression found ten loading presentation updates republished the Career board 200 times. Fix 7de3ca0 skips unchanged collection projection, batches staged snapshots and indexes airport identity; no gameplay/schema/safety changes. Hosted run 41699597542 passes 1,014 tests, both WPF programs (including 100-update stability), guards/CLI/package. Exact artifact and pending personal-career startup acceptance are recorded in the handoff. The repaired local distribution is installed with its prior build backed up, not yet launched.

## Aircraft-selector / fresh-offer display repair

Current source 1939202 passes hosted Windows run 41702499937: 1,016 regression tests, 208 SiteUiSmoke and 7 LoadingWpfSmoke assertions, guards/CLI/package. Same-value WPF selector writes no longer recursively notify; aircraft selection does not republish ItemsSource or change physical assignment. Actual WPF selector alternates C152/Caravan twenty times. New offers sort first, scheduled refresh is tested and the saved next-update time is visible. Existing three-hour/six-hour generation/expiry semantics remain pending Matt's optional immediate-replacement policy choice. Installed exact tested build at the existing shortcut after eleven hashes/receipt/backup checks; no launch or save mutation. Latest artifact/evidence links are in the handoff.

## Immediate company work and inline fuel — final approved behaviour

Matt explicitly approved fresh available jobs on each Refresh work click, superseding the earlier pending/reload-only policy. Source cc74814 passes hosted run 41853639269: 1,029 tests, 216 site/Career and 7 loading WPF assertions, guards/CLI/package. Economy schema 16 appends idempotent refresh receipts and reads prior saves; only unaccepted generated company cargo/personnel/observation offers are replaced, preserving accepted custody, proofs, money and player contracts. Internal/automatic refresh keeps its established clock. New fuel UI beneath stations shares the existing loading VM, actual tanks/capacities and guarded fuel readback. Cargo fuel recovery retains intent, explicitly rechecks outside after a write has begun, and requires renewed retention/gross verification before confirming cargo; cargo never writes fuel. Bound physical aircraft, engines-off and avatar permissions protect explicit fuel loading. Final viewport/input screenshots inspected. Exact source/artifact and installed-distribution receipt are in the handoff; no live fuel acceptance, personal data editing, app launch or merge.
