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
phases-complete: 1

## Problem Statement
After PR #283 (fix for #274), `SqlAssembler.NeedsDistinctOrderByWrap` and `MayNeedDistinctOrderByWrap` on SQL Server compare projection-column-reference SQL (rendered via `RenderProjectionColumnRef` → `AppendProjectionColumnSql`, which now wraps window-function int projections with `CAST(... AS INT)`) against ORDER-BY-term SQL rendered via `SqlExprRenderer.Render` (no cast). The strings won't match, so `Distinct() + window-function projection + ORDER BY (window function)` triggers an unnecessary derived-table wrap on Ss only. Output is functionally correct, just less efficient than necessary.

Baseline test run (2026-04-29): 3423 tests passing across Quarry.Tests (3076), Quarry.Migration.Tests (201), Quarry.Analyzers.Tests (146). 0 failures. No pre-existing failures to exclude.

## Decisions
- **2026-04-30** — Fix approach: Option 1 — add a `forComparison: bool = false` parameter to `SqlAssembler.AppendProjectionColumnSql`; when true, skip the `CAST(... AS INT)` wrap. `RenderProjectionColumnRef` passes `true` so the comparison-side string omits the cast and matches `SqlExprRenderer.Render`'s un-wrapped ORDER BY rendering. Emission path is unchanged. Reason: narrowest fix that addresses the cross-path string mismatch without touching `SqlExpr`/`SqlExprRenderer` (Option 2) or duplicating logic (Option 3) or fragile post-hoc string stripping (Option 4). Rejected alternatives have larger blast radius or higher risk for the same outcome.
- **2026-04-30** — Test scope: extend `CrossDialectDistinctOrderByTests`. Add (a) Distinct + window-function projection + ORDER BY on the SAME window expression → no wrap on any dialect (the regression case for #286), and (b) Distinct + window-function projection + ORDER BY on a DIFFERENT non-projected expression → wrap on all dialects with CAST inside the inner SELECT only on Ss (positive: wrap still fires when truly needed, and the inner CAST is still emitted). Cover both ROW_NUMBER and RANK so the fix isn't accidentally function-specific. Reason: the cross-dialect file already concentrates wrap-detection coverage; #286 is a wrap-detection bug, so it belongs there.
- **2026-04-30** — Test plan revised after probe: `Sql.RowNumber(...)` in `OrderBy` is currently rejected by `SqlExprBinder` (window functions are recognized only by `ProjectionAnalyzer`). The chain emits `QRY019` and falls back to runtime LINQ — no `ORDER BY` clause is rendered, so the cross-path string mismatch in #286 is **unreachable through the public chain API today**. Phase 1's fix is preventive (guards future emit-only wrappers and any future window-function-in-OrderBy support). Decision: skip cross-dialect tests; rely on existing tests + the doc-comment guard on `AppendProjectionColumnSql.forComparison`. Reason: pinning private helper internals would test implementation detail, not behavior; cross-dialect tests can't reach the bug today.
- **2026-04-30** — Bundle a one-line fix to QRY019's diagnostic `messageFormat`. During the probe, QRY019 fired with doubled phrasing ("translated to SQL clause could not be translated to SQL") because `CallSiteTranslator.ErrorMessage` already contains the verbose clause-kind context that `messageFormat`'s prefix repeats. Edit: change `messageFormat` from `"{0} clause could not be translated to SQL at compile time. The original runtime method will be used instead."` to `"{0}. The original runtime method will be used instead."`. Reason: tiny, surfaced during this investigation, no test impact (no test pins the exact wording).

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-29 INTAKE | IMPLEMENT (in progress) | Created branch+worktree from master, baseline tests green (3423/0), workflow.md initialized for issue #286. Explored SqlAssembler wrap paths. DESIGN approved: Option 1 forComparison flag + extend CrossDialectDistinctOrderByTests with ROW_NUMBER and RANK cases. PLAN written and approved (3 phases). |
