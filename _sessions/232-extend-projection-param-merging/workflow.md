# Workflow: 232-extend-projection-param-merging
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #232
pr:
session: 1
phases-total: 2
phases-complete: 2
## Problem Statement
Window function variable parameters (e.g., `Sql.Ntile(n, ...)` where `n` is a captured variable) are correctly parameterized in `AnalyzeChainGroup`, but `AnalyzeOperandChain` (used for set operation operands) does not include the same `@__proj{N}` -> `{@globalIndex}` placeholder remapping logic. If a set operation operand contains a Select with variable window function args, the `@__proj{N}` placeholders will remain unresolved in the SQL.

Baseline: 3101 tests, 0 failures, 0 pre-existing issues.
## Decisions
- 2026-04-09: Copy projection parameter merging block from AnalyzeChainGroup (lines 1262-1295) into AnalyzeOperandChain after BuildProjection call at line 2549. Add cross-dialect set-operation test with variable window function arg.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Loaded issue #232, created worktree, baseline green (3101 tests) |
