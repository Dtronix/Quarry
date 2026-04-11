# Workflow: 241-incremental-sql-mask-rendering
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #241
pr:
session: 1
phases-total: 4
phases-complete: 2
## Problem Statement
Incremental SQL mask rendering for compile speed. Full SQL is re-rendered per mask variant in `SqlAssembler.RenderSelectSql`. For chains with many conditional clauses, this duplicates work rendering the shared prefix (SELECT, FROM, static WHERE clauses). Splitting into a shared prefix + per-mask suffix would reduce compile-time SQL generation cost.

SQL variants differ only in which conditional WHERE/HAVING clauses are included. The shared prefix (SELECT columns, FROM table, static clauses) is identical across all variants. Parameter indices may shift between variants, complicating a simple prefix/suffix split.

Baseline: 3190 tests all passing (175 Migration, 103 Analyzers, 2912 Quarry). No pre-existing failures.

## Decisions
- 2026-04-10: Scope includes SELECT + DELETE query kinds. UPDATE skipped due to conditional SET complicating paramIndex flow.
- 2026-04-10: Conditional ORDER BY must be handled (not fall back to full re-render). Apply all-terms pre-computation to ORDER BY and post-union WHERE, consistent with main WHERE pattern.
- 2026-04-10: paramIndex flows are made mask-invariant by pre-computing offsets from ALL terms (active + inactive) for WHERE, ORDER BY, and post-union WHERE. This enables sharing prefix/middle/suffix across masks and also fixes a theoretical correctness issue with parameterized conditional ORDER BY expressions.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Started from issue #241, created worktree, baseline 3190 tests all green |
| 1 | DESIGN | PLAN | Explored SqlAssembler.cs, QueryPlan, carrier param binding. Identified mask-invariant segments. User approved SELECT+DELETE scope with conditional ORDER BY support. |
| 1 | PLAN | IMPLEMENT | 4-phase plan approved: (1) Fix ORDER BY/post-union WHERE paramIndex, (2) Batch SELECT, (3) Batch DELETE, (4) Unit tests. |
