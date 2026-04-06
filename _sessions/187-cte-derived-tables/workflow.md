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
session: 1
phases-total: 9
phases-complete: 3

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
- Current phase: IMPLEMENT phase 4/9 (Discovery — not started yet)
- Phases 1-3 committed: IR types (CteDef, CteColumn), context API (With/FromCte), CteDtoResolver
- Immediate next step: Phase 4 — modify UsageSiteDiscovery.cs to recognize With/FromCte calls
- WIP commit: none (working tree clean)
- Test status: all 2779 tests passing
- Key files for remaining work:
  - `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — add With/FromCte to InterceptableMethods, handle chain root detection for CTE chains, tag inner chain sites
  - `src/Quarry.Generator/IR/CallSiteBinder.cs` — resolve CTE DTO types via CteDtoResolver, register pseudo-entities
  - `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — two-pass analysis (inner chains first), CTE composition, parameter merging
  - `src/Quarry.Generator/IR/SqlAssembler.cs` — prepend WITH clause in RenderSelectSql
  - `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — emit CTE carrier (With as chain root, inner param extraction)
  - `src/Quarry.Generator/CodeGen/FileEmitter.cs` — route CteDefinition/FromCte interceptor methods, suppress inner chains
  - `src/Quarry.Generator/IR/RawCallSite.cs` — add CteEntityTypeName, CteInnerTerminal, IsCteInnerChain fields
- Architecture decision: With<TDto>() is the chain root for CTE chains (creates carrier). Users()/FromCte() are clause methods that set primary table.
- The plan.md has full details on each phase

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #187 selected, worktree created, baseline green |
| 1 | DESIGN | PLAN | Design decisions confirmed: DTO-based, CTEs+derived, Option A+G API |
| 1 | PLAN | IMPLEMENT | 9-phase plan approved |
| 1 | IMPLEMENT | IMPLEMENT | Phases 1-3 committed, suspended at phase 4 (context exhaustion) |
