# Workflow: 181-set-operations
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #181
pr:
session: 1
phases-total: 6
phases-complete: 2
## Problem Statement
Add UNION / UNION ALL / INTERSECT / EXCEPT set operations to the chain API. Currently these require RawSqlAsync. The chain API is single-entity-rooted, so a "two chains merge into one" IR concept is needed. Result type is determined by the left-hand chain's projection.

Baseline: 2779 tests pass (65 analyzer, 2714 runtime). No pre-existing failures.
## Decisions
- 2026-04-05: Post-union API = full IQueryBuilder<T> re-chain (WHERE wraps in subquery, ORDER BY/LIMIT apply directly)
- 2026-04-05: Cross-entity unions supported — any compatible TResult projection (different entities OK)
- 2026-04-05: All 6 operators: Union, UnionAll, Intersect, IntersectAll, Except, ExceptAll
- 2026-04-05: Multiple chained set operations supported (q1.Union(q2).Except(q3))
- 2026-04-05: Auto subquery wrapping for post-union WHERE/HAVING/GroupBy
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Started from issue #181. Explored codebase, clarified design decisions. |
