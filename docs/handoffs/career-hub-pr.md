# Prepared draft PR: Add Career Hub for employment, fleet, loading and mapped jobs

Target: master; head: codex/career-hub; draft: true. Creation blocked by connector HTTP 403. Check for an existing PR first. Nothing merged.

## Summary

Career brings employer information, authorised aircraft, condition inspection, loading and work into Overview, Fleet, Find Work, Job Board and Create Jobs. Fleet and accepted jobs open the same original loading controls with the persistent GUID; existing transport, payload, flight-proof, wages and economy services retain authority. The unified board maps existing cargo, personnel and observation routes/areas, with unqualified platforms unavailable.

Integration: based on PR #10 head d5d4f23b8f6fc7a13167b8f55a2398a3d157cd49, including unmerged fleet/loading/passenger work. Other branches/worktrees preserved. Review Career changes after that commit and dependencies before merging.

## Specification

[Approved scope](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/career-hub/docs/career-hub.md), [handoff and failure history](https://github.com/MoneyWithMatt/frontier-pilot/blob/codex/career-hub/docs/handoffs/career-hub.md).

## Test results

Clean implementation ea32975ea8ef9a1b1b1941aec85969053627bc98: [Windows CI passed](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204). 981 xUnit / 0 failures / 0 skips; 7 loading WPF and 185 site/Career WPF assertions, 9 simulated verification guards, historical CLI and self-contained Windows packaging. Zero build warnings/errors. Exact commands and preserved failures are in the handoff. Later viewport/test-only coverage runs in the subsequent branch workflow; inspect the head check before review.

## Persistence / migrations

No schema changes or personal save migration. Existing stores remain authoritative. Pending ownership and unsaved cargo edits block GUID changes. Opening a reservation does not bind/transfer it. Payload/fuel safeguards and once-only settlement are preserved.

## Simulator verification and limitations

No live MSFS test, personal save change, scenery installation or new executable run on Matt's PC. Screenshots are real WPF renders of synthetic data. SF50 physical boarding and H125/platform gates stay blocked. Visible passengers/mixed transport are not newly qualified. Maintenance is inspection-only. Employer switching, aviation-company authority and multiplayer hiring remain unavailable. Create Jobs reuses authorised owned-site logistics only, not NPC contracts.

## Development artifact

[ea32975 Windows bundle](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204/artifacts/13229275192); [synthetic screenshots/diagnostics](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41630988204/artifacts/13228037288). Checksums/build metadata identify the build; download is not local execution approval.

## Short playtesting checklist

- [ ] Actual employer/base/pay/fleet display; no invented physical location/health.
- [ ] Fleet and accepted jobs reach the same loading controls and GUID.
- [ ] Condition inspection exposes no repair/healthy-reset action.
- [ ] Board maps routes/areas, retains selection, accepts supported work and displays progress.
- [ ] Passenger handling and legacy recovery remain reachable at compact desktop heights.
- [ ] Employer switching, unqualified aircraft and unauthorised company contracts remain blocked.
