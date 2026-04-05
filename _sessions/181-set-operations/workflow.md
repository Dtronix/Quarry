# Workflow: 181-set-operations
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: suspended
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
- Current phase: IMPLEMENT phase 3/6 (Chain Analysis & SQL Assembly), merging with phase 4 (Code Generation)
- In progress: Operand chain carrier generation — the core blocking issue
- WIP commit: 3e175b9
- Test status: All 2779 existing tests pass. 7 new CrossDialectSetOperationTests fail (operand chain not intercepted)
- Immediate next step: Solve the operand chain carrier problem (see details below)

### Blocking Issue: Operand Chain Carrier Generation

When the user writes `db.Users().Where(a).Union(db.Users().Where(b)).Prepare()`, both `db.Users()` calls share the same ChainId (same root `db` in same method). The discovery phase groups ALL sites into one chain group.

The inline operand splitting in ChainAnalyzer (line ~290) correctly identifies which sites belong to the operand (sites between the Union call and the next terminal/set-op that start with a new ChainRoot). It extracts them and builds a QueryPlan. The SQL assembly correctly renders `SELECT ... UNION SELECT ...`.

**BUT**: The operand chain's `db.Users()` and `.Where()` calls still execute at runtime. They need interceptors (ChainRoot creates the carrier, Where stores parameters). Since the operand sites are extracted from the main chain during splitting, they don't get carrier interceptors. The `db.Users()` call throws `NotSupportedException`.

### Possible Solutions (not yet attempted)
1. **Dual carrier approach**: After splitting, create a second AnalyzedChain for the operand sites so it gets its own carrier. The operand carrier holds the operand's parameters. The Union interceptor on the main carrier copies parameter values from the operand carrier to the main carrier's fields. This requires the operand chain to produce its own AssembledPlan → CarrierPlan → carrier class.
2. **Shared carrier approach**: Don't split the operand sites out. Keep them in the main chain. The operand's ChainRoot and clauses get carrier interceptors that mutate the same carrier instance. The Union interceptor is a no-op. The SQL is prebuilt with both WHERE clauses in their correct positions (left SELECT vs right SELECT). This avoids dual carriers but requires careful parameter field assignment — the operand's parameters must be assigned to different carrier fields than the main chain's parameters.
3. **Dummy builder approach**: Generate a lightweight "operand builder" class that captures parameters without full carrier machinery. The operand chain's interceptors create this builder. The Union interceptor on the main carrier receives it and copies parameters.

Approach 2 (shared carrier) seems simplest but requires distinguishing "left WHERE" from "right WHERE" parameters in the carrier's field assignment. Approach 1 (dual carrier) is cleanest but requires more codegen changes.
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Started from issue #181. Explored codebase, clarified design. Completed phases 1-2 (types, API, discovery). Phase 3-4 in progress — blocked on operand carrier generation. |
