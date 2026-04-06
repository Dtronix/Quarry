# Workflow: 187-cte-derived-tables

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
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
- Current phase: IMPLEMENT phase 9/9 (Tests — partially complete)
- WIP commit: (none — clean commit a748e02)
- Test status: all 2780 tests passing (65 analyzer + 2715 main), including 1 CTE test (4-dialect FromCte)
- **Completed in this session**:
  1. Fixed `DetectCteInnerChain` candidate symbols fallback — non-SQLite dialects now correctly detect CTE inner chains
  2. Expanded `Cte_FromCte_SimpleFilter` test to verify SQL for all 4 dialects (SQLite, Pg, My, Ss)
  3. Added `TryResolveViaChainRootContext` — resolves context-specific methods after CTE With() calls
  4. Added `DiscoverPostCteSites` — forward-scans CTE chains for unresolvable post-With methods
  5. Added `DiscoverPreparedTerminalsForCteChain` — discovers prepared terminals on CTE chain variables
- **Blocking issue for CTE+Join/captured vars/multiple CTEs**:
  - `QuarryContext.With<TDto>()` returns `QuarryContext` (base class) during source generation
  - Context-specific methods like `Users()` can't be resolved on `QuarryContext`
  - This cascades: everything after `Users()` (Join, Select, Prepare, terminals) also fails to resolve
  - The `DiscoverPostCteSites` infrastructure partially addresses this but the full chain type resolution remains incomplete (incorrect builder types in generated interceptors)
  - Root cause: the generated context class (with `new TestDbContext With<TDto>()` returning concrete type) isn't available during the generator's own semantic model analysis
  - Potential fixes:
    a. Self-referencing generic pattern: `QuarryContext<TSelf>` where `With()` returns `TSelf`
    b. Comprehensive syntactic chain discovery (expand `DiscoverPostCteSites` with full type tracking)
    c. Make context CTE methods available via `RegisterPostInitializationOutput`
- **Remaining Phase 9 work** (all blocked by above):
  1. CTE+Join test (With→Users→Join→Select)
  2. Captured variable test (inner query with captured var parameter)
  3. Multiple CTE test (With→With→Users→Join→Join)
  4. All 3 require `.Users()` after `.With()`, which triggers the cascade
- Key files:
  - `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — DetectCteInnerChain fix, post-CTE discovery
  - `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — 4-dialect FromCte test

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #187 selected, worktree created, baseline green |
| 1 | DESIGN | PLAN | Design decisions confirmed: DTO-based, CTEs+derived, Option A+G API |
| 1 | PLAN | IMPLEMENT | 9-phase plan approved |
| 1 | IMPLEMENT | IMPLEMENT | Phases 1-3 committed, suspended at phase 4 (context exhaustion) |
| 2 | IMPLEMENT | IMPLEMENT | Resumed — phases 4-8 committed, suspended at phase 9 (context exhaustion) |
| 3 | IMPLEMENT | IMPLEMENT | Resumed — Phase 9 pipeline fixes (6 fixes), SQLite CTE test passing, suspended (multi-dialect inner chain detection issue) |
| 4 | IMPLEMENT | IMPLEMENT | Resumed — Fixed multi-dialect CTE detection (candidate symbols fallback), expanded FromCte test to 4 dialects, added post-CTE discovery infrastructure. CTE+Join/captured vars/multiple CTEs blocked by With() return type cascade. |
