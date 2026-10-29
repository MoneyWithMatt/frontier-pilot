# Handoff — cargo logistics investigation

- Authority: Matt's 2026-10-29 investigation/design request. **No proposed implementation authorized.** [Scope](../specs/cargo-logistics-investigation.md), [structured report A–F](../research/cargo-logistics/report.md), [verification](../research/cargo-logistics/verification.md), [static mock-up](../research/cargo-logistics/manifest-mockup.svg).
- Worktree/branch: isolated `codex/cargo-logistics-investigation`, based on `origin/master` `d5ff51650789e45e46c8828893acaba071d957e7`. [Documentation-only draft PR #5](https://github.com/MoneyWithMatt/frontier-pilot/pull/5) targets master. Do not merge or modify ongoing Offshore/destination research.
- Last validated report checkpoint: `dd0d5f4217ead5f30e3c7a92eeb74f6ec744f2d3`, clean merge `44122eb6e6b4fb3f648f1e2881155a933d64a1c5`, [run 18](https://github.com/MoneyWithMatt/frontier-pilot/actions/runs/40934268493): 744/0/0, zero warnings/errors, nine guards, both WPF programs, package and uploads passed. Final report-only follow-up adds historical Duke/PA28 context and this evidence; final head/CI is recorded in PR #5 without another self-referential source commit.
- Inspected development baseline: `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`, draft PR #3; current main as above. PR #4 head `6a6bfc723ace99ed5ebca2b1d991455a507bb2c4` contains destination research, no src/tests difference. Pinned references deliberately distinguish main from unmerged passenger work.

## Findings / deliverables completed

Existing shipments already preserve job/resource/origin/destination/quantity; native stations represent aggregate mass. Multiple acceptance and same-route grouped/partial operations exist. Mixed destinations, continuing legs, explicit per-consignment UI amounts, outstation pickups and mixed logical passenger/cargo are restricted/incomplete. Current desired-state cargo hard gross/payload gates, passenger gross/CG gates and fixed dispatch mass fixtures conflict with the new pilot-responsibility direction; report records them without changing code. SDK fields are not evidence of universal native setter clamps.

Report includes current source/method/test evidence, status matrix, resource/cancellation/recovery/payment risks, minimal shipment/placement/leg extension, loader and fax-style paper study, future pilot worksheet boundary, staged tests/live acceptance/success criteria and three product decisions. Per-leg wage settlement, off-route cargo custody and outstation generation require decisions only before their affected stages. No need to ask about routine IDs, aggregate station mass or MTOW/CG policy already specified.

## Validation

- Existing BuildTest in separate clean `f7df3ff` checkout: **794/0/0**, SDK 8.0.424, full Release zero warnings/errors, receipt `sourceDirty=false`. Command/TRX digest in verification record; receipt/TRX/log copied to ignored `.ci-output-cargo-investigation/baseline/` before cleanup.
- Research Python: **57 pinned source/test anchors** checked, all report reference labels covered. Static mock-up conservation/text bounds pass and PNG visually reviewed. `git diff --check` clean; production/test/architecture/roadmap/testing diff against master empty.
- No new product tests or behavior. No local WPF/application runtime, simulator/Package Tool, installed package or personal-save operation. No live acceptance inferred.
- Documentation-only hosted run 18 passed unchanged main's **744/0/0**, not the unmerged passenger snapshot's 794 cases. [Verification record](../research/cargo-logistics/verification.md) has commands/artifact links/digests; diagnostics independently checksummed, clean source receipts and TRX inspected. No investigation CI failure. Automatic application artifact not downloaded/executed.
- Temporary baseline verification worktree was archived only after receipt/TRX/build log were copied to the investigation worktree's ignored evidence directory. The report/mock-up worktree remains available. Primary Offshore checkout, PR #3 and PR #4 were not changed. GitHub connector create returned 403; ignored configured-Git-auth API helper created/updates PR #5 only, without logging credentials.

## Exact next actions

1. Inspect status/HEAD and PR #5. Check final report/handoff checkpoint CI only if its result is not already recorded in the PR. Never create another PR or source commit merely to repeat completed external actions.
2. Review report A–F and static mock-up. Distinguish current implementation, synthetic tests, source-derived risk and proposed work; no new simulator acceptance.
3. Obtain a new implementation instruction selecting a small stage; resolve the relevant owner decisions before paid multi-leg/custody/generation work. Keep report worktree available and ignored evidence preserved.
4. **Stop at the investigation**; do not implement stages, remove loading gates, modify ongoing Offshore work or merge a PR under this task.

No agent delegation, automatic resumption or quota bypass was used. Existing main checkout, Offshore branch/PR #3 and destination research branch/PR #4 were read-only inputs and remain untouched.
