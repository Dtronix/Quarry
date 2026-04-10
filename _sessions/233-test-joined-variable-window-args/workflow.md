# Workflow: 233-test-joined-variable-window-args
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #233
pr: #237
session: 1
phases-total: 1
phases-complete: 1
## Problem Statement
Add integration tests for variable window function arguments in joined queries. The joined query path (`AnalyzeJoinedSyntaxOnly` -> `BuildJoinedLagLeadSql`) was modified to thread `SemanticModel` and `projectionParams` for variable parameterization, but no integration test exercises this path with actual captured variables.

Baseline: All 3101 tests pass (97 Migration, 103 Analyzers, 2901 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Add 4 joined-query variable-parameter tests: Lag variable offset, Lead variable offset, Lag variable default, Ntile variable. Covers all variable-parameterized joined window function code paths.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | INTAKE | Issue #233 loaded. Branch and worktree created. Baseline green (3101 tests). |
