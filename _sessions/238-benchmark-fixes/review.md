# Review: 238-benchmark-fixes

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 4 phases implemented as specified | Info | Phase 1 (IsDBNull fix at 3 sites), Phase 2 (DapperOrderLagDto), Phase 3 (split 12 classes into 32), Phase 4 (document-only, no code changes) all match the plan. |
| All 3 fix sites in ProjectionAnalyzer.cs updated | Info | Line 614 (`AnalyzeSingleAggregateInJoinedProjection`), line 752 (`ResolveJoinedAggregate`), and line ~1715 (`AnalyzeProjectedExpression`) all changed from `isNullable: false` to `IsConvertedTypeNullable(semanticModel, ...)`. |
| Helper method `IsConvertedTypeNullable` extracted as planned | Info | Static helper checks both `NullableAnnotation.Annotated` and `Nullable<T>` wrapping, as specified. |
| Tests added as planned | Info | `WindowFunction_Lag_NullableDto_Execution` in CrossDialectWindowFunctionTests (execution test), `GenerateReaderDelegate_WithNullableDecimalAggregate_EmitsIsDBNullCheck` in EntityReaderTests (unit test), and the comment update at `WindowFunction_Lag_NullableColumn` all present. |
| Benchmark split naming follows plan convention | Info | All 32 new classes use `{Category}{TestType}Benchmarks` naming (e.g., `AggregateCountBenchmarks`, `WindowLagBenchmarks`). |
| Special handling for FilterWhereByIdBenchmarks and InsertBenchmarks implemented | Info | `FilterWhereByIdBenchmarks` has `GlobalSetup` override with `_whereByIdTarget = 42`. Both `InsertSingleBenchmarks` and `InsertBatchBenchmarks` have `IterationSetup`/`IterationCleanup`. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Orphaned XML doc comment above `IsConvertedTypeNullable` | Low | At line 1882-1885, the `<summary>` block for `ResolveAggregateClrType` is stranded above the new method's own `<summary>` block. This means `IsConvertedTypeNullable` has two adjacent `<summary>` elements (the first is orphaned), and `ResolveAggregateClrType` (at line 1907) lost its doc comment entirely. Not a runtime issue, but a documentation correctness problem that will confuse IDEs and generated API docs. |
| `IsConvertedTypeNullable` null-safety is correct | Info | The helper correctly guards against `semanticModel == null` and `convertedType == null` before checking nullability. Returns `false` (safe default) when the semantic model is unavailable. |
| Third fix site uses `expression` instead of `invocation` | Info | At line 1715, `expression` is passed rather than `invocation`. This is correct because `expression is InvocationExpressionSyntax invocation` on line 1699 means they reference the same node. `ConvertedType` resolves identically for both. |
| `DapperOrderLagDto` uses `double` and `double?` for all numeric columns | Info | Correct for SQLite REAL affinity. The `Total` field is also `double` (not `decimal`), matching what SQLite actually returns. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | Changes are to source generator logic, test code, and benchmark infrastructure. No user input handling, no network code, no secret management affected. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Execution test covers the core fix path | Info | `WindowFunction_Lag_NullableDto_Execution` verifies end-to-end: source generator emits `IsDBNull`, reader handles NULL from `LAG()`, and `decimal?` property receives `null`. Checks all 3 rows including the NULL case. |
| Unit test verifies reader codegen in isolation | Info | `GenerateReaderDelegate_WithNullableDecimalAggregate_EmitsIsDBNullCheck` constructs a `ProjectedColumn` with `isNullable: true, isAggregateFunction: true, isValueType: true` and asserts the exact `IsDBNull` guard pattern. |
| No negative test for non-nullable target | Low | There is no test confirming that when the target type is `decimal` (not `decimal?`), `isNullable` remains `false` and no `IsDBNull` guard is emitted. The existing test suite likely covers this implicitly through the many aggregate tests that use non-nullable targets, but an explicit regression test would strengthen confidence. |
| No test for `semanticModel == null` path | Low | The helper returns `false` when the semantic model is null. This is the safe default but has no dedicated test. In practice, the semantic model is always available during normal compilation, so the risk is minimal. |
| Manifest output updated (+1 discovered, +1 consolidated) | Info | The addition of `OrderLagDto` as a new test DTO correctly causes the manifest to discover one more query shape. The count change from 610/182 to 611/183 is consistent. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All split benchmark classes follow the established pattern | Info | Each class inherits from `BenchmarkBase`, has a `[Benchmark(Baseline = true)]` `Raw_*` method, uses the same namespace and `using` directives, and matches the structure of the 6 pre-existing single-group classes (`ColdStartBenchmarks`, `DeleteBenchmarks`, etc.). |
| `DapperOrderLagDto` in Benchmarks has good doc comment explaining why | Info | The `<summary>` clearly explains the SQLite type affinity reason. This prevents future developers from "fixing" it back to `decimal?`. |
| `OrderLagDto` exists in both `Quarry.Benchmarks.Infrastructure` and `Quarry.Tests.Samples` | Low | Two classes with the same name in different namespaces. They serve different purposes (benchmarks vs. tests) and have different property types (`decimal Total` in both, so actually identical structure). Not a compile error since they're in separate assemblies, but could cause confusion. Consider whether the test DTO could reference or share the benchmark one, though cross-project references may not be desirable. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No references to old benchmark class names remain | Info | Grep confirms no `.cs`, `.yml`, `.yaml`, `.json`, or `.csproj` files reference the deleted class names (`AggregateBenchmarks`, `FilterBenchmarks`, etc.). |
| `BenchmarkSwitcher.FromAssembly()` auto-discovers all classes | Info | The `Program.cs` runner uses assembly scanning, so no manual registration of new class names is needed. The 32 new classes will be discovered automatically. |
| CI workflow merge step handles more JSON files | Info | The `*-report-full.json` glob in `benchmark.yml` will match 32+ report files instead of 12+. The `jq -s` merge is file-count-agnostic. |
| Benchmark history discontinuity | Low | The `benchmark-action/github-action-benchmark` tool tracks metrics by fully-qualified method name (e.g., `Quarry.Benchmarks.Benchmarks.AggregateBenchmarks.Raw_Count`). Renaming classes changes these keys, so historical trend lines for the old class names will end and new ones will start. This is acceptable for a one-time restructuring but worth noting in the PR description for anyone reviewing benchmark history. |
| No public API changes | Info | The `IsConvertedTypeNullable` helper is `private static`. The `ProjectedColumn` constructor already accepted `isNullable` -- only the values passed at the 3 call sites changed. No consumer-facing API surface is affected. |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Orphaned XML doc comment | Correctness | A | Fixed — moved doc comment back to ResolveAggregateClrType |
| No negative test for non-nullable target | Test Quality | D | Covered implicitly by existing aggregate tests |
| No test for semanticModel == null path | Test Quality | D | Trivially correct, semantic model always available |
| Benchmark history discontinuity | Integration | D | Expected one-time cost, noted in PR description |
| OrderLagDto in two assemblies | Consistency | D | By design — separate assemblies, no coupling desired |

## Issues Created
- None
