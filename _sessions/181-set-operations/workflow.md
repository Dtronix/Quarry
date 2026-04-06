# Workflow: 181-set-operations
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: #181
pr: #201
session: 2
phases-total: 6
phases-complete: 6
## Problem Statement
Add UNION / UNION ALL / INTERSECT / EXCEPT set operations to the chain API. Currently these require RawSqlAsync. The chain API is single-entity-rooted, so a "two chains merge into one" IR concept is needed. Result type is determined by the left-hand chain's projection.

Baseline: 2779 tests pass (65 analyzer, 2714 runtime). No pre-existing failures.
## Decisions
- 2026-04-05: Post-union API = full IQueryBuilder<T> re-chain (WHERE wraps in subquery, ORDER BY/LIMIT apply directly)
- 2026-04-05: Cross-entity unions supported — any compatible TResult projection (different entities OK)
- 2026-04-05: All 6 operators: Union, UnionAll, Intersect, IntersectAll, Except, ExceptAll
- 2026-04-05: Multiple chained set operations supported (q1.Union(q2).Except(q3))
- 2026-04-05: Auto subquery wrapping for post-union WHERE/HAVING/GroupBy
- 2026-04-05: Dual-carrier approach for operand chains — each operand gets its own carrier class with parameter fields; Union interceptor copies from operand carrier to main carrier at correct offsets
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Started from issue #181. Explored codebase, clarified design. Completed phases 1-2 (types, API, discovery). Phase 3-4 in progress — blocked on operand carrier generation. |
| 2 | IMPLEMENT | REVIEW | Resumed. Solved operand carrier generation (dual-carrier approach). Phases 3-6 complete. Post-union WHERE subquery wrapping. QRY041/QRY042 diagnostics. All 2787 tests pass. Cross-entity unions deferred (requires interceptor signature changes). |
