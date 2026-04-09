# Workflow: 216-set-op-lambda-context-resolution
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #216
pr:
session: 1
phases-total: 2
phases-complete: 1
## Problem Statement
Lambda inner chain sites inside set-op lambdas (Union/Intersect/Except) get the wrong context class when the entity type is registered in multiple contexts. CTE lambdas are unaffected because `With()` is resolved on the context class, but set-op lambdas on `IQueryBuilder` have ambiguous resolution.

Key files:
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — `DetectInnerChain` context resolution
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — lambda set-op recursive analysis

Baseline: 3027 tests, 0 failures, 0 pre-existing issues.
## Decisions
- 2026-04-08: Option A selected — propagate parent context from DetectInnerChain at discovery time. Fixes root cause before binding. Option C (ChainAnalyzer-level) rejected: too late in pipeline, binding already committed to wrong context. Option D (lambda-aware ResolveContextFromCallSite) rejected: mixes concerns, risks side effects on non-inner-chain lambdas.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Issue #216 loaded, worktree created, baseline green |
