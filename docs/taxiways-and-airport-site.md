# Airport site and basic taxiways — 2026-09-08

## Status and scope

Matt reports that the preceding version's runway creation, parking placement and survey → validate → build → install flow work in MSFS 2024. The pending parking layout editor was preserved as commit f72d384 after all 255 baseline tests passed. That report does **not** prove the new taxiways or distant parking in the simulator.

This change implements a shared site envelope and basic click-to-draw taxiways. No simulator was launched or controlled, no Package Tool build was run, and no real Community content was written. Installed SDK files, official samples, FPT1, FPSC and older evidence remain untouched. New generated XML passes the installed SDK 1.2.4 schema, but live compilation and visual acceptance are still pending Matt's test.

## Why placement stopped around 500 m

There were three application restrictions: LayoutEditor.Apply rejected either east/north component above 500 m; LayoutEditor.Commit applied the same square limit to add/drag; ParkingGeometry.Validate required the whole stand circle to fit within a 500 m radius. The inconsistent square/circle checks could allow an edit that validation later rejected. These were prototype safety choices, not a screen-pixel conversion problem or an established SDK limit. The template contains no 500 m airport radius. Existing WGS84 Geodesy.Direct already supported distances up to 10 km.

Core.AirportSite now owns one shared, runway-oriented envelope: the runway rectangle expanded by 3,000 m in every direction, intersected with the 10,000 m anchor reach supported by Geodesy. The geometry range constant is shared with the solver. A stand's full circle and each taxiway's half-width must fit. This supports long runways and infrastructure several kilometres along their length rather than imposing a small circular anchor radius. The 3 km allowance is an explicit prototype site policy, not a Microsoft requirement. It guards accidental remote placement and is independent of zoom/pan.

Editor and Core use this policy consistently. Generation calls full definition validation before creating files. The envelope is convex, so checking the expanded endpoints also bounds every straight segment between them. Existing parking/runway and parking/parking clearances remain enforced. Width, heading, finite-number and identifier checks remain in place. Shrinking the runway can invalidate existing infrastructure; invalid taxiways can still be removed individually to recover.

## Taxiways

Core.Taxiway holds a stable number, a unique 1–8 character uppercase alphanumeric name, a width (3–60 m), and 2–128 ordered local east/north vertices. Limit: 64 paths. Successive points must be at least 1 m apart. Eight parking stands remain supported, as before.

Choose **New taxiway**, click the runway centreline, click each bend, click the destination parking circle, and choose **Finish taxiway** (or Enter with the canvas focused). Escape/Cancel drawing discards the unfinished path. Clicks within the runway footprint snap to its centreline. Parking endpoint references resolve against current stand coordinates; moving a stand keeps its connection attached. Deleting a stand retains its taxiway endpoint as an ordinary point at the former location, with undo restoring the connection. Distant stand hit tolerance affects selection only; committed coordinates remain the exact stand location.

Select a line to edit its name/width with **Apply taxiway properties**, or Delete it and redraw its route. Undo/redo covers completed path creation, property edits, deletion and parking changes in one history. Individual vertex dragging and curve editing are not implemented. Starting a new path and committing an edit clear old validation/build/install results. Drafts block validation, build, install and layout saving until finished/cancelled/applied. This extends the existing layout Save/Load payload to retain taxiways; it adds no new persistence feature or survey-resume capability. Old parking-only layouts still load for the exact matching survey/runway.

All taxiways use the airport's current **Dirt or Asphalt** surface and existing deterministic material library. There is no independent per-taxiway material selector. Changing airport surface affects runway and taxiways and requires revalidation/rebuild. Defaults are 12 m width, no edge lights, no edge lines, asphalt centreline on/dirt centreline off. Dirt uses ground merging. No apron is created.

## SDK interpretation and remaining limitations

Official documentation inspected on 2026-09-08:
- [Taxiway XML Properties](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Taxiway_XML_Properties.htm)
- [Airport XML Properties](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Airport_XML_Properties.htm)
- Installed schema: `C:\Users\mattr\Desktop\MSFS 2024 SDK\Tools\bin\bglcomp.xsd` (read-only).

The generator emits TaxiwayPoint, existing TaxiwayParking, TaxiName, then TaxiwayPath in SDK order. Parking retains indices 1–99; ordinary points start at 100. Equal local points share one node; stable path-number order makes allocation deterministic. Each consecutive vertex pair produces one segment. Segments touching parking use PARKING; other segments use TAXI. The surface references the same material GUID as the runway. Existing runway starts and parking XML are unchanged by adding taxiways.

This is a visible, piecewise straight route for manual taxiing. A runway-endpoint click provides physical overlap with the runway; it does not create a complete RUNWAY/HOLD_SHORT graph. ATC/AI routing is **not** promised. Crossing paths are not automatically split into network junctions. No smoothing, turn-radius/wingspan-clearance analysis, signs, lights or service routing is added.

Points have no invented altitude: native taxiway terrain-following is expected. No route elevation survey or flattening is generated. A flat runway-centre survey cannot prove that ground 1 km away is level. Inspect all segments, bends, runway join and parking join manually; vegetation exclusion and material rendering remain subject to MSFS acceptance. XSD validation cannot prove Package Tool acceptance, routing, texture appearance or terrain fit.

## Exact offline test

From the repository in PowerShell, run `./scripts/Start-Desktop.ps1 -DryRun`.
1. Check and connect, then Capture survey. This uses recorded Namibia frames and no simulator connection.
2. Enter a new name and ID, such as `FPTAX03`, keep 200 × 20 m and Dirt, and tick the simulated site check.
3. Add stand; set East to **1200**, North to **200**, radius to **10**, then Apply properties. Click Fit layout. This is more than 1.2 km from the anchor.
4. New taxiway → click the runway centre dot → click a bend off the runway → click a second bend towards the stand → click the stand circle → Finish taxiway.
5. Select the line, set width **18**, name **A**, Apply taxiway properties. Undo/redo once and check the width returns correctly.
6. Validate → Generate and compile → Install airport. The compiler is simulated; installation only writes the temporary test sandbox. Do not copy this synthetic package to MSFS.
7. Project → PackageSources/data/airport.xml should contain the distant TaxiwayParking and three TaxiwayPath segments, ending at the parking index. Edit the path width or move the stand; Build/Install should require validation and rebuilding again.

## Exact live MSFS acceptance test

1. Start MSFS at a new, flat, empty site with at least 1.5 km of clear ground east/north of your intended runway centre. Check existing airports and choose an unused ID (for example `FPTAX03`, only if unused). Keep clear of FPT1/FPSC and other airports.
2. From the repository run `./scripts/Start-Desktop.ps1` **without -DryRun**. Check and connect. Exit the parked aircraft, stand upright at the intended runway centre, face along the runway and capture using the normal five-second safeguards.
3. Keep the builder open. Enter the airport name, chosen ID, 200 m length, 20 m width and Dirt (or Asphalt for a paved test). Confirm the world-map/site check.
4. Add stand. Enter East **1200**, North **200**, radius **10**, then Apply properties and Fit layout. The stand is approximately 1,217 m from the survey anchor. Use a small GA aircraft for the first test.
5. New taxiway → runway centre dot → first bend off the runway → second bend towards parking → parking circle → Finish taxiway. Use gentle, generously spaced bends. Select the line and set width **18**, name **A**, then Apply taxiway properties. The taxiway shares your chosen runway surface.
6. Validate. Close MSFS completely, leaving the builder open. Generate and compile; retain the detailed build log if it fails. Install only after a verified successful build. Existing safety checks and package-specific backups apply.
7. Restart MSFS. Search your chosen ID in Free Flight. First spawn at the runway and inspect the whole taxiway route. Then start at Parking 1, confirm it is well beyond the old 500 m boundary, and taxi through every bend onto the runway.
8. Report whether the distant parking start appears in the selector, actual spawn position, whether all three segments are visible, width/surface, gaps at joins, turn shape, terrain bumps, buried/floating edges and obstacles. Include the chosen ID and build log on failure. These new behaviours remain unproven until you report the result.

## Automated evidence

Final Release solution build: zero warnings/errors. All **278 tests** pass, including the original 255 baseline tests with the obsolete 501 m rejection case updated to a genuinely out-of-site 4 km case. New tests cover 501/1000/2500 m placements, heading-oriented long-runway envelopes, full footprint and solver limits, finite inputs, zoom-independent geography, multi-segment deterministic XML, shared nodes/materials, bad widths/points/identities/references, pre-write refusal, parking attachment and removal, combined undo/redo, drawing cancellation/busy guards, selection recovery, shrinking-site removal, complete dry-run installation and edit invalidation. Existing capture, failure and safe-installation tests still pass.

See `evidence/taxiways/`: TRX results, solution build output, generated XML, schema check and offline UI screenshots. No live Package Tool or simulator test was performed in this task.
