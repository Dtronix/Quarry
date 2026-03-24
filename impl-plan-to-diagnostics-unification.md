# Implementation Plan: ToDiagnostics Unification & Compiler-Sourced Diagnostics

**Issue:** #60 — Compiler-Sourced QueryDiagnostics Improvements
**Branch:** `feature/compiler-sourced-query-diagnostics-60`
**Prerequisite:** All tests passing on current baseline.

## 1. Goals

This plan merges two related changes into a single coordinated effort:

**A. Emitter unification** — Eliminate the non-carrier ToDiagnostics code path. All ToDiagnostics chains go through the carrier, sharing SQL dispatch and parameter binding code with execution terminals. This prevents drift between diagnostic output and execution output (masks, bindings, clause metadata).

**B. Expanded diagnostic surface** — The compiler already computes SQL variants, optimization tier, carrier eligibility, parameter metadata, conditional masks, clause breakdowns, projection metadata, join metadata, and disqualification reasons. Currently only a subset is emitted. This plan makes `QueryDiagnostics` the single canonical debugging surface carrying everything the compiler knows.

**C. ToSql removal** — Remove `ToSql()` from all public interfaces. Replace all call sites with `ToDiagnostics().Sql`. Eliminate `InterceptorKind.BatchInsertToSql` and its emitter.

### Core Principle

The compiler emits literal constants for all diagnostic data. The only runtime computation is mask evaluation for conditional chains and parameter value extraction from carrier fields. The same shared emitter helpers produce SQL dispatch and parameter binding for both execution terminals and diagnostic terminals. No separate emitter paths that can drift.

## 2. Constraints

- **netstandard2.0 target** — No `record` types, no `init` properties.
- **Additive public API** — `QueryDiagnostics` changes add new properties. No removed properties. Existing constructor call sites remain compatible via default parameter values.
- **All query kinds** — ToDiagnostics must work for Select, Delete, Update, Insert, BatchInsert.
- **All optimization tiers** — PrebuiltDispatch and RuntimeBuild. (PrequotedFragments deferred — no test chains currently produce this tier.)
- **Runtime builders are temporary** — Runtime SQL builder classes (`QueryBuilder`, `DeleteBuilder`, etc.) will be removed in a future phase. Minimal changes to their internals.
- **Parameter sensitivity** — `IsSensitive` flag marks parameters containing sensitive data. Diagnostic output must respect this.

## 3. Current Architecture

### Emitter Paths for ToDiagnostics

There are currently **five** separate ToDiagnostics emitter entry points, split across carrier and non-carrier paths:

| Entry Point | File | Carrier? | Query Kind |
|---|---|---|---|
| `EmitDiagnosticsTerminal` | TerminalBodyEmitter:343 | No (falls through to carrier if eligible) | Select/Delete/Update |
| `EmitCarrierToDiagnosticsTerminal` | CarrierEmitter:903 | Yes | Select/Delete/Update |
| `EmitRuntimeDiagnosticsTerminal` | TerminalBodyEmitter:463 | No | RuntimeBuild fallback |
| `EmitInsertDiagnosticsTerminal` | TerminalBodyEmitter:633 | Falls through to carrier | Insert |
| `EmitCarrierInsertToDiagnosticsTerminal` | CarrierEmitter:874 | Yes | Insert |
| `EmitBatchInsertDiagnosticsTerminal` | TerminalBodyEmitter | Falls through to carrier | BatchInsert |
| `EmitBatchInsertToSqlTerminal` | TerminalBodyEmitter:745 | Falls through to carrier | BatchInsert (ToSql) |

### Duplicated Code Between Paths

The following logic is duplicated between carrier and non-carrier diagnostic emitters:

- **SQL dispatch table** — `InterceptorCodeGenerator.GenerateDispatchTable()` (non-carrier) vs inline mask switch in `EmitCarrierToDiagnosticsTerminal` (carrier). Both produce `const string sql` or `var sql = mask switch { ... }` but from different code paths.
- **Clause diagnostic array** — `EmitNonCarrierDiagnosticClauseArray()` (compile-time only, no collection expansion) vs `EmitDiagnosticClauseArray()` (carrier-aware, reads mask from carrier, handles collection expansion).
- **Parameter diagnostic array** — Non-carrier emits `Array.Empty<DiagnosticParameter>()` (no access to values). Carrier emits full `DiagnosticParameter[]` via `EmitDiagnosticParameterArray()` reading carrier fields.
- **Method signature resolution** — Both paths independently resolve `thisParamType` and `concreteParamType` from entity types, join types, and result types.
- **QueryDiagnostics construction** — Both paths construct the final `new QueryDiagnostics(...)` call with overlapping but subtly different argument lists.

### Carrier Eligibility Gate for ToDiagnostics

`CarrierAnalyzer.AnalyzeNew()` (line 73-80) has a special gate that makes trivial ToDiagnostics chains ineligible for carrier optimization:

```csharp
if (kind == InterceptorKind.ToDiagnostics
    && plan.Parameters.Count == 0
    && plan.ConditionalTerms.Count == 0
    && plan.WhereTerms.Count == 0
    && plan.SetTerms.Count == 0)
    return CarrierPlan.Ineligible("trivial ToDiagnostics chain");
```

This gate produces the 259 non-carrier PrebuiltDispatch interceptor sites found in the test suite — all trivial ToDiagnostics chains with no parameters or conditions.

### Data Available to Emitters

All metadata flows directly from IR types. No bridge or conversion layer exists. Key sources per chain:

- `AssembledPlan.SqlVariants` — `Dictionary<ulong, AssembledSqlVariant>` (SQL string + parameter count per mask)
- `AssembledPlan.Plan.Tier` / `.NotAnalyzableReason` — optimization tier and disqualification reason
- `AssembledPlan.Plan.ConditionalTerms` — conditional clause bit indices and branch kinds
- `AssembledPlan.Plan.PossibleMasks` — all valid mask combinations
- `AssembledPlan.Plan.Parameters` — `IReadOnlyList<QueryParameter>` with type, value expression, sensitivity, enum, collection, TypeMappingClass
- `AssembledPlan.Plan.Projection` — `SelectProjection` with columns, kind, IsOptimalPath, NonOptimalReason
- `AssembledPlan.Plan.Joins` — `IReadOnlyList<JoinPlan>` with table, kind, ON condition
- `AssembledPlan.Plan.IsDistinct`, `.Pagination` — flags and literal/parameter limit/offset
- `AssembledPlan.ClauseSites` — `IReadOnlyList<TranslatedCallSite>` with FilePath, Line, Column per clause
- `AssembledPlan.ExecutionSite.InsertInfo` — identity column, insert columns
- `CarrierPlan.IsEligible`, `.IneligibleReason`, `.ClassName` — carrier metadata

### What Is NOT Emitted Today

The compiler knows but does not currently surface in `QueryDiagnostics`:

- Tier classification reason / disqualification reason
- Carrier ineligibility reason
- Full SQL variants map (only the active variant is returned)
- Per-variant parameter count
- Parameter types, sensitivity flags, enum metadata, TypeMapping class
- Conditional bit index per clause, branch kind (Independent vs MutuallyExclusive)
- Projection column metadata (names, types, ordinals, TypeMapping, FK info)
- Projection non-optimal reason
- IsDistinct flag
- Pagination metadata (literal or parameter-sourced Limit/Offset)
- Insert identity column name
- Unmatched method names
- Clause source locations (file, line, column)
- Schema name

## 4. Target Architecture

### Single Emitter Path

After this change, all carrier-eligible ToDiagnostics chains use the carrier path. The trivial ToDiagnostics gate is removed — even `db.Users().ToDiagnostics()` gets a carrier. The non-carrier diagnostic emitter code paths are deleted.

Remaining paths:

| Path | When | Emitter |
|---|---|---|
| Carrier ToDiagnostics | All PrebuiltDispatch chains | Shared helpers in `TerminalEmitHelpers` called by `CarrierEmitter` |
| Runtime ToDiagnostics | RuntimeBuild chains only | `TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal` (wraps runtime result with compiler metadata) |

### Shared Emitter Helpers

A new internal static class `TerminalEmitHelpers` extracts the shared logic used by both execution terminals and diagnostic terminals. Both `CarrierEmitter` (for execution and diagnostic terminals) and `TerminalBodyEmitter` delegate to these helpers.

Shared helpers:

```csharp
internal static class TerminalEmitHelpers
{
    // SQL dispatch: emits const string or mask switch from SqlVariants
    internal static void EmitSqlDispatch(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);

    // Parameter value locals: emits var __pVal0 = (object?)__c.P0 ?? DBNull.Value
    internal static void EmitParameterLocals(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);

    // Collection expansion: emits __col0 loop and sql.Replace()
    internal static void EmitCollectionExpansion(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);

    // Diagnostic parameter array: emits DiagnosticParameter[] from carrier fields
    internal static void EmitDiagnosticParameterArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);

    // Diagnostic clause array: emits ClauseDiagnostic[] with per-clause params and metadata
    internal static void EmitDiagnosticClauseArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);

    // QueryDiagnostics construction: emits new QueryDiagnostics(...) with all fields
    internal static void EmitDiagnosticsConstruction(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier);
}
```

Execution terminals call `EmitSqlDispatch` + `EmitParameterLocals` + `EmitCollectionExpansion` then proceed to DbCommand binding. Diagnostic terminals call the same three, then call `EmitDiagnosticParameterArray` + `EmitDiagnosticClauseArray` + `EmitDiagnosticsConstruction`. The SQL dispatch and parameter locals are identical — same code path, zero drift.

### Carrier ReadOnly Optimization

The carrier preamble currently detects ToDiagnostics and skips `Ctx` initialization since no database connection is needed. This optimization is preserved. The carrier's `EmitCarrierPreamble` checks `isReadOnly` (true for ToDiagnostics terminals) and omits the `Ctx = __b.State.ExecutionContext` assignment.

## 5. Expanded QueryDiagnostics Type

### 5.1 New Properties on QueryDiagnostics

```csharp
public sealed class QueryDiagnostics
{
    // Existing (unchanged)
    public string Sql { get; }
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }
    public DiagnosticOptimizationTier Tier { get; }
    public bool IsCarrierOptimized { get; }
    public DiagnosticQueryKind Kind { get; }
    public SqlDialect Dialect { get; }
    public string TableName { get; }
    public IReadOnlyList<ClauseDiagnostic> Clauses { get; }

    // New
    public string? TierReason { get; }
    public string? DisqualifyReason { get; }
    public ulong ActiveMask { get; }
    public int ConditionalBitCount { get; }
    public IReadOnlyDictionary<ulong, SqlVariantDiagnostic>? SqlVariants { get; }
    public IReadOnlyList<DiagnosticParameter> AllParameters { get; }
    public IReadOnlyList<ProjectionColumnDiagnostic>? ProjectionColumns { get; }
    public string? ProjectionKind { get; }
    public string? ProjectionNonOptimalReason { get; }
    public string? CarrierClassName { get; }
    public string? CarrierIneligibleReason { get; }
    public string? SchemaName { get; }
    public IReadOnlyList<JoinDiagnostic>? Joins { get; }
    public bool IsDistinct { get; }
    public int? Limit { get; }
    public int? Offset { get; }
    public string? IdentityColumnName { get; }
    public IReadOnlyList<string>? UnmatchedMethodNames { get; }
}
```

**Key semantics:**
- `Parameters` — active parameters only (filtered by mask for conditional chains, derived from active clauses).
- `AllParameters` — every parameter in the chain regardless of mask state, with full metadata.
- `SqlVariants` — complete map of all possible mask values to their SQL strings and parameter counts. Null for RuntimeBuild chains.
- `ActiveMask` — the runtime mask value read from the carrier. Zero for unconditional chains.
- `TierReason` — human-readable explanation of tier classification.
- `DisqualifyReason` — why the chain is RuntimeBuild (null for PrebuiltDispatch).
- `CarrierIneligibleReason` — why carrier optimization was not used (null when carrier-optimized). After unification, this will only be non-null for RuntimeBuild chains.

### 5.2 Expanded DiagnosticParameter

```csharp
public sealed class DiagnosticParameter
{
    // Existing
    public string Name { get; }
    public object? Value { get; }

    // New
    public string? TypeName { get; }
    public string? TypeMappingClass { get; }
    public bool IsSensitive { get; }
    public bool IsEnum { get; }
    public bool IsCollection { get; }
    public bool IsConditional { get; }
    public int? ConditionalBitIndex { get; }
}
```

### 5.3 Expanded ClauseDiagnostic

```csharp
public sealed class ClauseDiagnostic
{
    // Existing
    public string ClauseType { get; }
    public string SqlFragment { get; }
    public bool IsConditional { get; }
    public bool IsActive { get; }
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }

    // New
    public ClauseSourceLocation? SourceLocation { get; }
    public int? ConditionalBitIndex { get; }
    public DiagnosticBranchKind? BranchKind { get; }
}
```

### 5.4 New Diagnostic Types

```csharp
public sealed class SqlVariantDiagnostic
{
    public string Sql { get; }
    public int ParameterCount { get; }
}

public sealed class ProjectionColumnDiagnostic
{
    public string PropertyName { get; }
    public string ColumnName { get; }
    public string ClrType { get; }
    public int Ordinal { get; }
    public bool IsNullable { get; }
    public string? TypeMappingClass { get; }
    public bool IsForeignKey { get; }
    public string? ForeignKeyEntityName { get; }
    public bool IsEnum { get; }
}

public sealed class JoinDiagnostic
{
    public string TableName { get; }
    public string? SchemaName { get; }
    public string JoinKind { get; }
    public string Alias { get; }
    public string OnConditionSql { get; }
}

public sealed class ClauseSourceLocation
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
}

public enum DiagnosticBranchKind
{
    Independent,
    MutuallyExclusive
}
```

### 5.5 Expanded Constructor

All new parameters have defaults so existing call sites are source-compatible.

```csharp
internal QueryDiagnostics(
    // Existing
    string sql,
    IReadOnlyList<DiagnosticParameter> parameters,
    DiagnosticQueryKind kind,
    SqlDialect dialect,
    string tableName,
    DiagnosticOptimizationTier tier = DiagnosticOptimizationTier.RuntimeBuild,
    bool isCarrierOptimized = false,
    IReadOnlyList<ClauseDiagnostic>? clauses = null,
    object? rawState = null,
    int insertRowCount = 0,
    // New
    string? tierReason = null,
    string? disqualifyReason = null,
    ulong activeMask = 0,
    int conditionalBitCount = 0,
    IReadOnlyDictionary<ulong, SqlVariantDiagnostic>? sqlVariants = null,
    IReadOnlyList<DiagnosticParameter>? allParameters = null,
    IReadOnlyList<ProjectionColumnDiagnostic>? projectionColumns = null,
    string? projectionKind = null,
    string? projectionNonOptimalReason = null,
    string? carrierClassName = null,
    string? carrierIneligibleReason = null,
    string? schemaName = null,
    IReadOnlyList<JoinDiagnostic>? joins = null,
    bool isDistinct = false,
    int? limit = null,
    int? offset = null,
    string? identityColumnName = null,
    IReadOnlyList<string>? unmatchedMethodNames = null);
```

## 6. Tier Reason Algorithm

The `TierReason` string is constructed at emit time from `AssembledPlan` metadata:

- **PrebuiltDispatch, no conditionals** — `"unconditional chain, single SQL variant"`
- **PrebuiltDispatch, with conditionals** — `"{N} conditional bits, {M} mask variants"`
- **RuntimeBuild** — use `QueryPlan.NotAnalyzableReason` directly (already a human-readable string)

## 7. Work Breakdown

### Phase 1: Remove ToSql (Public Surface)

**Step 1.1 — Remove ToSql from interfaces**

Remove `string ToSql()` declarations from all public builder interfaces.

Interfaces affected:
- `IQueryBuilder<T>` — `src/Quarry/Query/IQueryBuilder.cs:91`
- `IQueryBuilder<T, TResult>` — `src/Quarry/Query/IQueryBuilder.cs:186`
- `IJoinedQueryBuilder<T1, T2>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IJoinedQueryBuilder<T1, T2, TResult>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IJoinedQueryBuilder3<T1, T2, T3>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IJoinedQueryBuilder3<T1, T2, T3, TResult>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IJoinedQueryBuilder4<T1, T2, T3, T4>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>` — `src/Quarry/Query/IJoinedQueryBuilder.cs`
- `IEntityAccessor<T>` — `src/Quarry/Query/IEntityAccessor.cs:67`
- `IDeleteBuilder<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`
- `IExecutableDeleteBuilder<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`
- `IUpdateBuilder<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`
- `IExecutableUpdateBuilder<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`
- `IInsertBuilder<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`
- `IExecutableBatchInsert<T>` — `src/Quarry/Query/Modification/IModificationBuilder.cs`

**Step 1.2 — Remove ToSql from carrier bases**

Remove all `ToSql()` implementations from carrier base classes. These are explicit interface implementations that throw `InvalidOperationException`.

Files:
- `src/Quarry/Internal/CarrierBase.cs` — 5 occurrences across `CarrierBase<T>` and `CarrierBase<T, TResult>`
- `src/Quarry/Internal/JoinedCarrierBase.cs` — 7 occurrences across both generic forms
- `src/Quarry/Internal/JoinedCarrierBase3.cs` — 9 occurrences across both generic forms
- `src/Quarry/Internal/JoinedCarrierBase4.cs` — 11 occurrences across both generic forms
- `src/Quarry/Internal/ModificationCarrierBase.cs` — 10 occurrences across Delete/Update/Insert/BatchInsert carrier bases

**Step 1.3 — Remove ToSql from runtime builders**

Remove `ToSql()` implementations from runtime builder classes. These classes are being removed in a future phase, so minimal changes — just remove the method.

Files:
- `src/Quarry/Query/QueryBuilder.cs` — 2 implementations (QueryBuilder<T> and QueryBuilder<T, TResult>)
- `src/Quarry/Query/JoinedQueryBuilder.cs` — 6 implementations across all arity variants
- `src/Quarry/Query/EntityAccessor.cs` — 1 implementation + 1 reference in `Explain()`
- `src/Quarry/Query/Modification/UpdateBuilder.cs` — 2 implementations
- `src/Quarry/Query/Modification/DeleteBuilder.cs` — 2 implementations
- `src/Quarry/Query/Modification/InsertBuilder.cs` — 1 implementation + `ToSqlDirect()` internal helper

**Step 1.4 — Remove ToSql from generator**

Remove `InterceptorKind.BatchInsertToSql` and all its references:

- `src/Quarry.Generator/Models/InterceptorKind.cs` — remove `BatchInsertToSql` enum value
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — remove `"ToSql"` from candidate list (line 102), remove `"ToSql" =>` mappings (lines 341, 367), remove `"ToSql"` from method name check (line 129)
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — remove `BatchInsertToSql` from `IsExecutionKind` and QueryKind determination
- `src/Quarry.Generator/IR/CallSiteBinder.cs` — remove `BatchInsertToSql` reference
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — remove `BatchInsertToSql` case in dispatch
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — remove `EmitBatchInsertToSqlTerminal` method
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — remove `BatchInsertToSql` from carrier eligibility checks
- `src/Quarry.Generator/Parsing/AnalyzabilityChecker.cs` — remove ToSql reference if present

**Step 1.5 — Update test call sites**

Replace all `.ToSql()` calls in tests with `.ToDiagnostics().Sql`:

- `src/Quarry.Tests/SqlOutput/CrossDialectBatchInsertTests.cs` — 6 occurrences
- `src/Quarry.Tests/SqlOutput/VariableStoredChainTests.cs` — 3 occurrences

### Phase 2: Remove Trivial ToDiagnostics Gate

**Step 2.1 — Remove the gate in CarrierAnalyzer**

Remove lines 73-80 in `CarrierAnalyzer.AnalyzeNew()` that return `CarrierPlan.Ineligible("trivial ToDiagnostics chain")`. After this change, all PrebuiltDispatch ToDiagnostics chains — including bare `db.Users().ToDiagnostics()` — are carrier-eligible.

File: `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs`

**Impact:** The 259 non-carrier PrebuiltDispatch interceptor sites in the test suite will become carrier-optimized. All generated interceptor files will now contain `file sealed class Chain_` for ToDiagnostics chains. Test assertions checking `IsCarrierOptimized` may need updating.

### Phase 3: Extract Shared Terminal Helpers

**Step 3.1 — Create TerminalEmitHelpers**

Create `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` with the shared helpers extracted from existing `CarrierEmitter` and `InterceptorCodeGenerator` methods.

Methods to extract/consolidate:

`EmitSqlDispatch(StringBuilder, AssembledPlan, CarrierPlan)` — Consolidated from `CarrierEmitter`'s inline SQL dispatch and `InterceptorCodeGenerator.GenerateDispatchTable`. Emits either `const string sql = @"..."` (single variant) or `var sql = __c.Mask switch { ... }` (multiple variants). Reads mask from carrier field `__c.Mask`. Handles collection parameter placeholder tokens.

`EmitParameterLocals(StringBuilder, AssembledPlan, CarrierPlan)` — Extracted from `CarrierEmitter.EmitCarrierParameterLocals`. Emits `var __pVal{N} = (object?)__c.P{N} ?? DBNull.Value` for each scalar parameter. Handles sensitive parameter masking, enum casting, TypeMapping extraction. Emits pagination parameter locals (`__pValL`, `__pValO`) when Limit/Offset are parameterized.

`EmitCollectionExpansion(StringBuilder, AssembledPlan, CarrierPlan)` — Extracted from `CarrierEmitter`'s collection handling. Emits `var __col{N} = __c.P{N}; var __col{N}Len = __col{N}.Count;` loop and `sql = sql.Replace(...)` for collection parameters. Dialect-aware placeholder formatting.

`EmitDiagnosticParameterArray(StringBuilder, AssembledPlan, CarrierPlan)` — Consolidated from `InterceptorCodeGenerator.EmitDiagnosticParameterArray`. Emits `DiagnosticParameter[]` construction reading values from carrier fields. Handles scalar, collection, enum, sensitive, and conditional parameters. Includes new metadata fields (TypeName, TypeMappingClass, IsSensitive, IsEnum, IsCollection, IsConditional, ConditionalBitIndex).

`EmitDiagnosticClauseArray(StringBuilder, AssembledPlan, CarrierPlan)` — Consolidated from `InterceptorCodeGenerator.EmitDiagnosticClauseArray`. Emits `ClauseDiagnostic[]` with SQL fragments, per-clause parameters, conditional status. Includes new metadata (SourceLocation, ConditionalBitIndex, BranchKind). Handles collection token replacement in clause SQL fragments.

`EmitDiagnosticsConstruction(StringBuilder, AssembledPlan, CarrierPlan)` — New method. Emits the `new QueryDiagnostics(...)` constructor call with all fields — existing and new. Reads SQL from the dispatch local, parameters from the diagnostic array, metadata from compile-time constants. Emits SqlVariants dictionary literal, projection metadata, join metadata, pagination, and all other new properties. Single source of truth for diagnostic construction.

**Step 3.2 — Refactor CarrierEmitter execution terminals to use shared helpers**

Replace inline SQL dispatch and parameter local emission in `EmitCarrierExecutionTerminal`, `EmitCarrierNonQueryTerminal`, and `EmitCarrierInsertTerminal` with calls to the shared helpers:

```csharp
// Before: inline code duplicated across methods
// After:
TerminalEmitHelpers.EmitSqlDispatch(sb, chain, carrier);
TerminalEmitHelpers.EmitParameterLocals(sb, chain, carrier);
TerminalEmitHelpers.EmitCollectionExpansion(sb, chain, carrier);
// ... then DbCommand binding (execution-specific) ...
```

The DbCommand construction and executor method calls remain in `CarrierEmitter` since they are execution-only concerns.

**Step 3.3 — Refactor CarrierEmitter ToDiagnostics terminals to use shared helpers**

Replace `EmitCarrierToDiagnosticsTerminal` and `EmitCarrierInsertToDiagnosticsTerminal` bodies with shared helper calls:

```csharp
TerminalEmitHelpers.EmitSqlDispatch(sb, chain, carrier);
TerminalEmitHelpers.EmitParameterLocals(sb, chain, carrier);
TerminalEmitHelpers.EmitCollectionExpansion(sb, chain, carrier);
TerminalEmitHelpers.EmitDiagnosticParameterArray(sb, chain, carrier);
TerminalEmitHelpers.EmitDiagnosticClauseArray(sb, chain, carrier);
TerminalEmitHelpers.EmitDiagnosticsConstruction(sb, chain, carrier);
```

The first three calls are identical to what execution terminals use. The last three are diagnostic-specific. The SQL dispatch and parameter binding cannot drift because they are the same function calls.

### Phase 4: Delete Non-Carrier Diagnostic Code

**Step 4.1 — Remove non-carrier diagnostic path in TerminalBodyEmitter.EmitDiagnosticsTerminal**

The method currently has a non-carrier fallback (lines 429-456) that emits SQL dispatch via `GenerateDispatchTable`, clause diagnostics via `EmitNonCarrierDiagnosticClauseArray`, and constructs `QueryDiagnostics` with `Array.Empty<DiagnosticParameter>()`. After Phase 2, all PrebuiltDispatch chains are carrier-eligible, so this path is dead code. Remove it.

The method reduces to: resolve method signature, delegate to `CarrierEmitter.EmitCarrierToDiagnosticsTerminal` (which now uses shared helpers).

**Step 4.2 — Remove EmitNonCarrierDiagnosticClauseArray**

Delete `InterceptorCodeGenerator.EmitNonCarrierDiagnosticClauseArray`. All clause diagnostic emission now goes through the unified `TerminalEmitHelpers.EmitDiagnosticClauseArray`.

File: `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs`

**Step 4.3 — Remove non-carrier prebuilt executor code (if unused)**

After unification, verify whether the non-carrier prebuilt execution path (`AllocatePrebuiltParams`, `ExecuteWithPrebuiltSqlAsync`, `ExecuteWithPrebuiltParamsAsync`) is still used by any execution terminal. If all execution terminals are also carrier-optimized, the non-carrier prebuilt code in `TerminalBodyEmitter.EmitReaderTerminal` (lines 129-156) and related methods is also dead code. However, this may still be needed for edge cases where `CanEmitReaderTerminal` returns false. Assess and remove only if confirmed dead.

### Phase 5: Expand Diagnostic Emission

**Step 5.1 — Emit new metadata in TerminalEmitHelpers.EmitDiagnosticsConstruction**

The `EmitDiagnosticsConstruction` helper emits the full `new QueryDiagnostics(...)` call. Add emission of all new fields:

**From AssembledPlan.Plan (QueryPlan):**
- `tierReason` — string literal computed by tier reason algorithm (Section 6)
- `disqualifyReason` — `QueryPlan.NotAnalyzableReason` as string literal (null for PrebuiltDispatch)
- `conditionalBitCount` — integer literal from `QueryPlan.ConditionalTerms.Count`
- `isDistinct` — bool literal from `QueryPlan.IsDistinct`
- `unmatchedMethodNames` — string array literal from `QueryPlan.UnmatchedMethodNames` (null if empty)

**From AssembledPlan.SqlVariants:**
- `sqlVariants` — dictionary literal: `new Dictionary<ulong, SqlVariantDiagnostic> { { 0UL, new SqlVariantDiagnostic("SELECT ...", 2) }, ... }`. Emit all mask keys with their SQL and parameter count.

**From carrier runtime state:**
- `activeMask` — `__c.Mask` read (ulong, zero for unconditional)

**From AssembledPlan.Plan.Parameters:**
- `allParameters` — `DiagnosticParameter[]` with full metadata per parameter. Values read from carrier fields. TypeName, TypeMappingClass, IsSensitive, IsEnum, IsCollection, IsConditional, ConditionalBitIndex all emitted as literals from `QueryParameter` metadata.

**From AssembledPlan.Plan.Projection / ProjectionInfo:**
- `projectionKind` — string literal from `ProjectionInfo.Kind.ToString()`
- `projectionNonOptimalReason` — string literal (null if optimal)
- `projectionColumns` — `ProjectionColumnDiagnostic[]` from projection columns with PropertyName, ColumnName, ClrType, Ordinal, IsNullable, TypeMappingClass, IsForeignKey, ForeignKeyEntityName, IsEnum

**From AssembledPlan.Plan.Joins:**
- `joins` — `JoinDiagnostic[]` with TableName, SchemaName, JoinKind, Alias, OnConditionSql

**From AssembledPlan.Plan.Pagination:**
- `limit` — integer literal if `PaginationPlan.LiteralLimit` is set; read from `__c.Limit` carrier field if parameterized; null if no Limit clause
- `offset` — same pattern as limit

**From CarrierPlan:**
- `carrierClassName` — string literal from `CarrierPlan.ClassName`
- `carrierIneligibleReason` — null (all PrebuiltDispatch chains are now carrier-eligible)

**From AssembledPlan metadata:**
- `schemaName` — string literal from `AssembledPlan.SchemaName`
- `identityColumnName` — string literal from `InsertInfo.IdentityColumnName` (null for non-insert chains)

**Step 5.2 — Emit expanded ClauseDiagnostic fields**

In `TerminalEmitHelpers.EmitDiagnosticClauseArray`, add emission of new `ClauseDiagnostic` constructor arguments:

- `SourceLocation` — `new ClauseSourceLocation("path", line, column)` from `TranslatedCallSite.Location`. The file path is emitted as a string literal. Line and column as integer literals.
- `ConditionalBitIndex` — integer literal from the conditional term's bit index (null for non-conditional clauses)
- `BranchKind` — `DiagnosticBranchKind.Independent` or `.MutuallyExclusive` from `ConditionalTerm.BranchKind` (null for non-conditional clauses)

**Step 5.3 — Emit expanded DiagnosticParameter fields**

In `TerminalEmitHelpers.EmitDiagnosticParameterArray`, add emission of new `DiagnosticParameter` constructor arguments per parameter:

- `TypeName` — string literal from `QueryParameter.ClrType`
- `TypeMappingClass` — string literal from `QueryParameter.TypeMappingClass` (null if none)
- `IsSensitive` — bool literal from `QueryParameter.IsSensitive`
- `IsEnum` — bool literal from `QueryParameter.IsEnum`
- `IsCollection` — bool literal from `QueryParameter.IsCollection`
- `IsConditional` — bool literal, true if parameter belongs to a conditional clause
- `ConditionalBitIndex` — integer literal of the owning conditional term's bit (null if non-conditional)

**Step 5.4 — Update RuntimeBuild emitter**

Change `TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal` signature to accept disqualify reason:

```csharp
internal static void EmitRuntimeDiagnosticsTerminal(
    StringBuilder sb, TranslatedCallSite site, string methodName,
    string? disqualifyReason);
```

The generated code calls an internal `RuntimeToDiagnostics()` method on the concrete builder (to avoid infinite recursion with the intercepted `ToDiagnostics()`), then wraps the result with compiler-known metadata (tier, disqualifyReason, tierReason).

Add `internal QueryDiagnostics RuntimeToDiagnostics()` to runtime builder classes: `QueryBuilder<T>`, `DeleteBuilder<T>`, `UpdateBuilder<T>`, `InsertBuilder<T>`. Implementation delegates to existing runtime SQL generation.

Update `FileEmitter.EmitInterceptorMethod` to pass `AssembledPlan.Plan.NotAnalyzableReason` to the runtime emitter.

### Phase 6: Update Tests

**Step 6.1 — Update existing diagnostic assertions**

Tests that check `IsCarrierOptimized` for ToDiagnostics chains may need updating since previously non-carrier chains are now carrier-optimized. The key assertion change: trivial ToDiagnostics chains that previously asserted `IsCarrierOptimized == false` should now assert `true`.

Key test files:
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` — `CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch` test needs updating (bare ToDiagnostics now produces a carrier)
- `src/Quarry.Tests/SqlOutput/CrossDialectDiagnosticsTests.cs` — verify all assertions still pass

**Step 6.2 — Add new diagnostic property tests**

Add tests verifying:
- `TierReason` contains meaningful description for each tier
- `DisqualifyReason` is populated for RuntimeBuild chains, null for PrebuiltDispatch
- `SqlVariants` contains all mask-keyed entries for conditional chains
- `ActiveMask` reflects which conditional branches were taken
- `AllParameters` includes full metadata (TypeName, IsSensitive, IsEnum, IsCollection, TypeMappingClass)
- `ProjectionColumns` matches entity column metadata
- `ProjectionKind` and `ProjectionNonOptimalReason` populated correctly
- `Joins` contains join metadata for multi-entity chains
- `IsDistinct` is true after `.Distinct()`, false otherwise
- `Limit`/`Offset` reflect literal pagination values
- `IdentityColumnName` populated for insert chains with identity columns
- `ClauseDiagnostic.SourceLocation` contains file/line/column
- `ClauseDiagnostic.ConditionalBitIndex` and `BranchKind` set for conditional clauses
- `CarrierClassName` is non-null for all PrebuiltDispatch chains

**Step 6.3 — Replace ToSql test call sites**

Covered in Phase 1, Step 1.5.

## 8. Files Changed Summary

### Quarry Runtime (`src/Quarry/`)

| File | Changes |
|---|---|
| `Query/QueryDiagnostics.cs` | Expand type: new properties, constructor params, new diagnostic types |
| `Query/IQueryBuilder.cs` | Remove `ToSql()` from both interface variants |
| `Query/IJoinedQueryBuilder.cs` | Remove `ToSql()` from all 6 interface variants |
| `Query/IEntityAccessor.cs` | Remove `ToSql()` |
| `Query/Modification/IModificationBuilder.cs` | Remove `ToSql()` from all 6 interface variants |
| `Internal/CarrierBase.cs` | Remove `ToSql()` stubs |
| `Internal/JoinedCarrierBase.cs` | Remove `ToSql()` stubs |
| `Internal/JoinedCarrierBase3.cs` | Remove `ToSql()` stubs |
| `Internal/JoinedCarrierBase4.cs` | Remove `ToSql()` stubs |
| `Internal/ModificationCarrierBase.cs` | Remove `ToSql()` stubs |
| `Query/QueryBuilder.cs` | Remove `ToSql()`, add `RuntimeToDiagnostics()` |
| `Query/JoinedQueryBuilder.cs` | Remove `ToSql()` from all arity variants |
| `Query/EntityAccessor.cs` | Remove `ToSql()`, update `Explain()` reference |
| `Query/Modification/UpdateBuilder.cs` | Remove `ToSql()`, add `RuntimeToDiagnostics()` |
| `Query/Modification/DeleteBuilder.cs` | Remove `ToSql()`, add `RuntimeToDiagnostics()` |
| `Query/Modification/InsertBuilder.cs` | Remove `ToSql()` + `ToSqlDirect()`, add `RuntimeToDiagnostics()` |

### Generator (`src/Quarry.Generator/`)

| File | Changes |
|---|---|
| `CodeGen/TerminalEmitHelpers.cs` | **New file** — shared SQL dispatch, param locals, collection expansion, diagnostic array, diagnostic construction helpers |
| `CodeGen/CarrierEmitter.cs` | Refactor execution + diagnostic terminals to use `TerminalEmitHelpers`. Remove `EmitCarrierToDiagnosticsTerminal` standalone logic. |
| `CodeGen/TerminalBodyEmitter.cs` | Remove non-carrier diagnostic path from `EmitDiagnosticsTerminal`. Remove `EmitBatchInsertToSqlTerminal`. Update `EmitRuntimeDiagnosticsTerminal` to accept disqualify reason. |
| `CodeGen/CarrierAnalyzer.cs` | Remove trivial ToDiagnostics gate (lines 73-80) |
| `CodeGen/FileEmitter.cs` | Remove `BatchInsertToSql` dispatch case. Pass disqualify reason to runtime emitter. |
| `Generation/InterceptorCodeGenerator.cs` | Remove `EmitNonCarrierDiagnosticClauseArray`. `EmitDiagnosticParameterArray` and `EmitDiagnosticClauseArray` may move to `TerminalEmitHelpers` or be removed if fully superseded. |
| `Models/InterceptorKind.cs` | Remove `BatchInsertToSql` |
| `Parsing/UsageSiteDiscovery.cs` | Remove `"ToSql"` from candidate list and all `"ToSql" =>` mappings |
| `Parsing/ChainAnalyzer.cs` | Remove `BatchInsertToSql` from `IsExecutionKind` and QueryKind determination |
| `IR/CallSiteBinder.cs` | Remove `BatchInsertToSql` reference |
| `Parsing/AnalyzabilityChecker.cs` | Remove ToSql reference |

### Tests (`src/Quarry.Tests/`)

| File | Changes |
|---|---|
| `SqlOutput/CrossDialectBatchInsertTests.cs` | Replace `.ToSql()` → `.ToDiagnostics().Sql` (6 sites) |
| `SqlOutput/VariableStoredChainTests.cs` | Replace `.ToSql()` → `.ToDiagnostics().Sql` (3 sites) |
| `Generation/CarrierGenerationTests.cs` | Update `CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch` — now expects carrier |
| `SqlOutput/CrossDialectDiagnosticsTests.cs` | Add new property assertions |
| New test file(s) | Expanded diagnostic property coverage |

## 9. Ordering and Dependencies

```
Phase 1 (ToSql removal) has no dependencies on other phases.

Phase 2 (trivial gate removal) has no dependencies but should precede Phase 4.

Phase 3 (shared helpers) must precede Phase 4 (delete non-carrier code)
  because the carrier path must use shared helpers before the non-carrier
  path is removed.

Phase 4 (delete non-carrier diagnostic code) depends on Phase 2 + Phase 3.

Phase 5 (expand diagnostic emission) depends on Phase 3 (helpers exist)
  and is independent of Phase 4. Can be done in parallel with Phase 4.

Phase 6 (tests) is ongoing — update tests as each phase lands.
```

Recommended execution order: Phase 1 → Phase 2 → Phase 3 → Phase 4 + Phase 5 (parallel) → Phase 6.

Phase 1 can ship independently as a standalone PR. Phases 2-5 should ship together as a single PR to avoid intermediate broken states where the gate is removed but the non-carrier code isn't yet deleted.
