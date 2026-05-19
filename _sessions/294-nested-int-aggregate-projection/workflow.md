# Workflow: 294-nested-int-aggregate-projection

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #294
pr:
session: 1
phases-total: 4
phases-complete: 1

## Problem Statement
Generator's projection-type resolver mis-resolves nested int aggregates as `decimal`, emitting an interceptor signature that doesn't match the user lambda's return tuple. Compilation fails with CS9144.

Reproduces with:
- 2-level: `u.Orders.Sum(o => o.Items.Count())` (inner aggregate is `int`)
- 3-level: `u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(...)))`

Does NOT reproduce with:
- 1-level: `u.Orders.Count()`
- Nested decimal-only chains (e.g., `Orders.Sum(o => o.Items.Sum(i => i.LineTotal))`)

Suggested area: `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`, specifically `TryResolveAggregateTypeFromSql` (referenced near line 2009).

### Baseline test status
Full suite green at 1052b46:
- Quarry.Analyzers.Tests: 146/146 passed
- Quarry.Migration.Tests: 201/201 passed
- Quarry.Tests: 3138/3138 passed

No pre-existing failures.

## Decisions
- **2026-05-19** Fix approach: extend `ChainAnalyzer.TryResolveSelectorClrType` to recognize nested `SubqueryExpr` selectors and recursively call `ResolveSubqueryResultType` using the parent's target entity as the nested call's outer entity. Threads `EntityRegistry` through `TryResolveSelectorClrType`. Handles arbitrary nesting depth (Count, Sum, Min, Max, Avg).
- **2026-05-19** Test scope: add new tests with full cross-dialect coverage (SQLite/Pg/MySQL/SqlServer) matching the style of `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal`. Tests live in `CrossDialectNestedSubqueryTests.cs`.
- **2026-05-19** Existing test: flip `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` to use the originally-planned 3-level `Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(...)))` shape from the test comment. Drop the workaround note about #294. Recompute expected SQL and result values for the 3-level traversal.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-15 | | INTAKE: loaded issue #294, created worktree, baseline 3485/3485 green. Entering DESIGN. |
