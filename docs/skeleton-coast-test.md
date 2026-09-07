# Skeleton Coast Test Strip — FPSC

Compiled and installed 7 September 2026. This is the shortened rough visual test explicitly requested by Matt to limit usage. Full footprint and opposite-end surveying were waived for this experiment; terrain fit is unverified. No WPF UI was built.

- Identifier: FPSC; name: Skeleton Coast Test Strip.
- Centre: -22.58616679921284, 14.539071285657734 (22.586167 S, 14.539071 E).
- Ground altitude: 13.94779940508306 metres, captured live from the explicit walking avatar. Object altitude differs by about 6 mm.
- True heading: 108.63324971143716 degrees; runway starts 12 and 30. MAGVAR -12.503734 degrees was used as east-positive magnetic variation.
- Dimensions: 200 m long, 20 m wide; DIRT physical surface, Frontier-owned procedural tan dirt albedo and ground merging.
- Terrain observation: user described very flat but somewhat rocky. A point 124.37 m forward was 1.805 m higher. Both generated starts use centre elevation, as in the proven flat template. No claim that this removes rocks or matches that slope; bumps, buried/floating edges and start-height problems are possible.

The current OurAirports dataset lists Swakopmund as the nearest airport, about 9 km away; the prototype ID FPSC was not found in that dataset. This does not prove uniqueness against every installed add-on or every unlisted airstrip. Site selection was based on the user's in-simulator observation and recorded coordinates; no independent visual inspection was claimed.

## Build and installation evidence

`evidence/skeleton-coast/build` preserves SDK command, invocation, input hashes, generated sources, output tree, logs and successful result: exit 0, no builder errors, eight layout entries, ten package files. The first failed attempt is retained separately: SDK rejected a material without a diffuse texture. Adding the deterministic procedural albedo resolved that failure. Texture generation source is scripts/Create-DirtTexture.py; the PNG is Frontier-authored procedural data, not an edited SDK image. Existing SDK sample and FPT1 template were not changed.

Package: `artifacts/fpsc/frontier-airport-fpsc.zip`; SHA256 6EF64C76CA8D23CB0D9FD7CBF85D80F79AE2043B5CC973B7DBD9ED7E0417E62C.

Installed to `C:\Users\mattr\AppData\Roaming\Microsoft Flight Simulator 2024\Packages\Community2024\frontier-airport-fpsc` using scripts/Install-SkeletonCoastTest.ps1. Simulator closed checks precede staging and installation. No previous FPSC installation existed; future replacements preserve its prior folder outside Community. All ten installed files match the archive, unrelated entry metadata and UserCfg are unchanged, and all thirteen FPT1 file hashes still match their original installation. Receipt: evidence/skeleton-coast/install-20260907-073032-004. Final unit test run: 169 passed.

## Quick visual test

1. Launch MSFS, open Free Flight/world map, search FPSC or Skeleton Coast Test Strip. If search fails, inspect the coordinates above, about 9 km north of Swakopmund.
2. Select runway 12 as departure with a small aircraft, daylight and clear weather.
3. In external view, check dirt surface, approximate 200 x 20 m footprint, east-southeast/west-northwest alignment, rocks, bumps and buried/floating edges. Taxi slowly if the surface looks usable.
4. Start a second flight from runway 30 and check that you spawn at the opposite end facing the other way.

## User visual-test result — 7 September 2026

Matt reported: “it worked perfectly, no rocks, bumps or buried or floatng edges. Terrain looks good”. This is user-observed confirmation that FPSC loaded and its visible terrain fit was satisfactory at the surveyed Namibia site. Individual runway-start outcomes were not separately enumerated, and no screenshot or independent measurement was supplied; do not infer additional quantitative checks.

The prototype chain is demonstrated at this site: live avatar capture → automatic ground elevation → deterministic airport generation → SDK compilation → safe Community installation → successful user visual test. This does not establish terrain correctness at all locations or remove the documented SDK contradiction and installed-build restrictions. No additional work was required on the package after the visual report.
