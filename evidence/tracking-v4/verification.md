# Tracking V4 verification — 2026-09-08

## Automated verification

- Full Release tests: 356 passed, 0 failed, 0 skipped.
- Clean Release solution build: 0 warnings, 0 errors.
- Coverage includes WGS84 local round-trip beyond 500 m; runway/distant-parking airport recognition; outside boundary; stand threshold; taxi-through versus five-second stopped qualification; airborne refusal; nearest stand; multiple airports; schema-1 compatibility; schema-2 location persistence; catalogue-load ordering; and removal of stale airport/stand associations.

## Desktop artifact

- A Windows x64 self-contained single-file candidate was staged at `dist\.v4-ready-2122104a\FrontierPilot.exe` while the prior Desktop build remained open.
- Candidate size: 212,870,264 bytes; SHA-256: `2F4FF2989C58CEFCE7E7D214854443E4C74AE99568732A31A95159975FBA9A58`.
- The staged candidate opened a responsive `Frontier Pilot · Prototype 0.1 · DRY RUN` window and that test instance was then closed.
- The stable Desktop shortcut target has not yet been replaced because the running prior Frontier Pilot window may contain user work.

No MSFS process was launched or controlled, Package Tool was not invoked, and Community content was not changed. Airport/stand recognition remains pending Matt's live simulator acceptance.
