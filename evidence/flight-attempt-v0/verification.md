# Flight Attempt V0 verification — 2026-09-10

Automated coverage exercises disconnected/airborne/moving/outside-origin refusal, ten-second origin qualification, idempotent load and InTransit transition, restart recovery, missing airborne evidence, movement and 10-NM evidence, both configured destinations, timer reset across disconnect/invalid/gapped samples, wrong aircraft/route/job/shipment refusal, UI evidence isolation and exactly-once inventory/payment across restart.

Focused Flight Attempt tests: 12 passed.

Focused Economy and Flight Attempt tests: 30 passed.

Full Release tests: 402 passed. The full Release solution build completed with zero warnings and zero errors.

The self-contained Windows x64 application was republished and the Desktop shortcut refreshed. Final `FrontierPilot.exe` SHA-256: `22C3CB4FE1C23D44F9E5D463E0FC9ED5C88599AA1B6109774D9496B9AA6B1D43`. A dry-run build launch during verification opened a responsive Frontier Pilot window; the launched test processes were then closed.

## Live Eros-to-Arandis acceptance

Matt completed the first live delivery in MSFS on 2026-09-10. The application showed a Piper PA28-236 with matching recorded and observed identity, on-ground arrival 0.01 NM from Arandis, airborne and movement evidence, a 126.29 NM maximum distance from Eros, `ReadyToUnload`, `DeliveryCommitted`, **Delivered and paid**, and a replacement offer. This accepts the core Eros-to-Arandis load/flight/arrival/unload/payment path.

The same observation revealed a post-terminal diagnostics bug: continued telemetry emitted `OriginQualificationInterrupted` with `reason=Live aircraft telemetry received` after `DeliveryCommitted`. The tracker now treats `DeliveredAndPaid` as a hard qualification/event boundary. Regression coverage sends telemetry immediately and repeatedly after delivery and after restart, verifies live values still refresh, verifies no new qualification/progression events, and verifies exactly-once inventory/payment.

Terminal-lifecycle focused Flight Attempt tests: 25 passed.

Full Release tests: 441 passed. The Release solution build completed with zero warnings and zero errors.

The self-contained Windows x64 application was republished and the existing Desktop shortcut refreshed. Corrected `FrontierPilot.exe` SHA-256: `E79D565B53043F31D3256EDA001EFC0FCEE23D944C5C397B62FBFCA9A75FCD82`.
