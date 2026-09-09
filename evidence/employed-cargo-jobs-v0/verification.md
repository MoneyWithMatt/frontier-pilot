# Employed Cargo Jobs V0 verification — 2026-09-09

## Authored data evidence

- Matt reported manual MSFS 2024 inspection of FYWE, FYUS and FYAR. This user evidence is stored as `ManualInSimulatorInspection`; no automated simulator claim was made.
- Published coordinate values and URLs are stored separately as `PublishedCatalogue` data from OurAirports.
- Catalogue schema 2 / version 0.2.0 validates thirty-one total records, three verified records and exactly two active destination customers.

## Automated verification

- Focused catalogue and employed-cargo tests: 16 passed.
- Full Release solution tests: 390 passed.
- Full Release solution build: succeeded with zero warnings and zero errors.
- The self-contained Windows x64 development distribution was republished and the Desktop shortcut refreshed. Final `FrontierPilot.exe` SHA-256: `BF04EC27B1B6D500546F3ABC878DE03AD37580267C536044C0FBFB2CEFDCB3F6`.
- A dry-run launch opened a responsive `Frontier Pilot · Prototype 0.1 · DRY RUN` window; the test processes were then closed.

Tests cover guarded offer creation, deterministic Uis/Arandis values, idempotent reservation, second-active-job refusal, restart persistence, destination inventory/ledger immutability at acceptance and the absence of a UI delivery/payment action.

## Acceptance boundary

The Jobs selection and acceptance UI has not yet been manually exercised by Matt. No SimConnect flight, delivery or payment was attempted or claimed.
