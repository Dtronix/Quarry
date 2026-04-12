# Implementation Plan: generator-review-fixes

## Key Concepts

**SubqueryExpr traversal completeness:** The SqlExpr tree has two traversal paths that must stay in sync — parameter extraction (`SqlExprClauseTranslator`) and parameter collection (`SqlExprRenderer.CollectParamsRecursive`). Both must visit every node type that can contain `ParamSlotExpr` or `CapturedValueExpr` children. Currently both miss `SubqueryExpr.Selector` and the renderer also misses `RawCallExpr.Arguments`.

**Incremental equality contract:** Every immutable, codegen-significant property on pipeline models must participate in `Equals`/`GetHashCode`. Missing properties cause Roslyn's incremental cache to serve stale generated code when only those properties change between builds.

**Emission-time caching:** `GetClauseEntries()`, `ResolveSiteParams()`, and `BuildParamConditionalMap()` are called repeatedly during code emission with identical inputs. Memoizing or pre-computing these eliminates redundant allocations and iterations during build.

---

## Phase 1: Fix SubqueryExpr traversal (C1+C2+C5)

Three related bugs where SubqueryExpr children are incompletely traversed or lost during reconstruction.

### Changes

**`IR/SqlExprClauseTranslator.cs` — `ExtractSubqueryParameters` (lines 21-46):**

The method currently only processes `sub.Predicate` and early-returns if predicate is null. It also drops `sub.Selector` and `sub.ImplicitJoins` during reconstruction.

Fix: 
1. Remove the `if (sub.Predicate == null) return sub` early return — a subquery with no predicate but a selector containing captured values must still be processed.
2. Extract parameters from `sub.Selector` via `ExtractParametersCore(sub.Selector, ...)` in addition to the predicate.
3. Track whether either predicate or selector changed (not just predicate).
4. When reconstructing the resolved SubqueryExpr (line 30-38), pass `selector: newSelector` to the constructor (the resolved constructor at `SqlExprNodes.cs:638` already accepts `selector` as an optional parameter).
5. After reconstruction, call `.WithImplicitJoins(sub.ImplicitJoins)` to preserve implicit joins.
6. For the unresolved path (line 40-45), also pass `selector: newSelector` (the unresolved constructor at `SqlExprNodes.cs:620` also accepts `selector`).

The reconstruction should look like:
```csharp
var newPredicate = sub.Predicate != null 
    ? ExtractParametersCore(sub.Predicate, parameters, ref paramIndex, subqueryPredicate: true) 
    : null;
var newSelector = sub.Selector != null 
    ? ExtractParametersCore(sub.Selector, parameters, ref paramIndex, subqueryPredicate: true) 
    : null;

bool predicateChanged = !ReferenceEquals(newPredicate, sub.Predicate);
bool selectorChanged = !ReferenceEquals(newSelector, sub.Selector);
if (!predicateChanged && !selectorChanged) return sub;

SubqueryExpr result;
if (sub.IsResolved)
{
    result = new SubqueryExpr(
        sub.OuterParameterName, sub.NavigationPropertyName, sub.SubqueryKind,
        newPredicate, sub.InnerParameterName,
        sub.InnerTableQuoted!, sub.InnerAliasQuoted!, sub.CorrelationSql!,
        selector: newSelector);
}
else
{
    result = new SubqueryExpr(
        sub.OuterParameterName, sub.NavigationPropertyName, sub.SubqueryKind,
        newPredicate, sub.InnerParameterName,
        selector: newSelector);
}
return sub.ImplicitJoins != null ? result.WithImplicitJoins(sub.ImplicitJoins) : result;
```

**`IR/SqlExprRenderer.cs` — `CollectParamsRecursive` (lines 58-105):**

Add two missing cases:
1. `case RawCallExpr rawCall:` — iterate `rawCall.Arguments` and recurse.
2. In the existing `case SubqueryExpr sub:` (line 100), also recurse into `sub.Selector` when non-null.

### Tests

Add tests to `Quarry.Tests` that exercise:
- Aggregate subquery with a captured variable in the selector (e.g., `u.Orders.Sum(o => o.Total * discount)` where `discount` is a local variable).
- Subquery with both predicate and selector containing parameters.
- Verify the generated SQL contains proper `@p` placeholders (not raw captured value text).
- If existing cross-dialect tests cover aggregate subqueries, verify they still pass.

### Dependencies
None — this is the first phase.

---

## Phase 2: Fix equality gaps (C3+C4)

### Changes

**`IR/AssembledPlan.cs` — `Equals` (lines 153-166):**

Add 8 missing immutable properties to the equality check. The properties to add (all get-only, set at construction, affect generated code):
- `IsTraced` (bool) — controls trace comment emission
- `BatchInsertReturningSuffix` (string?) — literal SQL suffix
- `BatchInsertColumnsPerRow` (int) — parameter index calculation
- `ExecutionSite` (TranslatedCallSite) — terminal interceptor signature
- `ClauseSites` (IReadOnlyList<TranslatedCallSite>) — clause interceptor generation
- `PreparedTerminals` (IReadOnlyList<TranslatedCallSite>?) — prepared query terminals
- `PrepareSite` (TranslatedCallSite?) — Prepare() site interceptor
- `InsertInfo` (InsertInfo?) — insert column metadata

Properties correctly excluded (mutable, set after caching boundary): `ProjectionInfo`, `JoinedTableInfos`, `TraceLines`, `ReaderDelegateCode`.

Note: `ReaderDelegateCode` has a setter but IS currently included in Equals. It should remain included since it's compared as a deterministic derived value.

For `Equals`, append after the existing checks:
```csharp
&& IsTraced == other.IsTraced
&& BatchInsertReturningSuffix == other.BatchInsertReturningSuffix
&& BatchInsertColumnsPerRow == other.BatchInsertColumnsPerRow
&& ExecutionSite.Equals(other.ExecutionSite)
&& EqualityHelpers.SequenceEqual(ClauseSites, other.ClauseSites)
&& EqualityHelpers.NullableSequenceEqual(PreparedTerminals, other.PreparedTerminals)
&& Equals(PrepareSite, other.PrepareSite)
&& Equals(InsertInfo, other.InsertInfo)
```

For `GetHashCode` (line 170-173), add `IsTraced` and `BatchInsertColumnsPerRow` to the existing `HashCode.Combine` call.

**`IR/QueryPlan.cs` — `QueryParameter.Equals` (lines 444-463):**

Add 4 missing immutable properties:
```csharp
&& EntityPropertyExpression == other.EntityPropertyExpression
&& NeedsUnsafeAccessor == other.NeedsUnsafeAccessor
&& IsDirectAccessible == other.IsDirectAccessible
&& CollectionAccessExpression == other.CollectionAccessExpression
```

For `GetHashCode` (line 467-469), add `NeedsUnsafeAccessor` to the `HashCode.Combine` call (it's a bool that meaningfully distinguishes parameter access patterns).

### Tests

The existing 2932 tests serve as the regression gate. These equality changes cannot break functionality — they can only cause the incremental pipeline to regenerate code that was previously (incorrectly) served from cache. No new tests needed beyond confirming the full suite passes.

### Dependencies
None — independent of Phase 1.

---

## Phase 3: Cache GetClauseEntries (H1)

### Changes

**`IR/AssembledPlan.cs`:**

Add a private backing field and convert `GetClauseEntries()` to use lazy initialization:

```csharp
private IReadOnlyList<ChainClauseEntry>? _clauseEntries;

public IReadOnlyList<ChainClauseEntry> GetClauseEntries()
{
    if (_clauseEntries != null) return _clauseEntries;
    // ... existing logic ...
    _clauseEntries = entries;
    return entries;
}
```

This is safe because `AssembledPlan`'s `ClauseSites` and `Plan.ConditionalTerms` (the inputs to this method) are immutable after construction. The `_clauseEntries` field should NOT be included in `Equals`/`GetHashCode` — it's a derived cache.

Note: `_clauseEntries` must be excluded from equality. Since it's a private field and `Equals` only checks named properties, this happens naturally.

### Tests

Existing tests serve as regression gate. No behavioral change — same output, fewer allocations.

### Dependencies
None — but enables Phase 4.

---

## Phase 4: Pre-compute ResolveSiteParams + cache BuildParamConditionalMap (H2+H3)

### Changes

**`CodeGen/TerminalEmitHelpers.cs` — `ResolveSiteParams` (lines 24-63):**

The current method iterates all clause entries from the beginning for each call to find the matching site and compute parameter offsets. Replace with a pre-computed dictionary approach.

Add a new method that builds the lookup once per chain:
```csharp
internal static Dictionary<string, (IReadOnlyList<QueryParameter> Params, int Offset)> 
    BuildSiteParamsMap(AssembledPlan chain)
{
    var entries = chain.GetClauseEntries();
    var map = new Dictionary<string, (IReadOnlyList<QueryParameter>, int)>(entries.Count);
    int offset = 0;
    foreach (var entry in entries)
    {
        var siteParams = entry.Site.TranslatedClause?.Parameters 
            ?? Array.Empty<QueryParameter>();
        // ... existing parameter extraction logic from ResolveSiteParams ...
        map[entry.Site.UniqueId] = (siteParams, offset);
        offset += siteParams.Count;
    }
    return map;
}
```

Update all 8 call sites of `ResolveSiteParams` across `ClauseBodyEmitter.cs`, `CarrierEmitter.cs`, and `JoinBodyEmitter.cs` to accept the pre-computed map as a parameter instead of calling `ResolveSiteParams` per-site.

**`CodeGen/TerminalEmitHelpers.cs` — `BuildParamConditionalMap` (lines 127-147):**

Similarly, callers should compute this once and pass it down. The 5 call sites in `CarrierEmitter.cs` (lines 624, 791, 1082) and `TerminalEmitHelpers.cs` (lines 193, 296) should receive the map as a parameter.

The approach for both: compute at the terminal emission entry point (e.g., `EmitCarrierExecutionTerminal` or `EmitCarrierToDiagnosticsTerminal`) and thread through as a parameter.

### Tests

Existing tests serve as regression gate. No behavioral change.

### Dependencies
Phase 3 (GetClauseEntries caching makes this more efficient but isn't strictly required).

---

## Phase 5: Fix SqlExpr node hash quality (H7)

### Changes

**`IR/SqlExprNodes.cs` — `ParamSlotExpr` constructor (line 121):**

Current hash: `HashCode.Combine(SqlExprKind.ParamSlot, localIndex, clrType, valueExpression, isCaptured, isCollection)` — misses 4 properties from `DeepEquals`: `ExpressionPath`, `CustomTypeMappingClass`, `IsEnum`, `EnumUnderlyingType`. (`ElementTypeName` is also missing but is less likely to differ independently.)

Switch to the builder pattern to include all equality-significant fields:
```csharp
: base(ComputeParamSlotHash(localIndex, clrType, valueExpression, isCaptured, 
    expressionPath, isCollection, elementTypeName, customTypeMappingClass, isEnum, enumUnderlyingType))
```

With a static helper:
```csharp
private static int ComputeParamSlotHash(int localIndex, string clrType, string valueExpression,
    bool isCaptured, string? expressionPath, bool isCollection, string? elementTypeName,
    string? customTypeMappingClass, bool isEnum, string? enumUnderlyingType)
{
    var hc = new HashCode();
    hc.Add(SqlExprKind.ParamSlot);
    hc.Add(localIndex);
    hc.Add(clrType);
    hc.Add(valueExpression);
    hc.Add(isCaptured);
    hc.Add(expressionPath);
    hc.Add(isCollection);
    hc.Add(elementTypeName);
    hc.Add(customTypeMappingClass);
    hc.Add(isEnum);
    hc.Add(enumUnderlyingType);
    return hc.ToHashCode();
}
```

**`IR/SqlExprNodes.cs` — `CapturedValueExpr` constructor (line 410):**

Current hash: `HashCode.Combine(SqlExprKind.CapturedValue, variableName, syntaxText, expressionPath)` — misses `ClrType` and `IsStaticField`.

Fix: `HashCode.Combine(SqlExprKind.CapturedValue, variableName, syntaxText, expressionPath, clrType, isStaticField)`

**`IR/SqlExprNodes.cs` — `LikeExpr` constructor (line 366):**

Current hash: `HashCode.Combine(SqlExprKind.LikeExpr, operand.GetHashCode(), pattern.GetHashCode(), isNegated)` — misses `LikePrefix`, `LikeSuffix`, `NeedsEscape`.

Fix: Switch to builder pattern to fit all 7 fields:
```csharp
private static int ComputeLikeHash(SqlExpr operand, SqlExpr pattern, bool isNegated,
    string? likePrefix, string? likeSuffix, bool needsEscape)
{
    var hc = new HashCode();
    hc.Add(SqlExprKind.LikeExpr);
    hc.Add(operand.GetHashCode());
    hc.Add(pattern.GetHashCode());
    hc.Add(isNegated);
    hc.Add(likePrefix);
    hc.Add(likeSuffix);
    hc.Add(needsEscape);
    return hc.ToHashCode();
}
```

### Tests

Existing tests serve as regression gate. Hash changes don't affect correctness — they only affect cache efficiency.

### Dependencies
None.

---

## Phase 6: Unify terminal eligibility validation (H8)

### Changes

`CarrierEmitter.WouldExecutionTerminalBeEmitted` (lines 82-106) and `FileEmitter.EmitInterceptorMethod` (lines 524-594) both check whether a terminal should be emitted, with slightly different logic that could diverge.

Extract a shared static method (e.g., in `TerminalEmitHelpers` or a new `TerminalEligibility` static class):

```csharp
internal static (bool ShouldEmit, string? SkipReason) CheckTerminalEligibility(
    TranslatedCallSite site, AssembledPlan chain)
```

This method consolidates the checks:
- `UnmatchedMethodNames` contains the site method → skip
- `ReaderDelegateCode == null` for fetch terminals → skip  
- Empty/unresolved result type → skip
- `ExecuteNonQuery` without SET clause → skip
- Ambiguous column mapping → skip

Both `CarrierEmitter` and `FileEmitter` call this single method instead of maintaining parallel logic. Read both existing implementations carefully to ensure the unified version covers all conditions from both.

### Tests

Existing tests serve as regression gate.

### Dependencies
None.

---

## Phase 7: Extract shared patterns (M2+M5)

### Changes

**M2 — CTE parameter remapping in `Parsing/ChainAnalyzer.cs`:**

Lines 1265-1295 and 2555-2585 contain identical blocks that:
1. Call `RemapParameters`
2. Build a `localToGlobal` dictionary
3. `parameters.AddRange`
4. Iterate `projection.Columns` replacing `@__proj` placeholders
5. Reconstruct `SelectProjection`

Extract a helper method:
```csharp
private static SelectProjection RemapProjectionParameters(
    SelectProjection projection, 
    IReadOnlyList<ParameterInfo> projParams,
    List<QueryParameter> parameters, 
    ref int paramGlobalIndex)
```

Replace both blocks with calls to this helper.

**M5 — SQL literal formatters:**

`UsageSiteDiscovery.FormatConstantAsSqlLiteralSimple` (line 2440) and `ProjectionAnalyzer.FormatConstantForSql` (line 2379) are near-duplicates.

Create a single `SqlLiteralFormatter.Format(object? value)` static method in a shared location (e.g., `Utilities/SqlLiteralFormatter.cs` or add to an existing utility class). It should cover the union of both methods' type handling. Both call sites delegate to the new method.

### Tests

Existing tests serve as regression gate.

### Dependencies
None.

---

## Phase 8: Small improvements (M8+M10+M11)

### Changes

**M8 — SqlExprRenderer StringBuilder reuse (`IR/SqlExprRenderer.cs` line 24-26):**

Add an overload of `Render` that accepts a `StringBuilder`:
```csharp
public static void Render(SqlExpr expr, SqlDialect dialect, StringBuilder sb, 
    int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
```

The existing `Render` that returns `string` delegates to the new overload. Callers that already have a `StringBuilder` (e.g., `SqlAssembler`) can use the new overload directly to avoid intermediate string allocations.

**M10 — Annotator bare catches (`IR/SqlExprAnnotator.cs` lines 33-37, 328-331, 579-582):**

Change all three bare `catch` blocks to `catch (Exception)` to avoid swallowing `OutOfMemoryException`, `StackOverflowException`, and other fatal CLR exceptions. The graceful-degradation behavior (return original expr) is preserved for normal exceptions.

**M11 — Parser ternary (`IR/SqlExprParser.cs` line 105):**

Currently emits `new SqlRawExpr(conditional.ToString())` which puts raw C# syntax into SQL. Replace with a proper diagnostic. Look up the appropriate diagnostic descriptor (likely QRY006 or QRY019) and emit it, returning a `SqlRawExpr` with a comment indicating the unsupported expression. If no suitable existing diagnostic exists, check `DiagnosticDescriptors.cs` for the right code to use.

### Tests

Existing tests serve as regression gate. For M11, if there are tests that rely on ternary expressions producing SqlRawExpr, they may need updating to expect a diagnostic instead.

### Dependencies
None.

---

## Deferred Items (GitHub issues to create during REMEDIATE)

- H4: ConsumedLambdaInnerSiteIds ThreadStatic → return value
- H5: EntityRegistry partitioning for reduced build-time invalidation
- H6: DisplayClassEnricher restructuring for per-method grouping
- H9: PipelineErrorBag side-channel → attach errors to TranslatedCallSite
- M1: ProjectionAnalyzer parallel hierarchy merge via IColumnResolver
- M4: UsageSiteDiscovery DiscoverPostJoinSites/DiscoverPostCteSites merge
- M7: InterceptorRouter.Categorize() cleanup
- M9: HasCarrierField linear scan → bitflag set
