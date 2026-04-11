# Workflow: 242-carrier-structural-tests
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #242
pr:
session: 1
phases-total: 1
phases-complete: 0
## Problem Statement
Add structural unit tests for generated carrier code shape. The carrier codegen efficiency changes (dead code removal, readonly fields, ParameterNames caching, reader field extraction) lack targeted structural assertions on generated code. Existing end-to-end tests provide indirect coverage through compilation and execution, but explicit shape assertions would catch regressions more precisely.

Baseline: All 3178 tests pass (0 failures).

## Decisions
- 2026-04-10: Four structural tests targeting dead code removal (Unsafe.As omission), readonly _sqlCache, ParameterNames.AtP in batch inserts, and _reader field extraction. All use existing test infrastructure (CreateCompilation + RunGenerator + string assertions). Approved by user.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Started from issue #242 |
