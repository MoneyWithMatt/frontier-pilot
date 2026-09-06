# Package Tool diagnosis — 2026-09-06

## Outcome

The silent exit-1 launch problem is resolved. The unchanged SDK 1.2.4 SimpleAirport now compiles its BGL, but fails package validation with the current simulator. A separate disposable copy with one package metadata addition produces a complete package with no new builder errors. This does **not** count as a successful strictly untouched baseline. No UI was started and no installed SDK sample was edited.

## Findings

1. The original sandboxed attempt left `steam.exe` PID 22184, parent 22080 (the original Package Tool), alive with the old sample build command. There was no MSFS process and Steam's logs had not advanced since September 2. A minimal retry from the copied project directory also returned 1 after 122 seconds, without compilation. After stopping that specifically identified orphan, Steam started normally, updated itself and the game, signed in and launched MSFS in package-build mode. This locates the original observed failure in the Steam/simulator launch path, before scenery compilation. The precise internal reason the orphan was stuck was not established.
2. The inherited `MSFS2024_SDK` value was `C:\Users\mattr\Desktop\`, which made the tool pass nonexistent `C:\Users\mattr\Desktop\ModelBehaviorDefs`. The actual directory is `C:\Users\mattr\Desktop\MSFS 2024 SDK\ModelBehaviorDefs`. Correcting this variable only in the child process is appropriate. A later warm-Steam control still compiled the airport with the old variable, so this configuration error was not sufficient to explain this sample's original exit 1.
3. All XML-referenced package/asset directories exist. The original structured `ProcessStartInfo.ArgumentList` quoting is valid, and observed Steam/game command lines preserve project/output paths. Tests from both the copied project directory and the SDK tool directory compile the BGL. No evidence points to quoting, long paths or working directory as the cause.
4. Steam automatically updated the simulator from `1.8.14.0` to `1.8.16.0` during the first successful launch. The SDK stayed at `1.2.4`; its tool and sample were not upgraded. Outcomes after this launch therefore apply to simulator `1.8.16.0`.
5. The current builder rejects the old sample's missing package order metadata. The exact current-run error is `Validate Package Manifest: Empty string for 'package_order_hint' key - in file manifest.json`, followed by `Package Validation failed for 'mycompany-airport'`. It produces the BGL and a manifest, but no `layout.json`. Importantly, the Package Tool returns **0** despite this validation failure.

## Experiments

All paths below are relative to the repository root. Full invocations and process command lines are in each evidence directory. All original sample files were hash-matched before build; installed inputs remained unchanged afterward.

| Evidence directory | Invocation / state | Result |
| --- | --- | --- |
| `evidence/diagnosis-20260906-130110` | Project working directory; project path + `-nopause`; original environment; orphaned Steam present initially | Exit 1, 122 seconds, no BGL |
| `evidence/diagnosis-20260906-130257` | SDK tool working directory; project path + `-nopause`; corrected process environment; Steam restarted | Exit 0, 93 seconds; BGL generated; package validation fails, layout missing |
| `evidence/diagnosis-20260906-130608` | Project working directory; project path + `-nopause`; original environment; Steam already working | Exit 0, 21 seconds; identical BGL; same validation failure |
| `evidence/diagnosis-20260906-130640` | Project working directory; same minimal invocation; corrected process environment; separate compatibility copy | Exit 0, 19 seconds; manifest + layout + BGL + textures; no new builder errors |

The compatibility experiment adds only this element under `<AssetPackage>` in the **disposable** `PackageDefinitions/mycompany-airport.xml`:

```xml
<PackageOrderHint>CUSTOM_AIRPORT</PackageOrderHint>
```

The installed package definition and all unchanged-baseline workspaces retain their original content. The airport geometry and BGL hash are identical across the three compiling runs: `FA036EDBC4C5AD7E4F368B931602F6CF701A790A64051093D45AB7F0639A99A6` (1,034 bytes).

The successful compatibility package is at:

`C:\Users\mattr\.codex\.chatgpt-projects\g-p-6a904cf3f8fc81919657c40eb26e3f77\frontier-airport-builder\workspaces\diagnosis-20260906-130640\SimpleAirport\Packages\mycompany-airport`

All 11 entries in its layout exist with matching byte lengths. Evidence includes manifest, layout, layout verification, complete file inventory/hashes, process observations, output logs and the exact modified package definition. This verifies compilation/package assembly, not visual loading in MSFS. Nothing was installed into Community.

## Diagnostics and simulator requirements

- Package Tool's captured standard output is empty on this machine; successful attachment writes `Attached to EXE - waiting for completion.` to standard error. Terminal retry/transcript did not reveal the original failure. Redirected console text is therefore insufficient.
- Actual validation errors are appended to `C:\Users\mattr\AppData\Roaming\Microsoft Flight Simulator 2024\BuilderLogError.txt`. This file also contains old errors for unrelated packages. The new diagnostic script captures only the text added during each sequential run. Do not treat historical errors as current results.
- Steam launch evidence is in `C:\Program Files (x86)\Steam\logs\console_log.txt`, `gameprocess_log.txt` and `bootstrap_log.txt`. The relevant launch excerpt is preserved in `evidence/diagnosis-20260906-130257/steam-launch.log`.
- No matching recent Windows Application error events were found during the initial scan. The copied project's generated files are inventoried per attempt.
- **MSFS does not need to be running beforehand.** No simulator was running before the successful attempt; Package Tool launched it through Steam in `BuildAssetPackages` mode, and it exited afterward. This is not a standalone compiler independent of an installed/licensed simulator.
- **An interactive Developer Mode session is not needed for the observed command-line workflow.** We did not enable or change any saved Developer Mode setting. Whether every saved DevMode setting can be disabled was not separately tested.
- Building through the in-simulator Project Editor requires MSFS running with Developer Mode enabled, according to the official sample instructions. That manual test is unnecessary to distinguish the present failures: the launch trace, current file-based validation error and successful one-field compatibility experiment already do so. No action from Matt inside MSFS was needed.

## Reproduction and next gate

`scripts/Test-SimpleAirport.ps1` now delegates to the diagnostic harness with a correct child-only SDK environment and minimal arguments. It always uses an untouched fresh copy. The script reports validation errors and layout presence rather than equating exit 0 with success. `scripts/Diagnose-SampleBuild.ps1 -CorrectSdkEnvironment -PackageOrderCompatibilityFix` explicitly opts into a separately labelled modified-copy experiment.

The environment correction is not persisted globally. Installed SDK assets remain read-only. The strict untouched baseline is still blocked by sample metadata compatibility. Next development decision: use an updated official sample or explicitly adopt the recorded metadata migration as the prototype's compatible baseline. UI and airport generation remain deferred in this task.

Official sources:

- [Package Tool invocation](https://docs.flightsimulator.com/msfs2024/retail/sdk-tools/package-tool/package-tool/): external invocation, optional arguments and Steam selection.
- [SimpleAirport in-editor workflow](https://docs.flightsimulator.com/msfs2024/retail/samples-tutorials/samples/sceneries/simpleairport/): Developer Mode and Build and Mount.
- [Package XML properties](https://docs.flightsimulator.com/msfs2024/html/8_SDK_Tools/Package_Tool/Package_Tool_XML_Properties.htm): `PackageOrderHint`, with `CUSTOM_AIRPORT` for third-party airport packages.

Final harness verification: evidence/diagnosis-20260906-130918-058 reran the untouched baseline with the corrected child environment from the project directory. It again returned 0 but correctly reported validation errors and missing layout; installed sample unchanged. This verifies the updated entry script reports the incomplete package accurately.
