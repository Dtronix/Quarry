# Workflow: 227-joined-over-failure-tests
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: FINALIZE
status: active
issue: #227
pr: #231
session: 1
phases-total: 1
phases-complete: 1
## Problem Statement
Issue #223 added failure-mode tests for single-entity OVER clause lambdas (`ParseOverClause` / `WalkOverChain`). The joined-entity variant `ParseJoinedOverClause` (which handles OVER clauses in joined Select projections) has the same failure modes but is not yet covered by tests.

Need to add test cases in `CarrierGenerationTests.cs` using joined Select projections (e.g., `db.Orders().Join(...).Select((o, u) => ...)`) with malformed OVER lambdas covering: block body lambda, unknown method in OVER chain, empty OVER clause, non-fluent-chain expression.

Baseline: All 3090 tests pass (97 Migration, 103 Analyzers, 2890 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Add 4 joined-entity failure-mode tests mirroring #223 single-entity tests: block body, unknown method, empty OVER, non-fluent chain.
- 2026-04-09: Use `db.Users().Join<Order>((u, o) => u.UserId == o.UserId).Select((u, o) => ...)` pattern to route through `ParseJoinedOverClause`.
- 2026-04-09: TestDbContext needs both `Users()` and `Orders()` accessors for join tests.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Issue #227 loaded, worktree created, baseline green (3090 tests). |
