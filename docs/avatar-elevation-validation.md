# Avatar elevation validation — guarded prototype accepted

Decision, 7 September 2026: Matt explicitly accepts the completed Sedona observations as sufficient empirical support for a guarded prototype, retaining the return-to-A caveat. This supersedes the requirement to repeat that test; it does not claim every original criterion passed. The original criteria below remain the historical record. Existing Sedona evidence is unchanged.

Avatar ground elevations at A/B/C were 1453.674939 / 1455.003911 / 1454.443409 metres. Aircraft ground remained near 1458.26 metres. Object-minus-ground offsets were approximately 1 mm / 0.2 mm / 10 mm. The accepted B revisit was within 45 cm horizontally and 1 mm vertically. Return A had 22.6 seconds recorded, within 1.7 cm vertically but 66 cm horizontally, outside the original 50 cm criterion. Surface classification was user-reported between helipads, not independently inspected. Recording ended during the usage interruption without a clean-close marker; complete preceding samples are retained, not presented as a cleanly completed session.

Approval is only for the installed simulator build (1.8.16.0, SHA256 82AF1D70611D0FA43291BB5D0DE71310A3AD458ADA8883AC3BBA370CF2A01F48) and SDK 1.2.4 SimConnect DLL (SHA256 E54A505BE0B0A4CDC98DFB45F0C202BA9637F5D494C0919040BC89AC67D92ADB). Use live explicit-avatar capture with runtime refusal for invalid identity, airborne/crouched state, stale/non-finite/unstable/inconsistent readings or mismatched binaries. Never use camera or aircraft coordinates as fallback. These are empirical simulator values, not real-world survey accuracy.

## Guided minimum test

Keep the aircraft parked for the whole calibration. Capture at least 10 seconds after settling at each visually identified location, without crouching, jumping or moving the view:

1. A: level ground, marked by an identifiable feature.
2. B: visible natural slope or accessible step at a clearly different height (aim for at least 1 metre difference). Describe which surface is underfoot. A platform/step may expose collision-surface behaviour rather than terrain, so label it explicitly.
3. C: another clearly different height on the slope or nearby ground.
4. Return to B, then A, as closely as practical, to check repeatability. If practical constraints prevent revisiting, report that rather than calling the test repeatable.

Record UTC phase markers and user descriptions, avatar/current/aircraft IDs, latitude, longitude, object altitude, targeted GROUND ALTITUDE, altitude above ground, on-ground and crouch state. Record simulator executable version/hash, SDK version and SimConnect hash. Raw readings remain candidates until reviewed.

## Predeclared prototype acceptance criteria

- At least 10 seconds and 20 finite samples for each required field, with no missing-data gap over 1.5 seconds in the accepted interval; reject errors, identity switches or stale data.
- Current object matches the avatar and differs from aircraft. Avatar is on ground and not crouched for the entire accepted interval.
- Each stationary point varies no more than 5 cm in each position axis, object altitude or ground altitude. Facing for a runway-centre capture varies no more than 1 degree circularly.
- Aircraft latitude/longitude and ground altitude stay within 5 cm across calibration points.
- At least three observed heights, with total ground range at least 1 m and adjacent chosen levels distinguishable by at least 0.25 m. Avatar ground must vary independently of the parked aircraft value.
- Object-minus-ground offset must remain consistent within 5 cm across accepted points and repeat visits; absolute offset must be within 10 cm for this feet-on-ground prototype. Otherwise do not substitute object altitude or remove an assumed offset.
- Repeat visits must be within 0.5 m horizontally of the original points and reproduce ground elevation within 10 cm. Prefer a distinct ground landmark to support the visual comparison.

These are conservative experiment thresholds, not statements about SDK precision. If an apparently stable reading is tied to the aircraft, if there is insufficient height contrast, or if the tests disagree, refuse elevation approval. A step/platform disagreement must not be averaged away.

## Documented contradiction and scope

Microsoft's [current miscellaneous SimVars documentation](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/miscellaneous-variables/), checked 6 September 2026, says GROUND ALTITUDE is computed at the user aircraft position regardless of camera or selected aircraft. Earlier Sedona observations suggest avatar-local behaviour on the installed build. This experiment tests that discrepancy empirically; it does not establish a supported portable contract or real-world survey accuracy.

Any approval must be bound to the tested simulator executable and SimConnect hashes, explicitly marked experimental, and revalidated after updates. Every runtime capture must independently pass identity, finite/fresh/stable/on-ground and offset checks. A calibration alone cannot prove flat terrain along a runway: Namibia requires visual review and centre/end/edge terrain samples before compiling a flat strip. No automatic movement, installation or airport generation is part of this calibration.
