# Taxiway build regression — 2026-09-08

## Observed failure

The preserved NTA1 project at `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\projects\73c9f4109ba947a6` used the expected project and package paths. Package Tool exited zero but its current-run BuilderLogError delta reports `#C2550 TaxiwayPath keyword MUST have ... WEIGHT`, followed by BGL compilation failure. `manifest.json` and other partial assets exist; `scenery\frontier\airport.bgl` and `layout.json` do not. The missing layout is a downstream consequence, not a changed output location or an incorrect lookup.

The failing project itself was not modified. Its generated airport XML, invocation, builder delta, Package Tool report and Frontier build report are preserved under `failed-nta1`.

## Cause and fix

The taxiway generator omitted `weightLimit`. The installed SDK 1.2.4 AirportKALO sample supplies `weightLimit="0"` on every TaxiwayPath. Microsoft documents zero as unrestricted aircraft weight. SDK 1.2.4's compiler requires the field even though the current MSFS 2024 XML reference labels it optional.

Every generated path now emits `weightLimit="0"`. Package validation also shows the first concrete `ERROR:` line from the current BuilderLogError delta while retaining strict manifest/layout/BGL checks. The CLI JSON converter was generalized so the existing parking enum values in NTA1's generated `airstrip.json` could be replayed through the regression build.

## Verification

- Exact NTA1 `airstrip.json`, regenerated into a new disposable workspace.
- Real SDK 1.2.4 Package Tool: exit 0, validation success, zero current builder errors.
- Genuine package metadata: eight `layout.json` entries.
- Compiled `scenery/frontier/airport.bgl`: 1,195 bytes.
- All nine NTA1 taxiway segments carry `weightLimit="0"`.
- Automated suite: 302 passed, zero failed or skipped (`tests.trx`, `test-output.txt`).
- Release solution build: succeeded with zero warnings and zero errors (`build-output.txt`).
- Corrected self-contained executable was published to the stable distribution after the previous app closed (`final-publish-output.txt`); SHA-256 `49DC7F86AAA46A2C1743348F17EFB87BC8FA09F24E04C8E33FA03A41CB22390`.
- The existing Desktop shortcut launched that exact executable and created the expected Frontier Airport Builder window (`final-launch-check.txt`).
- No Community package was installed and the preserved failed project was unchanged.

`successful-nta1` contains the generated XML, invocation, result, empty builder-error delta, real manifest and real layout. The BGL is recorded by size rather than committed as a generated binary.
