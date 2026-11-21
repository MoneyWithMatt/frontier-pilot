## Summary
Pilots can return selected cargo at its origin while keeping the job accepted, or cancel an ordinary cargo job without financial penalty anywhere during playtesting. Loaded cancellation completes only after guarded physical unloading and retained station readback. Other consignments remain aboard; remote unloaded cargo is abandoned without delivery credit or completion pay.

## Specification
[Approved cargo cancellation specification](docs/cargo-cancellation.md). Based on d7332f7 of codex/namibia-playtesting, including existing Career Hub, cargo, passenger and fleet development. This is a stacked development branch; do not merge existing PRs or master automatically.

## Test results
Implementation: 4d1f64f5a820316d231887bbf013e9cc2c1cb11b. Local PowerShell verification BuildTest: 1,040 passed / 0 failed / 0 skipped; Release solution and SiteUiSmoke compilation clean. Hosted Windows build, full regression, WPF, guards and package: https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875880218 passed: 1,040 xUnit / 0 failures / 0 skips; 236 SiteUiSmoke assertions, 7 LoadingWpfSmoke assertions, 9 guard scenarios, synthetic CLI review and packaging.

## Persistence / migrations
Economy schema 17 adds optional cancellation receipts, pending/history annotations and aborted-leg timestamps. Existing validated atomic migrations preserve jobs, stock, money and cargo ownership. No personal saves migrated during development.

## Simulator verification and limitations
Synthetic verification only. Exact artifact/scenario approval and stopped-aircraft origin/remote unloading acceptance remain required. Passenger and player-company contract cancellation are not implemented. Existing mixed-load restrictions and physical aircraft/fuel/readback safeguards remain.

## Development artifact
[Windows artifact 14695932886](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/44875880218/artifacts/14695932886); all 11 bundled checksums verified. See docs/handoffs/cargo-cancellation.md. Download is not approval to execute on the gaming PC. Scoped GitHub PR creation returned 403 Resource not accessible by integration; no credential or permission bypass attempted.

## Short playtesting checklist
- [ ] Return one consignment at origin, keep job accepted and unrelated cargo aboard.
- [ ] Cancel returned job; confirm origin stock restored and zero penalty/pay.
- [ ] Cancel loaded consignment using outside confirmation then cockpit apply; check retained baseline and other cargo.
- [ ] Restart/retry partial unload; no duplicate station removal or stock release.
- [ ] Cancel away from origin; abandoned mass has no destination credit, reserved origin stock is released.

