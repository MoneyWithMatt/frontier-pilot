# Experimental .NET platform presence

Scope: Matt's request to recognise contact on specific oil-platform decks using native starts/repositioning, without requiring him to fly or attend the PC. This is the .NET continuation of the [actual OL400/H125 diagnostic](offshore-landing-proof.md#latest-focused-result--platform-presence-2026-11-02), not activation of Offshore jobs.

## Implemented boundary

`Core/ExperimentalPlatformPresence.cs` provides a read-only `ExperimentalPlatformPresence` tracker and `ExperimentalPlatformContactProfile`. The only profile is the measured static OL400 / H125 Passengers case. It reports Unavailable, OffPlatform, Airborne, Contact or StableDeckContact. No APIs create flight attempts, `VerifiedDeliveryEvidence`, inventory, passenger carriage or payments. There is no production destination registry or gameplay integration yet.

It reuses `IAircraftConditionProfile` / `LiveAircraftCondition`, whose native session targets the explicit aircraft and brackets each group with title/model/object identity. There is no new native setter/event surface. The existing `AircraftConditionRecorder` streams create-only frames and provenance to a requested diagnostic file; no personal save is accessed. Missing/unavailable variables cannot be inferred from zero speed.

The .NET predicate uses the known 8 m conservative pad radius, measured 39.89600372314453 m deck MSL, empirical H125 reference height 0.3–1.5 m above deck, observed surface elevation within 0.5 m of the deck, on-ground AND reported contact, <=0.5 kt groundspeed, <=0.1 ft/s vertical speed, <=5-degree bank/pitch, non-slew/rate-1 and 20 continuous simulation seconds. All 20 contact variables must be received and valid; their individual skid meaning remains unqualified.

Each group must span at most one second and arrive within two seconds. Because the existing condition recorder samples roughly once per second, successive frames allow at most two seconds of wall/simulator separation and <=0.5 s disagreement between those clocks. This differs from the faster Python observer's one-second maximum gap and is not a change to normal flight-attempt thresholds. Object/title/model, runtime discontinuity, pause/stopped events, time reset, gaps, units, duplicates, missing values and contact/elevation loss reset dwell. Unknown pause-event state is explicitly reported; simulation-clock progress is still mandatory. This remains a diagnostic, not a qualified production pause/contact adapter.

Native starts can establish current diagnostic deck presence, but cannot prove that a flight arrived there. Current presence is cleared on recorder shutdown/disconnect; a previously observed maximum dwell never authorises a later operation. There is no persistent physical GUID inference from transient simulator IDs. No production H125 recognition/capacity/payload profile is added.

## Read-only CLI entry points

```powershell
dotnet FrontierAirportBuilder.App.dll inspect-loading <new.json> 'C:/MSFS 2024 SDK'
dotnet FrontierAirportBuilder.App.dll record-platform-contact <new.jsonl> 40 'C:/MSFS 2024 SDK'
```

`inspect-loading` calls the existing `SimConnectAircraftLoading.ReadAsync`, captures actual aircraft/title, station names/indices/weights and available fuel/weight diagnostics. It never calls payload/fuel setters. Missing limits and the adapter's candidate writability flags are not live write qualification or verified passenger capacity. The existing legacy tank-view limitation remains. Output is create-only and failures must be preserved.

`record-platform-contact` runs the .NET contact profile through the existing read-only recorder for 1–300 seconds, reports stage changes and explicitly clears status on close. The SDK path is supplied by the approved scenario. It does not spawn/reposition the aircraft, change controls/failures, load payload, alter careers or grant wages.

## Verification and execution

Local `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-platform-app-01`: **767 passed / 0 failed / 0 skipped**, zero Release warnings/errors, SDK 8.0.424. Receipt source e03de5c dirty plus .NET profile/tracker/CLI/tests. **23 new synthetic cases** cover stationary native-style presence, identical-position sea level with contact flags, loss of contact/motion/elevation/slew, stale/invalid/missing/wrong-unit data, clock/runtime/identity resets and aircraft support. Python suite remains **31/0/0**. These tests do not establish native .NET adapter acceptance.

Hosted Windows CI also uploads the clean Release App output as a small framework-dependent `FrontierPilot-experimental-cli-<merge-sha>` artifact with regression/smoke/package receipts. It contains no simulator/SDK installation or user configuration. Downloading/verifying this bundle does not approve execution. Record the exact CI merge/source, artifact digest and executable/dependency hashes before asking to run it. [AGENTS.md](../AGENTS.md) requires: **“New/modified executable artifacts do not inherit approval to run locally. Later allowlisted scenario approval is separate from artifact approval.”** Do not run a locally rebuilt substitute under approval of an earlier artifact.

At this checkpoint the new .NET CLI has **not been executed locally**. Previously documented Python live observations remain genuine but cannot be described as acceptance of this new .NET adapter. Approval of the concrete hosted bundle is the next execution gate. The intended initial scenario is read-only H125 payload inspection and a 40-second OL400 contact recording; no payload writes, flight, failure injection, Community changes or personal-store access.

## Next test-mission work

After inspection establishes the actual H125 station layout, select a supported compartment and prepare a small, explicitly reviewed physical loading test through the existing loading architecture. Do not infer seats/capacity or guess a station index. Keep test-owned weight separate from unrelated occupants/cargo; persist intent before writes, verify immediate and retained values, protect baseline/unrelated payload and fail closed on conflicts. PR #3's guarded passenger coordinator/configuration work remains separate and must be reused/coordinated, not duplicated or silently merged.

The subsequent isolated no-pay test mission can deliberately stage endpoint presence and exercise load/unload/retry without claiming a flown leg. Normal jobs must still obtain flight/delivery proof from the existing tracker. Loading/unloading and that test mission are **not implemented or live accepted by this read-only slice**. Cargo/passenger career flows, full helicopter flight and other rig qualification remain pending separately.
