# Workflow: 256-fix-sql-raw-select-projection
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: #256
pr:
session: 1
phases-total: 3
phases-complete: 3
## Problem Statement
`Sql.Raw<T>` used inside a `Select` tuple projection silently emits an empty string literal (`""`) in the generated SQL. No diagnostic, no build warning, no runtime error — just wrong SQL. Every existing test and converter fallback uses `Sql.Raw` in predicate positions (Where / Having) only.

**Baseline tests:** all 3242 passing (Quarry.Analyzers.Tests 103, Quarry.Migration.Tests 201, Quarry.Tests 2938).

Reference locations (from issue):
- `src/Quarry.Generator/IR/SqlExprParser.cs:586` — parses `Sql.Raw<T>(template, args...)` into `RawCallExpr`.
- `src/Quarry.Generator/IR/SqlExprRenderer.cs` — renders expressions; `RawCallExpr` handling in SELECT-projection context appears to return an empty string.
- `src/Quarry.Generator/IR/SqlExprNodes.cs:500` — `RawCallExpr` definition.
- Existing coverage: `src/Quarry.Tests/SqlOutput/CrossDialectMiscTests.cs` — Where-only.
## Decisions
- 2026-04-22: Bug root cause. `ProjectionAnalyzer.AnalyzeProjectedExpression` (and its joined counterpart `ResolveJoinedProjectedExpression`) route `Sql.Raw<T>` through the generic fallback that creates a `ProjectedColumn { columnName: "", sqlExpression: <C# source text>, isAggregateFunction: false }`. `QuarryGenerator.StripNonAggregateSqlExpressions` then nullifies `SqlExpression`, and `SqlAssembler.cs:1077` emits `QuoteIdentifier("")` → `""` because the aggregate/raw branch is gated on `IsAggregateFunction`. The IR-based Where/Having path already handles `RawCallExpr` correctly via `SqlExprRenderer`; projection analysis just never builds a `RawCallExpr`.
- 2026-04-22: Fix scope = full Sql.Raw<T> Select-projection support. Detect `Sql.Raw<T>` in the Sql.* detection branch of `AnalyzeProjectedExpression` (single) and `ResolveJoinedProjectedExpression` (joined). Build a canonical `sqlExpression` using `{ColumnName}` identifier-placeholders for column-ref args, inline SQL literals for compile-time const args, and `@__proj{N}` parameter-placeholders for captured runtime vars — same conventions as window-func scalar args (`ResolveScalarArgSql`). Set `IsAggregateFunction: true` on the resulting `ProjectedColumn` so existing pipelines (`SqlAssembler` branch at line 1077, `QuoteSqlExpression` identifier/param resolution, `ChainAnalyzer.RemapProjectionParameters`, `ReaderCodeGenerator`, non-enrichment suppression via `NeedsEnrichment`) emit it correctly. Semantically `IsAggregateFunction` already means "render from SqlExpression verbatim" (window functions also set it); the naming is a misnomer but behavior fits.
- 2026-04-22: Also support joined projections (`.Select((a, b) => ...)`): add parallel branch to `GetJoinedAggregateInfo` using `{alias}.{ColumnName}` for column refs, same as existing joined aggregates/window-funcs.
- 2026-04-22: Also support single-column projection `.Select(u => Sql.Raw<T>(...))`: the `AnalyzeSingleColumn`/`AnalyzeExpression` invocation path falls through to `AnalyzeInvocation` / the column detection; ensure this path reaches the new Sql.Raw branch as well (may need a targeted addition there, depending on code flow).
- 2026-04-22: Validation. `RawCallExpr.Validate()` logic (placeholder/arg count match) should apply to projections too. Emit QRY029 (existing diagnostic) via the projection pipeline when validation fails, so projection-context Sql.Raw errors are loud, matching Where behavior. If the template isn't a compile-time string literal, fall through to unsupported (same as existing behavior).
- 2026-04-22: Test scope. Mirror the existing `CrossDialectMiscTests.cs` Sql.Raw-in-Where tests to Select-projection: (a) column reference, (b) multiple column refs, (c) captured variable, (d) literal-parameter (compile-time const), (e) no-placeholder template, plus (f) tuple + DTO + single-column + joined-projection variants. All 4 dialects (SQLite/Pg/MySQL/SqlServer).
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-22 INTAKE | 2026-04-22 DESIGN | Loaded issue #256, created worktree, baseline tests green (3242 passing) |
