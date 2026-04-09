# Workflow: 223-over-clause-failure-tests
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #223
pr:
session: 1
phases-total: 1
phases-complete: 0
## Problem Statement
Add failure-mode tests for malformed OVER clause lambdas in window functions. The `WalkOverChain` in `ProjectionAnalyzer` silently returns `null` for malformed lambda bodies (block bodies, non-fluent-chain expressions, unknown methods). The resulting behavior is unverified by tests.

Scenarios: block body lambda, non-OVER-clause method in chain, empty OVER clause, non-fluent-chain expression.

Baseline: All 3062 tests pass (0 failures across 3 test projects).
## Decisions
- 2026-04-09: 4 test scenarios: block body lambda, unknown method in chain, empty OVER clause, non-fluent-chain expression. Tests 2&4 have intentional compilation errors to test generator resilience.
- 2026-04-09: Empty OVER (`over => over`) is actually a success path producing `ROW_NUMBER() OVER ()` — test verifies this succeeds.
- 2026-04-09: Failure cases degrade to RuntimeBuild chain (no crash, no QRY diagnostic) — tests verify graceful degradation.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Started work on issue #223 |
