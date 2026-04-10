# Workflow: add-advanced-benchmarks
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: discussion
pr: #235
session: 2
phases-total: 6
phases-complete: 6
## Problem Statement
Add new benchmark classes to Quarry.Benchmarks comparing Quarry against Raw ADO.NET, Dapper, EF Core, and SqlKata for features added since the initial benchmarks: Window Functions, CTEs, Subqueries (EXISTS/correlated), and Set Operations (UNION/INTERSECT/EXCEPT). Each benchmark follows the existing pattern (BenchmarkBase, same libraries, same database setup).

Baseline: All 3090 tests pass (97 Migration, 103 Analyzers, 2890 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Use raw SQL escape hatch for libraries that don't natively support features (EF Core FromSqlRaw, SqlKata raw expressions). Real-world comparison.
- 2026-04-09: Comprehensive scenarios (3-4 per category): Window (4), CTE (3), Subquery (4), Set Operations (3) = 14 total scenarios.
- 2026-04-09: Scenarios approved: Window(ROW_NUMBER, Running SUM, RANK, LAG), CTE(simple filter, CTE+JOIN, multi-CTE), Subquery(EXISTS, filtered EXISTS, scalar COUNT, aggregate SUM), SetOps(UNION ALL, INTERSECT, EXCEPT).

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Established scope: 4 new benchmark classes for window functions, CTEs, subqueries, and set operations. Baseline green. |
