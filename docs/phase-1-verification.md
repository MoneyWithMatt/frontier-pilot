# Phase 1 verification

Implementation branch: `codex/phase-1-development-foundation`.
Baseline: `53db07322ecd3d04fdc54c014cab3d6db06df4e2`, with historical 744-test evidence in `evidence/physical-aircraft-v1/verification.md`.

This change adds development infrastructure only. No gameplay or persistence schema changes, simulator operations, Community mutations, dependency installers, personal save access, releases or repository-setting changes are intended.

## Validation status

Local command: `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-local-2` using repository SDK 8.0.424, on the baseline plus the uncommitted infrastructure changes.

- Restore succeeded; full Release solution build: zero warnings/errors.
- Main xUnit: **744 passed, zero failed/skipped**. This exactly matches the historical count; no tests were removed, weakened or excluded.
- Local TRX: ignored `.ci-output-local-2/tests/regression.trx`; logs and dirty-source receipt are beside it.
- The first sandboxed restore exited 1 without diagnostics; the same scoped operation succeeded outside that restriction. This is not represented as a successful first attempt.
- Local WPF and published desktop execution were not performed.

## Hosted verification

[Initial hosted run](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40686988533) passed on Windows 2022 with SDK 8.0.424. PR head: `e2324eefc7d790c70d68eb741b40b4e54de60b9a`; clean tested PR merge commit: `7c049244584949cf921ebc7ea21d7b84942dfae0`.

- Clean restore and Release solution build passed with zero warnings/errors.
- xUnit: **744 passed, zero failed/skipped**.
- LoadingWpfSmoke and SiteUiSmoke both passed using synthetic data on the hosted runner.
- Self-contained win-x64 publish and development upload passed. The bundle includes TRX, source metadata and file checksums.
- [Development artifact](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40686988533/artifacts/12815182618): 113,113,364 bytes, seven-day retention. GitHub reports archive SHA-256 `9eaff76688742c57b33cac5255f6b601deead8850c180a006a649f0609317438`.
- The separate diagnostics upload warned that no files matched: upload-artifact excluded the hidden parent directory. The workflow now explicitly includes hidden files only for its narrowly listed synthetic output paths. Validate the follow-up run on PR #1 before merging.

No live simulator verification, personal-save migration, local published-binary execution or repository-setting changes were performed. Successful hosted compilation and smoke tests do not establish live acceptance.
