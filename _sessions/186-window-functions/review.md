# Review: 186-window-functions

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 4 enrichment tests missing. Plan specifies "Add unit test in existing enrichment test coverage verifying that window function SQL expressions resolve the correct column type." No such unit test was added; `ExtractColumnNameFromAggregateSql` fix is only implicitly tested via end-to-end SQL output tests. | Low | The fix itself is present and correct (truncating search region at ` OVER (`), but a direct unit test would catch regressions if the extraction logic changes. Implicit coverage via SumOver/CountOver execution tests partially compensates. |
| Phase 5 tests: only 1 of 2 planned joined tests implemented. Plan specifies `WindowFunction_Joined_RowNumber` and `WindowFunction_Joined_AggregateOver`. Only `WindowFunction_Joined_RowNumber` exists. | Low | The joined aggregate OVER path (`GetJoinedWindowFunctionInfo` aggregate branch) is untested. The code is present and structurally mirrors the non-joined path which IS tested, so risk is low. |
| Phase 3 execution tests: plan specifies 2 execution tests (#15 `WindowFunction_RowNumber_ExecuteFetchAll` and #16 `WindowFunction_Lag_ExecuteFetchAll`). Instead, execution assertions are embedded within `WindowFunction_RowNumber_OrderBy`, `WindowFunction_DenseRank_OrderByDescending`, `WindowFunction_SumOver`, `WindowFunction_CountOver`, and `WindowFunction_Joined_RowNumber`. LAG execution is not tested. | Low | Execution coverage is actually broader than planned (5 tests have execution assertions vs. 2 planned), but LAG execution is missing. LAG returns NULL for the first row -- verifying this boundary would be valuable. |
| Plan specifies `PartitionBy<T>(params T[] columns)` initially but Decisions log documents switch to `params object[]`. Implementation uses `params object[]`. | None | Decision was documented and implementation matches the final decision. No concern. |
| Aggregate OVER tests: plan lists SumOver and CountOver tests. No tests for AvgOver, MinOver, MaxOver OVER variants. | Low | These paths share `BuildAggregateOverSql` / `BuildJoinedAggregateOverSql` which are structurally identical to the tested Sum/Count paths. Risk is minimal but coverage could be improved. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `BuildNtileSql` uses `bucketsExpr.ToString()` to emit the NTILE argument directly into SQL. If the expression is a variable reference or complex expression rather than a literal, the raw C# source text is emitted into SQL (e.g., `NTILE(myVar)` instead of a parameterized value). | Medium | For the current API signature `Sql.Ntile(int buckets, ...)`, users will typically pass literals. However, passing a variable like `Sql.Ntile(n, ...)` would emit `NTILE(n)` which is invalid SQL. This is the same pattern used by LAG/LEAD offset/default args (`arguments[1].Expression`, `arguments[2].Expression`), so it is a systematic concern across all window functions with non-column arguments. Consider parameterizing or at least validating that the expression is a literal. |
| `BuildOverClauseString` produces an empty OVER clause string when neither PartitionBy nor OrderBy is specified (e.g., `Sql.RowNumber(over => over)`). This would generate `ROW_NUMBER() OVER ()` which is valid SQL but semantically surprising -- it provides no deterministic ordering. | Low | The API allows this but most databases accept `OVER ()`. This is more of a usability concern than a bug. |
| Joined query OVER clause produces unquoted table aliases: `t0."UserName"` instead of `"t0"."UserName"`. This is acknowledged in the test comment and is consistent with how `GetJoinedColumnSql` works for regular joined aggregates. | Low | SQL engines accept unquoted aliases (they are plain identifiers without special characters), so this works correctly. However, it creates visual inconsistency between the SELECT column list (quoted aliases) and the OVER clause (unquoted aliases). This is a pre-existing pattern, not introduced by this branch. |
| `ReQuoteSqlExpression` in `SqlFormatting.cs` assumes identifiers never contain escaped double quotes (i.e., `""` inside an identifier). If an identifier contains `""`, the re-quoting scan would misidentify the boundary. | Low | Column names with embedded double quotes are extremely rare in practice. The same assumption exists in `ExtractColumnNameFromAggregateSql`. |

## Security

No concerns. All SQL generation is compile-time with quoted identifiers. No user input flows into SQL string construction at runtime. The `Sql.*` methods throw at runtime, preventing misuse outside the generator pipeline.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No test for LAG/LEAD 3-argument overload (`Sql.Lag(o.Total, 2, 0m, over => ...)`). The `BuildLagLeadSql` method has a `nonLambdaArgCount == 3` branch that is untested. | Medium | This branch emits the default value argument using `arguments[2].Expression.ToString()`, which has the same raw-source-to-SQL concern as NTILE. Without a test, both the SQL shape and the argument handling are unverified. |
| No test for LEAD with offset (`Sql.Lead(o.Total, 2, over => ...)`). Only the simple LEAD overload is tested. | Low | Structurally identical to the tested LAG with offset path, since `BuildLagLeadSql` handles both LAG and LEAD identically. |
| No negative/failure-mode tests. What happens when the OVER lambda body is not a fluent chain (e.g., `over => { var x = over.OrderBy(o.Date); return x; }`)? The `WalkOverChain` would encounter a block body and `ParseOverClause` returns null, causing `GetWindowFunctionInfo` to return `(null, null)`. The resulting behavior (silent failure or diagnostic) is unverified. | Low | The generator likely emits a diagnostic or skips the column. Testing this would document the expected behavior for malformed OVER lambdas. |
| Cross-dialect tests are thorough: all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server) are asserted in every test. The re-quoting fix is validated by test expectations showing backtick/bracket quoting inside aggregate/window function expressions. | None | Good coverage. |
| Execution assertions verify actual query results against seeded data for RowNumber, DenseRank, SumOver, CountOver, and Joined RowNumber. | None | Good coverage of runtime correctness for the most common functions. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Window function code follows the same structural patterns as existing aggregate analysis: `GetWindowFunctionInfo` mirrors `GetAggregateInfo`, `GetJoinedWindowFunctionInfo` mirrors `GetJoinedAggregateInfo`, separated joined/non-joined helpers with matching signatures. | None | Excellent consistency with existing codebase patterns. |
| `IOverClause` follows the `IQueryBuilder` runtime-dummy pattern: interface + sealed class that throws `InvalidOperationException` on all methods. | None | Consistent with established project conventions. |
| `Sql.*` window function methods follow the exact same doc comment and exception pattern as existing `Sql.Count`, `Sql.Sum`, etc. | None | Consistent. |
| The `#region Window Function Analysis` region in `ProjectionAnalyzer.cs` adds 437 lines. While substantial, it is cleanly separated and self-contained. | None | Acceptable for the scope of the feature. |
| `ReQuoteSqlExpression` is placed in `SqlFormatting.cs` (shared project) and fixes a pre-existing bug (aggregate expressions had wrong quoting for MySQL/SQL Server). The fix is applied during `BuildProjection` enrichment in `ChainAnalyzer.cs`. | None | Good placement -- the fix benefits both aggregates and window functions. |
| The `CrossDialectAggregateTests` and `CrossDialectCompositionTests` expected values are updated to reflect the re-quoting fix. These are genuine bug fixes in existing test expectations. | None | Correctly updates existing tests to match the fix. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New public API surface: `IOverClause` interface (3 methods), `Sql.RowNumber`, `Sql.Rank`, `Sql.DenseRank`, `Sql.Ntile`, `Sql.Lag` (3 overloads), `Sql.Lead` (3 overloads), `Sql.FirstValue`, `Sql.LastValue`, plus aggregate OVER overloads for `Count` (2), `Sum` (4), `Avg` (4), `Min` (1), `Max` (1). All are additive -- no existing API signatures changed. | None | Purely additive. No breaking changes. |
| The `ReQuoteSqlExpression` fix changes the generated SQL for MySQL and SQL Server when aggregate functions contain column references (e.g., `SUM("Total")` becomes `SUM(\`Total\`)` for MySQL). This is a **behavioral change** for existing aggregate queries on these dialects. | Medium | Previously, MySQL/SQL Server queries with aggregate expressions had double-quoted identifiers inside the aggregate (e.g., `SUM("Total")`). MySQL supports `"` in ANSI mode but not by default; SQL Server also supports `"` but `[]` is standard. The fix makes generated SQL use the correct dialect-native quoting. This is a correctness improvement but could break users who had `sql_mode=ANSI_QUOTES` or similar workarounds. |
| `ProjectedColumn` is constructed with a positional constructor call (17+ parameters) in `ChainAnalyzer.cs` line 1819-1825. If `ProjectedColumn` gains new fields, this call would need updating. | Low | This is a fragility concern, not a breaking change. The same pattern exists elsewhere in the codebase. |

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | Phase 4 enrichment unit tests missing | Low | D | Implicit coverage sufficient |
| 2 | Plan Compliance | Phase 5 `WindowFunction_Joined_AggregateOver` test missing | Low | B | Added test |
| 3 | Plan Compliance | LAG execution test missing | Low | B | Added execution assertions |
| 4 | Plan Compliance | No Avg/Min/Max OVER tests | Low | D | Mirrors tested paths |
| 5 | Correctness | NTILE/LAG/LEAD non-column args use raw `.ToString()` | Medium | C | Tracked as separate issue |
| 6 | Correctness | Empty OVER clause allowed (`over => over`) | Low | D | Valid SQL |
| 7 | Correctness | Unquoted table alias in joined OVER clause | Low | D | Pre-existing pattern |
| 8 | Correctness | `ReQuoteSqlExpression` assumes no escaped double quotes | Low | D | Extremely rare |
| 9 | Test Quality | LAG/LEAD 3-arg overload untested | Medium | B | Added test |
| 10 | Test Quality | LEAD with offset untested | Low | D | Mirrors tested LAG path |
| 11 | Test Quality | No negative/failure-mode tests for malformed OVER lambdas | Low | C | Tracked as separate issue |
| 12 | Integration | Re-quoting fix changes MySQL/SQL Server aggregate SQL output | Medium | D | Intentional correctness fix |
| 13 | Integration | `ProjectedColumn` positional constructor fragility | Low | D | Pre-existing pattern |

## Issues Created
- #222: Parameterize non-column arguments in window function SQL generation
- #223: Add failure-mode tests for malformed OVER clause lambdas
- #224: Refactor SqlExpression to dialect-agnostic representation
