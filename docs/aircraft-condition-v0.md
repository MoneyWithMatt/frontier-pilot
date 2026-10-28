# Aircraft Condition Recorder V0

2026-10-19. Status: implemented and offline-tested; **no live aircraft condition compatibility accepted**. Builds on [the investigation and official SDK references](aircraft-condition-feasibility.md). No simulator writes, automatic faults, wear calculation, maintenance prices or gameplay penalties are implemented.

## What is usable now

- The existing CLI App can record MSFS condition observations to a new, streaming JSONL file. A PowerShell launcher supplies the repository .NET environment and Release configuration. No additional application or Desktop maintenance page was added.
- `Core/AircraftCondition.cs` separates operating telemetry, reported native condition, reported failure and future estimated condition. Each field carries its exact SimVar/unit, UTC receipt time, value or unavailable/invalid status and diagnostic reason. Successful receipt is named `ReceivedUnverified`, never “supported”. Estimates cannot enter the recorder's observation export.
- `StandardConditionProfile` builds a generic field list using the validated engine count (0–16), not model-name aliases. It includes engine running/failure/damage, RPM, oil, elapsed/Hobbs, cylinder/exhaust/turbine temperatures, N1/N2, control positions, fuel flow/pressure, generator switch/output, legacy main-bus voltage, gear/flap speed-damage and exceedance flags, brakes, aggregate native wear, IAS/Mach/G and latched touchdown data. ENGINE TYPE, slew and simulation rate are diagnostic context. Definitions use broad validity bounds, not operating limits.
- `MsfsSdk/LiveAircraftCondition` uses the existing native loading/request/dispatch pattern in a **separate optional SimConnect session**. It targets USER_AIRCRAFT exclusively. It does not change the position/avatar source, aircraft-loading adapter or Jobs connection. Only Open, AddToDataDefinition, RequestDataOnSimObject, GetLastSentPacketID, GetNextDispatch and Close are bound. There are no setters or event transmissions.
- Requests are individually correlated with their send IDs. A rejected variable becomes unavailable while siblings remain recordable. Rejected queries are suppressed for the remainder of the session; timeouts can retry. Unknown/late packets cannot populate another request. Malformed packets fail this diagnostic session, not Tracking. Each batch waits at most two seconds, with cancellation during dispatch.
- Identity is read before and after each field batch; title/model/object/engine-count changes end recording and refuse the mixed frame. The recorder independently checks identity and timestamps. A sequence of independent SimVars is not an atomic physics snapshot: timestamps expose that limitation.
- `Workflow/AircraftConditionRecorder` writes header, numbered frames and terminal outcome, flushing each line. Partial results survive normal cancellation or connection errors. A process crash can leave an incomplete final line; consumers must treat recordings without an end marker as incomplete. Output uses create-new semantics and never overwrites prior evidence. A recording is bounded to 1–3600 seconds by the CLI, and streamed rather than accumulated in memory.

The recorder does not infer a new landing, general overspeed event or damage from raw samples. It records the explicit flap/gear exceedance flags and the operating inputs for later analysis. Latched touchdown values may predate connection. A normal landing and a hard landing are not maintenance transactions. Pause is not fully classified in V0, so these samples are unsuitable for authoritative hours or exposure integration.

## Run a live recording

Load a flight yourself, then run from the repository:

```powershell
.\scripts\Record-AircraftCondition.ps1 -Seconds 300 -SdkRoot 'C:\MSFS 2024 SDK'
```

The launcher reports the new JSONL path under ignored `.tools/condition-recordings`. To choose a destination, supply `-OutputPath 'C:\your-test-directory\new-session.jsonl'`; the parent must exist and the file must not. Ctrl+C stops. Frontier Desktop can remain connected to Tracking/Jobs; the diagnostic session has its own lifetime. The script does not launch MSFS or Package Tool. Live acceptance must verify coexistence; architecture isolation alone is not live performance evidence.

Equivalent CLI after a Release build:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools/nuget'
& .tools/dotnet/dotnet.exe run --project src/App/FrontierAirportBuilder.App.csproj -c Release -- record-condition 'C:\your-test-directory\new-session.jsonl' 300 'C:\MSFS 2024 SDK'
```

Exports include SDK version, simulator process file version and SimConnect SHA-256 in each frame. Exact datum strings, profile ID and timestamps are included. Paths and unrelated career/economy data are not exported. Aircraft title/model and touchdown coordinates are included intentionally; review them before sharing. SDK metadata identifies the environment but does not qualify survey or condition support.

## Persistent individual-aircraft foundation

`AircraftConditionRecord` is a versioned document with an independent GUID, explicit label, exact simulator title/model binding, user-supplied package/version note, nullable airframe hours/cycles and extensible component records. Components have stable keys/types, nullable hours/estimated health and fault IDs. Unknown hours/health are **null**, not zero hours or perfect condition. Two identical C172s receive different GUIDs. No identity is derived from TITLE, ATC MODEL, tail number or transient SimConnect object ID.

`AircraftConditionFiles.Create/Load` supplies a small create-only persistence foundation. Creation flushes a same-directory temporary file and moves it without overwrite. Loading validates version, fields, finite/ranged estimates and unique component keys. Unknown fields, corrupt/future documents and existing destinations are refused and preserved. There is no update, repair, clear or migration API yet, and no automatic initialization from a failed load.

An optional explicit identity record can be created after obtaining the exact title/model from an unbound diagnostic export:

```powershell
& .tools/dotnet/dotnet.exe run --project src/App/FrontierAirportBuilder.App.csproj -c Release -- condition-airframe 'C:\your-test-directory\airframe.json' 'My Dakota 01' 'EXACT TITLE FROM EXPORT' 'EXACT ATC MODEL FROM EXPORT' 'Exact package/version from simulator, or explicitly unverified' --confirm-model-binding
.\scripts\Record-AircraftCondition.ps1 -AirframePath 'C:\your-test-directory\airframe.json' -Seconds 300
```

The selected document's GUID/package note appears in the export, and a title/model mismatch refuses frames. This is **explicit player selection plus model consistency**, not proof that MSFS identified a unique owned aircraft. A player could select the wrong one of two identical models; V0 cannot resolve that automatically. Package/version notes are user assertions. Future owned/operated fleet registration must bind this GUID to the existing company/player fleet without changing current cargo identity rules.

Recording never updates the selected document, hours, components or faults. Simulator observations are durable only in diagnostic exports, separate from Frontier estimates. No “last known” observation is applied on reconnection. The minimal component schema can preserve a fault reference, but there is no fault-authoring gameplay action or simulation effect.

### Future data extensions, not implemented

Use stable airframe/component-instance IDs and versioned component types; replacement creates a new component instance while preserving old history. Maintain immutable, idempotent events for damage reports, inspections, repairs, replacements and fault transitions. Each event needs source/confidence, observation interval, rule/model version, affected component and evidence reference. Repairs should act on the specified fault/component; an inspection does not automatically restore health.

Separate authoritative Frontier state (condition estimates, unresolved faults, component hours, service history) from native observations (value/source/time/compatibility) and synchronization receipts (desired fault revision, command/readback outcome). Native health and Frontier wear cannot both debit the same event without an explicit per-component authority policy. Existing condition is unknown until consciously established; do not fabricate historical wear from a simulator counter.

Before mutable gameplay persistence, add revision checks, managed storage and cross-process locking using the repository's established atomic stores. Keep fault/service events and their condition revision atomic. Do not add separate loosely coordinated databases for each component. V0's create-only document deliberately avoids a premature mutable maintenance database.

## Add-on compatibility strategy

`IAircraftConditionProfile` supplies field mappings; `IAircraftConditionSource` supplies timestamped observations. A future vendor source can use a documented external SDK or bridge without changing recording or condition identity. A profile can map documented L/I/O/Z variables; it must not guess private variable names or conflate a cockpit animation with underlying mechanical state. The current standard source does not enumerate Input Events, load custom modules or access vendor APIs.

Recognised package/model, read receipt, empirically meaningful observation, and verified fault control are different facts. Build future capability assessments per field/action, with package/preset/version, simulator version/hash, mapping, baseline/control comparison, test evidence ID and limitations. Profile-level integration labels should summarize only a declared component scope:

| Level | Required evidence | V0 status |
|---|---|---|
| Full | Qualified observation and apply/clear/readback for every advertised component in a defined scope | No aircraft qualifies |
| Partial | Qualified subset of observation and fault control; unsupported components explicit | No aircraft qualifies |
| Monitoring-only | At least useful operating observations qualified; fault application absent or untrusted | No aircraft automatically qualifies from a successful read |
| Unverified | No adequate empirical evidence, including recognised but untested aircraft | All current profiles/test targets |

“Unsupported” should be a field/action finding backed by evidence, not an automatic classification of the whole aircraft following one timeout. Version changes invalidate applicable assessments; do not promote a profile based on a variable existing in the SDK. The enum and profile boundary exist now; a mutable compatibility registry and promotion UI do not.

Standard aggregate native wear has no component indices. Component-specific W&T still needs a proven external addressing method; V0 does **not** invent multi-index strings. A supported WASM Vars API bridge is a future option, separately installed and read-only initially. Native health is documented non-settable; a bridge does not make read-only properties writable. Modern electrical/fuel component names need aircraft-specific mapping; the generic legacy bus and indexed pressure candidates are not universal system coverage. Custom aircraft may expose only basic motion/engine indicators or publish stale/default values.

## Initial aircraft matrix and reusable acceptance procedure

Read-only local inspection found these **StreamedPackages directory names** on 2026-10-19. The targeted directories contained no accessible `manifest.json` or `aircraft.cfg` from which to verify package version or runtime title. Directory presence does not prove the content is fully downloaded, active or controllable. Exact runtime identity and package versions remain manual/live requirements.

| Target | Local package candidate | Recognition | Operating/native condition | Apply/clear |
|---|---|---|---|---|
| Default C172 G1000 | `fs24-asobo-aircraft-c172sp-as1000` | Folder only; live identity pending | Unverified | Not implemented; unverified |
| Default C172 classic | `fs24-asobo-aircraft-c172sp-classic` | Folder only; separate preset test | Unverified | Not implemented; unverified |
| User's Carenado PA-28 Dakota | `fs24-microsoft-aircraft-pa28-236-dakota` | Folder matches requested family; confirm exact installed product/version in MSFS | Unverified; earlier PA28 cargo tests do not qualify condition | Not implemented; unverified |
| Baron G58 | `fs24-asobo-aircraft-baron-g58` | Folder only; confirm selected variant/add-on | Unverified, including independent engine indices | Not implemented; unverified |
| Other/custom aircraft | Explicit observed title/model + verified package identity | No guessed aliases | Per-field tests required | Vendor-specific qualification required |

Test C172 first, Dakota second, Baron third, using separate recordings for each configuration and package revision:

1. Record exact aircraft/package/version/preset, simulator build, mode, assistance/failure settings, test date and intended actions. Use a disposable flight, no active cargo job. Choose a new output each time. Keep the same settings through a test sequence.
2. Record cold-and-dark → normal start → stable idle → normal shutdown. Compare RPM, combustion, oil, volts and failure flags with cockpit/vendor diagnostics. A zero inactive-system reading is not a fault. Record readings that remain constant or disagree. For the Baron, test engines independently; confirm index 1/2 mapping rather than assuming it.
3. With normal simulator controls, observe flap travel and generator/pump switch changes where appropriate. V0 never operates these controls. Test boarding/walkaround/return in the Dakota: distinguish any add-on initialization/reset from Frontier behaviour. Stop if identity changes; begin a new file.
4. Record an ordinary flight/landing. Check IAS against the instrument, G scale, touchdown velocity/attitude and timestamp association. Do not deliberately overspeed or hard-land for this phase. A retained touchdown value on startup must remain merely a reading, not a new landing or damage event.
5. Disconnect/reconnect; exit to menu/reload; restart MSFS. Use new files and compare native condition, counters and component defaults. Test aircraft switching mid-recording: it must terminate rather than merge identities. Test unsupported variables without requiring the entire recording to fail. Confirm Tracking stays responsive and normal Jobs/loading behaviour is unaffected.
6. For each field, record at least baseline, action, observed response, external reference, units, errors, reset/reload behaviour and conclusion (`unverified`, `meaningful observation`, `unavailable`, `contradictory`). Link evidence to the exact profile/package/environment. Read success alone is insufficient. Store notes alongside JSONL; the recorder does not promote profiles.
7. Failure activation/clear tests are **deferred**. They require a separate user-controlled disposable session and vendor-specific recovery plan. This task sends no failure command, performs no repairs and changes no aircraft condition.

Reusable worksheet columns: test ID; package/preset/version; simulator/SDK identifiers; field/action; exact mapping; unit; initial state; normal control/action; expected response and basis; measured response/latency; cockpit/vendor comparison; reload result; evidence file; conclusion; limitations. Keep personal recordings out of source control; commit only reviewed/redacted acceptance evidence.

## Future persistent-fault synchronization

Example: “failed alternator” is a component-specific Frontier fault. `TOGGLE_ELECTRICAL_FAILURE` is not automatically an acceptable implementation: it may fail the whole electrical system and affect unrelated components. Without an exact qualified adapter, preserve the fault and show **not applied / monitoring only**. Do not silently clear it or replace it with a different failure.

Proposed state machine (design only): selected airframe → identity/initialization stable → capability and authority checked → observe baseline → build desired/actual difference → persist application intent → apply one qualified operation → verify readback **and effect after initialization/retention interval** → persist acknowledgement. Link each intent to airframe, simulator session, adapter version, condition revision and fault ID. Application is not atomic with a local file write.

Prefer idempotent set-state operations when documented. For legacy toggles, only act after reliable contrary-state readback. After a crash/disconnect, observe again before deciding whether to retry; uncertain outcomes remain pending, never blindly replay a toggle. Keep recovery separate from maintenance: clearing a simulator fault does not erase Frontier service history, and observing a healthy simulator does not silently repair a persisted fault.

Wait for aircraft initialization and verify again after boarding, reload or add-on resets. If the add-on overwrites state, stop fighting it; retain desired state and report application unavailable/conflicting. Establish whether Frontier or the vendor maintenance system owns each component. Never repeatedly force a variable every frame or bypass a vendor's persistence. A native health snapshot is not generally restorable through read-only W&T fields. The existing investigation documents failure events, selective-control limits and the fuel side effects of repair/refuel.

Nothing in V0 grants automatic synchronization authority. Future activation should be explicit, component-scoped and qualified. Graceful failure means continued observation with a visible unapplied fault, not invented success or automatic economic punishment.

## Future wear and probabilistic failure model

Design only; no coefficients, random draws, schedulers or wear calculations are active.

Accumulate validated exposure in simulation time under a declared policy, with resettable continuity windows across pause, loading, slew, teleport, connection gaps and aircraft changes. Store operating hours separately from airborne time and landing cycles. Aircraft-specific limits and phase context determine exposure; do not treat cold-start oil, intentional shutdown or autorotation as damage. Hard landing candidates need a fresh touchdown and aircraft/gear-specific evaluation; ambiguous events remain advisory.

For a future stochastic model, use a documented hazard rate `lambda(condition, exposure, unresolved faults, component type)` and interval probability `1 - exp(-integral(lambda dt))`. This avoids probability depending on recorder poll rate. Prefer a persisted per-component hazard threshold or reproducible random stream with a versioned algorithm, persisted state and event IDs. Accumulated hazard and crossing-event creation must commit in one condition revision so restart cannot redraw luck or duplicate a fault.

Initial calibration should target conservative measured failure rates over realistic operating hours, with component/aircraft limits, bounds and explicit player settings. Avoid unsupported precision and per-frame independent dice rolls. Normal wear, misuse and random failure need separate evidence and causes. Validate with deterministic replay, timestep/subdivision invariance, discontinuity tests, Monte Carlo rate checks and restart-at-every-commit tests before enabling effects.

A threshold crossing during flight would persist a single fault event, then enqueue a qualified synchronization intent; connecting is not the only trigger. Already-failed components cannot activate twice. Maintenance resolves specific faults or resets the appropriate replacement component, without rerolling unrelated components. If a fault cannot be applied, retain its unresolved synchronization status. No maintenance costs or economy policy are chosen here.

## Remaining gates and next step

Native live reads, units, default/constant semantics, DLL coexistence, add-on initialization, exact package versions, component W&T addressing, native cross-flight persistence and action/readback behaviour remain unverified. The broader investigation's SDK documentation is capability evidence, not empirical aircraft acceptance.

Next: run the three-aircraft read-only procedure, review exports, and approve a small set of per-field compatibility findings. Only then consider a separate, explicitly enabled apply/clear experiment for one precisely mapped fault on one disposable aircraft. Do not start automatic wear or maintenance gameplay first.

Automated/build evidence: [verification](../evidence/aircraft-condition-v0/verification.md).
