# Workflow: 215-cte-lambda-column-projection
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: #215
pr:
session: 1
phases-total: 2
phases-complete: 2
## Problem Statement
Lambda form `With<TEntity, TDto>(entity => entity.Select(e => new TDto { ... }))` inner CTE SQL selects all entity columns instead of only the projected DTO properties. The non-lambda form correctly applied column reduction because the inner chain had a standalone ChainRoot. The synthetic ChainRoot created for lambda inner chains lacks projection reduction metadata.

Baseline: All 3027 tests pass (97 migration + 103 analyzer + 2827 main). No pre-existing failures.
## Decisions
- 2026-04-08: Fix in ChainAnalyzer after lambda inner chain analysis. Filter the enriched identity projection using CTE DTO column metadata (`raw.CteColumns`) to reduce SELECT to only projected columns. Approved over alternatives (AnalyzabilityChecker fix, SqlAssembler fix).
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Set up worktree, ran baseline (3027 tests pass), loaded issue #215 |
