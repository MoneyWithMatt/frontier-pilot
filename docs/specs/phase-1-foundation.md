# PHASE-1 — Autonomous development foundation

Status: Approved scope, implementation subject to PR review.
Approval reference: developer's “Frontier Pilot — Phase 1: Autonomous Development Foundation” task, 28 October 2026.

## Objective / behavior
Enable a clean GitHub-hosted Windows machine to restore, build, test and publish Frontier Pilot, with useful instructions and reviewable evidence. No gameplay changes.

## Acceptance criteria
- Exact repository .NET 8 SDK; complete Release solution build; existing xUnit suite and separately reported WPF smoke checks.
- PR-to-master, master push and manual CI triggers; least privilege, pinned actions, obsolete-run cancellation and seven-day artifacts.
- Self-contained win-x64 development bundle with actual source SHA, UTC timestamp, SHA-256 manifest and test results. Failed required tests prevent development publication.
- Current architecture/testing/roadmap/agent/readme documents, lightweight spec/PR templates and durable session handoff.
- No ordinary CI dependency on MSFS, proprietary SDK installation, local caches, Community or player saves.

## Non-goals and confirmed decisions
Public repository intentionally; master integrates reviewed isolated branches. Existing subscriptions only: no paid APIs/services. No self-hosted runner on gaming PC. No releases, deployment, signing credentials, repository-settings changes or merge without approval. Later standing approval covers reviewed allowlisted scenarios; changed executables do not automatically inherit execution approval. No autonomous quota restart/bypass.

## Existing systems / persistence
Reuse .NET Core, MsfsSdk, Workflow, Desktop and CLI projects, current publish profile and synthetic tests. No application schema/migration changes. Preserve all survey, ownership, ledger and delivery-evidence safeguards and historical evidence.

## Required verification
Local main regression and Release build; actual clean hosted Windows run including separate smoke programs, publish, artifact and checksum inspection. Compare actual counts to historical 744 without enforcing that number. Report unsupported/blocked checks rather than inventing results.

## Simulator testing / design decisions
No live simulator test required or authorized for this infrastructure change. No new gameplay design. Owner reviews the PR and separately chooses recommended protection settings.
