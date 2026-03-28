# Work Handoff — Issue #115: Expression-to-Func Migration with UnsafeAccessorType

## Key Components

- `src/Quarry/Query/IQueryBuilder.cs`, `IEntityAccessor.cs`, `IJoinedQueryBuilder.cs`, `Modification/IModificationBuilder.cs` — Public API interfaces (Expression→Func migration)
- `src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` — **NEW** — Predicts compiler-generated display class names for `[UnsafeAccessorType]`
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — `EnrichDisplayClassInfo()` populates display class metadata on `RawCallSite`
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — Builds `[UnsafeAccessor]` extern methods from captured parameter metadata
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Emits carrier classes with `[UnsafeAccessor]` methods and interceptor bodies using `func.Target!`
- `src/Quarry.Generator/IR/RawCallSite.cs` — Added `DisplayClassName` and `CapturedVariableTypes` mutable properties
- `src/Quarry.Generator/Translation/ParameterInfo.cs` — Added `CapturedFieldName`, `CapturedFieldType`
- `src/Quarry.Generator/IR/QueryPlan.cs` — Added `CapturedFieldName`, `CapturedFieldType` to `QueryParameter`
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Removed `using System.Linq.Expressions` and `using System.Reflection` from generated code
- `src/Quarry/Internal/ExpressionHelper.cs` — **DELETED** — No longer needed
- PR: Dtronix/Quarry#120

## Completions (This Session)

- **Phase 1**: Built UnsafeAccessorGenTest PoC sample (later removed after validating the approach). Proved display class name prediction and `[UnsafeAccessor]+[UnsafeAccessorType]` extraction works for: single capture, multi-capture, string, object property chains, nested boolean expressions, cached accessors across iterations. 15/15 tests passed.
- **Phase 2**: Migrated all 52 `Expression<Func<>>` parameters to `Func<>` across 13 interfaces. Updated `UsageSiteDiscovery.GetExpressionLambdaParameterCount`, `IsDelegateParameterType`, `ClauseBodyEmitter`, `JoinBodyEmitter` interceptor signatures. Changed `expr` parameter name to `func`.
- **Phase 3**: Replaced `FieldInfo.GetValue()` reflection with `[UnsafeAccessor]+[UnsafeAccessorType]`. Created `DisplayClassNameResolver`. Extended IR with capture metadata. `CarrierAnalyzer` emits `[UnsafeAccessor]` extern methods. `CarrierEmitter` uses `func.Target!` + accessor. All 2157 unit tests + 56 analyzer tests pass.
- **Phase 4**: Removed `ExpressionHelper.cs`, `GenerateCachedExtraction`, `GenerateInlineNavigation`, `GenerateSegmentNavigation`. Removed `using System.Linq.Expressions` and `using System.Reflection` from generated file headers.
- **Audit**: Identified 11 trade-offs/shortcuts (see Known Issues below).
- **AOT sample testing**: Partially working — 11/15 scenarios pass in JIT mode. 4 fail due to display class name prediction errors for the `DeleteWithCapturedValue` local function.

## Previous Session Completions

None — this is the first session on this issue.

## Progress

Phases 1-4 complete. All 2213 existing tests pass. AOT sample has 4 remaining failures from display class name misprediction in a specific local function pattern. PR #120 is open but needs fixes before merge.

## Current State

The AOT sample (`src/Samples/Quarry.Sample.Aot`) crashes at runtime on `DeleteWithCapturedValue`:

```
System.MissingFieldException: Field not found: '<>c__DisplayClass0_6.id'
```

**Root cause**: `DeleteWithCapturedValue` is a `static` local function. The `id` variable (from `var id = await ...ExecuteScalarAsync<int>()`) is captured by two lambdas inside this function. The predicted display class name `Program+<>c__DisplayClass0_6` is wrong — closure ordinal 6 is incorrect.

**Why ordinal 6 is wrong**: `ComputeClosureOrdinal` walks `<Main>$`'s entire syntax tree (including all local function bodies) and assigns scope ordinals in pre-order. It assigns ordinal 6 to `DeleteWithCapturedValue`'s body scope. But the C# compiler may assign a different ordinal because its closure analysis handles static local function scopes differently from regular nested scopes.

### Failed approaches for local function display class naming:

1. **Walk up to parent method (`<Main>$`)** — Currently in the code. Works for most local functions (11/15 pass). Fails for `DeleteWithCapturedValue` because the closure ordinal within `<Main>$`'s scope tree doesn't match the compiler's ordinal for static local functions that have `await`-assigned captures.

2. **Match by source location** — Tried matching `methodSymbol.Locations` against `GetMembers()` entries. Failed because `GetMembers()` does NOT include synthesized local function methods during source generation. The synthesized methods are created during lowering, which happens after the generator runs.

3. **Match by name substring** (`>g__{Name}|`) — Tried searching `GetMembers()` for synthesized method names. Failed for the same reason — synthesized methods aren't in `GetMembers()` during source generation.

4. **Compute ordinal as `members.Length + localFuncIndex`** — Tried computing the ordinal as source member count + local function's 0-based index. Failed because the actual IL member ordering doesn't follow this formula. The first scenario (`SelectWithCapturedLocal`) got ordinal 3 but the runtime display class was at ordinal 0.

## Known Issues / Bugs

| Issue | Severity | File |
|---|---|---|
| `RawCallSite.WithResultTypeName` drops `DisplayClassName`/`CapturedVariableTypes` | **Fixed** | `RawCallSite.cs:181` |
| `QueryParameter.Equals`/`GetHashCode` omit `CapturedFieldName`/`CapturedFieldType` | Medium | `QueryPlan.cs:383` |
| `ParameterInfo.Equals`/`GetHashCode` omit `CapturedFieldName`/`CapturedFieldType` | Medium | `ParameterInfo.cs:107` |
| `CarrierStaticField.GetHashCode` inconsistent with `Equals` (missing `CapturedFieldName`/`CapturedFieldType`) | Medium | `CarrierField.cs:136` |
| `GetPropertyChainSuffix` uses `IndexOf` without word boundary check | Medium | `CarrierEmitter.cs:1118` |
| `NeedsFieldInfoCache` property name is vestigial (now drives UnsafeAccessor, not FieldInfo) | Low | `QueryPlan.cs:377` |
| `ExpressionPath` still flows through IR but is unused | Low | Multiple files |
| `CapturedFieldType` on `ParameterInfo`/`QueryParameter` is declared but never populated in the pipeline | Low | Translation pipeline |
| Debug vs Release closure ordinal divergence (Roslyn `MergeEnvironments`) not modeled | Unknown | `DisplayClassNameResolver.cs` |

## Dependencies / Blockers

- **`[UnsafeAccessorType]` requires .NET 10+** — shipped in .NET 10 preview 5. The project already targets `net10.0`.
- **Display class naming depends on Roslyn internals** — the ordinal computation replicates undocumented Roslyn behavior. Changes to Roslyn's closure lowering could break predictions. Since the generator runs in the same compilation, the inputs are consistent, but the algorithm must match exactly.

## Architecture Decisions

1. **Walk-up for local functions**: For local functions, `DisplayClassNameResolver.Resolve` walks up from the local function to the containing non-local method (e.g., `<Main>$`) and uses that method's ordinal. This works because Roslyn generates display classes under the containing method's hierarchy, not under the synthesized local function method. This was validated empirically — the PoC and 11/15 AOT scenarios confirm it.

2. **No fallback path**: The user explicitly rejected fallback strategies (Option A/B/C from the issue). If display class prediction fails, the generated code won't compile (missing `__ExtractP` method) or will crash at runtime (`MissingFieldException`). This is intentional — prediction must be correct.

3. **Scope-tree pre-order for closure ordinals**: Roslyn assigns closure ordinals by walking the scope tree in pre-order (parent before children). The method body scope gets the first ordinal if it has captured variables. This was validated in the PoC against actual runtime display class names.

4. **`CapturedVariableTypes` dictionary**: Stored on `RawCallSite` as a mapping of variable name → fully-qualified CLR type. This is needed because `QueryParameter.ClrType` may be `"?"` or `"object"` for unresolved types (e.g., variables assigned from generator-produced methods). The dictionary comes from `SemanticModel.AnalyzeDataFlow` during discovery, where type info is reliable.

5. **Error type fallback to `"object"`**: When `ILocalSymbol.Type.TypeKind == TypeKind.Error` (type unresolved during source generation), the captured field type falls back to `"object"`. This happens for variables like `var id = await ...ExecuteScalarAsync<int>()` where the intercepted method's return type isn't yet resolved.

## Open Questions

1. **How does Roslyn number closure scopes for static local functions with `await`-assigned variables?** The current pre-order scope walk gives ordinal 6 for `DeleteWithCapturedValue`'s body, but the runtime expects a different ordinal. Is the compiler treating static local function scopes as a separate numbering domain? Or does `await` state machine rewriting change the scope structure?

2. **Should `EnrichDisplayClassInfo` also run for post-join sites?** Currently only called from `DiscoverRawCallSites` (line 760). `DiscoverPostJoinSites` creates RawCallSites without enrichment (line 917). If post-join sites have captured parameters, they'll lack display class metadata.

3. **Is `MergeEnvironments` (Release mode optimization) a practical concern?** The optimization merges display classes across scopes in Release builds. This would change closure ordinals. All current testing is in Debug mode. Need to verify Release mode produces the same display class names.

## Next Work (Priority Order)

1. **Fix `DeleteWithCapturedValue` display class ordinal prediction** — The closure ordinal 6 is wrong. The most promising unexplored approach: dump the actual display class name at runtime (add a temporary test that prints `func.Target.GetType().FullName` for the `DeleteWithCapturedValue` lambda), then work backwards to understand how Roslyn numbers it. The ordinal algorithm in `ComputeClosureOrdinal` needs to be corrected for the specific pattern of static local functions with multiple closure scopes.

2. **Fix `Equals`/`GetHashCode` contract violations** — Add `CapturedFieldName`/`CapturedFieldType` to equality checks in `QueryParameter`, `ParameterInfo`, and `CarrierStaticField.GetHashCode`.

3. **Fix `GetPropertyChainSuffix` word boundary** — Use `StartsWith` instead of `IndexOf`, or check that the match is at position 0 or preceded by a non-identifier character.

4. **Verify Release mode** — Run the AOT sample and test suite in Release configuration to check for `MergeEnvironments` ordinal divergence.

5. **Enrich post-join sites** — Call `EnrichDisplayClassInfo` for sites created by `DiscoverPostJoinSites` (line 917 in `UsageSiteDiscovery.cs`).

6. **Rename `NeedsFieldInfoCache`** → `NeedsUnsafeAccessor` for clarity.

7. **Remove unused `ExpressionPath`** from `ParameterInfo`, `QueryParameter`, `CapturedValueExpr`, `ParamSlotExpr`.

8. **Commit and push fixes** — The PR (#120) needs the `WithResultTypeName` fix, AOT sample fixes, and equality fixes before merge. Run `dotnet test` to verify all 2213 tests still pass after each fix.
