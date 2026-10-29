# Feature: starter-destination-research — career destination review

Status: Approved research scope; integration recommendations remain Draft
Approved by / date: Matt's explicit research brief in this task, 2026-10-29

## Objective
Provide a reusable, evidence-backed master destination catalogue, separate career and flying-experience rankings, geospatial output, gap analysis and proposed integration handoff for the four implemented Namibia careers.

## Gameplay behavior
None changed. Research proposes destinations and validation priorities for owner review. It grants no runtime activation, aircraft/runway approval, simulator acceptance or new mission design.

## Acceptance criteria
- Review actual employers, bases, fleets, existing memberships and generator constraints.
- Cover all four careers in a complete first pass; prioritise evidence over a twenty-destination quota.
- Distinguish real facilities, cached simulator presence, manual evidence, identity conflicts and speculative/custom opportunities.
- Apply the requested purpose gate and exact weighted Stage A /100; keep five-criterion Stage B /25 separate.
- Supply one master XLSX, ranked career views, portable data and a map/geospatial output with base relationships.
- Document gaps, source provenance, aircraft/time assumptions and a proposed integration path without implementing it.

## Non-goals
No application, mission-generator, test, official roadmap or game-vision edits. No interference with active Offshore work. No aircraft substitutions, new platform registry, passenger rules, save migration, live airport activation, custom strip generation, simulator launch/query/write, Package Tool or installed scenery changes.

## Relevant existing systems
`CareerCatalogue.json`, `NamibiaDestinationResearch.json`, `EmployerDestinationCatalogue`, `SimulatorAirportStore`, `CompanyEmployment`, `EconomyJobBoard`, `FlightAttemptTracker`, company aircraft mappings and physical fleet. Offshore PR #3 and uncommitted shared-passenger work are context only.

## Persistence implications
None to application saves or embedded resources. New research files only; snapshot of public airport coordinates contains no personal telemetry or career data.

## Required tests
Research integrity, original-membership reconciliation, correct weights, purpose-gate blanks, formula/ranking sensitivity, file export and visual review. Normal hosted Windows CI may run on the documentation PR; no product behavior changed.

## Simulator testing requirements
No simulator execution in this research. Reuse recorded airport-list/manual evidence with its exact limitations. Selected airport/aircraft runway and scenery acceptance remains future work; platforms/H125 remain gated.

## Open design decisions
Owner selection of initial destination set; airport identity corrections and immutable-route versioning; approval of exact aircraft/usable runway data; optional later changes to time preferences/rotation; separate passenger and deck qualification. No answers are inferred from this research.
