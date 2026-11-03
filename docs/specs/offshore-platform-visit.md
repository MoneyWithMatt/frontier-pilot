# Feature: offshore-platform-visit — isolated OL400 contact mission

Status: Authorised implementation; new exact artifacts/live scenario require separate execution approval.
Approved by / date: Matt's “Ok. Can you do this?” after the successful5kg cycle, 2026-11-02.

## Objective
Prepare a restricted no-pay platform test and sustained departure/contact-loss detection without enabling H125 jobs or requiring Matt to fly.

## Gameplay behavior
Standalone CLI test mission: establish20s current OL400 deck presence, observe all20reported contacts clear/on-ground false and reference height>1.5m above known deck for3continuous simulation seconds, then require20new seconds of stable deck contact. Completion is historical diagnostic information only. This first contact-cycle test is not the future FYWB→platform→FYWB route, a career assignment or a delivery. No money, resource reservation or passenger ownership is created. The already-proven5kg cycle remains separate; this test performs no payload operations.

## Acceptance criteria
- Reuse ExperimentalPlatformPresence validation/profile, native condition source and create-only AircraftConditionRecorder. Preserve every existing deck/water gate.
- Pin transient source object/title/model across the recording, require exact H125 model and known unpaused/running state. No physicalGUID inference.
- Departure needs a current established deck baseline, all contacts clear, separation above the deck and3simseconds of contiguous observations within30m/15mheight. Conservative diagnostic motion/clock plausibility rejects jumps; these are not operational loading restrictions.
- Pauses, slew, rate changes, missing/stale data, disconnect/gaps or clock resets revoke live permission and unconfirmed departure dwell. Identity changes block a partial mission. Historical departure may survive disconnect; return needs20freshseconds. Completion records each of3milestones once, with no payment API.
- Create-only recording persists frames/session sequence; offline review deterministically reconstructs historical milestones and always revokes current deck permission. Truncated no-end recordings expose RecordingClosed=false; malformed/future/duplicate/trailing records are refused and preserved. New sessions never stitch old dwell; no automatic live resume/adoption.

## Non-goals
Production destination activation, full flown round trip, normal FlightAttemptTracker proof, wages, payload/passenger changes, user save migration, desktop UI, broad aircraft compatibility. Do not modify other PR branches.

## Relevant existing systems
Core.ExperimentalPlatformPresence supplies validated observations including continuity, contact count and separation. Core.ExperimentalPlatformVisit owns only diagnostic milestones. Workflow.ExperimentalPlatformVisitRecording delegates live writes to the existing read-only recorder and reviews only explicit supplied recordings. FlightAttemptTracker remains sole normal delivery-proof creator; its airport-radius/flight-history/economy paths are unsuitable for staged platform data and unchanged. Existing cargo/passenger ownership/recovery stays separate.

## Persistence implications
No personal stores/schema migration. Explicit new private JSONL only; existing recordings are read-only for review. Incomplete histories remain inspectable without becoming current permission. Completed historical visits are never paid. No automatic replay into live mission state.

## Required tests
802.NET cases at this checkpoint (22new visit/recording cases),38Python (7new constrained-harness guards), full Windows regression/WPF/guards/package. Preserve initial test-fixture interface compile failure; fixed to actual ConditionConnectionInfo type without weakening checks. Test no native SDK, personal state or Community in ordinary CI.

## Simulator testing requirements
New read-only CLI records `record-platform-visit <new.jsonl> 210 <SDK>`; offline `review-platform-visit <existing.jsonl>` never grants live permission. Verify exact hosted bundle/dependency/script hashes before approval/execution.

Separate `offshore_vertical_contact_test.py` uses SDK-documented lat/lon and attitude freeze TOGGLE events (not guessed SET parameters). Requires both flags initiallyfalse and altitude freezefalse; verify actual true flags on deck before any collective change. Bound150wallseconds,2m lateral radius,0.2–4m referenceheight above deck,<=1kt/3ft/s/5degree attitude; same pinned H125 explicit-object reads. Reuse calibrated collective conversion and experimental vertical feedback, no position/altitude/payload/fuel/failure setters.25simseconds baseline, gentle lift toward baseline+6ft,5s clear-contact interval, descent and25s rest. Restore actual original freeze flags and collective; abort uses SLEW_ON then restoration while frozen. Unknown/changed identity/state or restoration failure stops with explicit recovery_unresolved. No silent release/retry. It is unqualified until actual ground toggle/readback and vertical trial succeed; do not weaken guards to obtain success.

A constrained vertical cycle is diagnostic contact-loss evidence, never freely flown takeoff/approach/route proof. Independently record .NET observations and controller provenance, restore/pause through supported UI and inspect terminal journal before conclusions. If freeze/collective is unsupported, stop controls and report the exact blocker; do not ask Matt to fly or assume contact from altitude/proximity.

## Open design decisions
None for the isolated experiment. Production mission policy, generated platform qualifications, full roundtrip and mixed transport remain separate follow-ups.
