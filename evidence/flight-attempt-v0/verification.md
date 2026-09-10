# Flight Attempt V0 verification — 2026-09-10

Automated coverage exercises disconnected/airborne/moving/outside-origin refusal, ten-second origin qualification, idempotent load and InTransit transition, restart recovery, missing airborne evidence, movement and 10-NM evidence, both configured destinations, timer reset across disconnect/invalid/gapped samples, wrong aircraft/route/job/shipment refusal, UI evidence isolation and exactly-once inventory/payment across restart.

Focused Flight Attempt tests: 12 passed.

Focused Economy and Flight Attempt tests: 30 passed.

Full Release tests: 402 passed. The full Release solution build completed with zero warnings and zero errors.

The self-contained Windows x64 application was republished and the Desktop shortcut refreshed. Final `FrontierPilot.exe` SHA-256: `22C3CB4FE1C23D44F9E5D463E0FC9ED5C88599AA1B6109774D9496B9AA6B1D43`. A dry-run build launch during verification opened a responsive Frontier Pilot window; the launched test processes were then closed.

No MSFS session was launched or controlled for automated verification. Live lifecycle acceptance remains with Matt using the procedure in `docs/flight-attempt-v0.md`.
