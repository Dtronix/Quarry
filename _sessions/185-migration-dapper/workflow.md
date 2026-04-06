# Workflow: 185-migration-dapper
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #185
pr:
session: 1
phases-total: 8
phases-complete: 7
## Problem Statement
Create Quarry.Migration NuGet package for migrating from other data access libraries to Quarry. First target: Dapper. Uses shared SQL parser from Quarry.Shared/Sql/ (#182, already merged). Architecture: Detectors (find Dapper call sites via Roslyn), Translators (SQL AST → Quarry chain C# source), Schema Resolver (maps table/column names to entities/properties), Delivery (Roslyn analyzer code fixes + CLI).

Baseline: 2859 tests pass (103 analyzer + 2756 main). No pre-existing failures.
## Decisions
- 2026-04-05: Full scope — all four components (Detector, Translator, SchemaResolver, Delivery) in this PR
- 2026-04-05: Essential SQL features in v1: SELECT (columns + *), WHERE (comparisons, AND/OR, IN, BETWEEN, IS NULL, LIKE), JOINs (INNER/LEFT), ORDER BY, LIMIT/OFFSET, aggregates (COUNT/SUM/AVG/MIN/MAX + GROUP BY/HAVING)
- 2026-04-05: netstandard2.0 target for Quarry.Migration library (matches analyzer pattern); tests target net10.0
- 2026-04-05: New QRM diagnostic prefix for migration diagnostics (separate from QRA query analysis)
- 2026-04-05: Schema resolution via Roslyn semantic model (no separate compile step)
- 2026-04-05: CLI: new 'quarry convert --from dapper' top-level command in Quarry.Tool
- 2026-04-05: Separate Quarry.Migration.Analyzers project (new NuGet package, opt-in)
- 2026-04-05: Unmappable SQL → Sql.Raw<T>() fallback with diagnostic warning per Raw usage
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #185 selected, worktree created, baseline green (2859 tests) |
| 1 | DESIGN | PLAN | Full scope confirmed. 8 design decisions recorded. |
| 1 | PLAN | IMPLEMENT | 8-phase plan approved. Starting implementation. |
