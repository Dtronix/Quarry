# Work Handoff — Issue #115: Expression-to-Func Migration with UnsafeAccessorType

## Key Components

- `src/Quarry/Query/IQueryBuilder.cs`, `IEntityAccessor.cs`, `IJoinedQueryBuilder.cs`, `Modification/IModificationBuilder.cs` — Public API interfaces (Expression->Func migration)
- `src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` — Predicts compiler-generated display class names for `[UnsafeAccessorType]`, resolves error types from declaration syntax
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — `EnrichDisplayClassInfo()` populates display class metadata on `RawCallSite` (now also called for post-join sites)
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — Builds `[UnsafeAccessor]` extern methods, resolves carrier field types from `CapturedVariableTypes`, detects static field access
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Emits carrier classes with `[UnsafeAccessor]` methods, uses `GetEffectiveCastType` for resolved type casts
- `src/Quarry.Generator/IR/RawCallSite.cs` — `DisplayClassName` and `CapturedVariableTypes` mutable properties
- `src/Quarry.Generator/Translation/ParameterInfo.cs` — `CapturedFieldName`, `CapturedFieldType`
- `src/Quarry.Generator/IR/QueryPlan.cs` — `CapturedFieldName`, `CapturedFieldType`, `NeedsUnsafeAccessor`
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Removed `using System.Linq.Expressions` and `using System.Reflection` from generated code
- `src/Quarry/Internal/ExpressionHelper.cs` — **DELETED** — No longer needed
- PR: Dtronix/Quarry#120

## Completions (This Session)

- **Fixed UnsafeAccessor error type resolution**: The root cause of the AOT `DeleteWithCapturedValue` failure was NOT the display class ordinal (ordinal 6 was correct), but a **type mismatch**: `ref object` instead of `ref int`. Added `TryResolveErrorType()` to `DisplayClassNameResolver` that extracts the type from `var x = await ...Method<T>()` patterns by reading the generic type argument from the declaration syntax. Also updated `CarrierAnalyzer` to propagate resolved types to carrier fields via `displayClassByParam` lookup.
- **Fixed static field UnsafeAccessor detection**: When `CapturedVariableTypes` is null (lambda accesses only class-level fields, no captured locals), the static field detection in `CarrierAnalyzer` was skipped because it required `VarTypes != null`. Fixed condition to treat `VarTypes == null` as "field not in VarTypes". This fixed the pre-existing `Where_Contains_MutableField_RemainsParameterized` test failure.
- **Fixed GetEffectiveCastType pipeline**: Added `GetEffectiveCastType()` helper to `CarrierEmitter` that resolves the correct cast type from `CarrierPlan.Parameters` when `QueryParameter.ClrType` is unresolved. Applied to extraction casts, parameter logging (nullable check), and `ClauseBodyEmitter`.
- **Fixed Equals/GetHashCode contracts**: Added `CapturedFieldName` and `CapturedFieldType` to `Equals`/`GetHashCode` in `QueryParameter`, `ParameterInfo`, and `CarrierStaticField`.
- **Fixed GetPropertyChainSuffix**: Replaced `IndexOf` with `StartsWith` to prevent substring matching (e.g., "id" within "userId").
- **Enriched post-join sites**: Added `EnrichDisplayClassInfo()` call for synthetic post-join sites in `DiscoverPostJoinSites`.
- **Renamed NeedsFieldInfoCache**: Renamed to `NeedsUnsafeAccessor` across `QueryPlan`, `CarrierAnalyzer`, `ChainAnalyzer`.
- **Verified Release mode**: All 2213 tests and 15 AOT scenarios pass in both Debug and Release configurations. No `MergeEnvironments` ordinal divergence observed.

## Previous Session Completions

- **Phase 1**: Built UnsafeAccessorGenTest PoC sample (later removed). Proved display class name prediction and `[UnsafeAccessor]+[UnsafeAccessorType]` extraction works. 15/15 tests passed.
- **Phase 2**: Migrated all 52 `Expression<Func<>>` parameters to `Func<>` across 13 interfaces. Updated `UsageSiteDiscovery`, `ClauseBodyEmitter`, `JoinBodyEmitter` interceptor signatures.
- **Phase 3**: Replaced `FieldInfo.GetValue()` reflection with `[UnsafeAccessor]+[UnsafeAccessorType]`. Created `DisplayClassNameResolver`. Extended IR with capture metadata. `CarrierAnalyzer` emits `[UnsafeAccessor]` extern methods.
- **Phase 4**: Removed `ExpressionHelper.cs`, legacy extraction methods, `using System.Linq.Expressions` and `using System.Reflection` from generated code.
- **Audit**: Identified trade-offs/shortcuts.
- **Fixed** `WithResultTypeName` data loss and local function display class prediction.

## Progress

All phases complete. All 2213 tests pass (2157 unit + 56 analyzer). 15/15 AOT scenarios pass. Both Debug and Release verified. PR #120 needs the latest commits pushed.

## Current State

No active blockers. The implementation is complete and verified.

## Known Issues / Bugs

| Issue | Severity | Notes |
|---|---|---|
| `CapturedFieldType` on `ParameterInfo`/`QueryParameter` is declared but never populated in the translation pipeline | Low | The field exists but is always null; the type is resolved from `CapturedVariableTypes` in `CarrierAnalyzer` instead. Harmless redundancy. |
| `ExpressionPath` still flows through IR | Low | 27 active references — it IS used (collection detection, property chains, debug logging). The previous handoff incorrectly listed it as unused. |
| Debug vs Release closure ordinal divergence (`MergeEnvironments`) | Unknown | No divergence observed in current tests. May surface with more complex closure patterns. |

## Dependencies / Blockers

- **`[UnsafeAccessorType]` requires .NET 10+** — shipped in .NET 10 preview 5. The project targets `net10.0`.
- **Display class naming depends on Roslyn internals** — ordinal computation replicates undocumented Roslyn behavior. Since the generator runs in the same compilation, inputs are consistent, but algorithm must match.

## Architecture Decisions

1. **Error type resolution from declaration syntax**: When `ILocalSymbol.Type.TypeKind == TypeKind.Error` (variable assigned from a generator-produced method), `TryResolveErrorType` unwraps `await` and extracts the generic type argument from `Method<T>()`. This handles the common pattern `var id = await ...ExecuteScalarAsync<int>()`. The "object" fallback remains for patterns that can't be resolved.

2. **Static field detection via VarTypes nullability**: When `CapturedVariableTypes` is null (no captured locals) or doesn't contain the field name, the field is treated as a class-level static field. Uses `UnsafeAccessorKind.StaticField` with `null!` target instead of `UnsafeAccessorKind.Field` with `func.Target!`.

3. **GetEffectiveCastType for resolved types**: The `QueryParameter.ClrType` can be stale ("?" or "object") when the semantic model had error types. `GetEffectiveCastType` checks the `CarrierPlan.Parameters` (which has the resolved type from `CapturedVariableTypes`) and uses it for casts, logging, and field type decisions.

4. **No fallback path**: If display class prediction fails, the generated code won't compile or will crash at runtime. Prediction must be correct. Verified empirically against actual display class names.

5. **Walk-up for local functions**: `DisplayClassNameResolver.Resolve` walks up from local functions to the containing non-local method and uses that method's ordinal. Validated: all 15 AOT scenarios (including static local functions with `await`-assigned captures) pass.

## Open Questions

1. **Should `TryResolveErrorType` handle more patterns?** Currently only handles `var x = await method<T>()`. Other patterns (cast expressions, conditional expressions, chained method calls) could also have error types. May need extension if new failures surface.

2. **Is `MergeEnvironments` (Release mode optimization) a practical concern?** No divergence observed in current tests, but more complex closure patterns may trigger it. The optimization merges display classes across scopes in Release builds, changing closure ordinals.

## Next Work (Priority Order)

1. **Push commits to PR #120** — 4 new commits need to be pushed to the existing PR branch.
2. **Populate `CapturedFieldType` in the translation pipeline** — Currently always null; the type is resolved at `CarrierAnalyzer` time from `CapturedVariableTypes`. Could simplify the pipeline if populated earlier.
3. **Extend `TryResolveErrorType` for additional patterns** — Handle cases beyond `var x = await method<T>()` if new error type failures are reported.
4. **Consider removing `ExpressionPath` from `QueryParameter`** — It's used for collection detection (`__CONTAINS_COLLECTION__` marker) and debug logging, but the actual expression tree navigation it was designed for has been removed. Could be replaced with a simpler flag.
