# Plan: 233-test-joined-variable-window-args

## Overview

Add 4 integration tests to `CrossDialectWindowFunctionTests.cs` that exercise the joined query path (`BuildJoinedLagLeadSql` and `BuildNtileSql`) with captured variable arguments. These tests mirror the existing single-entity variable tests (lines 665-775) but use the `Users().Join<Order>()` pattern established by the existing joined tests (lines 337-383).

## Key Concepts

- **Variable parameterization**: When a non-constant variable (e.g., `var offset = 1`) is used as a window function argument, the source generator emits a parameterized placeholder (`@p0`, `$1`, `?`, `@p0` depending on dialect) instead of inlining the value. This is handled by `ResolveScalarArgSql` in `ProjectionAnalyzer.cs`.
- **Joined query path**: Joined queries use table aliases (`t0`, `t1`) and route through `BuildJoinedLagLeadSql` (for LAG/LEAD) and `BuildNtileSql` (for NTILE, shared with single-entity). The column references are resolved via `GetJoinedColumnSql` which produces `"t0"."Column"` style output.
- **Test pattern**: Each test creates 4 dialect variants (Lite/SQLite, Pg, My, Ss), calls `.Prepare()`, then asserts the generated SQL via `QueryTestHarness.AssertDialects()`.

## Phase 1: Add all 4 joined variable window function tests

This is a single atomic phase since all 4 tests follow the identical pattern and belong in the same region.

Add the following tests to the `#region Joined Queries` section (after the existing `WindowFunction_Joined_SumOver` test, before `#endregion`):

1. **`WindowFunction_Joined_Lag_VariableOffset`** — Captures `var offset = 1`, uses `Sql.Lag(o.Total, offset, over => over.OrderBy(o.OrderDate))` in a joined select. Asserts SQL contains `LAG("t1"."Total", @p0)` with dialect-appropriate parameter placeholder and table alias.

2. **`WindowFunction_Joined_Lead_VariableOffset`** — Captures `var offset = 1`, uses `Sql.Lead(o.Total, offset, over => over.OrderBy(o.OrderDate))` in a joined select. Same assertion pattern with `LEAD`.

3. **`WindowFunction_Joined_Lag_VariableDefault`** — Captures `var defaultVal = 0m`, uses `Sql.Lag(o.Total, 1, defaultVal, over => over.OrderBy(o.OrderDate))` in a joined select. Asserts the literal `1` is inlined and `defaultVal` is parameterized as `@p0`.

4. **`WindowFunction_Joined_Ntile_Variable`** — Captures `var buckets = 3`, uses `Sql.Ntile(buckets, over => over.OrderBy(o.Total))` in a joined select. Asserts `NTILE(@p0)` with dialect-appropriate placeholder.

Each test uses `Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => ...)` and the dialect-specific entity types (`Pg.Order`, `My.Order`, `Ss.Order`) matching the existing joined tests.

### Tests
- Run full test suite. All 3101+ existing tests must pass, plus the 4 new tests.
