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

### Concrete hosted build awaiting execution approval

Implementation **fe0388b4f4b4ad453bd889befbf474e198da55b1** passed [hosted run 54](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41472610355), clean merge **77304670dcba8f13c6670e95dede4312f1101292**: **767 .NET / 31 Python / 9 simulated failure guards / both WPF programs / packaging and three uploads**, zero test failures/skips and build warnings/errors. Regression/smoke/package receipts all identify that exact clean merge, SDK 8.0.424. TRX independently confirms 767/0/0; WPF error logs are empty.

[Experimental CLI artifact 13156520528](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41472610355/artifacts/13156520528), 3.84 MB ZIP, downloaded to ignored `.ci-output-offshore-hosted/run54/experimental-cli.zip`; SHA256 **08772a726f062a0cd68bc592e986afdbcde2b2e56f5f1fbd1f9b911b36750449**, independently matches GitHub metadata. Extracted at `.ci-output-offshore-hosted/run54/cli/src/App/bin/Release/net8.0/`. The copied regression receipt also matches. No executable was run during inspection/extraction.

| Module | SHA256 |
|---|---|
| FrontierAirportBuilder.App.dll | `e4809694762eee40d862db0e178f7a043eefb0aa2bbb26a2f7185f1568751831` |
| FrontierAirportBuilder.Core.dll | `a825e87a3529bd0dce1b0612d72a4ff150aa18ae881e7fa20635e23e4cff4688` |
| FrontierAirportBuilder.MsfsSdk.dll | `a9dad7612c918431a5a66dbd28c4eff766cf47aeca4fb563bb63b4e60f0a7eee` |
| FrontierAirportBuilder.Workflow.dll | `b66793c05cf752622fc5a5c0fdc6b645a53650d877629f75fa363ee56ef41f88` |

[Diagnostics artifact 13155483768](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41472610355/artifacts/13155483768), downloaded/hash verified at `.ci-output-offshore-hosted/run54/diagnostics.zip`, SHA256 **347af3240a96d9cf63cec6f53459c912751eaa6eea6df5fb1ad368531c1857d6**. The ordinary Desktop development bundle was not downloaded/executed.

Proposed allowlist, using the existing .NET host at `C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe` and **only this downloaded App.dll/dependency set**:

1. `inspect-loading .ci-output-offshore-live/csharp-h125-loading-01.json 'C:/MSFS 2024 SDK'` — read actual aircraft/payload/fuel metadata, preserve output/error. No station write.
2. `record-platform-contact .ci-output-offshore-live/csharp-ol400-contact-01.jsonl 40 'C:/MSFS 2024 SDK'` — observe .NET diagnostic on native OL400 H01; resume/pause MSFS through existing authorised UI as needed. No flight/position/fault/payload/career operation.

Both paths must still be absent immediately before invocation; outputs are create-only. This exact artifact/scenario was presented to Matt for approval. At this documentation checkpoint that approval is pending. Do not execute a later doc-checkpoint rebuild or local assembly under approval of run 54: its binary hashes may differ even if source behavior is unchanged.

## Next test-mission work

After inspection establishes the actual H125 station layout, select a supported compartment and prepare a small, explicitly reviewed physical loading test through the existing loading architecture. Do not infer seats/capacity or guess a station index. Keep test-owned weight separate from unrelated occupants/cargo; persist intent before writes, verify immediate and retained values, protect baseline/unrelated payload and fail closed on conflicts. PR #3's guarded passenger coordinator/configuration work remains separate and must be reused/coordinated, not duplicated or silently merged.

The subsequent isolated no-pay test mission can deliberately stage endpoint presence and exercise load/unload/retry without claiming a flown leg. Normal jobs must still obtain flight/delivery proof from the existing tracker. Loading/unloading and that test mission are **not implemented or live accepted by this read-only slice**. Cargo/passenger career flows, full helicopter flight and other rig qualification remain pending separately.

## Approved read-only execution — 2026-11-02

Matt approved the exact run 54 artifact/scenario above. Both commands were executed from its downloaded, rehashed App/dependency set (not local build outputs). MSFS 2024 1.9.21.0, H125 Passengers / H125, native OL400 H01 start. SDK/simulator hashes remain those in the main report. Effective aircraft-package version remains unresolved.

- `inspect-loading .ci-output-offshore-live/csharp-h125-loading-01.json 'C:/MSFS 2024 SDK'`: success. Nine one-based stations: Pilot, Passenger 1–4, Cargo 1–3, Co-Pilot. Cargo 1 is index 6, **99.7903214 kg**; Cargo 2 ~119.75 kg, Cargo 3 ~79.83 kg. No station maximum/arms reported. Names/writable candidates do not establish passenger capacity or physical write acceptance. One legacy center fuel tank was reported; no fuel changes requested.
- First 40-second recording: 38 actual frames, all paused, simulation time unchanged. Correctly unavailable; no confirmation. Retained, not discarded as a failure to detect the rig.
- Second 40-second recording: 38 actual unpaused/rate-1/non-slew frames spanning **39.4044 simulation seconds**. Contact began 16:15:19.5524406 UTC; StableDeckContact at 16:15:39.7622197 UTC after **20.2181848753 continuous seconds**. Distance ~0.0017 m, reference height ~0.708 m above deck, surface ~130.754 ft MSL, aircraft altitude ~133.212–133.217 ft MSL. Groundspeed <=0.01465 kt. Shutdown cleared presence. This is native-start static rest, not a flown touchdown/departure.

Private create-only receipts (ignored, not public telemetry): loading JSON SHA256 `f2fac92c87bbc19d38db93bfe10247f17081e3f056e21108fbfbf962899ad10b`; paused JSONL `eafd308a2000284681ad838c40dc0a40116252b0685c16d41337cb28320392d3`; running JSONL `bcc88cade0a5a2a3ceb16e033e6440763ce478dd22382657437206a141aebfa6`. Screenshot `screenshots/ol400-csharp-contact-confirmed.jpg` is private. Simulator left paused on the pad. No physical payload, fuel, fault, Community, career or money writes occurred.

The older approval-pending paragraphs above are historical checkpoints, superseded by this execution receipt. Actual water negatives remain the earlier Python captures; .NET sea-level rejection is automated coverage, not a second live .NET water test.

## Prepared physical diagnostic — not executed

See [fixed scenario specification](specs/offshore-platform-payload-diagnostic.md). `ExperimentalPlatformPayloadCycle` and CLI `test-platform-payload <new.jsonl> <SDK root> --confirm-five-kilogram-diagnostic` add **only 5 kg** to the inspected Cargo 1 baseline, confirm immediately/after three seconds, re-establish 20-second current deck presence, remove only that 5 kg and confirm again. It checks exact actual title/model/nine-station layout, fresh stationary pad snapshots, an unchanged pre-write payload, actual reported station limits, every unrelated station and gross delta adjusted for observed ordinary fuel burn. Gross/CG/performance information is advisory, never a dispatch veto. There is no fuel setter or economy/personal-store access.

Create-only, flushed journal records intent before each write and observations/readback. A repeated path is rejected before simulator access; a different path cannot adopt an already-increased baseline. Partial write, clamp, disconnect or conflict leaves `Unresolved` and stops without rollback/retry. If interrupted after adding mass, the extra 5 kg can remain aboard: inspect actual state and journal before proposing a separately reviewed removal. Never rerun with a new path to force recovery.

This controlled single-operator diagnostic uses the existing legacy loading adapter. Its check/write operations are not an atomic simulator transaction and do not pin the write across aircraft switches. No concurrent manual payload edits, aircraft changes or other Frontier loading process are allowed during the scenario. This is a documented qualification limitation, not production shared ownership/reconciliation. It neither adopts unrelated occupants/cargo nor qualifies passengers, visuals, mixed transport, a physical GUID, normal deliveries or wages. Production integration must coordinate PR #3/#8 rather than duplicating their ownership model.

Local BuildTest `.ci-output-platform-cycle-02` passed **780/0/0**, zero warnings/errors, SDK 8.0.424, source c9b61ae dirty with this implementation/docs. Earlier `.ci-output-platform-cycle-01` retained **779 passed / 1 failed / 0 skipped**: the test fixture could not inspect an open journal because its reader did not share write access. Fixed reader FileShare.ReadWrite, preserving the durable-intent assertion. The successful command was `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-platform-cycle-02`. Thirteen additional synthetic cases cover both writes/retention, duplicate path, wrong aircraft/layout/baseline, station refusal, manual edits, clamping, post-write exception, retention reset, disconnect and advisory-only gross limits. No physical test has run. Hosted WPF/package/artifact checks are the next gate, followed by exact artifact AND scenario approval under AGENTS.md line 11.
