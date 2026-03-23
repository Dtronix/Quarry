# Quarry Generator: .Trace() Chain Tracing System

## 1. Overview

This plan replaces the existing `PipelineOrchestrator.TraceLog` / `__Trace.*.g.cs` system with a per-chain opt-in tracing mechanism. Developers add `.Trace()` to any builder chain to get full pipeline trace comments emitted as inline C# comments in the generated `.g.cs` interceptor file.

**Key properties:**
- `.Trace()` is a compile-time-only signal. At runtime it is a no-op that returns the same builder type.
- Zero pipeline overhead for non-traced chains. Trace data is captured into a side-channel dictionary, gated behind the `QUARRY_TRACE` preprocessor symbol on the consumer project.
- When `QUARRY_TRACE` is not defined but `.Trace()` is present, a `QRY030` warning is emitted.
- The old `__Trace.*.g.cs` files and `PipelineOrchestrator.TraceLog` are removed entirely.

**End-state usage:**
```csharp
// Consumer adds to .csproj: <DefineConstants>QUARRY_TRACE</DefineConstants>
var result = db.Users()
    .Where(u => u.IsActive)
    .Trace()
    .Select(u => (u.UserId, u.UserName))
    .ExecuteFetchAllAsync(connection);
```

**End-state generated output (in the main .g.cs file):**
```csharp
// [Trace] Chain: abc123 (PrebuiltDispatch, Carrier-Optimized)
// [Trace] Discovery (Where):
//   kind=Where, onJoinedBuilder=false, isAnalyzable=true
//   path=ClauseInfo.IsSuccess -> TryParseLambdaToSqlExpr
//   parsedExpr=BinaryOpExpr(AND, ResolvedColumnExpr, LiteralExpr)
// [Trace] Binding (Where):
//   entity=User, resolvedColumns=[IsActive]
// [Trace] Translation (Where):
//   extractedParams=[] (no captured values)
//   resolvedExpr=SqlRawExpr("IsActive" = 1)
// [Trace] Assembly:
//   tier=PrebuiltDispatch, queryKind=Select
//   masks=[0], paramCount=0
//   sql=SELECT "UserId", "UserName" FROM "users" WHERE "IsActive" = 1
// [Trace] Carrier:
//   eligible=true, fields=0, staticFields=1(sql)
[InterceptsLocation(1, "...")]
public static Task<List<(int, string)>> ExecuteFetchAllAsync_abc123(...)
{
    // ...
}
```

---

## 2. Constraints

- **netstandard2.0 target**: No `ConcurrentDictionary` from newer APIs. Use `[ThreadStatic] static Dictionary<,>`.
- **Roslyn incremental pipeline**: Stages 2-4 process sites independently. Chain grouping happens at Stage 5 after `.Collect()`. The `.Trace()` flag is only discoverable at Stage 5.
- **IEquatable pipeline types**: `RawCallSite`, `BoundCallSite`, `TranslatedCallSite` must implement `IEquatable<T>` correctly. Trace data must NOT be stored on these types (it would break caching).
- **Side-channel only**: All trace data lives in a `[ThreadStatic]` dictionary outside the pipeline types.
- **Consumer gating**: The `QUARRY_TRACE` symbol is read from the consumer project's `Compilation.Options` at emission time (Stage 6). The side-channel capture in Stages 2-4 is always active when the generator runs (cheap dictionary writes), but trace data is only emitted when both `QUARRY_TRACE` is defined AND the chain has a `.Trace()` member.

---

## 3. Design Decisions

| Decision | Choice |
|---|---|
| API surface | `.Trace()` chained before any terminal |
| Type preservation | Per-interface extension method overloads |
| Runtime behavior | Compile-time signal only, no-op at runtime |
| Location | `Quarry.TraceExtensions` static class in main Quarry namespace |
| Detection | New `InterceptorKind.Trace`, no interceptor generated for it |
| Pipeline impact | Side-channel dictionary outside pipeline types |
| Consumer gating | `QUARRY_TRACE` preprocessor symbol on consumer project |
| Trace depth | Full pipeline: discovery, binding, translation, chain analysis, assembly, carrier |
| Output | Inline C# comments in generated .g.cs above interceptor methods |
| Old system | Remove `PipelineOrchestrator.TraceLog`, `__Trace.*.g.cs` files, all `TraceLog?.AppendLine()` calls |
| Missing flag | QRY030 diagnostic warning when `.Trace()` present but `QUARRY_TRACE` not defined |

---

## 4. Architecture

### 4.1 Side-Channel: TraceCapture

A static class with a `[ThreadStatic]` dictionary that accumulates trace messages keyed by site UniqueId. Every pipeline stage writes to this dictionary. At Stage 5, `ChainAnalyzer` detects `.Trace()` in a chain and marks the `AnalyzedChain`. At Stage 6 (emission), traced chains have their accumulated messages emitted as comments; non-traced chains' data is discarded.

The dictionary is cheap: a `List<string>` per site containing short formatted messages. For a typical project with 500 call sites, this is ~500 small lists that are mostly discarded. The lists are created lazily.

### 4.2 QUARRY_TRACE Gating

The `QUARRY_TRACE` symbol is read from the `Compilation` object available at emission time (Stage 6). The check uses `compilation.SyntaxTrees` to find any tree whose `Options.PreprocessorSymbolNames` contains `"QUARRY_TRACE"`. This is the standard Roslyn approach for reading consumer project defines.

When `QUARRY_TRACE` is not defined:
- The side-channel still captures data (it runs in the generator process, not gated by consumer defines)
- At emission time, trace data is silently discarded
- If any chain has `.Trace()`, QRY030 warning is reported

When `QUARRY_TRACE` is defined:
- Traced chains get inline trace comments in the .g.cs file
- Non-traced chains are unaffected

### 4.3 Pipeline Flow

```
Stage 2 (Discovery):
  DiscoverRawCallSite detects InterceptorKind.Trace
  -> RawCallSite created with kind=Trace, no interceptable location
  -> TraceCapture.Log(uniqueId, "Discovery: ...") for EVERY site (not just Trace)

Stage 3 (Binding):
  CallSiteBinder.Bind processes all sites including Trace
  -> Trace sites pass through (no entity binding needed)
  -> TraceCapture.Log(uniqueId, "Binding: ...") for every site

Stage 4 (Translation):
  CallSiteTranslator.Translate processes all sites
  -> Trace sites pass through (no expression to translate)
  -> TraceCapture.Log(uniqueId, "Translation: ...") for every site

Stage 5 (Chain Analysis):
  ChainAnalyzer groups by ChainId
  -> If any member has Kind==Trace, set IsTraced=true on AnalyzedChain
  -> TraceCapture.Log for chain-level analysis (tier, masks, SQL)
  -> Collect trace data for all chain member UniqueIds

Stage 6 (Emission):
  EmitFileInterceptors reads Compilation for QUARRY_TRACE
  -> If QUARRY_TRACE defined AND chain.IsTraced:
       Collect all TraceCapture entries for the chain's member UniqueIds
       Emit as // [Trace] comments above the interceptor method
  -> If .Trace() found but QUARRY_TRACE not defined:
       Report QRY030 diagnostic
  -> Discard all TraceCapture data
```

---

## 5. Work Breakdown

### Step 1: Add TraceCapture static class

New file: `IR/TraceCapture.cs`

Static class with `[ThreadStatic]` dictionary for accumulating trace messages.

**Public API:**
```csharp
internal static class TraceCapture
{
    [ThreadStatic]
    private static Dictionary<string, List<string>>? _data;

    internal static void Log(string uniqueId, string message);
    internal static void LogFormat(string uniqueId, string category, string key, string value);
    internal static IReadOnlyList<string>? Get(string uniqueId);
    internal static void Clear();
}
```

`Log` creates the dictionary and list lazily on first write. `Get` returns null if no data exists for the given UniqueId. `Clear` resets the entire dictionary (called at the start of each collected analysis pass to avoid stale data from incremental reruns).

### Step 2: Add InterceptorKind.Trace

**File:** `Models/InterceptorKind.cs`

Add `Trace` to the enum. Value placed after the existing terminal kinds.

**File:** `Parsing/UsageSiteDiscovery.cs`

Add to the `InterceptableMethods` dictionary:
```csharp
["Trace"] = InterceptorKind.Trace,
```

### Step 3: Handle Trace in DiscoverRawCallSite

**File:** `Parsing/UsageSiteDiscovery.cs`

In `DiscoverRawCallSite`, when `kind == InterceptorKind.Trace`:
- Create a `RawCallSite` with `interceptableLocationData: null` (no interceptor generated)
- Set `isAnalyzable: true` so it flows through the pipeline
- The site participates in ChainId grouping but produces no interceptor

Additionally, add `TraceCapture.Log()` calls for every site's discovery path:
- Which branch was taken (PendingClauseInfo / ClauseInfo.IsSuccess / else-if-analyzable / none)
- Expression type if parsed (`InExpr`, `BinaryOpExpr`, etc.)
- For Contains/IN patterns: whether constant inlining was attempted and why it succeeded/failed
- `isAnalyzable` value and reason if false
- `isOnJoinedBuilder` flag

### Step 4: Add trace logging to CallSiteBinder

**File:** `IR/CallSiteBinder.cs`

In `CallSiteBinder.Bind`, after binding completes:
```csharp
internal static ImmutableArray<BoundCallSite> Bind(
    RawCallSite raw, EntityRegistry registry, CancellationToken ct)
```

Log:
- Entity resolution result (found/not found, entity name)
- Joined entity resolution for join sites
- Column resolution results (which columns matched, which didn't)
- Navigation join detection

### Step 5: Add trace logging to CallSiteTranslator

**File:** `IR/CallSiteTranslator.cs`

In `CallSiteTranslator.Translate`, after translation completes:
```csharp
internal static TranslatedCallSite Translate(BoundCallSite bound, CancellationToken ct)
```

Log:
- Parameter extraction results (count, types, captured vs literal)
- For InExpr: whether values are CapturedValueExpr (runtime params) or LiteralExpr (inlined)
- Expression tree summary (top-level node type and immediate children)
- Join parameter mapping results

### Step 6: Add trace logging to ChainAnalyzer

**File:** `Parsing/ChainAnalyzer.cs`

In `ChainAnalyzer.Analyze` and `AnalyzeChainGroup`:

**Detection of .Trace():**
When grouping sites by ChainId, check if any site in the group has `Kind == InterceptorKind.Trace`. If so, set `IsTraced = true` on the `AnalyzedChain`. Trace sites are excluded from clause processing (they have no expression).

**Trace logging (for all sites, not just traced chains):**
- Chain tier classification and reason
- Conditional mask computation
- WHERE/ORDER/GROUP term construction
- Parameter index remapping
- Query kind determination

### Step 7: Add IsTraced to AnalyzedChain

**File:** `Parsing/ChainAnalyzer.cs`

Add `IsTraced` property to `AnalyzedChain`:
```csharp
internal sealed class AnalyzedChain
{
    public AnalyzedChain(
        QueryPlan plan,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        bool isTraced);

    public bool IsTraced { get; }
}
```

This flag propagates through the pipeline to emission.

### Step 8: Add trace logging to SqlAssembler

**File:** `IR/SqlAssembler.cs`

In `SqlAssembler.Assemble`:
- Log the rendered SQL for each mask variant
- Log parameter count and offsets
- Log which WHERE terms are active per mask
- Log ORDER BY, GROUP BY, HAVING rendering
- Log LIMIT/OFFSET, RETURNING/OUTPUT handling

### Step 9: Add trace logging to CarrierAnalyzer

**File:** `CodeGen/CarrierAnalyzer.cs`

In `CarrierAnalyzer.Analyze`:
- Log carrier eligibility decision and reason if ineligible
- Log field count, static field count
- Log mask type (byte/ushort/uint/ulong)
- Log parameter binding strategy

### Step 10: Propagate IsTraced through FileInterceptorGroup

**File:** `Models/FileInterceptorGroup.cs`

The `FileInterceptorGroup` holds chains per file. The `AssembledPlan` (which wraps `AnalyzedChain` data) needs to carry the `IsTraced` flag so the emitter can check it.

**File:** `IR/AssembledPlan.cs`

Add `IsTraced` field:
```csharp
public bool IsTraced { get; }
```

Set from `AnalyzedChain.IsTraced` during `PipelineOrchestrator.AnalyzeAndGroupTranslated`.

### Step 11: Read QUARRY_TRACE and emit trace comments

**File:** `QuarryGenerator.cs`

In `EmitFileInterceptors`, read the preprocessor symbol:
```csharp
private static bool HasQuarryTrace(Compilation compilation)
```

Check `compilation.SyntaxTrees.FirstOrDefault()?.Options.PreprocessorSymbolNames.Contains("QUARRY_TRACE")`. Cache the result for the duration of the emission call.

In `EmitFileInterceptorsNewPipeline`:
- For each `AssembledPlan` where `IsTraced == true`:
  - Collect `TraceCapture.Get(uniqueId)` for all chain member UniqueIds
  - Collect chain-level trace (assembly, carrier)
  - Pass collected trace lines to the emitter
- If `.Trace()` found in any chain but `QUARRY_TRACE` is not defined:
  - Report `QRY030` diagnostic on the `.Trace()` site's location

### Step 12: Update FileEmitter to emit trace comments

**File:** `CodeGen/FileEmitter.cs`

Add an optional trace data parameter to the per-chain emission path. When trace data is present, emit it as `// [Trace] ...` comment lines:

- **Above the `#region` for the chain:** chain-level summary (tier, query kind, carrier eligibility)
- **Above each interceptor method:** per-site trace (discovery path, binding, translation)
- **Inside the method body (as first lines):** assembly trace (SQL rendering, parameter mapping)

The trace comments use the `// [Trace]` prefix for easy searchability and filtering.

### Step 13: Add .Trace() extension methods to runtime library

**File:** `src/Quarry/TraceExtensions.cs` (new)

Per-interface extension methods that return the same type. Each is a one-line pass-through.

**Interfaces requiring overloads (14 total):**

| Interface | Signature |
|---|---|
| `IQueryBuilder<T>` | `Trace<T>(this IQueryBuilder<T> b) where T : class` |
| `IQueryBuilder<TEntity, TResult>` | `Trace<TEntity, TResult>(this IQueryBuilder<TEntity, TResult> b) where TEntity : class` |
| `IJoinedQueryBuilder<T1, T2>` | `Trace<T1, T2>(this IJoinedQueryBuilder<T1, T2> b) where T1 : class where T2 : class` |
| `IJoinedQueryBuilder<T1, T2, TResult>` | `Trace<T1, T2, TResult>(this IJoinedQueryBuilder<T1, T2, TResult> b) where T1 : class where T2 : class` |
| `IJoinedQueryBuilder3<T1, T2, T3>` | `Trace<T1, T2, T3>(this IJoinedQueryBuilder3<T1, T2, T3> b) where T1 : class where T2 : class where T3 : class` |
| `IJoinedQueryBuilder3<T1, T2, T3, TResult>` | `Trace<T1, T2, T3, TResult>(...) where T1..T3 : class` |
| `IJoinedQueryBuilder4<T1, T2, T3, T4>` | `Trace<T1, T2, T3, T4>(...) where T1..T4 : class` |
| `IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>` | `Trace<T1, T2, T3, T4, TResult>(...) where T1..T4 : class` |
| `IEntityAccessor<T>` | `Trace<T>(this IEntityAccessor<T> b) where T : class` |
| `IDeleteBuilder<T>` | `Trace<T>(this IDeleteBuilder<T> b) where T : class` |
| `IExecutableDeleteBuilder<T>` | `Trace<T>(this IExecutableDeleteBuilder<T> b) where T : class` |
| `IUpdateBuilder<T>` | `Trace<T>(this IUpdateBuilder<T> b) where T : class` |
| `IExecutableUpdateBuilder<T>` | `Trace<T>(this IExecutableUpdateBuilder<T> b) where T : class` |
| `IInsertBuilder<T>` | `Trace<T>(this IInsertBuilder<T> b) where T : class` |

### Step 14: Add QRY030 diagnostic descriptor

**File:** `Models/DiagnosticDescriptors.cs`

```csharp
internal static readonly DiagnosticDescriptor TraceWithoutFlag = new(
    id: "QRY030",
    title: ".Trace() requires QUARRY_TRACE",
    messageFormat: ".Trace() found on chain at {0} but QUARRY_TRACE is not defined. Add <DefineConstants>QUARRY_TRACE</DefineConstants> to enable trace output.",
    category: "Quarry",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

### Step 15: Remove old trace system

**Files to modify:**

| File | Change |
|---|---|
| `IR/PipelineOrchestrator.cs` | Remove `[ThreadStatic] TraceLog` field. Remove all `TraceLog?.AppendLine()` calls. Remove `TraceLog` initialization. |
| `QuarryGenerator.cs` | Remove `__Trace.*.g.cs` file emission (lines ~900-906). Remove trace StringBuilder creation in `EmitFileInterceptorsNewPipeline`. Remove trace exception logging to `__Trace` files. |
| `Parsing/ChainAnalyzer.cs` | Remove any `TraceLog?.AppendLine()` calls if present. |
| All files referencing `TraceLog` | Search for `TraceLog` across the generator and remove all references. |

### Step 16: Update tests

**Test changes:**
- Any test that asserts on `__Trace.*.g.cs` file content must be updated or removed.
- Add new tests that verify `.Trace()` produces inline comments when `QUARRY_TRACE` is defined.
- Add test for QRY030 diagnostic when `.Trace()` is present without `QUARRY_TRACE`.
- Verify `.Trace()` does not affect generated interceptor behavior (same SQL, same parameters).
- The `TestCapturedChains` hook in ChainAnalyzer is unaffected (it serves a different purpose).

---

## 6. Trace Message Format

All trace messages use a consistent format for searchability:

```
// [Trace] {Category} ({SiteName}):
//   {key}={value}
//   {key}={value}
```

**Categories:**
- `Discovery` - Which code path, expression type, analyzability
- `Binding` - Entity resolution, column matching
- `Translation` - Parameter extraction, expression transformation
- `ChainAnalysis` - Tier, masks, term construction
- `Assembly` - SQL rendering per mask, parameter offsets
- `Carrier` - Eligibility, field construction

**Example full trace for a single chain:**
```csharp
// [Trace] Chain: f7a2b (4 sites, PrebuiltDispatch, Carrier-Optimized)
//
// [Trace] Discovery (Where at line 42):
//   kind=Where, onJoinedBuilder=true, isAnalyzable=true
//   clauseAnalysis=SKIPPED (isOnJoinedBuilder)
//   path=else-if-analyzable -> TryParseLambdaToSqlExpr
//   parsedExpr=InExpr(ColumnRefExpr(o,Status), [CapturedValueExpr(statuses)])
//   inlineAttempt=FAILED: TryResolveConstantArray returned null
//     identifier found=true, symbol=ILocalSymbol(statuses)
//     declaringSyntaxRefs=1, initializerKind=ImplicitArrayCreation
//     elementCount=3, allConstant=true
//     resolvedValues=[pending, processing, shipped]
//
// [Trace] Binding (Where):
//   entity=Order (via join position 1)
//   resolvedColumns=[Status -> "t1"."Status"]
//
// [Trace] Translation (Where):
//   paramExtraction: CapturedValueExpr(statuses) -> ParamSlotExpr(0, isCollection=true)
//   elementType=string (inferred from column Status)
//
// [Trace] ChainAnalysis:
//   tier=PrebuiltDispatch
//   queryKind=Select, primaryTable=users
//   joins=[INNER JOIN orders ON t0.UserId = t1.UserId]
//   whereTerms=1 (InExpr), orderTerms=0
//   conditionalMasks=[], possibleMasks=[0]
//   params=[P0: string[] (collection, path=__CONTAINS_COLLECTION__)]
//
// [Trace] Assembly (mask=0):
//   sql=SELECT "t0"."UserName", "t1"."Total" FROM "users" AS "t0"
//       INNER JOIN "orders" AS "t1" ON "t0"."UserId" = "t1"."UserId"
//       WHERE "t1"."Status" IN (@p0, @p1, @p2)
//   paramCount=3, readerDelegate=generated
//
// [Trace] Carrier:
//   eligible=true, reason=PrebuiltDispatch+SingleMask
//   fields=[P0:object, P1:object, P2:object]
//   staticFields=[Sql:string]
//   maskType=none (single mask)
```

---

## 7. Files Changed Summary

| File | Action |
|---|---|
| `IR/TraceCapture.cs` | **New** - Side-channel trace accumulator |
| `Models/InterceptorKind.cs` | Add `Trace` enum value |
| `Models/DiagnosticDescriptors.cs` | Add `QRY030` descriptor |
| `Parsing/UsageSiteDiscovery.cs` | Add "Trace" to method lookup; add TraceCapture.Log calls in DiscoverRawCallSite |
| `IR/CallSiteBinder.cs` | Add TraceCapture.Log calls |
| `IR/CallSiteTranslator.cs` | Add TraceCapture.Log calls |
| `IR/SqlExprClauseTranslator.cs` | Add TraceCapture.Log for InExpr/parameter extraction |
| `Parsing/ChainAnalyzer.cs` | Detect .Trace() in chain, set IsTraced, add TraceCapture.Log |
| `IR/SqlAssembler.cs` | Add TraceCapture.Log for SQL rendering |
| `CodeGen/CarrierAnalyzer.cs` | Add TraceCapture.Log for eligibility |
| `Parsing/ChainAnalyzer.cs (AnalyzedChain)` | Add `IsTraced` property |
| `IR/AssembledPlan.cs` | Add `IsTraced` property |
| `IR/PipelineOrchestrator.cs` | Remove TraceLog; pass IsTraced through; call TraceCapture.Clear() |
| `QuarryGenerator.cs` | Remove __Trace emission; read QUARRY_TRACE; pass trace data to emitter; report QRY030 |
| `CodeGen/FileEmitter.cs` | Accept and emit trace comments above interceptor methods |
| `src/Quarry/TraceExtensions.cs` | **New** - 14 per-interface .Trace() extension methods |
| Test files | Update/remove __Trace assertions; add .Trace() tests |

---

## 8. Validation

- **All existing tests pass** (no behavioral change to generated interceptors).
- **No `__Trace.*.g.cs` files** generated anywhere.
- **No `PipelineOrchestrator.TraceLog`** references remain in codebase.
- **`.Trace()` with `QUARRY_TRACE`** produces inline comments in .g.cs for the traced chain only.
- **`.Trace()` without `QUARRY_TRACE`** produces QRY030 warning, no trace output.
- **Chains without `.Trace()`** are completely unaffected regardless of `QUARRY_TRACE`.
- **`.Trace()` does not generate an interceptor** for itself (no `[InterceptsLocation]` for the `.Trace()` call).
- **Type preservation**: `.Trace()` returns the exact same builder type. Terminals after `.Trace()` compile and resolve correctly.
