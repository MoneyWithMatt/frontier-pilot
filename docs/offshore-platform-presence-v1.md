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

## Physical diagnostic hosted receipt / exact next approval

Implementation **962165031b95c4378ae0ce80b734163fefc6265e** passed [hosted run56](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41483848318), clean merge **6dd821394b57863ae977b951d997af59fd1f5aa4**: **780 .NET /31 Python /9 simulated guards /both WPF smoke programs /packaging and three uploads**, zero test failures/skips and zero build warnings/errors. Downloaded diagnostic receipts all independently identify that exact clean merge, SDK8.0.424 and Passed; TRX780/0/0, both WPF error logs empty. Hosted commands remain verification entry point BuildTest/Smoke/Package with `-DotnetPath dotnet`, Python unittest and Test-VerificationGuards.ps1.

[Experimental CLI13161051021](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41483848318/artifacts/13161051021), 3.845 MB ZIP: SHA256 **99091e25f139a4ad6d6b31999b10d5bba0016fc3605e57c34b203d7c9a224ba3**, independently downloaded/digest-verified. Extracted `.ci-output-offshore-hosted/run56/cli/src/App/bin/Release/net8.0/`; no new executable run. [Diagnostics13161640604](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41483848318/artifacts/13161640604) SHA256 **5e48b5528d8dc848723b2a14340fb1b97caa0f69a7d52e8d9a6a5b0981b34382**, downloaded/hash-verified. Ordinary Desktop bundle13161330755 was not downloaded/executed. Artifacts expire seven days after upload.

| Module | SHA256 |
|---|---|
| FrontierAirportBuilder.App.dll | `c12a12e054a937fb560d5bbb6832f38900857d1026d31a38394e94af521d1f97` |
| FrontierAirportBuilder.Core.dll | `5793376131584e67bf736ea763a3f241bf236f520d7af6efd5d48921a389d3c5` |
| FrontierAirportBuilder.MsfsSdk.dll | `f7a41ecbdbde05bb591f60945322ed228f7137510e5a79f17d2d81e0ab4062e8` |
| FrontierAirportBuilder.Workflow.dll | `5ba0043021cc0c080dceadb4b6352210ae582318323ab66b53f755d25297341f` |

The next proposed allowlist uses only this exact bundle through the existing reviewed dotnet host:

1. Fresh read-only `inspect-loading .ci-output-offshore-live/csharp-h125-loading-before-cycle-01.json 'C:/MSFS 2024 SDK'` to confirm current state. Outputs must be absent.
2. Supported UI resume/pause only; no flight/reposition/failures. Single controlled operator, no other loader or manual payload/aircraft changes.
3. `test-platform-payload .ci-output-offshore-live/csharp-ol400-payload-cycle-01.jsonl 'C:/MSFS 2024 SDK' --confirm-five-kilogram-diagnostic` — bounded120s, fixed station6/Cargo1, baseline99.7903214±0.05 kg ->baseline+5->baseline, with fresh20s deck confirmation before each operation and immediate/three-second retained readback. Never writes fuel or other stations.
4. Review actual journal/readback and pause. Any unresolved write stops; no automatic retry, reset, rollback or new-path adoption. If test mass remains, inspect/report and prepare a separate recovery action.

Matt has been asked to approve this exact artifact plus fixed physical scenario. At this checkpoint answer is pending; no physical writes have occurred. Do not execute a later documentation-checkpoint rebuild or local substitute under run56 approval.

## Approved physical execution — OL400 / H125, 2026-11-02

**PASS for the fixed static on-pad ballast cycle.** Matt explicitly approved run56 + the 5 kg scenario after being told it was paused for that approval. Rechecked the exact ZIP and all four App/Core/MsfsSdk/Workflow hashes listed above; source962165031b95c4378ae0ce80b734163fefc6265e / clean hosted merge6dd821394b57863ae977b951d997af59fd1f5aa4. Executed downloaded run56 only, not local or later run57 assemblies. No competing Frontier/python/dotnet process was running before the test; actual simulator remained MSFS2024 1.9.21.0 / H125 Passengers / H125 on native OL400 H01.

Exact commands, existing dotnet host and SDK path as in the allowlist above:

- `inspect-loading .ci-output-offshore-live/csharp-h125-loading-before-cycle-01.json 'C:/MSFS 2024 SDK'` — exit0, actual unchanged nine-station layout and Cargo1 baseline99.79032140000001 kg. Position -22.938928769253206,14.504617446654764,133.215875ft MSL, on-ground true. Pre-test fuel72.51USgal reflects current state, not restoration of an older tank value.
- Supported UI Escape resumed the existing native-start flight; no position/flight-control/fault operation.
- `test-platform-payload .ci-output-offshore-live/csharp-ol400-payload-cycle-01.jsonl 'C:/MSFS 2024 SDK' --confirm-five-kilogram-diagnostic` — **exit0**, stderr0bytes, journal `Completed`, once only. Wall interval19:53:43.4906600–19:54:33.3759123UTC (~49.89s),40 actual deck-observation frames.

| Milestone | UTC | Observed result |
|---|---|---|
| Fresh deck permission / LoadIntent | 19:54:04.066 | 20.2664972 continuous simulation seconds, distance0.00217m, aircraft reference0.704m above known deck |
| LoadVerified, after immediate +3s retention | 19:54:08 | Cargo1 **104.79032140000005kg**, baseline+5kg |
| Fresh deck permission / UnloadIntent | 19:54:29.354 | 20.2272240 new continuous simulation seconds after detector reset; no stale permission reuse |
| Completed, after immediate +3s retention | 19:54:33.376 | Cargo1 **99.79032140000001kg**, original baseline restored |

All eight unrelated station weights matched the initial baseline exactly at both retained checkpoints, independently rechecked from the journal after execution. The native setter targeted index6 only. Gross delta over the load verification interval was+4.863050619kg; observed fuel burn -0.136029871kg gives **+4.999080490kg adjusted payload effect**. Unload gross delta-5.138572547kg with fuel burn-0.138656175kg gives **-4.999916372kg adjusted payload effect**. No fuel write/restoration occurred. Normal engine fuel consumption continues during running intervals. Final retained position: -22.93892877249717,14.504617451479293,133.202715385ft MSL, GS0.00361kt, on-ground true. The exact5kg diagnostic mass is removed; no unresolved recovery remains.

Private/ignored evidence (no personal telemetry/screenshots committed):

| Receipt | SHA256 |
|---|---|
| `csharp-h125-loading-before-cycle-01.json` | `e443bcd27f573e85a86e12cd8e03539a7989f763743639a050879fc9f51d188a` |
| `csharp-ol400-payload-cycle-01.jsonl` | `b781603859e2d3b9940abe2bb525530228b67870010f70b6c37782fe556355e8` |
| `csharp-ol400-payload-cycle-01.stdout.txt` | `8343cdd1c8f81921182ce4c5938c52bb882418c2c1012c4c4bc59b1f95d85866` |
| `screenshots/ol400-payload-cycle-completed-paused.jpg` | `ea56ba85e3bc3db506ad44bd11ccf3c1986a4989d49ac1fb208df5b984c7e988` |

Paused MSFS through Escape after completion; helicopter remains on OL400. The screenshot illustrates location/current pause, not proof of a physical mass delta; the native readbacks/gross effect provide that evidence. No Community changes, fuel/fault setters, career/personal save writes, new mission proof or wages. No repeated payload command.

**Acceptance boundary:** real static deck presence + a single inspected compartment's5kg loading/removal works in this version/session. This does not establish flown touchdown/takeoff, a complete mission, restart/conflict recovery in the real simulator, other payload stations/rigs, verified passenger capacity/visuals or mixed transport. Operational aircraft-package version is still incomplete. Production H125/platform generation stays disabled. Prior approval-pending/not-executed paragraphs are historical and superseded by this receipt.

**Next engineering action:** integrate an isolated no-pay platform-visit objective using the measured specific-platform profile and existing mission/recovery boundaries; retain native-start diagnostic provenance separate from tracker flight proof. Qualify actual contact-loss/takeoff transition and live .NET sea-level negative before enabling a production destination. Coordinate the PR3/PR6/PR8 identity/ownership/reconciliation work rather than extending this non-atomic legacy diagnostic into a production writer. No request for Matt to fly is needed to prepare that work.

Automated baseline remains run56 **780/0/0**,31Python,9guards,bothWPF,package. Subsequent documentation-only b024942 passed [run57](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/41484481740), same counts/allstages; these newly recorded live observations require no code/test change. This following receipt commit has its own hosted documentation check, never a substitute for the approved run56 artifact.

## Restricted platform test mission / constrained departure preparation — 2026-11-02

Matt authorised the next contact-loss/test-mission slice. Added `Core.ExperimentalPlatformVisit` (deck→sustained separation→return, diagnostic only), `Workflow.ExperimentalPlatformVisitRecording` and CLI `record-platform-visit` / `review-platform-visit`. `ExperimentalPlatformPresence` now exposes the same already-validated sample/continuity as an additive diagnostic field; existing predicates unchanged. FlightAttemptTracker/economy/pay/selected-airframe/cargo/passenger code is untouched. All production platform/H125 gates remain closed. [Exact spec and scenario](specs/offshore-platform-visit.md).

Departure needs20s baseline, known running/unpaused exactH125, all20contacts clear AND on-groundfalse, referenceheight>1.5m for3contiguoussimseconds, within30m/15mhigh, no implausible per-sample position/vertical jump. Any pause/gap/slew/rate/invalid/unknownruntime clears unconfirmed departure permission; identity change blocks. Recontact/hover near water alone cannot finish.20freshstabledeckseconds after a recorded departure completes the diagnostic once. Recorded milestones are historical; disconnect always revokes current permission. Offline review replays only the explicit file, checks schema/session/sequence/identity/termination, preserves invalid originals and returns no live authority. New recordings do not adopt old dwell; no automatic mission recovery into gameplay.

`record-platform-visit <new.jsonl> 210 'C:/MSFS 2024 SDK'` is a read-only live recording; `review-platform-visit <recording.jsonl>` is historical offline reconstruction. Existing create-only recorder/identity validation reused. No physical writes in either command. The test is local OL400 contact/departure/return, not the unverified FYWB roundtrip; existing5kg ballast acceptance remains separate.

### Autonomous contact-loss test preparation

Prior free helicopter controller drifted, so do not replay it as a qualified pilot. [Official MSFS2024 SDK freezing-position events](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/key-events/miscellaneous-events/#freezing-position) document separate latitude/longitude and attitude toggles and their actual-state simvars. The page's SET parameter descriptions are insufficient for a guessed boolean implementation; the new harness instead toggles only from freshly known flags and verifies readback. SDK documentation is not actual H125 freeze support.

New isolated `scripts/offshore_vertical_contact_test.py` prepares a constrained vertical cycle: actualH125/OL400 deck only, no initial position write; verify lat/lon+attitude constraints while altitude remains unfrozen,25sbaseline, collective-only lift toward+6ft,5sallcontacts clear above deck, descent and25srest. Reuses existing calibrated axis conversion and experimental vertical law, not lateral/yaw control. Bound150s/2mlateral/0.2–4mreferenceheight/1kt/3ft-s/5degree. Explicit native object pinned by existing probe reads, title checked before each command. No payload/fuel/fault/position/altitude setter, no new Community or save operation. On abort use SLEW_ON, restore observed original freeze state/collective while frozen, report any unresolved recovery; never target a replacement aircraft or blindly retry.

This changes only a diagnostic control artifact, not production authority. Constraint readbacks, lift/contact-loss/return and restoration are **not live verified**. A constrained cycle would verify detector transitions, not freely flown takeoff/arrival or route/pay. The controller and .NET recording run independently; raw provenance must disclose constraints. Both new exact artifacts and their allowlisted live scenario require approval before execution. User's previous run56 approval covers neither.

### Automated evidence

Local `pwsh -NoProfile -File scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' -OutputDirectory .ci-output-platform-visit-02`: **802passed/0failed/0skipped**, zero warnings/errors, SDK8.0.424, source6579c00dirty plus Core/Workflow/CLI/tests. Initial cycle01 build failed because the synthetic source used string instead of existing ConditionConnectionInfo; corrected fixture type, failure retained.22new.NET cases cover three ordered milestones/once-only completion, current permission revocation, initialairborne/water/contactflags/lowheight/jumps, runtime/disconnect/clock/identity resets, persistence/offline replay/duplicate commands/corruptfuturesequence/trailing/interruptedrecords. Python `python -m unittest discover -s tests/OffshoreProbe -v`: **38/0/0** (7newpureharness guard/allowlist tests). No native script or new executable has been run locally. Hosted checks/artifact verification pending before exact execution approval.
