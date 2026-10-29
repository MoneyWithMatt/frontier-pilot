# Handoff — cargo logistics investigation

- Authority: Matt's 2026-10-29 investigation/design request. **No proposed implementation authorized.** [Scope](../specs/cargo-logistics-investigation.md), [structured report A–F](../research/cargo-logistics/report.md), [verification](../research/cargo-logistics/verification.md), [static mock-up](../research/cargo-logistics/manifest-mockup.svg).
- Worktree/branch: isolated `codex/cargo-logistics-investigation`, based on `origin/master` `d5ff51650789e45e46c8828893acaba071d957e7`. Target one documentation-only draft PR to master. Do not merge or modify ongoing Offshore/destination research.
- Inspected development baseline: `f7df3ffffa620d39d55b36c7cb74d44805e1ab91`, draft PR #3; current main as above. PR #4 head `6a6bfc723ace99ed5ebca2b1d991455a507bb2c4` contains destination research, no src/tests difference. Pinned references deliberately distinguish main from unmerged passenger work.

## Findings / deliverables completed

Existing shipments already preserve job/resource/origin/destination/quantity; native stations represent aggregate mass. Multiple acceptance and same-route grouped/partial operations exist. Mixed destinations, continuing legs, explicit per-consignment UI amounts, outstation pickups and mixed logical passenger/cargo are restricted/incomplete. Current desired-state cargo hard gross/payload gates, passenger gross/CG gates and fixed dispatch mass fixtures conflict with the new pilot-responsibility direction; report records them without changing code. SDK fields are not evidence of universal native setter clamps.

Report includes current source/method/test evidence, status matrix, resource/cancellation/recovery/payment risks, minimal shipment/placement/leg extension, loader and fax-style paper study, future pilot worksheet boundary, staged tests/live acceptance/success criteria and three product decisions. Per-leg wage settlement, off-route cargo custody and outstation generation require decisions only before their affected stages. No need to ask about routine IDs, aggregate station mass or MTOW/CG policy already specified.

## Validation

- Existing BuildTest in separate clean `f7df3ff` checkout: **794/0/0**, SDK 8.0.424, full Release zero warnings/errors, receipt `sourceDirty=false`. Command/TRX digest in verification record; receipt/TRX/log copied to ignored `.ci-output-cargo-investigation/baseline/` before cleanup.
- Research Python: **57 pinned source/test anchors** checked, all report reference labels covered. Static mock-up conservation/text bounds pass and PNG visually reviewed. `git diff --check` clean; production/test/architecture/roadmap/testing diff against master empty.
- No new product tests or behavior. No local WPF/application runtime, simulator/Package Tool, installed package or personal-save operation. No live acceptance inferred.
- Documentation-only commit/PR/hosted CI checkpoint will be recorded after publication. Its CI validates unchanged master, not passenger code. Do not invent a current report commit before one exists.

## Exact next actions

1. Inspect status/HEAD and existing investigation PR before creating/updating anything; use one docs-only PR to master. Do not append research to PR #3 or rewrite its roadmap/handoff.
2. Finish documentation/reference validation, commit/push and inspect hosted Windows checks; report exact source/count/fail/skip status. Fix actual documentation/check failures without modifying production code for this task.
3. Update handoff with validated report/CI checkpoint. Preserve ignored baseline evidence before archiving the temporary baseline verification worktree. Keep the investigation report/mock-up worktree available for review.
4. Deliver recommendation and genuine stage-specific owner questions. **Stop at the investigation**; do not implement stages, remove loading gates or merge a PR without a new instruction.

No agent delegation, automatic resumption or quota bypass was used. Existing main checkout, Offshore branch/PR #3 and destination research branch/PR #4 were read-only inputs and remain untouched.
