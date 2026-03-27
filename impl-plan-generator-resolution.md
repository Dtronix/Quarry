# Implementation Plan: Fix Tuple Type Resolution in Conditional Chain Reassignment

## Problem Statement

When a query chain variable is reassigned after `.Select()` with a tuple projection, subsequent clauses (`.Where()`, `.OrderBy()`, etc.) emit interceptors with incorrect type arguments. The generator resolves the tuple as `(object, object, object)` instead of `(int, decimal, OrderPriority)`, causing CS9144 signature mismatch at compile time.

**Reproducing pattern:**
```csharp
var query = db.Orders().Select(o => (o.OrderId, o.Total, o.Priority));
query = query.Where(o => o.Priority == priority);  // CS9144
```

**Root cause:** During discovery, when `.Where()` is called on the reassigned variable `query`, the Roslyn semantic model cannot fully resolve the `TResult` type argument of `IQueryBuilder<Order, TResult>` because the tuple type was inferred by the generator in Pipeline 1. The semantic model returns error types or `object` for the unresolved tuple elements. This unresolved type propagates through the entire pipeline — `RawCallSite.ResultTypeName` is wrong from the start, and no downstream stage corrects it.

**Verification test:** `CrossDialectCompositionTests.ConditionalWhere_OnTupleProjection_ResolvesCorrectType` — currently fails with CS9144.

---

## Background: Generator Pipeline Stages

The Quarry generator processes query chains through a 6-stage pipeline. Understanding where type information flows is critical to this fix.

### Stage 1 — Discovery (`UsageSiteDiscovery`)
Each invocation expression matching a Quarry method is independently analyzed. The semantic model provides the receiver's type (`methodSymbol.ContainingType`), from which `EntityTypeName` and `ResultTypeName` are extracted via `ExtractTypeArguments`. For a `.Where()` on a variable whose type has unresolved generics, `ContainingType.TypeArguments[1]` (TResult) returns error types, which `ToFullyQualifiedDisplayString()` renders as `"object"` per element.

**Key method:** `UsageSiteDiscovery.ExtractTypeArguments(INamedTypeSymbol builderType)` — extracts `(entityTypeName, resultTypeName)` from `builderType.TypeArguments`. Located at line ~2659.

**Key property:** `RawCallSite.ResultTypeName` — set once during construction, never mutated. Used by all downstream stages.

### Stage 2 — Binding (`CallSiteBinder`)
Resolves entity from `EntityRegistry`, builds `InsertInfo`/`UpdateInfo`. Does not modify `ResultTypeName`.

### Stage 3 — Translation (`CallSiteTranslator`)
Runs expression binding and SQL rendering. Does not modify `ResultTypeName`. `TranslatedCallSite.ResultTypeName` delegates directly to `Bound.Raw.ResultTypeName`.

### Stage 4 — Chain Analysis (`ChainAnalyzer`)
Groups sites by `ChainId`, identifies terminals, builds `QueryPlan`. Calls `BuildProjection` for Select sites, which **does** resolve the correct tuple type from enriched projection columns. However, this resolved type is stored on `SelectProjection.ResultTypeName` — it is **not** propagated back to clause sites' `ResultTypeName`.

### Stage 5 — Assembly (`SqlAssembler`)
Renders SQL from `QueryPlan`. Uses `SelectProjection.ResultTypeName` for the chain-level result type. Does not modify clause site types.

### Stage 6 — Emission (`FileEmitter` → `ClauseBodyEmitter`)
Emits C# interceptor methods. Reads `site.ResultTypeName` (which is `TranslatedCallSite.ResultTypeName → Bound.Raw.ResultTypeName`) to determine the interceptor's type signature. **This is where the bug manifests** — the emitter uses the unresolved `(object, object, object)` to build the method signature.

### Existing Precedent: `PropagateChainUpdatedSites`
`PipelineOrchestrator.PropagateChainUpdatedSites` (line 149) already patches `TranslatedCallSite` objects after chain analysis. It replaces sites in the global array with chain-updated versions (e.g., sites with corrected `JoinedEntityTypeNames`). This is the exact mechanism needed for propagating resolved result types.

---

## Chosen Approach: Two-Phase Resolution in PipelineOrchestrator

After chain analysis resolves the correct `ResultTypeName` via `BuildProjection`, a new resolution phase patches clause sites whose `ResultTypeName` is unresolved. This runs inside `PipelineOrchestrator.AnalyzeAndGroupTranslated`, between chain analysis and emission, using the existing `PropagateChainUpdatedSites` pattern.

### Why This Approach
- **Clean separation:** Discovery remains single-pass. Resolution happens after all sites are grouped and analyzed.
- **Handles complex chains:** Multi-hop variable assignments, conditional branches, and any chain shape — all sites in a chain group are available for cross-reference.
- **Uses existing infrastructure:** `PropagateChainUpdatedSites` already patches sites post-analysis. The new resolution integrates naturally.
- **Correct from chain analysis onward:** All downstream consumers (assembly, emission, diagnostics) see the resolved type.

### What Changes
1. `RawCallSite` — add a `WithResultTypeName(string)` copy method
2. `ChainAnalyzer` — expose resolved `ResultTypeName` on `AnalyzedChain` (already available via `SelectProjection.ResultTypeName`)
3. `PipelineOrchestrator` — add `ResolveResultTypes` phase between chain analysis and `PropagateChainUpdatedSites`
4. `TranslatedCallSite` — add `WithResolvedResultType(string)` copy method (creates new `BoundCallSite` → new `RawCallSite`)

---

## Phase 1: Add Copy Methods to Immutable IR Types

### 1.1 `RawCallSite.WithResultTypeName`

**File:** `src/Quarry.Generator/IR/RawCallSite.cs`

Add a method that creates a copy of the `RawCallSite` with a different `ResultTypeName`. All other fields are copied verbatim.

```csharp
internal RawCallSite WithResultTypeName(string resolvedResultTypeName)
```

**Implementation notes:**
- Calls the constructor with all existing field values, substituting only `resultTypeName`.
- Must preserve `IEquatable<RawCallSite>` contract — two sites with different `ResultTypeName` are NOT equal (this is correct, as the patched site should replace the original in the pipeline).
- The constructor has ~30 parameters. Consider using a `with`-style pattern or simply calling the constructor directly.

### 1.2 `BoundCallSite.WithRaw`

**File:** `src/Quarry.Generator/IR/BoundCallSite.cs`

```csharp
internal BoundCallSite WithRaw(RawCallSite newRaw)
```

Creates a copy with a new `Raw` property, preserving all binding state (`Entity`, `Context`, `Dialect`, `InsertInfo`, etc.).

### 1.3 `TranslatedCallSite.WithResolvedResultType`

**File:** `src/Quarry.Generator/IR/TranslatedCallSite.cs`

```csharp
internal TranslatedCallSite WithResolvedResultType(string resolvedResultTypeName)
```

Chains through: creates a new `RawCallSite` via `WithResultTypeName`, wraps it in a new `BoundCallSite` via `WithRaw`, wraps that in a new `TranslatedCallSite`.

---

## Phase 2: Expose Resolved Result Type from Chain Analysis

### 2.1 `AnalyzedChain` — Resolved Result Type

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

The chain's `SelectProjection.ResultTypeName` already contains the resolved type after `BuildProjection` enrichment. This is accessible via `AnalyzedChain.Plan.Projection.ResultTypeName`. No new field is needed — the existing `QueryPlan.Projection.ResultTypeName` is the source of truth.

### 2.2 Identifying Unresolved Result Types

A `ResultTypeName` is considered "unresolved" when:
- It is `null`
- It contains `"object"` as a tuple element (e.g., `"(object, object, object)"`)
- It equals `"?"` (error type)
- It equals `"object"` (bare object)

**Helper method to add:**
```csharp
private static bool IsUnresolvedResultType(string? resultTypeName)
```

Located in `PipelineOrchestrator` (or a shared helper). Checks the conditions above. Must NOT flag legitimate `object` result types (e.g., `Sql.Raw<object>`) — the check should be specific to tuple-shaped types containing `object` elements.

**Heuristic:** A result type is unresolved if:
1. It is null, `"?"`, or `"object"`, OR
2. It starts with `"("` (tuple) and contains `"object"` as an element (split by `,` and check trimmed elements)

---

## Phase 3: Result Type Resolution Phase in PipelineOrchestrator

### 3.1 New Method: `ResolveChainResultTypes`

**File:** `src/Quarry.Generator/IR/PipelineOrchestrator.cs`

```csharp
private static void ResolveChainResultTypes(
    List<AssembledPlan> assembledPlans)
```

**Algorithm:**

1. For each `AssembledPlan`:
   a. Get the chain's resolved result type: `plan.Plan.Projection.ResultTypeName`
   b. If the chain has no projection or the projection result type is also unresolved, skip
   c. For each clause site in `plan.ClauseSites`:
      - If `clauseSite.ResultTypeName` is unresolved (per the heuristic above)
      - AND the chain's projection result type is resolved
      - Create a patched site via `clauseSite.WithResolvedResultType(resolvedType)`
      - Replace the site in the plan's clause sites list
   d. Also check `plan.ExecutionSite` — terminal interceptors also read `ResultTypeName`

**Key detail:** `AssembledPlan` stores references to `TranslatedCallSite` objects. After patching, the plan's `ClauseSites` and `ExecutionSite` must be updated to point to the new patched sites. Check if `AssembledPlan.ClauseSites` is mutable or needs a new `AssembledPlan` instance.

### 3.2 Integration Point

**File:** `src/Quarry.Generator/IR/PipelineOrchestrator.cs`, method `AnalyzeAndGroupTranslated`

Insert the call between chain analysis and `PropagateChainUpdatedSites`:

```csharp
// Chain analysis
var analyzedChains = ChainAnalyzer.Analyze(translatedSites, registry, ct, diagnostics);

// SQL assembly
var assembledPlans = ...;

// NEW: Resolve unresolved result types from chain projections
ResolveChainResultTypes(assembledPlans);

// Existing: Propagate updates to global site array
var updatedSites = PropagateChainUpdatedSites(translatedSites, assembledPlans);
```

The existing `PropagateChainUpdatedSites` will automatically pick up the patched sites from the assembled plans, since it iterates `plan.ClauseSites` and `plan.ExecutionSite`.

---

## Phase 4: Handle AssembledPlan Mutability

### 4.1 Check `AssembledPlan.ClauseSites` Mutability

**File:** `src/Quarry.Generator/IR/AssembledPlan.cs`

`AssembledPlan` wraps an `AnalyzedChain` which stores clause sites as `IReadOnlyList<TranslatedCallSite>`. If the list is actually a `List<T>` underneath, direct replacement is possible. If it's an immutable collection, a new list must be constructed.

**Approach:** Add a method to `AssembledPlan` or `AnalyzedChain`:
```csharp
internal void ReplaceClauseSite(int index, TranslatedCallSite newSite)
```

Or, if immutability must be preserved:
```csharp
internal AssembledPlan WithClauseSites(IReadOnlyList<TranslatedCallSite> newClauseSites)
```

**Trade-off:** Since this runs inside `PipelineOrchestrator` (a single-threaded pipeline step), in-place mutation is safe and simpler. The `IEquatable` contract on `AnalyzedChain` is not affected because the patched sites have different `ResultTypeName` values (they won't match the originals in equality checks, which is correct — the cache has already produced its output at this point).

---

## Phase 5: Tuple-Specific Resolution Logic

### 5.1 Rebuilding Tuple Type from Projection Columns

When the chain's `SelectProjection` has `Kind == ProjectionKind.Tuple` and resolved columns, the result type can be rebuilt from the column metadata. This is already done in `ChainAnalyzer.BuildProjection` via `BuildTupleResultTypeName`. The resolved type is stored on `SelectProjection.ResultTypeName`.

**No new logic needed** — the resolved type is already available. Phase 3's algorithm simply reads it and propagates it to unresolved clause sites.

### 5.2 Non-Tuple Projections

For `ProjectionKind.Entity` (e.g., `.Select(u => u)`) — the result type is the entity name, which is resolved during binding. This already works.

For `ProjectionKind.Dto` (e.g., `.Select(u => new UserDto { ... })`) — the result type is the DTO class name from the semantic model. If this is also unresolved (unlikely for named types but possible for generated DTOs), the same resolution logic applies.

For `ProjectionKind.SingleColumn` (e.g., `.Select(u => u.UserId)`) — the result type is the column's CLR type. Resolution via projection column metadata applies here too.

---

## Phase 6: Testing

### 6.1 Primary Verification Test

**Test:** `CrossDialectCompositionTests.ConditionalWhere_OnTupleProjection_ResolvesCorrectType`

Currently fails with CS9144. After the fix, this test must:
1. Compile without errors
2. Execute the query against SQLite
3. Return the correct filtered results

### 6.2 Additional Test Cases

Add to `CrossDialectCompositionTests`:

**Test: `ConditionalWhere_OnDtoProjection_ResolvesCorrectType`**
```csharp
var query = db.Users().Select(u => new UserListItem { ... });
query = query.Where(u => u.IsActive);
```
Verifies DTO projections also resolve correctly when reassigned.

**Test: `ConditionalWhere_OnEntityProjection_ResolvesCorrectType`**
```csharp
var query = db.Users().Select(u => u);
query = query.Where(u => u.IsActive);
```
Verifies entity projections (which already work) aren't regressed.

**Test: `ConditionalOrderBy_OnTupleProjection_ResolvesCorrectType`**
```csharp
var query = db.Orders().Select(o => (o.OrderId, o.Total));
query = query.OrderBy(o => o.Total);
```
Verifies non-Where clauses also benefit from the fix.

### 6.3 Regression Verification

Run the full test suite (currently 1869 tests). No regressions allowed.

---

## File Change Summary

| File | Change |
|---|---|
| `IR/RawCallSite.cs` | Add `WithResultTypeName(string)` copy method |
| `IR/BoundCallSite.cs` | Add `WithRaw(RawCallSite)` copy method |
| `IR/TranslatedCallSite.cs` | Add `WithResolvedResultType(string)` copy method |
| `IR/AssembledPlan.cs` | Add clause site replacement method (or verify mutability) |
| `IR/PipelineOrchestrator.cs` | Add `ResolveChainResultTypes` method; integrate into pipeline |
| `Tests/SqlOutput/CrossDialectCompositionTests.cs` | Existing test + 3 new test cases |

**Estimated scope:** ~100-120 lines of new code across 5-6 files. No changes to discovery, binding, translation, or emission stages.

---

## Risk Assessment

- **Low risk to existing chains:** Clause sites that already have resolved `ResultTypeName` are not touched (the heuristic only patches unresolved types).
- **Incremental caching:** The patching runs after `Collect()` — all caching has already completed. Patched sites don't affect incremental cache keys.
- **Equality contracts:** Patched `RawCallSite`/`TranslatedCallSite` objects have different `ResultTypeName` values than originals. They are not equal to the originals, which is correct — `PropagateChainUpdatedSites` replaces by `UniqueId` lookup, not by equality.
- **Non-tuple types:** The heuristic correctly identifies unresolved tuple types (starts with `(`, contains `object` elements) without false-positiving on legitimate `object` result types.

---

## Open Questions

1. **Should `ResolveChainResultTypes` also patch the `BuilderTypeName` on clause sites?** The `BuilderTypeName` (e.g., `"IQueryBuilder"`) doesn't include type arguments, so it should be unaffected. But verify that `ClauseBodyEmitter.EmitWhere` doesn't combine `BuilderTypeName` with type arguments in a way that could conflict.

2. **Should this fix also handle the `"?"` error type from the generated entity bootstrap problem?** The same resolution mechanism could fix entity-typed result names that resolve as `"?"`. However, entity projections already have a separate enrichment path. Scope this fix to tuple/DTO projections to minimize risk.
