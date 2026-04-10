# Plan: 238-benchmark-fixes

## Phase 1: Fix source generator IsDBNull for nullable value types

The source generator hardcodes `isNullable: false` when creating `ProjectedColumn` for aggregate/window function results. This means the reader code generator never emits `IsDBNull` checks, crashing when functions like `LAG()` return NULL and the target property is `decimal?`.

**Fix:** At each of the 3 creation sites in `ProjectionAnalyzer.cs`, use Roslyn's `SemanticModel.GetTypeInfo(expression).ConvertedType` to determine if the target type is nullable. The `ConvertedType` reflects the implicit conversion context — when `Sql.Lag(o.Total, ...)` (returning `decimal`) is assigned to a `decimal?` property, `ConvertedType` is `Nullable<decimal>`.

**Files to modify:**
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — 3 locations:
  - Line ~715: `AnalyzeProjectedExpression()` aggregate branch
  - Line ~614: `AnalyzeSingleAggregateInJoinedProjection()`  (note: `expression` is `invocation` here — use the invocation node)
  - Line ~752: `ResolveJoinedAggregate()`

At each site, add a helper call that checks `ConvertedType` for nullable annotation or `Nullable<T>` wrapping. Extract a small static helper method `IsConvertedTypeNullable(SemanticModel?, ExpressionSyntax)` to avoid duplication.

**Tests:**
- Add a new test in `CrossDialectWindowFunctionTests.cs` that uses a DTO with `decimal? PrevTotal` to verify the generated reader emits `IsDBNull`. This should be an execution test that actually reads NULL values.
- Add a unit test in `EntityReaderTests.cs` for `GenerateReaderDelegate` with a `ProjectedColumn` that has `isAggregateFunction: true` and `isNullable: true` and `isValueType: true` (e.g., nullable decimal aggregate).
- Update the comment at `CrossDialectWindowFunctionTests.cs:645-648` (`WindowFunction_Lag_NullableColumn`) to note this is fixed for DTOs with nullable properties, though tuple projections with non-nullable elements still skip execution.

## Phase 2: Fix Dapper_Lag benchmark with separate DTO

SQLite stores `Total` as `REAL` (Double). `LAG(Total, 1)` returns Double. Dapper can't auto-cast `Double` to `Nullable<Decimal>`.

**Files to modify:**
- `src/Quarry.Benchmarks/Infrastructure/Dtos.cs` — Add `DapperOrderLagDto` with `double? PrevTotal` instead of `decimal?`.
- `src/Quarry.Benchmarks/Benchmarks/WindowFunctionBenchmarks.cs` — Update `Dapper_Lag()` to use `DapperOrderLagDto`.

## Phase 3: Split benchmark classes into separate files

Split 12 multi-group benchmark classes into 32 single-group classes. Each new class inherits from `BenchmarkBase`, has its own `Raw_*` method with `Baseline = true`, and contains only the methods for its test type.

**Naming convention:** `{Category}{TestType}Benchmarks` (e.g., `AggregateCountBenchmarks`, `WindowFunctionLagBenchmarks`).

**Classes to split and their new files:**

| Original | New Classes |
|----------|-------------|
| AggregateBenchmarks | AggregateCountBenchmarks, AggregateSumBenchmarks, AggregateAvgBenchmarks |
| ComplexQueryBenchmarks | ComplexJoinFilterPaginateBenchmarks, ComplexMultiJoinAggregateBenchmarks |
| CteBenchmarks | CteSimpleBenchmarks, CteProjectionBenchmarks, CteMultiBenchmarks |
| FilterBenchmarks | FilterWhereActiveBenchmarks, FilterWhereCompoundBenchmarks, FilterWhereByIdBenchmarks |
| InsertBenchmarks | InsertSingleBenchmarks, InsertBatchBenchmarks |
| JoinBenchmarks | JoinInnerBenchmarks, JoinThreeTableBenchmarks |
| PaginationBenchmarks | PaginationLimitOffsetBenchmarks, PaginationFirstPageBenchmarks |
| SelectBenchmarks | SelectAllBenchmarks, SelectProjectionBenchmarks |
| SetOperationBenchmarks | SetUnionAllBenchmarks, SetIntersectBenchmarks, SetExceptBenchmarks |
| StringOpBenchmarks | StringContainsBenchmarks, StringStartsWithBenchmarks |
| SubqueryBenchmarks | SubqueryExistsBenchmarks, SubqueryFilteredExistsBenchmarks, SubqueryCountBenchmarks, SubquerySumBenchmarks |
| WindowFunctionBenchmarks | WindowRowNumberBenchmarks, WindowRunningSumBenchmarks, WindowRankBenchmarks, WindowLagBenchmarks |

**Special handling:**
- `FilterBenchmarks` has a `GlobalSetup` override that initializes `_whereByIdTarget = 42`. The `FilterWhereByIdBenchmarks` class needs this override; others inherit base `GlobalSetup`.
- `InsertBenchmarks` has `IterationSetup`/`IterationCleanup` (creates/disposes EfContext, cleans inserted rows). Both `InsertSingleBenchmarks` and `InsertBatchBenchmarks` need these.
- SQL string constants move to their respective new classes.
- Old files are deleted after splitting.

**No runner changes needed** — `BenchmarkSwitcher.FromAssembly()` auto-discovers classes.

## Phase 4: Document BatchInsert10 investigation

No code changes. The investigation found both implementations use identical multi-row `INSERT INTO ... VALUES` syntax. The 0.94x anomaly is run-to-run noise (Raw StdDev 3.6x higher than Quarry's). This will be documented in the PR description and as a comment on the issue.
