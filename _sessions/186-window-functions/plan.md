# Plan: Window Function Support (#186)

## Key Concepts

**Window functions** produce a value for each row based on a "window" of related rows, defined by an OVER clause. Unlike aggregates with GROUP BY (which collapse rows), window functions preserve row identity.

**OVER clause** specifies: PARTITION BY (group rows), ORDER BY (row ordering within partition), and optionally frame specs (ROWS/RANGE — deferred).

**Architecture**: Window function OVER lambdas are **not** inner query chains (unlike CTE lambdas). They're purely structural specifications within a SELECT column, analyzed syntactically by `ProjectionAnalyzer`. The `over` parameter references column expressions from the outer query's lambda parameter. No changes to `UsageSiteDiscovery`, `ChainAnalyzer` carriers, or emission pipeline are needed — the window SQL is pre-built at analysis time and stored in `ProjectedColumn.SqlExpression`, rendered by `AppendSelectColumns` via the existing `IsAggregateFunction` path.

**Enrichment compatibility**: For aggregate OVER forms (e.g., `SUM("Total") OVER (...)`), the existing `ExtractColumnNameFromAggregateSql` helper in `ChainAnalyzer` extracts the column name for type resolution. For pure ranking functions (ROW_NUMBER, RANK, etc.), the CLR type is always `int` — no enrichment needed.

## Phase 1: Runtime API — `IOverClause` interface and `Sql.*` window methods

Add the `IOverClause` interface and `OverClause` dummy implementation to the runtime library, then add all window function methods to `Sql`.

### 1a: `IOverClause` interface

Create `src/Quarry/Query/IOverClause.cs`:

```csharp
public interface IOverClause
{
    IOverClause PartitionBy<T>(params T[] columns);
    IOverClause OrderBy<T>(T column);
    IOverClause OrderByDescending<T>(T column);
}
```

The `PartitionBy` uses a `params T[]` for multi-column support: `over.PartitionBy(o.Category, o.Region)`. The generic `<T>` allows any column type without boxing.

Create `src/Quarry/Query/OverClause.cs` — runtime dummy that throws `InvalidOperationException` on every method (same pattern as all `Sql.*` methods). This class is never instantiated at runtime; the generator intercepts calls at compile time.

### 1b: `Sql.*` window function methods

Add to `src/Quarry/Query/Sql.cs`:

**Ranking functions** (no column argument — return `int`):
- `Sql.RowNumber(Func<IOverClause, IOverClause> over)` → `int`
- `Sql.Rank(Func<IOverClause, IOverClause> over)` → `int`
- `Sql.DenseRank(Func<IOverClause, IOverClause> over)` → `int`
- `Sql.Ntile(int buckets, Func<IOverClause, IOverClause> over)` → `int`

**Value functions** (column argument — generic return):
- `Sql.Lag<T>(T column, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Lag<T>(T column, int offset, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Lag<T>(T column, int offset, T defaultValue, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Lead<T>(T column, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Lead<T>(T column, int offset, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Lead<T>(T column, int offset, T defaultValue, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.FirstValue<T>(T column, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.LastValue<T>(T column, Func<IOverClause, IOverClause> over)` → `T`

**Aggregate OVER overloads** (new overloads on existing methods):
- `Sql.Count(Func<IOverClause, IOverClause> over)` → `int`
- `Sql.Count<T>(T column, Func<IOverClause, IOverClause> over)` → `int`
- `Sql.Sum(int column, Func<IOverClause, IOverClause> over)` → `int` (+ long/decimal/double overloads)
- `Sql.Avg(int column, Func<IOverClause, IOverClause> over)` → `double` (+ long/decimal/double overloads)
- `Sql.Min<T>(T column, Func<IOverClause, IOverClause> over)` → `T`
- `Sql.Max<T>(T column, Func<IOverClause, IOverClause> over)` → `T`

All methods throw `InvalidOperationException` at runtime with a descriptive message.

### Tests for Phase 1
No generator tests — this is runtime API only. Compile verification is implicit.

---

## Phase 2: `ProjectionAnalyzer` — Window function detection and OVER clause parsing

Extend `ProjectionAnalyzer` to recognize window function `Sql.*` calls and parse their OVER lambda argument.

### 2a: Extend `IsAggregateCall` → `IsSqlCall`

The existing `IsAggregateCall` helper (line 531) checks if an invocation is `Sql.*`. Rename conceptually — window functions are also `Sql.*` calls. The check already matches any `Sql.XXX(...)` pattern, so no actual code change to `IsAggregateCall` is needed; it already catches window functions. The handling diverges inside `GetAggregateInfo` / the new `GetWindowFunctionInfo`.

### 2b: Add `GetWindowFunctionInfo` method

New private method in `ProjectionAnalyzer`:

```csharp
private static (string? SqlExpression, string? ClrType) GetWindowFunctionInfo(
    string methodName,
    InvocationExpressionSyntax invocation,
    SemanticModel semanticModel,
    Dictionary<string, ColumnInfo> columnLookup,
    string lambdaParameterName,
    SqlDialect dialect)
```

This method:
1. Identifies the window function by `methodName` (RowNumber, Rank, DenseRank, Ntile, Lag, Lead, FirstValue, LastValue).
2. Finds the `Func<IOverClause, IOverClause>` lambda argument (always the last argument).
3. Calls `ParseOverClause(lambda, columnLookup, lambdaParameterName, dialect)` to extract PARTITION BY and ORDER BY columns.
4. Builds the SQL: e.g., `ROW_NUMBER() OVER (ORDER BY "Date")` or `LAG("Price", 1) OVER (PARTITION BY "Category" ORDER BY "Date")`.
5. Returns `(sqlExpression, clrType)` where clrType is `"int"` for ranking functions, or resolved from the column argument for value/aggregate functions.

### 2c: Add `ParseOverClause` method

New private method:

```csharp
private static string? ParseOverClause(
    LambdaExpressionSyntax lambda,
    Dictionary<string, ColumnInfo> columnLookup,
    string lambdaParameterName,
    SqlDialect dialect)
```

The lambda body is a fluent chain like `over.PartitionBy(o.Category).OrderBy(o.Price)`. This method:
1. Extracts the lambda parameter name (e.g., `over`).
2. Walks the fluent chain from outermost invocation inward (the chain is nested: `OrderBy(...)` wraps `PartitionBy(...)` wraps `over`).
3. For each method call, extracts the column arguments:
   - `PartitionBy`: Extract all `params` arguments → PARTITION BY columns
   - `OrderBy`: Extract column → ORDER BY ASC column
   - `OrderByDescending`: Extract column → ORDER BY DESC column
4. Resolves each column reference via existing `GetColumnSql(expr, columnLookup, lambdaParameterName, dialect)`.
5. Builds: `OVER (PARTITION BY col1, col2 ORDER BY col3 ASC, col4 DESC)`.

The chain walk algorithm processes the invocation tree recursively:
```
OrderBy(PartitionBy(over, o.Category), o.Price)
  └── method=OrderBy, arg=o.Price
       └── receiver: PartitionBy(over, o.Category)
            └── method=PartitionBy, arg=o.Category
                 └── receiver: over (lambda param — stop)
```
Collect methods in order (PartitionBy, OrderBy), then assemble the OVER clause.

### 2d: Add `GetJoinedWindowFunctionInfo` method

Parallel method for joined queries using `perParamLookup` and `GetJoinedColumnSql`:

```csharp
private static (string? SqlExpression, string? ClrType) GetJoinedWindowFunctionInfo(
    string methodName,
    InvocationExpressionSyntax invocation,
    Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
    SqlDialect dialect)
```

And corresponding `ParseJoinedOverClause` that uses `GetJoinedColumnSql` instead of `GetColumnSql`.

### 2e: Wire into existing dispatch

In `GetAggregateInfo` (line 1741): After the existing `switch (methodName)` cases, add a fallthrough to `GetWindowFunctionInfo` for unrecognized method names. If `GetWindowFunctionInfo` returns a result, use it. Otherwise return `(null, null)`.

Similarly in `GetJoinedAggregateInfo` (line 1887): Add fallthrough to `GetJoinedWindowFunctionInfo`.

This keeps the existing aggregate path untouched and adds window functions as an extension.

### 2f: Handle aggregate OVER overloads

For `Sql.Sum(o.Total, over => ...)`, `Sql.Count(over => ...)`, etc., these are the same method names as existing aggregates but with an additional lambda argument. In `GetAggregateInfo`, detect the OVER lambda argument:
- If the last argument is a lambda expression, treat it as a window function (aggregate OVER).
- Extract the aggregate column from the non-lambda arguments.
- Parse the OVER clause from the lambda.
- Build SQL: `SUM("Total") OVER (PARTITION BY ...)`.

### Tests for Phase 2
No tests yet — covered in Phase 3.

---

## Phase 3: Tests — Window function SQL output tests

Create `src/Quarry.Tests/SqlOutput/CrossDialectWindowFunctionTests.cs` following the pattern from `CrossDialectAggregateTests.cs`.

### Test cases:

**Ranking functions:**
1. `WindowFunction_RowNumber_OrderBy` — `Sql.RowNumber(over => over.OrderBy(o.Date))` → `ROW_NUMBER() OVER (ORDER BY "Date")`
2. `WindowFunction_Rank_PartitionBy_OrderBy` — `Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total))` → `RANK() OVER (PARTITION BY "Status" ORDER BY "Total")`
3. `WindowFunction_DenseRank_OrderByDescending` — `Sql.DenseRank(over => over.OrderByDescending(o.Total))` → `DENSE_RANK() OVER (ORDER BY "Total" DESC)`
4. `WindowFunction_Ntile` — `Sql.Ntile(4, over => over.OrderBy(o.Date))` → `NTILE(4) OVER (ORDER BY "Date")`

**Value functions:**
5. `WindowFunction_Lag_Simple` — `Sql.Lag(o.Total, over => over.OrderBy(o.Date))` → `LAG("Total") OVER (ORDER BY "Date")`
6. `WindowFunction_Lag_WithOffset` — `Sql.Lag(o.Total, 2, over => over.OrderBy(o.Date))` → `LAG("Total", 2) OVER (ORDER BY "Date")`
7. `WindowFunction_Lead_Simple` — `Sql.Lead(o.Total, over => over.OrderBy(o.Date))` → `LEAD("Total") OVER (ORDER BY "Date")`
8. `WindowFunction_FirstValue` — `Sql.FirstValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.Date))` → `FIRST_VALUE("Total") OVER (PARTITION BY "Status" ORDER BY "Date")`
9. `WindowFunction_LastValue` — `Sql.LastValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.Date))` → `LAST_VALUE("Total") OVER (PARTITION BY "Status" ORDER BY "Date")`

**Aggregate OVER:**
10. `WindowFunction_SumOver` — `Sql.Sum(o.Total, over => over.PartitionBy(o.Status))` → `SUM("Total") OVER (PARTITION BY "Status")`
11. `WindowFunction_CountOver` — `Sql.Count(over => over.PartitionBy(o.Status))` → `COUNT(*) OVER (PARTITION BY "Status")`

**Multi-column/mixed:**
12. `WindowFunction_PartitionByMultipleColumns` — `over.PartitionBy(o.Status, o.UserId)` → `PARTITION BY "Status", "UserId"`
13. `WindowFunction_MixedWithRegularColumns` — `Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.Date))))` — verify regular columns and window columns coexist in the same tuple projection
14. `WindowFunction_MultipleWindowsInSelect` — Two window functions in the same tuple projection

**Execution tests (SQLite):**
15. `WindowFunction_RowNumber_ExecuteFetchAll` — Execute and verify row numbers match expected ordering
16. `WindowFunction_Lag_ExecuteFetchAll` — Execute and verify LAG values

All SQL output tests use the cross-dialect `AssertDialects` pattern with `lt`/`pg`/`my`/`ss` prepared queries.

---

## Phase 4: Enrichment compatibility

Ensure the `ChainAnalyzer.BuildProjection` enrichment path handles window function columns correctly.

### 4a: `NeedsEnrichment` check

Window function columns will have `IsAggregateFunction = true` (they share the same `ProjectedColumn` flag). The existing `NeedsEnrichment` already skips type enrichment for aggregates unless the type is unresolved. For ranking functions (always `int`), no enrichment is needed. For value functions like `Lag<T>` and `FirstValue<T>`, the generic `T` type might be unresolved when the entity is generated — the existing `TryResolveAggregateTypeFromSql` should work because the SQL still contains a quoted column name (e.g., `LAG("Total") OVER (...)`). `ExtractColumnNameFromAggregateSql` finds the innermost quoted identifier, which is the column in the function args, not the OVER clause.

### 4b: Verify `ExtractColumnNameFromAggregateSql`

Test that for SQL like:
- `LAG("Total") OVER (ORDER BY "Date")` → extracts `"Total"` (from function args before OVER)
- `SUM("Total") OVER (PARTITION BY "Status")` → extracts `"Total"`
- `ROW_NUMBER() OVER (ORDER BY "Date")` → returns `null` (no column in function)

The existing regex-free scan finds the **last** quoted identifier in the string. For `LAG("Total") OVER (ORDER BY "Date")`, the last quoted identifier is `"Date"`, not `"Total"`. This is a bug for window functions.

**Fix**: Modify `ExtractColumnNameFromAggregateSql` to extract from the function arguments portion only (before ` OVER (`), not from the entire SQL expression. When the SQL contains ` OVER (`, split at that point and scan only the prefix.

### Tests for Phase 4
Add unit test in existing enrichment test coverage verifying that window function SQL expressions resolve the correct column type.

---

## Phase 5: Joined query window functions

Ensure window functions work in joined projection contexts (multi-parameter Select lambdas).

### 5a: Wire into joined projection dispatch

In `ResolveJoinedProjectedExpressionWithPlaceholder` (line 298), the code currently handles `MemberAccessExpressionSyntax` and `InvocationExpressionSyntax` (if `IsAggregateCall`). Window functions ARE aggregate calls per `IsAggregateCall` (they match `Sql.*` pattern), so they'll already flow into `ResolveJoinedAggregate`. The joined aggregate handler calls `GetJoinedAggregateInfo`, which needs the window function fallthrough added in Phase 2d.

### 5b: Tests

Add 2-3 joined window function tests to `CrossDialectWindowFunctionTests.cs`:
- `WindowFunction_Joined_RowNumber` — joined Select with window function on one entity
- `WindowFunction_Joined_AggregateOver` — joined Select with SUM OVER on joined entity

---

## Dependencies

```
Phase 1 (runtime API) → Phase 2 (analyzer) → Phase 3 (tests)
                                            → Phase 4 (enrichment fix)
                                            → Phase 5 (joined tests)
```

Phase 3, 4, and 5 can be interleaved since they all depend on Phase 2 but not on each other.
