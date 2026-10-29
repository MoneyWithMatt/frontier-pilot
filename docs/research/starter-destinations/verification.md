# Research verification and evidence

## Source and scope

- Baseline: `d5ff51650789e45e46c8828893acaba071d957e7` (`origin/master`).
- Initial research: `6a6bfc723ace99ed5ebca2b1d991455a507bb2c4`.
- Final research/data/artifact commit: `c00f65b35e87b179c8357bc29e47be96726a241e`. The final clarification labels conflicting cached points by their actual simulator code, including legacy FYKW for real-world Okangwati / database key NA-0160. All output calculations were rerun on the same bytes immediately before this commit; committing changed no content.
- [Draft PR #4](https://github.com/MoneyWithMatt/frontier-pilot/pull/4), targeting `master`; no merge or repository-setting changes.
- Application source, tests, official roadmap, architecture/testing entry documents and embedded catalogues are unchanged. Main Offshore checkout was initially dirty at `22b9e32`; a later read showed its separate work had advanced to clean `f7df3ff`. This task did not alter either state. A local Git exclude hides the nested research worktree from the parent checkout; it is not a source change.

## Research commands and results

From the research worktree, using only the bundled runtime executables:

```powershell
$researchPython = 'C:/Users/mattr/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$researchNode = 'C:/Users/mattr/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
& $researchPython docs/research/starter-destinations/tools/curate.py
& $researchNode docs/research/starter-destinations/tools/workbook.mjs
& $researchPython docs/research/starter-destinations/tools/report.py
& $researchNode docs/research/starter-destinations/tools/map-check.mjs
git diff --check
git diff origin/master -- src tests ARCHITECTURE.md TESTING.md ROADMAP.md
```

Results: **66 unique master records, 68 unique career assessments; all 53 original memberships reconciled**. **17/17 research integrity checks passed**, zero failed or skipped. All **68 weighted scores and experience totals** reconciled to independent calculations. Weighted-score sensitivity and actual ranked-view reordering passed after reversible edits/restoration. Formula-error scan matched zero entries; saved XLSX ZIP integrity and absence of error cells passed. All five sheets rendered and were visually reviewed. Map JavaScript syntax and layers reconciled: two bases, 58 ordinary located destinations, three custom clinic points, 59 proposed base relationships and five conflict points/links. Seven committed source-blob hashes independently matched the manifest after LF-normalisation.

The map is an offline geographic schematic plus independent WGS84 GeoJSON. Browser interactions were not exercised; native Excel was not opened. Workbook recalculation/export was checked in artifact-tool, not claimed as a native Excel application test. Unknown-located Sonop/Westies and mobile/development offshore concepts have no invented coordinates.

Initial preparation failed when FYNP lacked a current real-world database record. The repair preserved the gap and added a clearly labelled historical X-Plane datum/closure lead; it did not infer a new airport or silently use MSFS coordinates as independent real-world evidence. No product assertions were weakened. Network sandbox initially blocked public dataset retrieval; approved external downloads then succeeded. GitHub connector PR creation returned 403; the existing repository Git credential workflow created the single draft PR after an idempotent remote check. No credentials were printed or committed.

## Hosted Windows evidence

[Windows CI run 16](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40930781189) passed on research head `6a6bfc723ace99ed5ebca2b1d991455a507bb2c4`, tested clean merge `292b2a60301b6e892e55ae398b7c07b80372114e`, GitHub-hosted `windows-2022`, SDK **8.0.424**.

Commands configured by the unchanged workflow:

```powershell
./scripts/Invoke-Verification.ps1 -Stage BuildTest -DotnetPath dotnet
pwsh -NoProfile -File ./scripts/Test-VerificationGuards.ps1
./scripts/Invoke-Verification.ps1 -Stage Smoke -DotnetPath dotnet
./scripts/Invoke-Verification.ps1 -Stage Package -DotnetPath dotnet
```

Observed logs: **744 passed, 0 failed, 0 skipped**; Release build **0 warnings / 0 errors**. Verification guards, both WPF smoke programs, publish and development/diagnostics uploads passed. Application source is identical on final research commit `c00f65b`; only research records/artifacts changed. The final evidence/handoff-only push receives its own automatic PR check; inspect the current PR check rather than transferring a previous SHA's status. No unresolved hosted failure exists at this checkpoint.

No application artifact was downloaded or executed locally. Hosted artifacts expire under the existing seven-day policy and grant no local execution permission. Tests are synthetic/isolated; no live MSFS, aircraft/runway acceptance, passenger/deck compatibility or game-design approval is inferred.

## Outstanding owner decisions

Select the initial destination memberships and exact aircraft/airports to validate. Reconcile five gross code/coordinate conflicts, FYGK/Geluk and closure leads; obtain current usable runway data and operational permission. Native Excel/manual map interaction and targeted scenery/aircraft acceptance remain unperformed. No implementation or simulator operation follows automatically from this research.
