# Aircraft failures and condition — feasibility investigation

Investigation date: 2026-10-19. Repository baseline: `33be077`. Scope: existing integration, official MSFS 2024 SDK documentation, installed SDK inspection and offline verification. **No live condition reading or failure injection was performed. No gameplay or simulator integration code changed.**

## Recommendation

Build a read-only aircraft-condition compatibility recorder next. Do not begin with a universal wear formula or automatic failure injection. MSFS 2024 has native wear-and-tear simulation, but a documented variable is not proof that an external client can address every component, that an aircraft uses it, or that it survives another flight.

Generic observation is feasible. Generic diagnosis, player blame and persistent mechanical enforcement are not established. Frontier should eventually own its airframe identity, maintenance history and continuity policy, while keeping simulator-reported condition separate from any Frontier-estimated wear. Aircraft adapters must qualify both readings and consequences.

## Existing architecture, inspected in source

| Boundary | Current behaviour | Implication |
|---|---|---|
| `src/Core/AircraftTelemetry.cs` | UTC, title/model, latitude/longitude, altitude, heading, ground speed, on-ground; finite/range validation | No RPM, oil, IAS, G, touchdown or damage data in this record |
| `src/MsfsSdk/LiveAircraftTelemetry.cs` | Native DLL loading and function binding; Open/AddToDataDefinition/RequestDataOnSimObject/GetNextDispatch/Close; six numeric values every 250 ms and identity every ten seconds | Existing transport pattern is suitable, but condition definitions and decoding do not exist |
| Same tracking source | Synchronized aircraft/avatar/current requests; runtime object identity gates; malformed replies, exceptions and timeout fail the session | Optional condition fields must not be added to the essential position batch where an unsupported field could break Jobs |
| `src/Workflow/TrackingViewModel.cs` | Validated live world state, disconnect notifications and throttled last-known persistence | A last-known reading cannot prove current aircraft health |
| `src/Workflow/BuilderViewModel.cs:112` | Forwards tracking to Jobs and aircraft loading | Condition should be an independent subscriber/service, not new logic in WPF |
| `src/Workflow/FlightAttemptTracker.cs` | Qualified flight progression and delivery evidence | Condition diagnostics must not become delivery evidence or change completion boundaries |
| `src/MsfsSdk/SimConnectAircraftLoading.cs` | Separate short-lived native sessions, reads engine count/combustion, legacy tank quantities and payload; explicit payload/fuel writes with workflow verification | Combustion is already read for loading. Failure flags and wear are not. Existing write capability does not imply failure control is implemented |

Survey remains separate and retains exact-hash qualification, Microsoft's aircraft-local ground-altitude contradiction and the return-to-A caveat. The earlier PA-28 payload reset and Turbine Duke cargo acceptance establish loading compatibility only, not condition compatibility. Title/model and transient object IDs are not permanent airframe identity.

## Capability matrix

**Confirmed supported** means explicitly documented SDK functionality, not tested in this investigation against MSFS. **Partially supported** means useful observations exist but cannot establish the requested diagnosis. **Aircraft-dependent** means system implementation/configuration determines coverage. **Not supported** identifies an explicit read-only restriction or absence of an implemented Frontier capability. **Not yet verified** means a concrete unresolved API/runtime question. All aircraft-specific live reliability remains unverified here.

In the table, W&T refers to the component interface discussed below. A reported failure describes simulated state; it does not identify who caused it.

| Capability | Status | Observation and interpretation | Trigger/reset/influence |
|---|---|---|---|
| Engine failure | Confirmed supported; aircraft-dependent | `GENERAL ENG FAILED:n`, `ENG FAILED:n` (Bool) report failure; `GENERAL ENG DAMAGE PERCENT:n` reports damage percentage. [Engine variables][engine] | Engine 1–4 failure toggles documented; flags/damage are read-only. [Failure events][events] |
| Engine shutdown / abnormal running | Partially supported | `GENERAL ENG COMBUSTION:n`, RPM and operating parameters show running state, not cause. Intentional shutdown can look identical. [Engine variables][engine] | Writable combustion exists; EX1 can be reversed by automatic ignition. It is not durable failure enforcement. |
| Oil pressure / temperature / leak | Partially supported; aircraft-dependent | `GENERAL ENG OIL PRESSURE:n` (psf), `GENERAL ENG OIL TEMPERATURE:n` (Rankine), `GENERAL ENG OIL LEAKED PERCENT:n`. Limits and diagnosis require aircraft context. [Engine variables][engine] | No qualified generic oil-failure actuator identified; W&T oil components are read-only. |
| Electrical / alternator failure | Partially supported; aircraft-dependent | Named bus voltage/current and battery/switch/connection state; zero output alone is not a failed alternator. FSX, v1 and v2 electrical semantics differ. [Electrics][electrics] | `TOGGLE_ELECTRICAL_FAILURE` is a system failure command, not a verified individual-alternator actuator. [Events][events] |
| Fuel-system failure / starvation | Partially supported; aircraft-dependent | `FUELSYSTEM ENGINE PRESSURE` (kPa), line flow/pressure, pump active versus switch, valves and tank contents. Empty fuel, selected empty tank and broken feed are different causes. [Fuel variables][fuel] | Pump/valve events influence supply; switching one off is not component damage. [Fuel events][fuel-events] |
| Gear damage / failure to move | Confirmed supported for speed-damage flag; partial diagnosis | `GEAR DAMAGE BY SPEED` differs from `GEAR SPEED EXCEEDED`; compare handle, position and hydraulic pressure. Fixed gear needs different interpretation. [Gear][gear] | Damage flag is read-only. Hydraulic failure toggle may affect equipped aircraft; gear control is not a failure command. |
| Flap damage / failure to move | Confirmed supported for speed-damage flag; partial diagnosis | `FLAP DAMAGE BY SPEED`, `FLAP SPEED EXCEEDED`, handle/effective index and left/right surface positions. [Controls][controls] | Damage flag is read-only; no dedicated generic flap-break/reset event established. |
| Structural damage / overstress | Partially supported | `CRASH FLAG` includes overstress; crash state is not a component damage map. [Miscellaneous variables][misc] Native collision/wear can reduce component health. [Collision model][collision] | No qualified arbitrary airframe-health write. Crash detection/settings are not a maintenance API. |
| Overspeed / excessive G | Confirmed supported as telemetry; partial damage inference | `AIRSPEED INDICATED`, Mach, `G FORCE`, min/max G and applicable design limits. Exceedance is not proof of damage. [Flight model][flightmodel], [Career requirements][career] | No need to force an exceedance. Do not treat writing motion as damage simulation. |
| Hard landing / landing damage | Partially supported | `PLANE TOUCHDOWN NORMAL VELOCITY` (ft/s), touchdown pitch/bank/location plus an observed landing transition. A hard touchdown is an event, not automatically damage. [Touchdown variables][touchdown] | Native landing wear exists, including harder-landing effects, when enabled. [Collision model][collision] No universal landing-damage setter established. |
| Brake wear / fade / failure | Confirmed supported; aircraft-dependent | `BRAKE LEFT/RIGHT TEMPERATURE` (Kelvin), `TEMPERATURE EFFECT`, `WEIGHT REMAINING` (0–1). These distinguish heat, effectiveness and remaining material. [Gear][gear] | Left/right/total brake failure toggles documented. Temperature and remaining-weight fields are read-only. [Events][events] |
| Tyre damage / pressure | Aircraft-dependent; direct component access not yet verified | W&T lists tyre and tyre-pressure components; no universal diagnosis from wheel RPM/skid alone | No generic tyre-puncture/reset event established in reviewed event tables |
| Other failures | Confirmed supported mechanisms; aircraft-dependent | Fly-by-wire ELAC/FAC/SEC failure flags are documented. [Controls][controls] Pitot/static/vacuum/hydraulic failures have commands. [Events][events] | A warning light or switch is not itself failure evidence. Vendor-specific systems need explicit support. |
| Engine fire through `GENERAL ENG FIRE DETECTED` | Not supported as a reliable generic detector | Current retail engine documentation explicitly says this variable is no longer used. [Engine variables][engine] | Do not infer support from its presence in an older sample/index |
| Native component condition | Confirmed supported in simulator; external addressing partially supported/not yet verified | Aggregate and component W&T SimVars exist; see below | Native health values are read-only |
| Universal service history / component life remaining | Not yet verified; no general public interface found | No documented external maintenance ledger, authoritative remaining-life estimate or unique owned-airframe key established | Frontier would own its own history/policy |
| Arbitrary direct writes to W&T health | Not supported by documented setter contract | `WEAR AND TEAR LEVEL` and aggregate health are marked non-settable | Preflight check progress is writable, but is not component repair |
| Persistent Frontier condition enforcement | Not supported by current Frontier implementation | No condition store, adapter, failure dispatcher or reconciliation exists | Requires future explicit implementation and aircraft acceptance |

### Native condition changes the feasibility assessment

`WEAR AND TEAR LEVEL` is component health (1 healthy, 0 failed); `IS FAILED`, `IS WEAK` and `WEAK VALUE` distinguish failure and reduced functionality. Aggregate `EXPOSED PARTS LEVEL` and `EXPOSED PARTS LOWEST LEVEL` avoid component indices. Relevant component IDs include fuel 0–4, alternator 28, gear 35, brake 36, tyre 37, tyre pressure 38, piston engine 39, turbine engine 40 and oil 41–42. These are not a complete airframe fatigue ledger. The retail table marks these health readings read-only; `CHECK PROGRESSION` controls preflight inspection progress. Its prose also contains a false/1 typo: validate Boolean interpretation live. [Aircraft miscellaneous variables][wear]

MSFS calculates native wear and failures; aircraft authors can inhibit everything, categories or individual elements through `gameplay.cfg`. Component names and configuration therefore matter even for default aircraft. Native condition can affect operation, but Frontier cannot assume it is enabled in every aircraft or mode. [Gameplay configuration][gameplay]

The collision/flight-model documentation describes loss of component health and increased wear for hard landings and flap misuse. This is evidence of native behaviour, not proof that all loaded aircraft expose meaningful values or that any particular threshold represents player negligence. [Collision and wear model][collision]

### External SimConnect access is the main unresolved gate

Ordinary single-index SimVars fit existing `AddToDataDefinition` calls, for example `GENERAL ENG FAILED:1`. Official documentation also supports named L variables and component-scoped I/O/Z variables, with scope and reload restrictions. Those mechanisms do not reveal an add-on's undocumented private state. [Data definitions][definition]

Component W&T may require both component type and instance. Microsoft's general SimVar documentation explains extra parameters using an RPN stack example, including a `LEVER` typo where `LEVEL` is intended. It does **not** establish an equivalent two-index external datum string. Do not ship an invented `WEAR AND TEAR LEVEL:39:1` format as confirmed. Modular merging can also change numeric system indices; documented names are preferable where available. [SimVar conventions][simvars]

Installed SDK 1.2.4 exposes the ordinary AddToDataDefinition signature with no explicit parameter-array argument. Its WASM `fsVarsAircraftVarGet` accepts `FsVarParamArray`; this is a potential in-simulator bridge path, not a callable native function in Frontier's process. If direct addressing fails, investigate a small read-only WASM bridge exposing only approved fields through ClientData. That requires a separately installed module and its own qualification; it is not needed for initial aggregate-health and engine observations. Header inspection alone proves neither route's live behaviour.

### Condition persistence and control

Engine elapsed time is documented in hours; Hobbs uses seconds and configured operating thresholds. Neither establishes Frontier lifetime hours or ownership continuity. [Engine variables][engine] Frontier should accumulate only validated continuous operating intervals against a stable Frontier airframe ID, and record simulator counters separately. Stop accumulation across pause, loading, disconnect, time jumps and aircraft changes; decide simulation-time versus wall-time policy explicitly.

Flight files document system failure entries with health, armed state and failure timing. They can describe initial flight state; they are not an external per-airframe maintenance database. Saving/loading a whole flight would be disruptive and is not a proposed routine condition actuator. Whether native wear and counters persist across menu reload, simulator restart, Free Flight and Career needs separate tests. Do not edit the user's flight/state files. [FLT properties][flt]

Failure events can be mapped with `SimConnect_MapClientEventToSimEvent` and transmitted through `SimConnect_TransmitClientEvent` (EX1 supports multiple event parameters in the installed header). Success sending an event does not prove that the aircraft changed. Events may be masked; readback and an observable system effect are required. [Event transport][transport]

Legacy failure commands are **toggles**, not idempotent set-failed operations. Blind retry or replay after restart may repair a failure instead of applying one. A future adapter needs observed baseline, durable command intent, state verification and refusal of ambiguous recovery. If there is no trustworthy state readback, leave injection disabled. Turning off a valve or combustion can produce a symptom but does not create persistent component damage.

`REPAIR_AND_REFUEL` is documented, but changes fuel as well and is ignored with enforced flight realism. It must not be used as a convenient maintenance button: it could conflict with Frontier's fuel/accounting and custom aircraft persistence. [Fuel-system events][fuel-events] No generic selective repair operation was established. `SetDataOnSimObject` cannot bypass non-settable SimVars; the documented result is an exception. [Data writes][setdata]

## Aircraft compatibility

This is a qualification plan, not a list of accepted aircraft. No condition capability was live-qualified for any model during this task.

| Aircraft group | Useful starting coverage | Required specific work |
|---|---|---|
| Default MSFS 2024 aircraft | Standard telemetry plus native wear candidates | Test each package/preset, systems version, mode and inhibited categories; default does not mean uniform |
| Third-party using standard systems | Standard variables may describe actual simulated state | Verify values change, flags match effects, thresholds and component mappings; vendor may override or reset state |
| Third-party custom systems | Basic aircraft movement may remain usable | Vendor-documented L/I/O/Z variables, Input Events, ClientData or other supported SDK; otherwise mark internal health unavailable |
| PA-28 | Single piston operating observations | Exact vendor/model/version required. Existing PA28-236 loading evidence gives no failure support. Do not assume all PA-28 variants share internals |
| C172 | Good first simple-aircraft test candidate | Fixed gear, engine configuration and analogue/G1000/package variants; distinguish normal mixture shutdown from failure |
| Baron | Useful second test for independent engine indices | Verify left/right engine state independently; retractable gear and electrical redundancy complicate diagnosis |
| Bush aircraft | Landing/taxi/engine telemetry | Tailwheel, tundra tyres, floats and rough-strip operation need different interpretation; a single touchdown threshold is inappropriate |
| Turboprops, including Turbine Duke | Engine/torque/temperature trends | Engine-type-specific limits, condition lever, feather, spool and custom failure system. Existing cargo acceptance is unrelated |
| Helicopters, including H125 | Ground/air state and engine observations | Rotor/transmission/custom systems, autorotation, skid/float contact; do not apply piston or wheeled-aircraft rules |
| Business jets | Multi-engine operating state, buses and gear | Spool/temperature/pressure limits, FADEC, custom avionics and redundancy; vendor health may be entirely separate |

Known-variable access is a mechanism, not a vendor contract. Do not guess L-variable names or write custom state discovered by trial and error. Input-event enumeration can expose controls, but is not automatic discovery of failures or wear. Requalify adapters after simulator/aircraft updates; record exact versions and hashes where available.

## Avoiding false penalties

Keep four concepts separate: **raw observation**, **simulator-reported failure**, **inferred abnormal condition**, and **Frontier policy consequence**. A real failure flag still does not distinguish user-selected failures, mission failures, native wear or external add-ons.

Use engine-running/startup/shutdown context and qualified aircraft limits before classifying pressure, temperature or output. Compare commands with responses over a time window. Normal battery-off, mixture cutoff, feathering, autorotation or a pulled breaker must not be labelled mechanical damage merely because output stops.

Missing/unsupported/stale/constant-default fields are unknown, never healthy or failed by default. Validate units, ranges, object identity and session continuity per field. A zero read without an error does not prove a sensor exists. Keep an initial baseline and require controlled changes to qualify a capability.

For landings, require a fresh airborne-to-ground transition and a newly associated touchdown record. A touchdown SimVar retains the last touchdown; reconnecting on the ground must not create another landing. Bounces, scenery steps, slew, teleport, active pause and low sample rates can corrupt inference. Four-Hz position polling is inadequate for capturing impact peaks; use latched touchdown data plus a higher-frequency bounded observation buffer in a later recorder. Do not infer impact solely by differentiating altitude.

For speed/G exposure, use IAS/Mach and applicable aircraft/configuration limits, not ground speed or a universal threshold. Require sustained, continuous evidence for inferred exposure; min/max G may predate Frontier's session. The current retail release notes mention newer overstress variables while the retail table and flighting documentation differ; treat these as version-gated research candidates, not installed-1.2.4 guarantees. [Release notes][release]

Start with advisory observations and no deductions, job rejection or automatic failures. If native and Frontier wear eventually coexist, choose one authority per component to avoid double-counting a hard landing. Maintenance costs and reliability curves would be explicit Frontier gameplay, not facts reported by MSFS.

## Recommended integration and smallest next prototype

1. **Core:** a separate condition observation model with per-field value/unit, timestamp, availability, source and confidence; stable component keys within a qualified aircraft profile. No expansion of delivery evidence.
2. **MsfsSdk:** an optional read-only condition source using the existing native transport pattern and explicit aircraft target. Isolate unsupported-variable exceptions by definition/request; do not let them terminate position tracking. Begin with a separate diagnostic session, avoiding a premature shared-connection refactor.
3. **Workflow:** lifecycle, identity correlation, discontinuity handling, recording and compatibility results. A future condition store should use versioned atomic persistence and preserve corrupt/future documents. Store observed native health separately from estimated wear and maintenance actions.
4. **Desktop/CLI:** display/export only. Use the existing CLI project for the smallest probe and a later diagnostics pane; no new parallel application or simulator logic in the view.
5. **Later, separately authorized:** aircraft-qualified consequence adapters with explicit enablement and verified recovery. No actuator belongs in the read-only prototype.

The next implementation should record one C172 session and one exact PA-28 package, then a twin-engine Baron. First fields: engine count/type, combustion, failure, RPM, oil pressure/temperature, damage percentage, elapsed/Hobbs, aggregate W&T, IAS/Mach, G, on-ground and latched touchdown data. Add brake fields independently. Resolve multi-component addressing only after the simpler reads work.

Acceptance: baseline and commanded normal shutdown produce different conclusions from a deliberately selected simulator failure; unsupported fields show Unknown; reconnection cannot duplicate a landing; no writes occur; existing tracking/Jobs remain operational; output identifies versions, settings and exact datum/unit strings. There must be no economy changes. This is useful even if component W&T access fails: it establishes the compatibility boundary before gameplay investment.

## Practical verification and live procedure

Completed offline work and exact hashes are in [verification evidence](../evidence/aircraft-condition-investigation/verification.md). Native DLL loading/export resolution passed; 77 focused tests passed; Release solution build passed with zero warnings/errors. These results do not prove live condition support. No simulator process was present, and none was started. No Package Tool, Community mutation, save mutation or failure event was used. No distribution republish is needed for documentation-only changes.

The installed SDK already supplies a SimvarWatcher executable, so a new untested probe was not added merely to duplicate it. For a user-run initial test:

1. Use a disposable Free Flight, no active Frontier shipment, and a stationary aircraft. Record simulator version/build, SDK hash, aircraft package/version/preset, assistance/damage/failure settings and mode. Do not use a persistent third-party airframe until its reset/backup procedure is known.
2. Run `C:\MSFS 2024 SDK\Samples\VisualStudio\SimvarWatcher\bin\x64\Release\Simvars.exe` without modifying installed samples. Select the explicit user-aircraft type, not avatar/current. Add one variable at a time; enable value recording. Keep Frontier Tracking connected to check coexistence.
3. Start with `GENERAL ENG RPM:1` (rpm), `GENERAL ENG COMBUSTION:1` and `GENERAL ENG FAILED:1` (Bool), oil pressure (psf), oil temperature (Rankine), damage (Percent), Hobbs (seconds), then aggregate W&T (Percent Over 100). Record errors as well as values. Convert psf to psi by /144 and Rankine to Celsius by ×5/9−273.15 when comparing cockpit instruments.
4. Observe normal engine start, stable idle and normal shutdown. Pressure falling after shutdown must not produce a failed-engine verdict. For a twin, repeat independently for engines 1 and 2. Add electrical readings only after identifying the actual system and bus names.
5. Record a normal flight and landing with `PLANE TOUCHDOWN NORMAL VELOCITY` (feet per second), touchdown attitude/location, G and IAS. Confirm timestamp association, units, sign and repeat-landing behaviour. Do not deliberately crash to validate the reader.
6. Test unsupported/missing component handling and aircraft reload. For W&T component instances, compare with an SDK RPN/Vars inspection in a separate developer session. Record the exact successful addressing method; the Watcher only appends a single index and does not prove multi-parameter support.
7. Disconnect/reconnect, exit to menu/reload, then restart MSFS. Compare wear, damage and counters without writing them. Test Free Flight and Career separately if native persistence is relevant. Record resets rather than repairing them automatically.
8. **Separate disruptive phase, only after user approval:** use the simulator/vendor's documented failure controls on a disposable aircraft, one failure at a time. Compare baseline → selected failure → observed system effect → documented recovery. Cover engine, electrical, hydraulic and brake failures before any flight test. If testing a SimConnect toggle later, record the command, asynchronous exception, state and effect; do not retry blindly. Avoid repair/refuel because of fuel side effects. No such phase was run here.

For every row capture: aircraft/configuration, exact query and index/name, units, baseline, controlled action, response/latency, simulator and cockpit agreement, reset result, reload result and conclusion. A variable which registers successfully but never changes is **not yet verified**, not supported. Publish compatibility at field/action level rather than awarding blanket aircraft support.

## Official reference links

Sources were consulted on 2026-10-19; prefer retail MSFS 2024 pages over MSFS 2020 or flighting documentation. Some older `html` URLs remain the official API/detail references. Setter icons disappear in plain-text extraction, so selected retail table HTML was also inspected directly.

[engine]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-engine-variables/
[events]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-miscellaneous-events/
[electrics]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-electrics-variables/
[fuel]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-fuel-variables/
[fuel-events]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-fuel-system-events/
[gear]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-brake-and-landing-gear-variables/
[controls]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-control-variables/
[misc]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Miscellaneous_Variables.htm
[flightmodel]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_FlightModel_Variables.htm
[career]: https://docs.flightsimulator.com/msfs2024/retail/content-configuration/careers/general-career-information/general-career-mode-requirements/
[touchdown]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_Misc_Variables.htm
[wear]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/aircraft-simvars/aircraft-miscellaneous-variables/
[gameplay]: https://docs.flightsimulator.com/msfs2024/retail/content-configuration/cfg-files/gameplay.cfg/
[collision]: https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/CFG_Files/Flight_Model/flight_model_info.htm
[definition]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_AddToDataDefinition.htm
[simvars]: https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/simulation-variables/
[flt]: https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/FLT_Files/FLT_Properties.htm
[transport]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_TransmitClientEvent.htm
[setdata]: https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_SetDataOnSimObject.htm
[release]: https://docs.flightsimulator.com/msfs2024/retail/introduction/sdk-release-notes/
