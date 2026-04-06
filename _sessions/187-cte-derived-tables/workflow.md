# Workflow: 187-cte-derived-tables

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: suspended
issue: #187
pr:
session: 4
phases-total: 9
phases-complete: 8

## Problem Statement
Support CTEs (WITH ... AS) and derived tables (subqueries in FROM clause) in the Quarry query builder. Both introduce "query-as-entity" — a subquery result set whose columns are determined by the inner query's projection, not by a schema entity class. Requires significant IR extension to compose inner query SQL as CTE prefix or FROM-clause subquery.

Baseline: 2779 tests passing (65 analyzer + 2714 main), 0 pre-existing failures.

## Decisions
- 2026-04-05: DTO class required for CTE/derived table column shape (not type inference from anonymous projections)
- 2026-04-05: Scope includes both CTEs and derived tables
- 2026-04-05: API: Option A+G — `db.With<TDto>(innerQuery)` before main table (mirrors SQL syntax); derived tables unified as CTEs via `.FromCte<TDto>()` instead of separate IR path
- 2026-04-05: CTE name derived from DTO class name (no user-specified string parameter)
- 2026-04-05: DTO columns inferred from public properties — no attribute required
- 2026-04-05: Inner CTE queries support captured variable parameters from the start
- 2026-04-05: Inner chain handling: nested chain analysis — generator analyzes inner chain, captures SQL, suppresses standalone interception. Outer carrier includes CTE SQL as a prebuilt string.
- 2026-04-05: Multiple CTEs supported from the start — `db.With<A>(a).With<B>(b).Users()...`

## Suspend State
- Current phase: REMEDIATE — (A)/(B) items fixed, (C) issues created, rebase pending
- WIP commit: (none — clean commits on branch, rebase was aborted)
- Test status: all 2780 tests passing (65 analyzer + 2715 main) on pre-rebase branch
- **Immediate next step**: Rebase on `origin/master`. Known conflicting files:
  - `src/Quarry.Generator/Models/InterceptorKind.cs` — set operations enum values added on master; CTE enum values added on branch. Resolution: keep both, place CTE values after set operations.
  - `src/Quarry.Generator/IR/QueryPlan.cs` — set operation constructor params added on master; CTE constructor param added on branch. Resolution: keep both.
  - `src/Quarry.Generator/IR/RawCallSite.cs` — likely similar additive conflicts.
  - `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — large file, may have context conflicts.
- After rebase: run full test suite, then push and create PR.
- **Review classifications applied**:
  - **(A) Fixed**: With<TEntity,TDto> interceptor signature (commit bef1b90)
  - **(B) Done**: CteDtoResolver.Resolve() marked TODO (commit bef1b90)
  - **(B) Deferred**: Dedicated DTO class test — requires 2-arg With overload, part of CTE+Join follow-up
  - **(C) Issues created**: #205 (CTE+Join blocker), #206 (carrier conflict), #207 (boilerplate duplication)
  - **(D) Ignored**: EmitFromCte validation, manifest cosmetic, formatting, comments, bounds check

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #187 selected, worktree created, baseline green |
| 1 | DESIGN | PLAN | Design decisions confirmed: DTO-based, CTEs+derived, Option A+G API |
| 1 | PLAN | IMPLEMENT | 9-phase plan approved |
| 1 | IMPLEMENT | IMPLEMENT | Phases 1-3 committed, suspended at phase 4 (context exhaustion) |
| 2 | IMPLEMENT | IMPLEMENT | Resumed — phases 4-8 committed, suspended at phase 9 (context exhaustion) |
| 3 | IMPLEMENT | IMPLEMENT | Resumed — Phase 9 pipeline fixes (6 fixes), SQLite CTE test passing, suspended (multi-dialect inner chain detection issue) |
| 4 | IMPLEMENT | REMEDIATE | Fixed multi-dialect detection, 4-dialect FromCte test, post-CTE discovery infra, review completed, (A)/(B) fixes applied, (C) issues #205/#206/#207 created. Suspended mid-rebase. |
