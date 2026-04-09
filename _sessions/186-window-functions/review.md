# Review: #225

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 4 enrichment unit tests missing. Plan specifies "Add unit test in existing enrichment test coverage verifying that window function SQL expressions resolve the correct column type." No such unit test was added; the `ExtractColumnNameFromAggregateSql` fix is only implicitly validated via end-to-end cross-dialect SQL output tests. | Low | The fix itself is present and correct (truncating search region at ` OVER (`), but a targeted unit test would catch regressions if the extraction logic changes independently. Implicit coverage via SumOver/CountOver execution tests partially compensates. |
| Phase 5: only 1 of 2 planned joined tests implemented. Plan specifies `WindowFunction_Joined_RowNumber` and `WindowFunction_Joined_AggregateOver`. Only `WindowFunction_Joined_RowNumber` was delivered. | Low | The `WindowFunction_Joined_SumOver` test exists and covers a joined aggregate OVER, but the plan name was `WindowFunction_Joined_AggregateOver`. Functionally equivalent. However, the joined path for `GetJoinedWindowFunctionInfo` aggregate OVER branches (Avg, Min, Max, Count) is untested. Risk is low because the code mirrors the tested non-joined path. |
| Phase 3 execution tests: plan specifies 2 dedicated execution tests. Instead, execution assertions are embedded in 5 other tests (RowNumber, DenseRank, SumOver, CountOver, Joined_RowNumber). LAG execution is not tested end-to-end against a database. | Low | Execution coverage is broader than planned for most functions, but LAG/LEAD execution is absent. The LAG_Execution test explicitly skips execution due to NULL-to-non-nullable-decimal mapping concerns, only verifying SQL shape. |
| No tests for AvgOver, MinOver, MaxOver aggregate OVER variants. | Low | These paths share `BuildAggregateOverSql` / `BuildJoinedAggregateOverSql` which are structurally identical to the tested Sum/Count paths. Minimal risk. |
| Plan specifies `PartitionBy<T>(params T[] columns)` initially; decisions log documents switch to `params object[]`. Implementation matches the final decision. | None | Decision was documented and implementation is consistent. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| LAG/LEAD/NTILE non-column arguments emit raw C# source text into SQL via `.ToString()`. For LAG 3-arg overload, `Sql.Lag(o.Total, 1, 0m, over => ...)` produces `LAG("Total", 1, 0m) OVER (...)`. The `0m` is a C# decimal literal, not valid SQL. All four manifests contain this invalid SQL. The test passes because it only checks SQL string shape without executing. Similarly, passing a variable like `Sql.Ntile(n, ...)` would emit `NTILE(n)` which is semantically invalid SQL. | High | This produces SQL that will fail at execution time on all database engines. The `0m` suffix is C#-specific; SQL expects `0` or `0.0`. While the offset arguments (`1`, `2`) happen to be valid in both C# and SQL, any non-integer-literal argument (variables, expressions, decimal literals) will produce broken SQL. This affects `BuildLagLeadSql` (nonLambdaArgCount == 2 and == 3 branches) and `BuildNtileSql`. |
| `BuildOverClauseString` produces an empty string when neither PartitionBy nor OrderBy is specified (e.g., `Sql.RowNumber(over => over)`). This generates `ROW_NUMBER() OVER ()` which is valid SQL syntax but provides no deterministic ordering, making results unpredictable. | Low | Most databases accept `OVER ()`. This is more a usability concern than a correctness bug. No guard or diagnostic is emitted. |
| Joined query OVER clause produces unquoted table aliases: `t0."UserName"` instead of `"t0"."UserName"`. Visible in all four manifest outputs and explicitly acknowledged in test comments. | Low | SQL engines accept unquoted aliases (they are plain identifiers without special characters), so queries execute correctly. This is consistent with how `GetJoinedColumnSql` works for regular joined aggregates -- a pre-existing pattern. |
| `ReQuoteSqlExpression` assumes identifiers never contain escaped double quotes (`""` inside an identifier). If an identifier contains embedded double quotes, the re-quoting scan misidentifies the boundary. | Low | Column names with embedded double quotes are extremely rare in practice. The same assumption exists in `ExtractColumnNameFromAggregateSql`. Not a realistic risk. |
| `ReQuoteSqlExpression` handles an unmatched opening `"` (no closing quote) by falling through and copying the `"` character literally. This is defensive but means a malformed SQL expression would silently produce incorrect output rather than failing. | Low | Malformed SQL expressions at this stage would indicate a bug in the generator, not user input. Silent pass-through is acceptable. |

## Security

No concerns. All SQL generation occurs at compile-time via source generator analysis of syntax trees. No user input flows into SQL string construction at runtime. The `Sql.*` methods and `OverClause` throw at runtime, preventing misuse outside the generator pipeline.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The LAG 3-argument overload test (`WindowFunction_Lag_WithOffsetAndDefault`) verifies the SQL string contains `0m` -- the C# literal -- as if it were correct SQL. The test passes but validates the wrong behavior. This is effectively a test that enshrines a bug. | High | The test provides false confidence. When the `ToString()` issue is fixed (to emit proper SQL numeric literals), this test expectation will need to change. More critically, no execution assertion catches the fact that this SQL would fail at runtime. |
| No execution test for LAG. The `WindowFunction_Lag_Execution` test explicitly skips execution, only checking SQL shape. The comment explains that LAG returns NULL for the first row, which cannot map to non-nullable `decimal`. | Medium | This reveals an API design gap: LAG/LEAD naturally return NULL when there is no previous/next row, but the generic `T` return type does not communicate this. Users selecting LAG into a non-nullable tuple field will get runtime errors on NULL rows. A test demonstrating the nullable pattern (`decimal?`) would document the correct usage. |
| No test for LEAD with offset or LEAD with offset+default. Only the simple LEAD overload (`Sql.Lead(o.Total, over => ...)`) is tested. | Low | Structurally identical to the tested LAG with offset path -- `BuildLagLeadSql` handles both LAG and LEAD identically via the `functionName` parameter. |
| No negative/failure-mode tests. There are no tests for what happens when: (a) the OVER lambda has a block body instead of expression body, (b) an unrecognized method is called on the over clause, (c) PartitionBy is called with zero arguments, (d) the OVER lambda references a variable that is not a column. The `WalkOverChain` returns `false` and `ParseOverClause` returns `null`, causing `GetWindowFunctionInfo` to return `(null, null)`. The downstream behavior (silent skip, diagnostic, or compile error) is undocumented. | Low | The generator likely silently omits the column or emits a diagnostic. Testing one failure case would document the expected behavior and prevent regressions. |
| Cross-dialect coverage is thorough: all 18 tests assert all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server). The re-quoting fix is validated by tests showing backtick/bracket quoting inside aggregate and window function expressions. | None | Good coverage. |
| Execution assertions verify actual query results against seeded data for RowNumber, DenseRank, SumOver, CountOver, and Joined RowNumber (5 of 18 tests). | None | Good execution coverage for the most common window functions. |
| No test combines window functions with WHERE filtering, GROUP BY, or ORDER BY clauses on the outer query. All tests use `.Where(o => true)` as a passthrough. | Low | Window functions in SELECT should coexist with other query clauses without interference. The `.Where(o => true)` pattern avoids testing this. A test with a real WHERE predicate (e.g., `.Where(o => o.Status == "Shipped")`) would verify no interaction bugs. |
| No test for PartitionBy with a single column that differs in type from other PartitionBy arguments (the motivating case for `params object[]` instead of `params T[]`). The `PartitionByMultipleColumns` test uses `o.Status, o.UserId` which may coincidentally be the same type or may not -- test does not surface this. | Low | The `params object[]` decision was made to handle mixed types. A test explicitly using columns of different types (e.g., `o.Status` (string) and `o.OrderId` (int)) in the same PartitionBy call would validate this works. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Window function code follows existing aggregate analysis patterns: `GetWindowFunctionInfo` mirrors `GetAggregateInfo`, `GetJoinedWindowFunctionInfo` mirrors `GetJoinedAggregateInfo`, with matching method signatures and dispatch structure. | None | Excellent structural consistency. |
| `IOverClause` follows the `IQueryBuilder` runtime-dummy pattern: interface with a sealed class that throws `InvalidOperationException` on all methods. | None | Consistent with established conventions. |
| `Sql.*` window function methods follow the exact same XML doc comment and exception pattern as existing `Sql.Count`, `Sql.Sum`, etc. | None | Consistent. |
| The `#region Window Function Analysis` region in `ProjectionAnalyzer.cs` adds 437 lines. While substantial, it is cleanly separated and self-contained within a single region. | None | Acceptable scope for the feature. |
| `ReQuoteSqlExpression` is placed in `SqlFormatting.cs` (shared project) and applied during `BuildProjection` enrichment in `ChainAnalyzer.cs`. This fixes a pre-existing quoting bug that affected existing aggregate expressions for MySQL and SQL Server. | None | Good placement -- benefits both aggregates and window functions. |
| Updated `CrossDialectAggregateTests` and `CrossDialectCompositionTests` expected values reflect the re-quoting fix. Changes are purely in expected SQL strings (e.g., `SUM("Total")` -> `SUM(\`Total\`)` for MySQL). | None | Correctly updates existing tests to match the behavioral fix. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New public API surface is purely additive: `IOverClause` (3 methods), 12 new `Sql.*` window function methods, and 12 aggregate OVER overloads. No existing API signatures are changed or removed. | None | No breaking changes to the public API. |
| The `ReQuoteSqlExpression` fix changes generated SQL for MySQL and SQL Server when aggregate functions contain column references. Previously: `SUM("Total")` (double-quoted inside backtick-quoted SQL). Now: `SUM(\`Total\`)` for MySQL, `SUM([Total])` for SQL Server. | Medium | This is a correctness improvement -- the previous behavior emitted PostgreSQL-dialect quoting inside MySQL/SQL Server queries, which only worked because these engines have partial tolerance for double-quoted identifiers (MySQL with `ANSI_QUOTES` mode, SQL Server with `QUOTED_IDENTIFIER`). The fix produces correct native quoting. However, users with existing deployed code that relied on the previous (technically incorrect) SQL output will see different queries after upgrading. This should be documented in release notes. |
| `ProjectedColumn` is reconstructed with a positional constructor call (18 parameters) in `ChainAnalyzer.cs` for the re-quoting path. If `ProjectedColumn` gains new fields, this call must be updated. | Low | This is a fragility concern, not a breaking change. The same pattern of positional construction exists elsewhere in the codebase. Consider a `with` expression or copy-constructor in a future refactoring. |
| The `HasOverClauseLambda` check is applied before the existing aggregate `switch` in both `GetAggregateInfo` and `GetJoinedAggregateInfo`. This means any `Sql.*` call whose last argument happens to be a lambda will be routed to window function processing. If a future `Sql.*` method uses a lambda for non-OVER purposes, it would be incorrectly intercepted. | Low | Currently all lambdas on `Sql.*` methods are OVER clauses, so there is no conflict. The check is narrow enough (last-argument-is-lambda) that accidental interception is unlikely. Adding new lambda-bearing `Sql.*` methods would require adjusting the dispatch. |

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | Phase 4 enrichment unit tests missing | Low | D | Implicit coverage sufficient |
| 2 | Plan Compliance | Phase 5 joined test naming mismatch | Low | D | Functionally equivalent test exists |
| 3 | Plan Compliance | LAG execution not tested end-to-end | Low | B | Add nullable LAG execution test |
| 4 | Plan Compliance | No Avg/Min/Max OVER tests | Low | B | Add tests |
| 5 | Correctness | LAG/LEAD/NTILE args emit raw C# source text | High | C | Already tracked as #222 |
| 6 | Correctness | Empty OVER clause allowed | Low | D | Valid SQL |
| 7 | Correctness | Unquoted table aliases in joined OVER clause | Low | B | Fix quoting for window function joined OVER |
| 8 | Correctness | ReQuoteSqlExpression assumes no escaped double quotes | Low | B | Add escaped-quote handling |
| 9 | Correctness | ReQuoteSqlExpression silent pass-through for unmatched quotes | Low | D | Acceptable defensive behavior |
| 10 | Test Quality | LAG 3-arg test enshrines `0m` bug | High | C | Tied to #222 |
| 11 | Test Quality | No LAG execution test — nullable gap | Medium | B | Add nullable decimal? LAG execution test |
| 12 | Test Quality | No LEAD with offset/default tests | Low | B | Add tests |
| 13 | Test Quality | No negative/failure-mode tests | Low | B | Add malformed OVER lambda test |
| 14 | Test Quality | No test with real WHERE/GROUP BY | Low | B | Add test with real WHERE predicate |
| 15 | Test Quality | No mixed-type PartitionBy test | Low | B | Add test |
| 16 | Integration | ReQuoteSqlExpression behavioral change | Medium | D | Intentional correctness fix |
| 17 | Integration | ProjectedColumn positional constructor fragility | Low | C | Track as tech debt with all affected sites |
| 18 | Integration | HasOverClauseLambda dispatch fragility | Low | D | No current conflict |

## Issues Created
- #222: Parameterize non-column arguments in window function SQL generation (pre-existing)
- #223: Add failure-mode tests for malformed OVER clause lambdas (pre-existing)
- #224: Refactor SqlExpression to dialect-agnostic representation (pre-existing)
- #226: Refactor ProjectedColumn to reduce positional constructor fragility
