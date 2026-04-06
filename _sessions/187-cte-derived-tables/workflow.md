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
session: 3
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
- Current phase: IMPLEMENT phase 9/9 (Tests — in progress)
- WIP commit: f0a8812 (Phase 9 test infrastructure and pipeline fixes)
- Test status: all 2780 tests passing (65 analyzer + 2715 main), including 1 new CTE test
- SQLite FromCte test passes end-to-end (WITH clause generated, query executes, results correct)
- **Blocking issue**: `DetectCteInnerChain` in `UsageSiteDiscovery.cs` fails for non-SQLite dialect contexts (Pg/My/Ss). The inner chain sites (Orders, Where inside With() argument) are not tagged with `:cte-inner:` suffix on their ChainId, so they get mixed into the outer chain group. This breaks CTE composition for those dialects.
  - Root cause: unknown — detection works for SQLite (TestDbContext) but not for PgDb/MyDb/SsDb. The syntactic and semantic checks in DetectCteInnerChain appear correct. Likely a subtle difference in how the semantic model resolves the parent `With()` invocation for different context types.
  - Diagnostic approach: add conditional logging inside DetectCteInnerChain for each dialect's inner chain sites to compare `parentSymbol`, `containingType`, and `IsQuarryContextType` results.
- **Fixes implemented this session** (all in the WIP commit):
  1. Added `With<TDto>()` and `FromCte<TDto>()` to `QuarryContext` base class so the semantic model can resolve CTE method calls during incremental generator discovery (before generated code exists)
  2. Added `new` keyword to generated context CTE methods to suppress shadowing warnings
  3. Fixed `EmitCteDefinition` to use `Unsafe.As<ContextClass>()` for carrier-to-context return type
  4. Fixed `DiscoverCteSite` to resolve concrete context class from receiver expression (walks chain root) instead of using method's containing type (QuarryContext)
  5. Added inner CTE chains to `ChainAnalyzer.Analyze()` results so they get carrier classes and interceptors at runtime
  6. Removed inner chain suppression from `PipelineOrchestrator` file grouping
- **Remaining Phase 9 work**:
  1. Fix multi-dialect inner chain detection (blocking)
  2. Expand CrossDialectCteTests with proper 4-dialect assertions
  3. Add CTE+Join test (requires CTE DTO entity resolution)
  4. Add captured variable test
  5. Add multiple CTE test
  6. Verify all 4 dialects produce correct SQL
- Key files:
  - `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs:DetectCteInnerChain` — multi-dialect fix needed
  - `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — expand tests
  - `src/Quarry/Context/QuarryContext.cs` — CTE base class methods (new in session 3)

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #187 selected, worktree created, baseline green |
| 1 | DESIGN | PLAN | Design decisions confirmed: DTO-based, CTEs+derived, Option A+G API |
| 1 | PLAN | IMPLEMENT | 9-phase plan approved |
| 1 | IMPLEMENT | IMPLEMENT | Phases 1-3 committed, suspended at phase 4 (context exhaustion) |
| 2 | IMPLEMENT | IMPLEMENT | Resumed — phases 4-8 committed, suspended at phase 9 (context exhaustion) |
| 3 | IMPLEMENT | IMPLEMENT | Resumed — Phase 9 pipeline fixes (6 fixes), SQLite CTE test passing, suspended (multi-dialect inner chain detection issue) |
