# Workflow: 217-lambda-cte-diagnostic-tests

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #217
pr:
session: 1
phases-total: 1
phases-complete: 0

## Problem Statement
The lambda CTE form (`With<TDto>(dto => ...)`) lacks negative/diagnostic test coverage. Phase 5c of #213 called for QRY080/QRY081/QRY082 equivalents for the lambda form, but only QRY082 (duplicate CTE name) was migrated.

Missing test coverage:
- **QRY080 equivalent**: Lambda body that isn't a valid chain (empty lambda, lambda returning variable)
- **CteInnerChainNotAnalyzable**: Cases where the inner chain cannot be analyzed

Baseline: 2831 tests pass, 0 failures (post-rebase on origin/master including #219, #220).

## Decisions
- 2026-04-08: Add three lambda-form QRY080 test cases to CarrierGenerationTests.cs: (1) identity lambda, (2) variable-returning lambda, (3) non-Quarry method lambda. Follow existing test pattern (assert QRY080 present, QRY900 absent). No new diagnostics needed — existing QRY080 covers these.
- 2026-04-08: Rebased on origin/master (#219, #220). ChainAnalyzer lambda success path changed (projection reduction), but error/diagnostic paths unchanged. Design still valid.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE     |           | Loaded issue #217, created worktree, baseline 2827 tests pass |
