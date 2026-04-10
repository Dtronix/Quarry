# Review: 222-parameterize-window-function-args

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phases 1-5 all implemented and committed (67dceae, e7c1445, 96e3d1a, 5272f4c, f68fa3d). The plan's five-phase progression is fully present. | Info | Confirms scope completeness. |
| Plan Phase 2 specified adding a separate `ProjectionParameters` property to `RawCallSite` and `TranslatedCallSite`. Implementation instead embedded parameters in the existing `ProjectionInfo` object, which is already carried on `RawCallSite` and forwarded through `TranslatedCallSite.ProjectionInfo`. | Info | Reasonable simplification that avoids redundant properties. The data flows correctly via the existing `ProjectionInfo` path. |
| Plan Phase 3 specified applying projection parameter merging "in the other `AnalyzeChainGroup` overloads that handle CTE inner chains and set operation chains." The `AnalyzeOperandChain` method (line ~2440) also builds projections via `BuildProjection` but does NOT include the `@__proj{N}` placeholder remapping or parameter merging logic. | Medium | Window functions with variable args inside CTE inner queries or set operation operands will retain unresolved `@__proj{N}` placeholders in their SQL, producing invalid SQL at runtime. This is an edge case but one the plan explicitly called out. |
| Plan Phase 5 specified tests for `WindowFunction_Lag_VariableOffset`, `WindowFunction_Lag_VariableDefault`, `WindowFunction_Ntile_Variable`, and `WindowFunction_Ntile_ConstVariable`. All four are present, plus `WindowFunction_Lead_VariableOffset` and `WindowFunction_Ntile_ConstVariable` (const test). CarrierGenerationTests also added two source-generator-level tests. | Info | Test coverage matches or exceeds plan specification. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `ProjectionInfo.GetHashCode()` does not include `ProjectionParameters` but `Equals()` does. | Low | This follows the pre-existing pattern in the codebase (other fields like `NonOptimalReason`, `FailureReason`, `CustomEntityReaderClass` are also in `Equals` but not `GetHashCode`), so it is consistent. However, it violates the hash code contract: two objects that are `Equal` must have the same hash code. If `ProjectionInfo` is ever used as a dictionary key or in a `HashSet` where two instances differ only by `ProjectionParameters`, lookups will fail silently. The risk is low because the existing code has the same issue with other fields. |
| In `ResolveScalarArgSql`, when the expression is a complex non-identifier expression (member access chain like `obj.Prop`, method call, etc.), the method returns `null` which triggers a runtime fallback `(null, null)` from `BuildNtileSql`/`BuildLagLeadSql`. This causes the entire projection to fail to the non-optimal path. | Low | Correct behavior per the plan ("If none of the above ... return null triggers runtime fallback"). The user gets a working query, just not the compile-time optimized path. Documented in the handoff as a known limitation. |
| In `ChainAnalyzer` placeholder remapping, the `localToGlobal` dictionary uses string keys like `@__proj0`, `@__proj1`. The `Replace` call iterates all entries. If `@__proj1` is a substring of `@__proj10` (hypothetical 11+ params), the naive `string.Replace` could match `@__proj1` inside `@__proj10`. | Low | In practice, SQL expressions with 10+ window function variable parameters in a single projection are extremely unlikely. However, a more robust approach would sort by descending key length or use regex word boundaries. |
| `FormatConstantForSql` fallback case uses `value.ToString()` for unhandled types (e.g., `byte`, `short`, `uint`, `ulong`). | Low | For numeric types not explicitly handled, `ToString()` without `CultureInfo.InvariantCulture` could produce locale-dependent output on some runtimes. In practice, integral types smaller than `int` are promoted to `int` by the C# compiler in constant evaluation, so this path is rarely hit. |
| The `BuildLagLeadSql` refactoring from a `switch` expression to `if/else` chains is functionally equivalent and correctly handles 1, 2, and 3 non-lambda argument counts. | Info | No logic change, just structural refactor to accommodate `ResolveScalarArgSql` calls that can return null. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `FormatConstantForSql` for string constants uses single-quote escaping (`s.Replace("'", "''")`). This is the standard SQL string literal escaping. | Info | Correct for SQL Server, PostgreSQL, SQLite, and MySQL in standard mode. No injection risk for compile-time constant strings. |
| Variable (non-constant) expressions are parameterized as `@p{N}` placeholders, never interpolated into SQL. | Info | This is the correct, safe approach -- the core purpose of this change is to move from unsafe `.ToString()` interpolation to parameterized queries. |

No concerns beyond the above info-level notes.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Cross-dialect tests (`WindowFunction_Ntile_Variable`, `WindowFunction_Lag_VariableOffset`, `WindowFunction_Lead_VariableOffset`, `WindowFunction_Lag_VariableDefault`) assert correct dialect-specific parameter syntax (`@p0` for SQLite/SQL Server, `$1` for PostgreSQL, `?` for MySQL). This validates the full pipeline from `ResolveScalarArgSql` through `ChainAnalyzer` remapping to `SqlFormatting.QuoteSqlExpression`. | Info | Excellent coverage of the dialect-aware rendering path. |
| `WindowFunction_Ntile_ConstVariable` tests the const-inlining path with `const int buckets = 2` and asserts `NTILE(2)` appears in SQL (not parameterized). Also executes the query and asserts results. | Info | Good boundary test between const and variable paths. |
| `CarrierGeneration_WindowFunction_NtileVariableParameter` and `CarrierGeneration_WindowFunction_NtileConstantInlined` are source-generator-level tests that verify the generated interceptor code contains expected patterns. | Info | Validates code generation without requiring a full database. |
| No test for multiple variable parameters in a single window function call (e.g., `Sql.Lag(col, variableOffset, variableDefault, over => ...)` where both offset and default are variables). | Low | This would exercise the case where multiple `@__proj{N}` placeholders exist in a single SQL expression, testing the remapping logic more thoroughly. The `WindowFunction_Lag_VariableDefault` test uses a literal `1` for offset and variable `defaultVal` for default, so it only tests one variable parameter per function. |
| No test for the joined (multi-entity) path with variable window function arguments. | Medium | The joined path (`AnalyzeJoinedSyntaxOnly` -> `BuildJoinedLagLeadSql`) was modified to thread `SemanticModel` and `projectionParams`, but no test exercises this path with actual variable arguments. If a regression occurs in the joined path specifically, it would go undetected. |
| No test for the `ResolveScalarArgSql` fallback path where a complex expression (e.g., `someObj.Property`) is passed, resulting in a runtime fallback. | Low | This is a defensive path, but verifying it produces a graceful fallback rather than a crash would be valuable. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `List<ParameterInfo>? projectionParams = null` is threaded as an optional parameter through ~20 methods in `ProjectionAnalyzer.cs`. This follows the existing pattern used for `SemanticModel? semanticModel = null` threading. | Info | Consistent with existing code patterns. |
| `delegateParamName = "func"` in `CarrierAnalyzer.cs` (line 306) matches the existing pattern used for WHERE/HAVING captured variables (line 300). | Info | Consistent naming. |
| The `{@N}` placeholder format in `SqlFormatting.QuoteSqlExpression` is a new convention alongside the existing `{identifier}` format for quoted identifiers. The disambiguation logic (check for `@` prefix + integer parse) is clean. | Info | Well-integrated into the existing formatting infrastructure. |
| `EmitCarrierSelect` signature change from 2 params to 5 params (3 optional) maintains backward compatibility since all new params have defaults. | Info | Non-breaking change. |

No concerns.

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The `AnalyzeJoinedSyntaxOnly` public method signature changed from 3 to 4 parameters (added `SemanticModel? semanticModel = null`). Since the new parameter has a default value, existing callers are unaffected. | Info | Non-breaking API change. |
| Existing test assertions changed from `0m` to `0` in SQL output (e.g., `LAG("Total", 1, 0m)` -> `LAG("Total", 1, 0)`). This reflects the bug fix -- previously invalid SQL now generates valid SQL. Manifest output files updated accordingly across all four dialects. | Info | This is the intended fix for issue #222. Any consumers previously working around the `0m` in SQL will need to update their expectations, but since `0m` was always invalid SQL, no consumer should have been relying on it. |
| The `{@N}` format in `SqlFormatting.QuoteSqlExpression` could theoretically conflict if a user had a column identifier starting with `@` followed by digits. | Low | In practice, column identifiers like `@0` are not valid SQL identifiers and would never appear in the existing `{identifier}` placeholder format. The check requires both `@` prefix and successful integer parse, making false positives essentially impossible. |

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Plan Compliance | Missing projection param merging in AnalyzeOperandChain | Medium | C | Track as issue |
| 2 | Correctness | ProjectionInfo.GetHashCode() omits ProjectionParameters | Low | D | Follows existing pattern |
| 3 | Correctness | @__proj1 substring collision with @__proj10 | Low | D | Practically impossible |
| 4 | Correctness | FormatConstantForSql fallback lacks InvariantCulture | Low | A | Fix now |
| 5 | Test Quality | No test for joined path with variable window args | Medium | C | Track as issue |
| 6 | Test Quality | No test for multiple variable params in single call | Low | D | Current coverage adequate |
| 7 | Test Quality | No test for complex-expression fallback path | Low | D | Defensive path covered indirectly |
| 8 | Integration | {@N} format could conflict with @-prefixed identifiers | Low | D | Practically impossible |

## Issues Created
- #232: Extend projection parameter merging to AnalyzeOperandChain
- #233: Add tests for variable window function args in joined queries
