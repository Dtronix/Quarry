# Workflow: 244-adonet-last-commandtext
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #244
pr:
session: 1
phases-total: 1
phases-complete: 1
## Problem Statement
The ADO.NET `FindCommandTextAssignment` method in `src/Quarry.Migration/AdoNetDetector.cs` returns the first `CommandText` assignment found in the block, not the last one before the `Execute*` call. If `CommandText` is reassigned, the wrong SQL may be used. The fix should collect all matching assignments, filter to those before the Execute call's span, and return the last one.

Baseline: All 3178 tests pass (0 pre-existing failures).

## Decisions
- 2026-04-10: Fix FindCommandTextAssignment to collect all CommandText assignments and return the last one before the Execute call, using span comparison. Pass the invocation node to the method for positional filtering. Add test for reassigned CommandText.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Started from issue #244 |
