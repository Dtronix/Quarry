# Implementation Plan: Runtime Builder Removal (Issue #59)

**Goal:** Remove all runtime SQL builder infrastructure and establish a carrier-only architecture. Every query chain flows through source-generator-emitted carrier interceptors. Unanalyzable chains become compile-time errors. The `ToSql()` API is removed; consumers use `ToDiagnostics().Sql`.

---

## 1. Architectural Overview

### Current State

Three optimization tiers exist:

- **Tier 1 (PrebuiltDispatch):** Chains with ≤4 conditional bits. The generator pre-builds SQL string literals for every possible clause mask combination and emits a dispatch table (`Mask switch { 0 => "...", 1 => "...", ... }`). A generated carrier class holds typed parameter fields. All clause interceptors bind parameters to carrier fields; the terminal interceptor selects SQL by mask and executes via `QueryExecutor.ExecuteCarrierWithCommandAsync`.
- **Tier 2 (PrequotedFragments):** Defined but never implemented. Chains with >4 conditional bits were intended to assemble SQL from pre-quoted fragments at runtime. In practice, these chains fall through to Tier 1 logic. The enum value, fragment properties on `QueryState`, and `SqlBuilder.BuildFromPrequotedFragments` are dead code.
- **Tier 3 (RuntimeBuild):** Chains that fail analysis (forked chains, unsupported syntax, deep nesting). The generator emits no carrier; instead, runtime builder classes (`QueryBuilder<T>`, `DeleteBuilder<T>`, etc.) construct SQL dynamically via `SqlBuilder` / `SqlModificationBuilder` and execute via `QueryExecutor` / `ModificationExecutor` methods that accept state objects.

Additionally, some Tier 1 chains fail carrier eligibility checks (ambiguous projection columns, malformed SQL, unmatched methods) and fall through to a **non-carrier prebuilt path** that uses dispatch tables but delegates execution through `QueryState`-based methods.

### Target State

- **Single tier: PrebuiltDispatch (carrier-only).** Every analyzable chain produces a carrier class.
- **Tier 2 removed.** Dead code deleted.
- **Tier 3 → compile-time error.** The generator emits diagnostic `QRY032` at **Error** severity (upgraded from Info) when a chain cannot be analyzed, directing the user to restructure.
- **Non-carrier Tier 1 gaps → fixed.** `CarrierAnalyzer` is updated to handle all Tier 1 chains (ambiguous projections, etc.) so no chain falls through to a non-carrier path.
- **All runtime builders, state objects, and runtime SQL generators deleted.**
- **`ToSql()` removed.** `ToDiagnostics().Sql` is the single API for SQL inspection.
- **`ModificationExecutor` deleted.** All execution flows through `QueryExecutor.ExecuteCarrier*` methods.
- **`EntityAccessor<T>` deleted.** Generated context methods throw; the carrier (which implements `IEntityAccessor<T>`) is the actual runtime object.

### What Remains

| Component | Purpose |
|-----------|---------|
| `CarrierBase<T>`, `CarrierBase<T, TResult>` | Abstract carrier implementing `IEntityAccessor<T>` + `IQueryBuilder<T>` with throw stubs |
| `JoinedCarrierBase<T1, T2>` (and 3, 4 variants) | Join carrier bases |
| `DeleteCarrierBase<T>`, `UpdateCarrierBase<T>`, `InsertCarrierBase<T>`, `BatchInsertCarrierBase<T>` | Modification carrier bases |
| `QueryExecutor` (carrier methods + shared helpers only) | `ExecuteCarrier*WithCommandAsync` methods |
| `BatchInsertSqlBuilder` | Runtime VALUES clause expansion for batch inserts |
| All interfaces (`IEntityAccessor<T>`, `IQueryBuilder<T>`, etc.) | Public API contracts (unchanged) |
| `QueryDiagnostics`, `DiagnosticsHelper` | Diagnostics infrastructure (QueryDiagnostics expanded in #67 with SqlVariants, ProjectionColumns, Joins, ClauseSourceLocation, etc.) |
| `PreparedQuery<TResult>` (minus `ToSql`) | Multi-terminal prepared query shell (enhanced Prepare() support in #70) |
| `SqlFormatting`, `SqlClauseJoining` | Shared SQL formatting utilities (used by generator + batch insert) |
| `TerminalEmitHelpers` (generator) | Consolidated diagnostic emission: `EmitSqlDispatch`, `EmitParameterLocals`, `EmitDiagnosticParameterArray`, `EmitDiagnosticClauseArray`, `EmitDiagnosticsConstruction`. Created in #67 as single source of truth for diagnostics. |

---

## 2. Files to Delete

### Runtime Builder Classes (~2,737 lines)

| File | Lines | Role |
|------|-------|------|
| `src/Quarry/Query/QueryBuilder.cs` | 1,061 | Mutable SELECT builder with `IQueryBuilder<T>` and `IQueryBuilder<T, TResult>` implementations |
| `src/Quarry/Query/JoinedQueryBuilder.cs` | 1,107 | Mutable JOIN builder with 6 generic variants (2/3/4-table, with/without projection) |
| `src/Quarry/Query/Modification/DeleteBuilder.cs` | 204 | Two-phase DELETE builder (`DeleteBuilder<T>` → `ExecutableDeleteBuilder<T>`) |
| `src/Quarry/Query/Modification/UpdateBuilder.cs` | 261 | Two-phase UPDATE builder (`UpdateBuilder<T>` → `ExecutableUpdateBuilder<T>`) |
| `src/Quarry/Query/Modification/InsertBuilder.cs` | 104 | Single INSERT builder accumulating entity parameters |

### State Objects (~978 lines)

| File | Lines | Role |
|------|-------|------|
| `src/Quarry/Query/QueryState.cs` | 626 | Immutable state container for SELECT queries. Holds `ImmutableArray` of where conditions, order-by clauses, join clauses, parameters, prebuilt fragments. Clone-based mutation. |
| `src/Quarry/Query/Modification/InsertState.cs` | 100 | Mutable state for INSERT: columns, rows (parameter indices), parameters list |
| `src/Quarry/Query/Modification/UpdateState.cs` | 142 | Mutable state for UPDATE: SET clauses, WHERE conditions, parameters, AllowAll flag |
| `src/Quarry/Query/Modification/DeleteState.cs` | 110 | Mutable state for DELETE: WHERE conditions, parameters, AllowAll flag |

### Runtime SQL Generators (~394 lines)

| File | Lines | Role |
|------|-------|------|
| `src/Quarry/Query/SqlBuilder.cs` | 247 | Static `BuildSelectSql(QueryState)` and dead `BuildFromPrequotedFragments(QueryState)`. Thread-static `StringBuilder` cache. |
| `src/Quarry/Query/Modification/SqlModificationBuilder.cs` | 147 | Static `BuildInsertSql(InsertState)`, `BuildUpdateSql(UpdateState)`, `BuildDeleteSql(DeleteState)`, `GetLastInsertIdQuery(SqlDialect)` |

### Runtime Entry Point (~80 lines)

| File | Lines | Role |
|------|-------|------|
| `src/Quarry/Query/EntityAccessor.cs` | 80 | Readonly struct factory that creates runtime builders on demand. Implements `IEntityAccessor<T>`. |

### Executors to Delete (~343 lines)

| File | Lines | Role |
|------|-------|------|
| `src/Quarry/Internal/ModificationExecutor.cs` | 343 | `ExecuteInsertNonQueryAsync`, `ExecuteUpdateNonQueryAsync`, `ExecuteDeleteNonQueryAsync` + prebuilt variants. All carrier paths go through `QueryExecutor` instead. |

### Supporting Types

| File | What to Remove |
|------|---------------|
| `src/Quarry/Query/Modification/ModificationState.cs` | `ModificationParameter` struct — check if carrier-generated code references it. If only used by deleted state objects and `ModificationExecutor`, delete the file. If referenced by generated carrier code for parameter binding, keep. |

### Test Files to Delete

All test files that directly test runtime builders, `QueryState`, `SqlBuilder`, or `SqlModificationBuilder`:

| File | Approx Tests | Reason |
|------|-------------|--------|
| `src/Quarry.Tests/QueryBuilderTests.cs` | ~36 | Tests `QueryBuilder<T>` construction |
| `src/Quarry.Tests/ModificationBuilderTests.cs` | ~36 | Tests `InsertBuilder`, `UpdateBuilder`, `DeleteBuilder` |
| `src/Quarry.Tests/SingleInsertTests.cs` | ~24 | Tests `InsertBuilder<T>` directly |
| `src/Quarry.Tests/JoinOperationsTests.cs` | ~53 | Tests `QueryState` join operations |
| `src/Quarry.Tests/SelectProjectionTests.cs` | ~41 | Tests runtime projection analysis |
| `src/Quarry.Tests/SqlOutput/SelectTests.cs` | ~9 | Tests `QueryState` → `SqlBuilder` |
| `src/Quarry.Tests/SqlOutput/WhereTests.cs` | ~11 | Tests `QueryState` → `SqlBuilder` |
| `src/Quarry.Tests/SqlOutput/OrderByTests.cs` | varies | Tests `QueryState` order-by |
| `src/Quarry.Tests/SqlOutput/JoinTests.cs` | varies | Tests `QueryState` joins |
| `src/Quarry.Tests/SqlOutput/InsertTests.cs` | varies | Tests `InsertState` → `SqlModificationBuilder` |
| `src/Quarry.Tests/SqlOutput/DeleteTests.cs` | varies | Tests `DeleteState` → `SqlModificationBuilder` |
| `src/Quarry.Tests/SqlOutput/UpdateTests.cs` | ~15 | Tests `UpdateState` → `SqlModificationBuilder` |
| `src/Quarry.Tests/SqlOutput/AggregateTests.cs` | ~13 | Tests `QueryState` aggregates |
| `src/Quarry.Tests/SqlOutput/StringOperationTests.cs` | ~11 | Tests `QueryState` string ops |
| `src/Quarry.Tests/SqlOutput/ParameterFormattingTests.cs` | ~30 | Tests runtime parameter formatting |
| `src/Quarry.Tests/SqlOutput/ConditionalChainSqlTests.cs` | ~18 | Tests conditional SQL chains via `QueryState` |
| Other `SqlOutput/*.cs` files using `QueryState` directly | varies | Any remaining files constructing `QueryState` |

**Note:** CrossDialect tests (`CrossDialect*.cs`) use source-generated interceptors and `ToDiagnostics()` — these are **kept unchanged** as they test the carrier path.

---

## 3. Files to Modify

### 3.1 QueryExecutor.cs (src/Quarry/Internal/QueryExecutor.cs)

**Current:** 923 lines with three categories of methods.

**Remove** all methods that accept `QueryState`:

```csharp
// DELETE — Pure runtime (build SQL from state)
ExecuteFetchAllAsync<TResult>(QueryState, Func<DbDataReader, TResult>, CancellationToken)
ExecuteFetchFirstAsync<TResult>(QueryState, Func<DbDataReader, TResult>, CancellationToken)
ExecuteFetchFirstOrDefaultAsync<TResult>(QueryState, Func<DbDataReader, TResult>, CancellationToken)
ExecuteFetchSingleAsync<TResult>(QueryState, Func<DbDataReader, TResult>, CancellationToken)
ExecuteScalarAsync<TScalar>(QueryState, CancellationToken)
ExecuteNonQueryAsync(QueryState, CancellationToken)
ToAsyncEnumerable<TResult>(QueryState, Func<DbDataReader, TResult>, CancellationToken)

// DELETE — Prebuilt SQL + QueryState (non-carrier dispatch table path)
ExecuteWithPrebuiltSqlAsync<TResult>(QueryState, string, Func<DbDataReader, TResult>, CancellationToken)
ExecuteFirstWithPrebuiltSqlAsync<TResult>(QueryState, string, Func<DbDataReader, TResult>, CancellationToken)
ExecuteFirstOrDefaultWithPrebuiltSqlAsync<TResult>(QueryState, string, Func<DbDataReader, TResult>, CancellationToken)
ExecuteSingleWithPrebuiltSqlAsync<TResult>(QueryState, string, Func<DbDataReader, TResult>, CancellationToken)
ExecuteScalarWithPrebuiltSqlAsync<TScalar>(QueryState, string, CancellationToken)
ToAsyncEnumerableWithPrebuiltSql<TResult>(QueryState, string, Func<DbDataReader, TResult>, CancellationToken)
```

**Remove** private helpers that only serve deleted methods:

```csharp
// DELETE — Only used by QueryState paths
PromotePaginationParameters(QueryState)
ExecuteFetchAllCoreAsync<TResult>(QueryState, string, ...)  // if only called by deleted methods
ExecuteFetchFirstCoreAsync<TResult>(QueryState, string, ...)
ExecuteFetchFirstOrDefaultCoreAsync<TResult>(QueryState, string, ...)
ExecuteFetchSingleCoreAsync<TResult>(QueryState, string, ...)
ExecuteScalarCoreAsync<TScalar>(QueryState, string, ...)
ToAsyncEnumerableCore<TResult>(QueryState, string, ...)
LogParameters(long, QueryState)
CreateCommand(DbConnection, string, QueryState, TimeSpan)
```

**Keep** carrier methods:

```csharp
ExecuteCarrierWithCommandAsync<TResult>(long, IQueryExecutionContext, DbCommand, Func<DbDataReader, TResult>, CancellationToken)
ExecuteCarrierFirstWithCommandAsync<TResult>(long, IQueryExecutionContext, DbCommand, Func<DbDataReader, TResult>, CancellationToken)
ExecuteCarrierFirstOrDefaultWithCommandAsync<TResult>(long, IQueryExecutionContext, DbCommand, Func<DbDataReader, TResult>, CancellationToken)
ExecuteCarrierSingleWithCommandAsync<TResult>(long, IQueryExecutionContext, DbCommand, Func<DbDataReader, TResult>, CancellationToken)
ExecuteCarrierScalarWithCommandAsync<TScalar>(long, IQueryExecutionContext, DbCommand, CancellationToken)
ExecuteCarrierNonQueryWithCommandAsync(long, IQueryExecutionContext, DbCommand, CancellationToken)
ToCarrierAsyncEnumerableWithCommandAsync<TResult>(long, IQueryExecutionContext, DbCommand, Func<DbDataReader, TResult>, CancellationToken)
```

**Keep** shared helpers used by carrier methods:

```csharp
CheckSlowQuery(long, IQueryExecutionContext, double, string)
NormalizeParameterValue(object?)
```

### 3.2 PreparedQuery.cs (src/Quarry/Query/PreparedQuery.cs)

**Remove** `ToSql()` method:

```csharp
// DELETE
public string ToSql()
    => throw new NotSupportedException("...");
```

**Keep** all other methods (`ToDiagnostics`, `ExecuteFetchAllAsync`, etc.).

### 3.3 DiagnosticsHelper.cs (src/Quarry/Query/DiagnosticsHelper.cs)

**Remove** `ConvertParameters(ImmutableArray<QueryParameter>)` overload (depends on `QueryState.QueryParameter`).

**Remove** `ConvertParameters(List<ModificationParameter>)` overload if `ModificationParameter` is deleted.

**If both overloads are removed**, delete the file entirely. Generated carrier diagnostics emit `DiagnosticParameter[]` inline without using `DiagnosticsHelper`.

### 3.4 QueryDiagnostics.cs (src/Quarry/Query/QueryDiagnostics.cs)

**Note:** This file was substantially expanded in commit #67 (dad461f). It now contains rich diagnostic types: `SqlVariantDiagnostic`, `ProjectionColumnDiagnostic`, `JoinDiagnostic`, `ClauseSourceLocation`, `DiagnosticBranchKind`, `DiagnosticOptimizationTier`, `DiagnosticQueryKind`, plus 18+ new properties on `QueryDiagnostics` itself.

**Remove** `DiagnosticOptimizationTier.RuntimeBuild` enum value — only `PrebuiltDispatch` remains.

**Remove** `RawState` property if it exposes `QueryState`.

**Audit** `InsertRowCount` property — if it reads from `InsertState`, remove or rewire to carrier data.

### 3.5 OptimizationTier.cs (src/Quarry.Generator/Models/OptimizationTier.cs)

**Remove** `PrequotedFragments` enum value. Only `PrebuiltDispatch` and `RuntimeBuild` remain. (`RuntimeBuild` is kept as a classification — it now triggers a compile error instead of a fallback.)

---

## 4. Generator Changes

### 4.1 Upgrade QRY032 to Error Severity

**File:** `src/Quarry.Generator/DiagnosticDescriptors.cs`

Change `QRY032` (ChainNotAnalyzable) from `DiagnosticSeverity.Info` to `DiagnosticSeverity.Error`. Update the description to remove the phrase "The existing runtime SqlBuilder path will be used" and replace with guidance to restructure the query.

### 4.2 Fix CarrierAnalyzer for All Tier 1 Chains

**File:** `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs`

Currently, `AnalyzeNew` rejects chains for:
1. `RuntimeBuild` tier → keep as rejection (these are now compile errors)
2. Unmatched methods → emit error diagnostic instead of silent ineligibility
3. Empty SQL variants → emit error diagnostic
4. Malformed SQL → emit error diagnostic

For conditions 2-4, either:
- Fix the root cause so these chains produce valid carrier plans, OR
- Report a compile-time error diagnostic explaining the issue

**File:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

`WouldExecutionTerminalBeEmitted` currently returns `false` for:
- Unmatched method names → should now be a compile error (already caught)
- Cannot resolve result type → fix projection resolution or emit error
- `ReaderDelegateCode` is null → fix reader code generation or emit error
- Ambiguous projection columns → fix column disambiguation

Each failure condition must be addressed: either make it work or make it a clear compile error. No silent fallthrough.

### 4.3 Remove Non-Carrier Emission Paths

**File:** `src/Quarry.Generator/CodeGen/FileEmitter.cs` (801 lines)

The `EmitInterceptorMethod` method has three-way routing: carrier path, non-carrier prebuilt path, and runtime path. Remove the non-carrier and runtime paths:

- Remove the `chainLookup` / `chainClauseLookup` dictionaries (non-carrier lookup). All chains use `carrierLookup` / `carrierClauseLookup`.
- Remove fallthrough logic where `isCarrierSite == false` delegates to non-carrier emitters.
- Remove the `EmitRuntimeDiagnosticsTerminal` fallback in the `ToDiagnostics` case.
- Remove the `ToSql` case entirely from the dispatch switch.

**File:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` (1,010 lines, 13 methods)

Current methods (post-#67 refactor):
1. `EmitReaderTerminal` — has carrier and non-carrier branches
2. `EmitJoinReaderTerminal` — has carrier and non-carrier branches
3. `EmitNonQueryTerminal` — has carrier and non-carrier branches
4. `EmitDiagnosticsTerminal` — delegates to `CarrierEmitter.EmitCarrierToDiagnosticsTerminal` or falls back to `EmitRuntimeDiagnosticsTerminal`
5. `EmitRuntimeDiagnosticsTerminal` — **DELETE entirely** (runtime builder fallback)
6. `EmitInsertNonQueryTerminal` — has carrier and non-carrier branches
7. `EmitInsertScalarTerminal` — has carrier and non-carrier branches
8. `EmitInsertDiagnosticsTerminal` — has carrier and non-carrier branches
9. `EmitBatchInsertNonQueryTerminal` — carrier path
10. `EmitBatchInsertScalarTerminal` — carrier path
11. `EmitBatchInsertDiagnosticsTerminal` — carrier path
12. `EmitToSqlTerminal` — **DELETE entirely** (ToSql removal)
13. `EmitPrepareInterceptor` — keep

Simplify methods 1-4, 6-8 to remove `carrier: null` branches. Every terminal method can assume `carrier != null`.

**File:** `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` (660 lines, created in #67)

This file consolidates diagnostic emission as a single source of truth. Key methods:
- `EmitSqlDispatch` — SQL variant dispatch (const or mask switch)
- `EmitParameterLocals` — extract `__pVal*` from carrier fields
- `EmitCollectionExpansion` — expand collection parameter tokens in SQL
- `EmitDiagnosticParameterArray` — build `DiagnosticParameter[]` inline
- `EmitDiagnosticClauseArray` — build `ClauseDiagnostic[]` with source locations
- `EmitDiagnosticsConstruction` — central `QueryDiagnostics` constructor call
- `GetParameterValueExpression` — inline value expression by type classification

**No methods in this file need deletion.** All are carrier-path-only. However, audit for any references to runtime builder types (e.g., `EntityAccessor`, `QueryBuilder`) that may need cleanup.

**File:** `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs` (363 lines, slimmed in #67)

Post-#67, `EmitDiagnosticParameterArray` and `EmitDiagnosticClauseArray` are thin wrappers delegating to `TerminalEmitHelpers`. `GenerateDispatchTable` still exists here. Audit for runtime builder type references.

**File:** `src/Quarry.Generator/CodeGen/ClauseBodyEmitter.cs`

Remove the non-carrier clause paths:
- Path 2 (prebuilt non-carrier): Calls `AllocatePrebuiltParams()`, `BindParam()`, `AddWhereClause()` on the builder. This entire path is deleted.
- Path 3 (runtime): Calls builder runtime methods. This entire path is deleted.
- Only the carrier path (Path 1) remains: `EmitCarrierClauseBody`.

### 4.4 Remove ToSql from Generator Pipeline

**File:** `src/Quarry.Generator/Models/InterceptorKind.cs`

Remove `ToSql` enum value.

**File:** `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`

Remove `"ToSql"` → `InterceptorKind.ToSql` mapping from `InterceptableMethods` dictionary. Remove `ToSql` from `IsTerminalOrTransitionMethodName()` and `PreparedQueryTerminal` classification.

**File:** `src/Quarry.Generator/CodeGen/InterceptorRouter.cs`

Remove `InterceptorKind.ToSql` from the `Categorize` switch (currently maps to `EmitterCategory.Terminal`).

**File:** `src/Quarry.Generator/CodeGen/FileEmitter.cs`

Remove the `case InterceptorKind.ToSql:` block from the main dispatch switch.

### 4.5 Remove EntityAccessor from ContextCodeGenerator

**File:** `src/Quarry.Generator/Generation/ContextCodeGenerator.cs`

Change the generated entity accessor method body from:

```csharp
public partial IEntityAccessor<User> Users()
    => new EntityAccessor<User>(_dialect, "users", _schemaName, (IQueryExecutionContext)this);
```

To:

```csharp
public partial IEntityAccessor<User> Users()
    => throw new NotSupportedException("Entity accessor methods must be intercepted by the Quarry source generator.");
```

This removes the dependency on `EntityAccessor<T>`. The ChainRoot interceptor always replaces the call site, so this body is never executed.

### 4.6 Remove Tier 2 References

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

The tier classification logic at lines 310-316:

```csharp
if (totalBits <= MaxTier1Bits)
    tier = OptimizationTier.PrebuiltDispatch;
else
    tier = OptimizationTier.PrequotedFragments;
```

Change the `else` branch. With Tier 2 removed, chains with >4 conditional bits need a decision:
- Option A: Raise the `MaxTier1Bits` limit (allow larger dispatch tables).
- Option B: Classify >4 bits as `RuntimeBuild` (compile error). This forces users to reduce conditional complexity.

**Recommendation:** Raise `MaxTier1Bits` to a reasonable limit (e.g., 8 = 256 variants, or 6 = 64 variants). Beyond that, classify as `RuntimeBuild` with a clear diagnostic. The dispatch table size grows as 2^N, so a practical cap prevents code bloat.

**File:** `src/Quarry.Generator/QuarryGenerator.cs`

Remove the `QRY031` (ChainOptimizedTier2) diagnostic emission for `PrequotedFragments` chains.

**File:** `src/Quarry/Query/QueryPlan.cs`

Remove `QueryPlanTier.PrequotedFragments` enum value.

### 4.7 Remove Runtime Builder References from Generator Emitters

Several emitter methods cast to concrete runtime builder types. These must be removed:

**TerminalBodyEmitter.cs:**
- `EmitRuntimeDiagnosticsTerminal`: Casts to `EntityAccessor<T>`, `ExecutableDeleteBuilder<T>`, `ExecutableUpdateBuilder<T>`, `InsertBuilder<T>`, `QueryBuilder<T>`, `JoinedQueryBuilder<T>`. **Delete entire method.**
- `EmitToSqlTerminal`: Same casts. **Delete entire method.**
- Non-carrier paths in `EmitReaderTerminal`, `EmitJoinReaderTerminal`, `EmitNonQueryTerminal`: Cast to `QueryBuilder<T>`, `JoinedQueryBuilder<T>`, `ExecutableDeleteBuilder<T>`, `ExecutableUpdateBuilder<T>`. **Remove non-carrier branches; carrier path remains.**

**ClauseBodyEmitter.cs:**
- Non-carrier prebuilt path calls `builder.AllocatePrebuiltParams()`, `builder.BindParam()`, `builder.AddWhereClause()` — methods defined on runtime builders. **Delete this path.**
- Runtime path calls `builder.Where(...)`, `builder.OrderBy(...)` etc. on runtime builders. **Delete this path.**

**CarrierEmitter.cs** (989 lines, 26 methods post-#67):
- `EmitCarrierChainEntry`: Currently references `__b.State.ExecutionContext` where `__b` is a runtime builder. After removal, the chain entry interceptor creates the carrier from the context directly. The generated ChainRoot interceptor already passes `ctx` (the QuarryContext). Verify that carrier chain entry does NOT reference `State` on a builder — if it does, change to get `ExecutionContext` from the context parameter.
- `EmitCarrierToDiagnosticsTerminal`: Now delegates to `TerminalEmitHelpers` for heavy lifting (refactored in #67). Should be clean of runtime builder references but audit.
- `GetJoinedConcreteBuilderTypeName`: May reference `JoinedQueryBuilder` types — audit and remove if so.
- `ResolveCarrierReceiverType`: May reference concrete builder types — audit and remove references.

**TerminalEmitHelpers.cs** (660 lines, created in #67):
- Audit for any runtime builder type name strings. This file should be carrier-only but may contain references inherited from the pre-refactor code.

**InterceptorCodeGenerator.cs** (363 lines, slimmed in #67):
- `GenerateDispatchTable`: Should be clean but audit.
- Delegating methods (`EmitDiagnosticParameterArray`, `EmitDiagnosticClauseArray`): Thin wrappers to TerminalEmitHelpers, should be clean.
- `GetJoinedBuilderTypeName`: May reference `JoinedQueryBuilder` — audit.
- `GetColumnValueExpression`, `EmitInsertColumnSetup`, `EmitInsertEntityBindings`: Audit for runtime builder references.

### 4.8 Remove References in Projection/Translation

**File:** `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

Contains `TranslateStringMethodToSql()` and `TranslateSubstringToSql()` — these are internal SQL translation helpers unrelated to the `.ToSql()` API. **No changes needed.**

**File:** `src/Quarry.Generator/CodeGen/InterceptorCodeGenerator.Utilities.cs`

References `EntityAccessor<T>` for casting. Remove these references.

---

## 5. ToSql Removal — Test Migration

### Files with ToSql Call Sites

| File | Occurrences | Migration |
|------|------------|-----------|
| `src/Quarry.Tests/SqlOutput/PrepareTests.cs` | 7 | Replace `prepared.ToSql()` → `prepared.ToDiagnostics().Sql` |
| `src/Quarry.Tests/Integration/PrepareIntegrationTests.cs` | 3 | Replace `prepared.ToSql()` → `prepared.ToDiagnostics().Sql` |

### Generator Test Files

Any tests that verify `InterceptorKind.ToSql` emission or `EmitToSqlTerminal` behavior. Search for `ToSql` in:
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`
- `src/Quarry.Tests/GeneratorTests.cs`

Remove or update these tests to reflect that `ToSql` is no longer a recognized interceptor kind.

### Other References

Files referencing `ToSql` in comments or strings:
- `src/Quarry.Tests/SqlOutput/VariableStoredChainTests.cs`
- `src/Quarry.Tests/SqlOutput/CrossDialectBatchInsertTests.cs`
- `src/Quarry.Tests/SqlOutput/EndToEndSqlTests.cs`

Audit each for actual `.ToSql()` calls vs. comment references. Migrate or remove accordingly.

---

## 6. Implementation Order

### Step 1: Create Branch

```
git checkout -b feature/remove-runtime-builders
```

### Step 2: Remove ToSql from Public API and Generator

Low-risk, isolated change. Validates that the generator pipeline works without `ToSql`.

1. Remove `ToSql()` from `PreparedQuery<TResult>`
2. Remove `InterceptorKind.ToSql` and all generator routing/emission
3. Migrate 10 test call sites from `.ToSql()` → `.ToDiagnostics().Sql`
4. **Run tests — verify green**

### Step 3: Remove Tier 2 Dead Code

1. Remove `OptimizationTier.PrequotedFragments`
2. Update `ChainAnalyzer` tier classification (raise `MaxTier1Bits` or classify as `RuntimeBuild`)
3. Remove `QueryPlanTier.PrequotedFragments`
4. Remove `DiagnosticOptimizationTier.RuntimeBuild` if applicable (or keep for diagnostics reporting)
5. Remove `QRY031` diagnostic
6. Remove `SqlBuilder.BuildFromPrequotedFragments` and fragment properties from `QueryState` (cleaned up in Step 5 when QueryState is deleted)
7. **Run tests — verify green**

### Step 4: Upgrade QRY032 to Error + Fix Carrier Gaps

1. Change `QRY032` severity to `Error`, update message
2. Fix `CarrierAnalyzer.AnalyzeNew` to handle all Tier 1 chains:
   - Investigate each ineligibility condition
   - Fix root causes so all Tier 1 chains produce valid carrier plans
3. Fix `CarrierEmitter.WouldExecutionTerminalBeEmitted` failure conditions
4. **Run tests — verify green**

### Step 5: Remove Non-Carrier Emission Paths from Generator

1. Remove non-carrier dispatch paths from `FileEmitter.EmitInterceptorMethod`
2. Remove non-carrier branches from `TerminalBodyEmitter` (reader, join reader, non-query, insert terminals)
3. Remove non-carrier paths from `ClauseBodyEmitter`
4. Delete `TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal`
5. Delete `TerminalBodyEmitter.EmitToSqlTerminal` (already removed in Step 2, confirm deletion)
6. Remove runtime builder type references from all emitter code
7. **Run tests — verify green**

### Step 6: Remove EntityAccessor and Update Context Generator

1. Update `ContextCodeGenerator` to emit `throw new NotSupportedException(...)` instead of `new EntityAccessor<T>(...)`
2. Delete `src/Quarry/Query/EntityAccessor.cs`
3. Remove `EntityAccessor` references from `InterceptorCodeGenerator.Utilities.cs`
4. **Run tests — verify green**

### Step 7: Delete Runtime Builders and State Objects

1. Delete runtime builder files:
   - `QueryBuilder.cs`, `JoinedQueryBuilder.cs`
   - `DeleteBuilder.cs`, `UpdateBuilder.cs`, `InsertBuilder.cs`
2. Delete state objects:
   - `QueryState.cs`, `InsertState.cs`, `UpdateState.cs`, `DeleteState.cs`
3. Delete SQL generators:
   - `SqlBuilder.cs`, `SqlModificationBuilder.cs`
4. Delete or clean up `ModificationState.cs` (contains `ModificationParameter`)
5. Fix any remaining compilation errors from deleted types
6. **Run tests — expect failures from deleted test dependencies**

### Step 8: Delete Runtime Builder Tests

1. Delete all test files listed in Section 2 (test files to delete)
2. Delete any remaining test files that fail due to missing `QueryState`, `SqlBuilder`, etc.
3. **Run tests — verify green**

### Step 9: Simplify QueryExecutor

1. Remove all `QueryState`-accepting methods (runtime + prebuilt SQL paths)
2. Remove private helpers only used by deleted methods
3. Remove `using` statements for deleted types
4. **Run tests — verify green**

### Step 10: Delete ModificationExecutor

1. Delete `src/Quarry/Internal/ModificationExecutor.cs`
2. Remove any remaining references
3. **Run tests — verify green**

### Step 11: Clean Up DiagnosticsHelper and QueryDiagnostics

1. Remove `DiagnosticsHelper` overloads for deleted types (or delete file entirely)
2. Clean up `QueryDiagnostics` — remove `RawState` if it exposed `QueryState`, clean up `DiagnosticOptimizationTier`
3. **Run tests — verify green**

### Step 12: Final Audit

1. Search for any remaining references to deleted types across the entire codebase
2. Search for `QueryState`, `InsertState`, `UpdateState`, `DeleteState`, `SqlBuilder`, `SqlModificationBuilder`, `ModificationExecutor`, `EntityAccessor`, `ToSql` in all `.cs` files
3. Clean up `using` statements, dead code, orphaned files
4. **Run full test suite — verify green**
5. Review all changes for namespace issues, visibility mismatches, missing call sites

---

## 7. Risk Mitigation

### Incremental Validation

Each step ends with "run tests — verify green." Never proceed to the next step with failing tests.

### Rollback Safety

Steps 2-6 are purely subtractive on generator emission paths and can be reverted independently. Step 7 (deleting runtime builders) is the point of no return — after this, the non-carrier paths are structurally impossible.

### Coverage Gaps

Before deleting test files in Step 8, audit each file's tests against existing CrossDialect coverage. If any edge case is not covered by CrossDialect tests, note it. The user has confirmed that CrossDialect tests are sufficient, but a final audit prevents regressions.

### Carrier Analyzer Fixes (Step 4)

This is the highest-risk step. If a Tier 1 chain cannot be made carrier-eligible without substantial generator changes, the fallback is to emit a compile error diagnostic for that specific pattern. The user has requested fixing the analyzer, but individual patterns may need to be escalated.

---

## 8. Key Concepts Reference

### Carrier Chain

A generated sealed class that holds typed parameter fields and implements the query builder interfaces via `Unsafe.As` casting. The source generator emits interceptors for every method call in a chain. Clause interceptors bind parameters to carrier fields and set bits on a clause mask. The terminal interceptor selects pre-built SQL by mask value, creates a `DbCommand`, binds parameters, and calls `QueryExecutor.ExecuteCarrier*WithCommandAsync`.

### Dispatch Table

A switch expression mapping `ClauseMask` values to pre-built SQL string literals. For a chain with N conditional clauses, there are up to 2^N variants. Example with 2 conditional clauses (4 variants):

```csharp
var sql = __c.Mask switch {
    0 => "SELECT * FROM users",
    1 => "SELECT * FROM users WHERE IsActive = 1",
    2 => "SELECT * FROM users ORDER BY Name",
    3 => "SELECT * FROM users WHERE IsActive = 1 ORDER BY Name",
};
```

### Clause Mask

A `byte` / `ushort` / `uint` (sized by conditional bit count) on the carrier. Each conditional clause occupies one bit. When a conditional clause interceptor fires, it sets its bit via `__c.Mask |= (1 << bitIndex)`. The terminal reads `__c.Mask` to select the correct SQL variant.

### ChainRoot Interceptor

The first interceptor in a chain. Replaces the context's entity accessor factory call. Creates a new carrier instance, sets `Ctx` from the context, and returns it cast as `IEntityAccessor<T>`.

### PreparedQuery

A sealed class returned by `.Prepare()`. Allows calling multiple terminals (e.g., `ToDiagnostics()` then `ExecuteFetchAllAsync()`) on the same compiled chain without re-evaluation. The source generator intercepts `Prepare()` and returns the carrier cast as `PreparedQuery<TResult>` via `Unsafe.As`. Terminals on `PreparedQuery` are separately intercepted.

### QueryDiagnostics

Rich diagnostic object returned by `ToDiagnostics()`. Contains: SQL string, parameters with metadata, clause breakdown, optimization tier, carrier class name, projection columns, join info, pagination values, SQL variants map, and source locations. Generated carrier code builds this inline.

---

## 9. Affected Namespaces and Assemblies

### Quarry (runtime library)

- `Quarry.Query` — Delete: `QueryBuilder`, `JoinedQueryBuilder`, `EntityAccessor`, `QueryState`, `SqlBuilder`. Modify: `PreparedQuery`, `QueryDiagnostics`, `DiagnosticsHelper`.
- `Quarry.Query.Modification` — Delete: `DeleteBuilder`, `UpdateBuilder`, `InsertBuilder`, `SqlModificationBuilder`, `InsertState`, `UpdateState`, `DeleteState`. Possibly delete: `ModificationState` (contains `ModificationParameter`).
- `Quarry.Internal` — Delete: `ModificationExecutor`. Modify: `QueryExecutor`. Keep: `CarrierBase`, `JoinedCarrierBase*`, `ModificationCarrierBase`, `BatchInsertSqlBuilder`.

### Quarry.Generator (source generator)

- `Quarry.Generator.Models` — Modify: `InterceptorKind` (remove `ToSql`), `OptimizationTier` (remove `PrequotedFragments`).
- `Quarry.Generator.CodeGen` — Modify: `FileEmitter` (801 lines), `TerminalBodyEmitter` (1,010 lines), `ClauseBodyEmitter`, `CarrierEmitter` (989 lines), `CarrierAnalyzer` (188 lines), `InterceptorRouter`, `TerminalEmitHelpers` (660 lines, new in #67). Audit: `InterceptorCodeGenerator` (363 lines, slimmed in #67).
- `Quarry.Generator.Parsing` — Modify: `UsageSiteDiscovery` (remove ToSql), `ChainAnalyzer` (remove Tier 2).
- `Quarry.Generator.Generation` — Modify: `ContextCodeGenerator` (EntityAccessor removal).
- `Quarry.Generator.IR` — Audit: `SqlAssembler` (remove RuntimeBuild early return comments if desired).
- `Quarry.Generator` — Modify: `DiagnosticDescriptors` (QRY032 severity), `QuarryGenerator` (remove QRY031).

### Quarry.Tests

- Delete ~19 test files that test runtime builders/state/SQL generators directly.
- Modify `PrepareTests.cs` and `PrepareIntegrationTests.cs` (ToSql → ToDiagnostics().Sql).
- Modify generator tests that verify ToSql emission or RuntimeBuild fallback behavior.
