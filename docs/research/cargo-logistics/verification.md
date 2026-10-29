# Investigation verification

## Scope and immutable inputs

- Main inspected: `d5ff51650789e45e46c8828893acaba071d957e7` (`origin/master`, fetched 2026-10-29).
- Current development inspected/tested: `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`, unmerged draft PR #3. No source/test changes made to it.
- PR #4 inspected: `6a6bfc723ace99ed5ebca2b1d991455a507bb2c4`; destination research only. `git diff origin/master <PR4 SHA> -- src tests` returned no changes.
- Investigation branch is separately based on main. Its diff must contain only `docs/` research/spec/handoff files. No production/test, architecture index, roadmap, dependency, workflow or save changes.

## Existing regression run, not new feature implementation

Ran in a fresh isolated managed checkout of exact PR #3 head, using PowerShell 7 on Windows and the existing pinned SDK:

```powershell
./scripts/Invoke-Verification.ps1 -Stage BuildTest `
  -DotnetPath 'C:/Users/mattr/Documents/ChatGPT/Frontier Pilot/.tools/dotnet/dotnet.exe' `
  -OutputDirectory .ci-output-cargo-investigation-baseline
```

Clean receipt: `sourceCommit=f7df3ffffa620d39d55b36c7cb74d44805e1ab91`, `sourceDirty=false`, SDK `8.0.424`, completed `2026-10-29T18:30:16.2515371+00:00`, outcome Passed. Full Release solution build: **zero warnings/errors**. TRX counters: **794 total/executed/passed, zero failed/notExecuted**. Original TRX SHA256: `91ea652af13ae34609d011666663f464c17061d44517de19cba702aeb84f9261`.

Public-safe summary is committed here; original receipt/TRX/build log are preserved in ignored `.ci-output-cargo-investigation/baseline/` in the investigation checkout. They are not personal save data. No new product test or diagnostic scenario was added. Code-derived edge risks in the report are not claimed as newly reproduced failures. No application executable, local WPF program or simulator was launched.

Historical [development run 15](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40904863044) passed the same 794-case source checkpoint plus nine script guard scenarios, both WPF programs and publication on clean PR merge `3a4ea30220c2589d02bdbffba25081187b6d259c`. This was inspected previously; historical success does not verify proposed multi-stop behavior or live aircraft support.

## Research artifacts

Bundled Python 3, using existing Pillow for the static illustration; no dependency install:

```powershell
& '<bundled-python>/python.exe' docs/research/cargo-logistics/verify-references.py --write
& '<bundled-python>/python.exe' docs/research/cargo-logistics/verify-references.py
& '<bundled-python>/python.exe' docs/research/cargo-logistics/render-mockup.py
git diff --check
git diff origin/master -- src tests ARCHITECTURE.md TESTING.md ROADMAP.md
```

- **57 source/test anchors** resolve uniquely against committed PR #3 source; all report reference labels are covered. The check reads Git objects, not mutable production files. No proposed gameplay logic runs.
- Mock-up arithmetic asserts cargo `35 → 25 → 0 kg`, one example station `47 → 37 → 12 kg`, and text bounds. PNG visually inspected: labels/tables/instructions fit, dark text on off-white paper, no overlapping text, and illustrative/unconfirmed status explicit. SVG contains no script/network asset; it is the scalable counterpart of the same static study.
- Final diff/links are checked before commit. Documentation-only hosted CI status and PR URL are maintained in [handoff](../../handoffs/cargo-logistics-investigation.md) and the investigation PR. That run validates unchanged main runtime, not a merge of PR #3.

## SDK evidence and limits

Read official Microsoft MSFS 2024 [payload SimVars](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simvars/aircraft-simvars/aircraft-system-variables/), [station/weight configuration](https://docs.flightsimulator.com/msfs2024/retail/content-configuration/cfg-files/flight_model.cfg/) and [setter API](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_setdataonsimobject/). A legacy documentation URL returned 404; the canonical retail setter page above was found and inspected. No SDK installation/sample modification or aircraft write was used to infer support. Existing source/historical loading reports establish the implementation and observed older aircraft-specific behavior; this task confers **no new simulator acceptance**.

## Documentation PR hosted checkpoint

[Draft PR #5](https://github.com/MoneyWithMatt/frontier-pilot/pull/5), report head `dd0d5f4217ead5f30e3c7a92eeb74f6ec744f2d3`, passed [run 18](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40934268493) on clean tested merge `44122eb6e6b4fb3f648f1e2881155a933d64a1c5`. **744 passed / 0 failed / 0 skipped**, SDK 8.0.424, Release zero warnings/errors, nine script guards, both WPF smoke programs, publication and both uploads. This is master's unchanged 744-case runtime, distinct from the isolated development snapshot's 794-case run. No cases were removed to obtain the lower count.

Exact hosted commands: `./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet`; `pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1`; verification entry point with `-Stage Smoke -DotnetPath dotnet` and `-Stage Package -DotnetPath dotnet`.

[Diagnostics](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40934268493/artifacts/12924660577), SHA256 `d0039bfd4ed4060d15a74ce0ef9701223c83d33603dc14474f34cd1f304d7745`, independently downloaded/checksummed; clean source receipts and 744-case TRX inspected. [Automatic development artifact](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40934268493/artifacts/12924740364), archive SHA256 `ea8dcebd9a6fd4e623901da6224c3cf22517b9f473daba1d7466068ecc45e89e`, not downloaded/executed and not a cargo upgrade. Seven-day retention. The final report/handoff-only follow-up has no application/test changes; exact final-head CI is recorded in PR #5 to avoid a self-referential evidence commit loop.

## Remaining review

Owner reviews report/manifest and decides the stage-specific product questions. Only then should an implementation slice be authorized. Every proposed stage lists automated and approved exact-aircraft simulator acceptance separately. Existing Offshore PR #3, destination-research PR #4, platform qualification and merge boundaries remain unchanged.
