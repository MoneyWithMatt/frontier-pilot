# Handoff: task 2 — shared physical passengers

- Authority: Matt's 2026-10-29 shared-passenger instruction; [approved objective/spec](../specs/shared-passengers.md), [implementation, SDK research and exact live procedure](../shared-passengers.md).
- Branch `codex/offshore-career`; reuse [draft PR #3](https://github.com/MoneyWithMatt/frontier-pilot/pull/3), targeting `master`, and [task #2](https://github.com/MoneyWithMatt/frontier-pilot/issues/2). Never merge without human approval.
- Previous clean checkpoint: head `22b9e32dda56a747b7025c66761462943f34fd24`, merge `3ed5d34adc167b59a000c35bf90368ad7941c890`, [run 11](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40856502864), 774/0/0 with guards/WPF/publish. The shared implementation is currently being committed from dirty changes on that baseline; hosted validation of its new exact commit is pending.

## Completed implementation

Core shared people/groups, explicit manifest kg, configuration-backed seat areas/capacity, reservation/boarding/carriage/arrival/disembarkation and owned station intents contain no employer, wage or fixed aircraft. `PassengerPayloadCoordinator` reuses native loading, persists absolute targets before writes, checks identity/layout/current payload immediately before writing and verifies immediate/separate retained readback, gross/CG and protected payload/fuel. Partial writes, manual conflicts, restart and already-applied targets are explicit and idempotent. No automatic rollback/adoption. Native passenger writes compare in the same SimConnect session; repeated reads use distinct definitions.

Career adapters keep Offshore $800/hr and Missionary $400/hr. Missionary adds a two-person doctor/community-liaison visit to existing eligible catalogue routes, without invented medical facilities. Tracker proof/time policy stays authoritative; verified disembarkation precedes one atomic mission/wage/balance receipt. Schema 12 preserves cargo, money and legacy simulated aboard saves without invented physical ownership. Generic disjoint/mixed carriage is possible; interim Frontier cargo/passenger exclusion is workflow-only and checked in atomic mutations. A cross-process payload lease serializes cargo/fuel/passenger writes. WPF has manifest, configuration/support/recovery status and explicit reconnect payload check.

## Capability blockers / acceptance boundaries

- Reviewed production candidate: Black Square Caravan Professional 0.1.2 gear, exact three source hashes (see report), eight individual passenger areas, native indices 3–10, actual title/company mapping and selected GUID required. No live acceptance. Missing structural station maxima/overall payload and planned CG remain explicit, not fabricated; enforce available limits and pilot aircraft limitations.
- Vision Jet SF50 and stock C152/Caravan configurations are streamed archives, no legitimate readable effective seat/station evidence obtained. New physical Vision Jet boarding is blocked. Existing schema-11 simulated aboard Offshore flights can still finish. Do not guess seats/indices, decrypt archives or change employer fleet to work around this.
- No supported seated-person or animation control established. Physical mass is separate from visible people; no external character substitute. Continue legitimate configuration/aircraft-control research, not guessed local variables.
- Platform endpoints remain unavailable until exact successful live qualification plus reviewed adapter/registry/deck rules. H125 cargo and survey/inspection remain deferred.

## Verification / preserved failures

- SDK 8.0.424, local Release BuildTest commands and earlier failures are recorded in [report](../shared-passengers.md). Full runs 1/2/4 passed 792/793/793 cases respectively with zero warnings/errors. Run 5 passed 794/0/0 after adding saved-configuration tampering coverage. Final local command `./scripts/Invoke-Verification.ps1 -Stage BuildTest -OutputDirectory .ci-output-shared-passengers-full-6` passed **794/0/0**, zero Release warnings/errors, dirty baseline `22b9e32` plus the complete implementation; only documentation changed afterward.
- Final count = 774 previous + 20 additions (16 physical payload cases, four mission reuse/recovery/save cases). Existing passenger tests use production coordinator with synthetic profiles; cargo assertions retained. SiteUiSmoke drives actual manifest and transfer controls with a synthetic guarded simulator, no installed simulator dependency.
- Preserved arrival timing run: 18/2/0; corrected read-only check timing and explicit physical post-gap timer start. Full run 3: 792/1/0 because a cargo command assertion ran before async lease acquisition finished. Corrected bounded completion waits, preserving every mass/idempotence assertion. No skips/weakened production guards.
- No local WPF, published executable, MSFS, Package Tool or live payload write. No personal save/Community modifications, SDK install, repository settings or merge. Hosted checks must cover guards, both WPF programs, publication and uploads before claiming success.
- [Run 12](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40902840515), head `f42c239ad9416ee65880168193c18bfe1d70998c` / clean merge `9c2b8bf9069f0534f451824c3bc36bce16cd64a0`: 794/0/0, nine guards and LoadingWpfSmoke passed; SiteUiSmoke compilation failed because linked synthetic fixture lacked explicit `System.IO` under WPF implicit usings. Added the import; no runtime/assertion changes. Packaging/development upload correctly blocked. Failure diagnostics linked in report; repair CI pending.

## Exact next steps

1. Inspect status/HEAD and existing PR/task/CI; do not repeat creation blindly. Finish checking full-6, commit and push this coherent implementation to existing branch/PR, then inspect exact hosted Windows regression/WPF/package results. Repair failures without bypasses.
2. Download only public-safe diagnostics if needed, verify archive checksum, inspect source receipt/TRX/screenshots. Do not execute the development artifact locally. Update this handoff with actual validated commit/run/artifacts and update task #2/PR #3.
3. Obtain explicit approval identifying the exact executable hash/build and read-only/weight-changing scenario before live action. Use disposable isolated Windows profile/reviewed harness; no general app data-root switch exists. Follow the focused procedure and record exact aircraft/simulator/SDK hashes, separate physical/model/animation outcomes and all unresolved cases.
4. Establish legitimate effective SF50 configuration before adding its profile and resuming new physical Offshore transfers. Human review/merge is separate. Do not activate platforms or expand H125/survey scope.

GitHub connector writes previously returned 403; ignored configured-auth helper `.ci-output-offshore-api/github.ps1` can update issue/PR without logging secrets (inspect current remote state first). No quota bypass/automatic continuation was added.
