# Implementation Plan: Parameterize window function scalar arguments

## Key Concepts

**Projection parameters**: Non-column arguments to window functions (`Sql.Ntile(buckets)`, `Sql.Lag(col, offset, default)`) that need to be either inlined as SQL literals (for compile-time constants) or parameterized as `@p{N}` SQL parameters (for runtime variables).

**Constant extraction**: Using `SemanticModel.GetConstantValue()` to evaluate compile-time constants (literal expressions like `2`, `0m`, `1L`, as well as `const` variables and constant expressions). The returned `object` value is formatted for SQL, stripping C# type suffixes.

**Display class capture**: The C# compiler generates "display class" objects to hold variables captured by lambda closures. The existing infrastructure detects these via `DisplayClassEnricher` and generates `[UnsafeAccessor]` methods to extract values at runtime. Currently only wired for WHERE/HAVING/etc lambdas — needs extension to Select lambdas.

**Local projection placeholders**: During projection analysis, non-constant args use `@__proj{N}` placeholders (local indices). During `ChainAnalyzer` assembly, these are remapped to global `@p{globalIndex}` to merge with clause parameters.

## Algorithm: `ResolveScalarArgSql`

Given an `ExpressionSyntax` and optional `SemanticModel`:

```csharp
1. If SemanticModel available:
   a. Try GetConstantValue(expr)
   b. If has value → FormatConstantForSql(value) → return SQL literal string
2. If expr is LiteralExpressionSyntax (fallback when no SemanticModel):
   a. Extract from Token.Value, format for SQL → return SQL literal string
3. Expression is non-constant (runtime variable):
   a. Determine CLR type from SemanticModel.GetTypeInfo(expr)
   b. Get symbol info to detect if it's a captured variable (ILocalSymbol/IParameterSymbol)
   c. Create ParameterInfo with IsCaptured=true, CapturedFieldName=symbol.Name
   d. Assign local projection index, return "@__proj{N}" placeholder
4. If none of the above (complex expression, no semantic model): return null (triggers runtime fallback)
```

`FormatConstantForSql` handles: `int`, `long`, `float`, `double`, `decimal` → `value.ToString(CultureInfo.InvariantCulture)`; `bool` → `TRUE`/`FALSE`; `null` → `NULL`; `string` → escaped and single-quoted.

---

## Phase 1: Core infrastructure and literal fix

**Files**: `ProjectionAnalyzer.cs`, `ProjectionInfo.cs`

Add `IReadOnlyList<Translation.ParameterInfo>? Parameters` property to `ProjectionInfo`. Update constructor, `Equals`, `GetHashCode`, and `CreateFailed`. Reuse the existing `Translation.ParameterInfo` class since it already has all needed fields (`IsCaptured`, `CapturedFieldName`, `ClrType`, etc.).

Add two static helper methods to `ProjectionAnalyzer`:
- `FormatConstantForSql(object? value)` — converts a CLR constant to its SQL literal representation.
- `ResolveScalarArgSql(ExpressionSyntax expr, SemanticModel? semanticModel, List<Translation.ParameterInfo> projectionParams)` — implements the algorithm above.

Update `BuildNtileSql` signature to accept `SemanticModel?` and `List<Translation.ParameterInfo>`. Replace `bucketsExpr.ToString()` with `ResolveScalarArgSql(bucketsExpr, ...)`. If it returns null, the whole method returns `(null, null)` (runtime fallback).

Update `BuildLagLeadSql` (already has `SemanticModel`): add `List<Translation.ParameterInfo>` parameter. Replace `arguments[1].Expression` and `arguments[2].Expression` string interpolation with `ResolveScalarArgSql(...)`.

Update `BuildJoinedLagLeadSql` signature to accept `SemanticModel?` and `List<Translation.ParameterInfo>`. Same replacement.

Thread `SemanticModel?` through the call chain: `GetWindowFunctionInfo` → `BuildNtileSql/BuildLagLeadSql`, and `GetJoinedWindowFunctionInfo` → `BuildNtileSql/BuildJoinedLagLeadSql`. Also thread through `AnalyzeJoinedSyntaxOnly` (optional param) down into `AnalyzeJoinedExpressionWithPlaceholders` and the joined window function path.

Collect projection parameters during analysis by passing a shared `List<Translation.ParameterInfo>` through the analysis methods. Store the final list on `ProjectionInfo.Parameters`.

**Tests to update**: Fix existing tests in `CrossDialectWindowFunctionTests.cs` that assert `0m` in SQL output — change expected SQL from `LAG("Total", 1, 0m)` to `LAG("Total", 1, 0)`.

**Commit gate**: All 3062 tests pass (with updated assertions). Literals produce correct SQL.

---

## Phase 2: Discovery enrichment for Select lambdas

**Files**: `UsageSiteDiscovery.cs`, `RawCallSite.cs`

In `UsageSiteDiscovery.DiscoverRawCallSites()` (the main discovery path, Step 10 around line 587): after `ProjectionAnalyzer.AnalyzeFromTypeSymbol()` or `AnalyzeJoinedSyntaxOnly()` succeeds, check if `projectionInfo.Parameters` has any captured entries. If so, call `EnrichDisplayClassInfo(rawSite, invocation)` to set `EnrichmentLambda` on the Select call site. This enables `DisplayClassEnricher` to resolve the display class name and captured variable types for the Select lambda.

Add `ProjectionParameters` property to `RawCallSite` (`IReadOnlyList<Translation.ParameterInfo>?`) to carry projection parameter metadata from discovery through to ChainAnalyzer. Set it when `projectionInfo.Parameters` is non-empty. Copy it in `Clone` / `WithOverrides` methods.

Add corresponding `ProjectionParameters` property to `TranslatedCallSite` that forwards from `Bound.Raw.ProjectionParameters`.

**Commit gate**: All tests pass. Display class enrichment correctly populates `DisplayClassName` and `CapturedVariableTypes` on Select call sites with projection parameters.

---

## Phase 3: ChainAnalyzer parameter merging

**Files**: `ChainAnalyzer.cs`

In `AnalyzeChainGroup()`, after clause parameters are collected (around line 984) and before pagination (line 1270), add projection parameter merging:

1. Find the Select clause site in the iteration. When it has `ProjectionParameters`:
   a. Call `RemapParameters(projectionParams, ref paramGlobalIndex)` to assign global indices and create `QueryParameter` objects.
   b. Add to the `parameters` list.
   c. Remap placeholder markers in the projection's `ProjectedColumn.SqlExpression` strings: replace each `@__proj{N}` with `@p{globalIndex}` using the mapping from `RemapParameters`.

For SQL expression remapping: since `ProjectedColumn` is immutable, rebuild affected columns with updated `SqlExpression` strings. Create a helper method `RemapProjectionPlaceholders(IReadOnlyList<ProjectedColumn> columns, Dictionary<int, int> localToGlobalMap)` that returns new columns with substituted placeholders.

Apply the same logic in the other `AnalyzeChainGroup` overloads that handle CTE inner chains and set operation chains.

**Commit gate**: All tests pass. Projection parameters appear in `QueryPlan.Parameters` with correct global indices. SQL expressions contain `@p{N}` with correct indices.

---

## Phase 4: Carrier extraction and Select interceptor

**Files**: `CarrierAnalyzer.cs`, `CarrierEmitter.cs`

**CarrierAnalyzer.BuildExtractionPlans()**: Add handling for Select clause sites. After the existing conditions for CTE, set operations, and clause parameters (line 286-305), add:

```
if (cs.Kind == InterceptorKind.Select && cs.ProjectionParameters?.Count > 0)
{
    // Create extraction plan for Select lambda's captured variables
    // Use cs.DisplayClassName, cs.CapturedVariableTypes from DisplayClassEnricher
    // Create CapturedVariableExtractor for each captured projection parameter
    // delegateParamName = "projection" (or "func" to match existing pattern)
}
```

The `CapturedVariableExtractor` objects use the same pattern as WHERE clause extractors: `__ExtractVar_{fieldName}_{clauseIndex}` method names, `[UnsafeAccessor]` for display class field access.

**CarrierEmitter**: When generating the Select interceptor method, check if the clause site has an extraction plan. If so:
- Don't discard the lambda parameter (use `projection` instead of `_`)
- Generate extraction code in the interceptor body: extract captured variables from `projection.Target` and store in carrier fields
- Generate `[UnsafeAccessor]` methods for the captured fields

**Commit gate**: All tests pass. Generated interceptor code correctly captures Select lambda parameters.

---

## Phase 5: Tests for variable parameterization

**Files**: `CrossDialectWindowFunctionTests.cs`, `CarrierGenerationTests.cs`

Add new tests to `CrossDialectWindowFunctionTests.cs`:
- `WindowFunction_Ntile_Variable`: Use a captured variable for buckets (`int n = 3; Sql.Ntile(n, ...)`). Assert SQL contains `NTILE(@p0)` (or appropriate global index).
- `WindowFunction_Lag_VariableOffset`: Use a captured variable for offset. Assert SQL parameterizes it.
- `WindowFunction_Lag_VariableDefault`: Use a captured variable for default value. Assert SQL parameterizes it.
- `WindowFunction_Ntile_ConstVariable`: Use a `const int` variable. Assert it's inlined (not parameterized).

Add a source-generator-level test in `CarrierGenerationTests.cs` (or similar) that verifies the generated interceptor code:
- Carrier has fields for projection parameters
- Select interceptor captures the lambda and extracts variables
- UnsafeAccessor methods are generated for projection captures

**Commit gate**: All tests pass including new tests. Both literal and variable paths verified.

---

## Dependencies

Phase 1 is independent. Phase 2 depends on Phase 1 (needs `ProjectionInfo.Parameters`). Phase 3 depends on Phase 2 (needs `RawCallSite.ProjectionParameters`). Phase 4 depends on Phase 3 (needs projection parameters in the query plan). Phase 5 depends on Phase 4 (tests exercise the full pipeline).
