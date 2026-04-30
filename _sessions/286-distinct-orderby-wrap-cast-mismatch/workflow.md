# Workflow: 286-distinct-orderby-wrap-cast-mismatch

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #286
pr:
session: 1
phases-total: 3
phases-complete: 0

## Problem Statement
After PR #283 (fix for #274), `SqlAssembler.NeedsDistinctOrderByWrap` and `MayNeedDistinctOrderByWrap` on SQL Server compare projection-column-reference SQL (rendered via `RenderProjectionColumnRef` → `AppendProjectionColumnSql`, which now wraps window-function int projections with `CAST(... AS INT)`) against ORDER-BY-term SQL rendered via `SqlExprRenderer.Render` (no cast). The strings won't match, so `Distinct() + window-function projection + ORDER BY (window function)` triggers an unnecessary derived-table wrap on Ss only. Output is functionally correct, just less efficient than necessary.

Baseline test run (2026-04-29): 3423 tests passing across Quarry.Tests (3076), Quarry.Migration.Tests (201), Quarry.Analyzers.Tests (146). 0 failures. No pre-existing failures to exclude.

## Decisions
- **2026-04-30** — Fix approach: Option 1 — add a `forComparison: bool = false` parameter to `SqlAssembler.AppendProjectionColumnSql`; when true, skip the `CAST(... AS INT)` wrap. `RenderProjectionColumnRef` passes `true` so the comparison-side string omits the cast and matches `SqlExprRenderer.Render`'s un-wrapped ORDER BY rendering. Emission path is unchanged. Reason: narrowest fix that addresses the cross-path string mismatch without touching `SqlExpr`/`SqlExprRenderer` (Option 2) or duplicating logic (Option 3) or fragile post-hoc string stripping (Option 4). Rejected alternatives have larger blast radius or higher risk for the same outcome.
- **2026-04-30** — Test scope: extend `CrossDialectDistinctOrderByTests`. Add (a) Distinct + window-function projection + ORDER BY on the SAME window expression → no wrap on any dialect (the regression case for #286), and (b) Distinct + window-function projection + ORDER BY on a DIFFERENT non-projected expression → wrap on all dialects with CAST inside the inner SELECT only on Ss (positive: wrap still fires when truly needed, and the inner CAST is still emitted). Cover both ROW_NUMBER and RANK so the fix isn't accidentally function-specific. Reason: the cross-dialect file already concentrates wrap-detection coverage; #286 is a wrap-detection bug, so it belongs there.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-29 INTAKE | IMPLEMENT (in progress) | Created branch+worktree from master, baseline tests green (3423/0), workflow.md initialized for issue #286. Explored SqlAssembler wrap paths. DESIGN approved: Option 1 forComparison flag + extend CrossDialectDistinctOrderByTests with ROW_NUMBER and RANK cases. PLAN written and approved (3 phases). |
