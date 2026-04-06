# Workflow: 181-set-operations
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #181
pr: #201
session: 3
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
- Current phase: REVIEW (user requested reset to REVIEW twice — wants another review pass)
- WIP commit: bdc6dda
- Test status: All 2870 tests pass (2767 runtime + 103 analyzer), 3 diagnostic tests skipped (test infra limitation)
- Immediate next step: Run fresh REVIEW analysis pass, classify findings, then REMEDIATE → FINALIZE
- PR #201 is open and pushed. Branch is rebased on origin/master (up to date).

### Completed remediation from prior reviews:
- Fixed operand parameter placeholder indices (@p0 collision → correct @p{offset})
- Fixed diagnostic ID collision (QRY041 → QRY070, QRY042 → QRY071)
- Fixed chained set operations (SetOperationBodyEmitter uses per-index dispatch)
- Fixed PostUnionWhereTerms/GroupByExprs/HavingExprs in QueryPlan.Equals
- Added post-union GroupBy/Having redirect to subquery wrapping
- Added PipelineErrorBag reporting for silent catch blocks
- Added cross-dialect parameterized SQL assertions (SQLite/PostgreSQL/MySQL/SQL Server)
- Added post-union GroupBy test
- Added chained set operations test
- Added QRY070/071 diagnostic test scaffolding (marked [Ignore])

### Known remaining items (from second review):
- AnalyzeOperandChain duplicates ~215 lines from AnalyzeChainGroup (refactoring deferred)
- Cross-entity unions deferred (requires interceptor signature changes for generic type params)
- GetSetOperatorKeyword default case returns "UNION" instead of throwing (low risk)
- Post-union GroupBy paramIndex not advanced for parameterized expressions (unlikely in practice)
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Started from issue #181. Explored codebase, clarified design. Completed phases 1-2 (types, API, discovery). Phase 3-4 in progress — blocked on operand carrier generation. |
| 2 | IMPLEMENT | REVIEW | Resumed. Solved operand carrier generation (dual-carrier approach). Phases 3-6 complete. Two review passes with remediation. Fixed: param indices, diagnostic IDs, chained set ops, Equals gaps, cross-dialect tests, post-union GroupBy. PR #201 created. User requested third review pass before merge. |
| 3 | REVIEW | REMEDIATE | Resumed. Third review: 10 findings, all classified A (fix now). Fixed: QRY072/073 diagnostics, GetSetOperatorKeyword throw, GROUP BY paramIndex, RawCallSite.Equals, stale comment, XML doc, diagnostic tests, HAVING test, AnalyzeOperandChain refactor. 2873 tests pass, 0 skipped. |
