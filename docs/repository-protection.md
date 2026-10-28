# Recommended repository protection (not applied)

Owner approval is required before changing repository settings. Public visibility is intentional. Use standard GitHub-hosted runners only; no self-hosted runner on the gaming PC, paid API automation, deployment secrets or `pull_request_target` execution.

After the first successful CI run, configure a rule for `master`:

- Require pull requests, resolved conversations and the **Windows build, regression and WPF** status check from Windows CI; require the branch to be current with its base.
- Block force pushes and deletion. Restrict bypass/direct pushes, including automation, according to the owner's access policy.
- Require one independent approving review if a second trusted reviewer is available; dismiss stale approval after code changes. GitHub authors cannot approve their own PR, so a sole maintainer must choose an achievable review rule rather than accidentally deadlocking integration. Human merge approval remains required regardless.
- Review workflow, script, instruction and test-definition changes particularly carefully. The same agent must not weaken checks to approve itself. Consider CODEOWNERS once a real independent reviewer is designated; no fictional reviewer is assigned here.
- Keep Actions permissions read-only by default, do not allow Actions to approve PRs, and retain fork workflow approval controls. Pin reviewed actions to immutable SHAs.

The workflow itself requests only `contents: read`, does not persist checkout credentials and has no secrets. Fork builds run untrusted code only on disposable hosted machines. Successful tests/checksums do not establish that a binary is safe to execute on the gaming PC. Existing allowlisted scenario approval and approval of a changed executable are separate decisions.

Artifacts/logs for this public repository must be treated as public. Upload only synthetic test output. Do not add personal recordings, saves, tokens, SDK installers or local configuration. Seven-day retention reduces accumulation but is not a privacy boundary. Git ignore patterns are defense in depth, not a secret scanner; inspect staged diffs before pushing.

If Actions is disabled or external actions require owner approval, enable/approve the pinned official checkout/setup-dotnet/upload-artifact actions after review. No additional paid service is needed. Do not configure billing increases automatically.
