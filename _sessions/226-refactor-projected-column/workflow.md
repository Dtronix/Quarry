# Workflow: 226-refactor-projected-column
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: FINALIZE
status: active
issue: #226
pr: #230
session: 1
phases-total: 5
phases-complete: 5
## Problem Statement
`ProjectedColumn` is a C# record with 18+ fields constructed via positional parameters across 30 call sites in 3 files. This is fragile — adding or reordering fields silently breaks all call sites. Issue identified during PR #225 review.

Baseline: All 3062 tests pass (97 Migration, 103 Analyzers, 2862 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Add IsExternalInit polyfill to enable C# 9 records in netstandard2.0 Quarry.Generator project
- 2026-04-09: Convert ProjectedColumn from sealed class to sealed record to enable `with` expressions
- 2026-04-09: Use `with` expressions at clone-with-modification sites (~16 sites across ChainAnalyzer, QuarryGenerator, ProjectionAnalyzer)
- 2026-04-09: Convert any remaining mixed positional/named call sites to fully named parameters
- 2026-04-09: Scope limited to ProjectedColumn only (not ProjectionInfo)
- 2026-04-09: Update test file call sites for consistency and named parameters

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Starting work on issue #226 |
