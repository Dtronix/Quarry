# QueryDiagnostics Improvement Plan: Compiler-Sourced Diagnostics

## 1. Overview

Replace the hybrid runtime/compile-time `ToDiagnostics()` implementation with a fully compiler-sourced diagnostic surface. The compiler already computes SQL, optimization tier, carrier eligibility, parameter metadata, conditional masks, clause breakdowns, and disqualification reasons. Currently this information is partially emitted as interceptor constants and partially reconstructed at runtime. This plan makes `QueryDiagnostics` the single canonical debugging surface that carries everything the compiler knows, with runtime mask evaluation for conditional chains.

**Prerequisite:** All 2,943 tests passing (25 remaining failures resolved first).

**Core principle:** The compiler emits literal constants for all diagnostic data. The only runtime computation is mask evaluation for conditional chains and parameter value extraction from carrier fields.

## 2. Constraints

- netstandard2.0 target: No `record` types, no `init` properties.
- `QueryDiagnostics` is a public API type in the `Quarry` namespace. Changes must be additive (new properties) not breaking (no removed properties).
- `ToDiagnostics()` must work for all query kinds: Select, Delete, Update, Insert.
- `ToDiagnostics()` must work for all optimization tiers: PrebuiltDispatch, PrequotedFragments, RuntimeBuild.
- RuntimeBuild chains produce structured metadata (tier, disqualify reason) plus runtime-generated SQL via fallback to the concrete builder.
- Trace files (`__Trace.*.g.cs`) remain as an internal debug channel gated by a compiler flag.
- Parameter values are included in diagnostics. The `IsSensitive` flag marks parameters that contain sensitive data.

## 3. Current State

**QueryDiagnostics (src/Quarry/Query/QueryDiagnostics.cs):**
- Properties: `Sql`, `Parameters`, `Tier`, `IsCarrierOptimized`, `Kind`, `Dialect`, `TableName`, `Clauses`, `RawState`, `InsertRowCount`
- `DiagnosticParameter`: `Name`, `Value`
- `ClauseDiagnostic`: `ClauseType`, `SqlFragment`, `IsConditional`, `IsActive`, `Parameters` (to be expanded with `SourceLocation`, `ConditionalBitIndex`, `BranchKind`)
- `DiagnosticOptimizationTier`: `RuntimeBuild`, `PrebuiltDispatch`

**Current emitter paths (TerminalBodyEmitter):**
- `EmitDiagnosticsTerminal`: Prebuilt chains. Emits SQL constants, mask switch for conditionals, clause array. Carrier path delegates to `CarrierEmitter.EmitCarrierToDiagnosticsTerminal`.
- `EmitRuntimeDiagnosticsTerminal`: No prebuilt chain available. Casts to concrete builder and calls its runtime `ToDiagnostics()`.
- `EmitInsertDiagnosticsTerminal`: Insert-specific path.

**What the compiler already knows (per chain):**
- SQL variants keyed by mask (AssembledPlan.SqlVariants)
- Optimization tier and reason (QueryPlan.Tier, NotAnalyzableReason)
- Conditional terms with bit indices (QueryPlan.ConditionalTerms)
- Possible masks (QueryPlan.PossibleMasks)
- Parameter metadata: type, value expression, sensitivity, enum status, collection status (QueryPlan.Parameters)
- Projection columns and kind (QueryPlan.Projection)
- Carrier eligibility, carrier class name, and ineligibility reason (CarrierPlan.IsEligible, IneligibleReason)
- Clause sites with roles and source locations (AnalyzedChain.ClauseSites, UsageSiteInfo.FilePath/Line/Column)
- Conditional branch kind per clause (ConditionalClause.BranchKind: Independent vs MutuallyExclusive)
- Join metadata (QueryPlan.Joins)
- Entity and table metadata
- IsDistinct flag (QueryPlan.IsDistinct)
- Pagination: literal Limit/Offset values and parameter indices (QueryPlan.Pagination)
- Insert identity column name (InsertInfo.IdentityColumnName)
- Unmatched method names (QueryPlan.UnmatchedMethodNames)
- Projection optimality (ProjectionInfo.IsOptimalPath, NonOptimalReason)
- Per-parameter TypeMapping class (QueryParameter.TypeMappingClass)
- Per-column TypeMapping, FK metadata (ProjectedColumn.CustomTypeMapping, IsForeignKey)
- Per-variant parameter count (AssembledSqlVariant.ParameterCount)

**What is NOT emitted today:**
- Tier classification reason (why PrebuiltDispatch vs RuntimeBuild)
- Disqualification reason for RuntimeBuild chains
- Carrier ineligibility reason (CarrierPlan.IneligibleReason)
- Full SQL variants map (only the active variant is returned)
- Per-variant parameter count (AssembledSqlVariant.ParameterCount)
- Parameter types, sensitivity flags, enum metadata, TypeMapping class
- Conditional bit index per clause, branch kind (Independent vs MutuallyExclusive)
- Projection column metadata (names, types, ordinals, TypeMapping, FK info)
- Projection non-optimal reason (ProjectionInfo.NonOptimalReason)
- IsDistinct flag
- Pagination metadata (Limit, Offset — literal or parameter-sourced)
- Insert identity column name (InsertInfo.IdentityColumnName)
- Unmatched method names (QueryPlan.UnmatchedMethodNames)
- Clause source locations (file, line, column from UsageSiteInfo)

## 4. Expanded QueryDiagnostics Type

### 4.1 New Properties on QueryDiagnostics

```csharp
public sealed class QueryDiagnostics
{
    // Existing properties (unchanged)
    public string Sql { get; }
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }
    public DiagnosticOptimizationTier Tier { get; }
    public bool IsCarrierOptimized { get; }
    public DiagnosticQueryKind Kind { get; }
    public SqlDialect Dialect { get; }
    public string TableName { get; }
    public IReadOnlyList<ClauseDiagnostic> Clauses { get; }

    // New properties
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

### 4.2 Expanded DiagnosticParameter

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

### 4.3 New Diagnostic Types

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

### 4.4 DiagnosticOptimizationTier Expansion

```csharp
public enum DiagnosticOptimizationTier
{
    RuntimeBuild,
    PrebuiltDispatch,
    PrequotedFragments  // New: tier 2 for chains with >4 conditional bits
}
```

## 5. Expanded Constructor

The internal constructor gains new optional parameters. All new parameters have defaults so existing call sites remain compatible.

```csharp
internal QueryDiagnostics(
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
    // New parameters
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
    IReadOnlyList<string>? unmatchedMethodNames = null)
```

## 6. ToSql() Delegation

`ToSql()` becomes a thin wrapper. The concrete builder implementations (`QueryBuilder<T>`, `ExecutableDeleteBuilder<T>`, `ExecutableUpdateBuilder<T>`, `InsertBuilder<T>`) change their `ToSql()` method:

```csharp
public string ToSql() => ToDiagnostics().Sql;
```

This eliminates the separate runtime SQL generation path in `ToSql()`. Both methods return identical SQL from the same source.

Files affected:
- `src/Quarry/Query/QueryBuilder.cs`
- `src/Quarry/Query/Modification/UpdateBuilder.cs` (both UpdateBuilder and ExecutableUpdateBuilder)
- `src/Quarry/Query/Modification/DeleteBuilder.cs` (both DeleteBuilder and ExecutableDeleteBuilder)
- `src/Quarry/Query/Modification/InsertBuilder.cs`

## 7. Emitter Changes

### 7.1 PrebuiltDispatch Path (Carrier-Optimized)

`CarrierEmitter.EmitCarrierToDiagnosticsTerminal` is the primary path for most chains. It currently emits SQL dispatch, parameter locals, clause array, and constructs `QueryDiagnostics`.

**Changes:**
- Emit `tierReason` as a string constant derived from ConditionalTerms count and PossibleMasks count.
- Emit `sqlVariants` dictionary literal with all mask-keyed `SqlVariantDiagnostic` entries (SQL string + parameter count per variant).
- Emit `activeMask` read from `__c.Mask`.
- Emit `conditionalBitCount` as integer literal.
- Emit `allParameters` array with full metadata (type, TypeMappingClass, sensitivity, enum, collection, conditional bit).
- Emit `projectionColumns` array from QueryPlan.Projection columns, including TypeMappingClass, IsForeignKey, ForeignKeyEntityName, IsEnum.
- Emit `projectionKind` string from QueryPlan.Projection.Kind.
- Emit `projectionNonOptimalReason` string from ProjectionInfo.NonOptimalReason (null if optimal).
- Emit `carrierClassName` string literal.
- Emit `carrierIneligibleReason` — null for carrier-optimized path (always eligible here); for non-carrier path see 7.2.
- Emit `schemaName` string literal.
- Emit `joins` array from QueryPlan.Joins.
- Emit `isDistinct` bool from QueryPlan.IsDistinct.
- Emit `limit` and `offset`: if literal values exist in PaginationPlan, emit as integer constants. If parameterized, read from carrier field at runtime. Null if not present.
- Emit `identityColumnName` string from InsertInfo.IdentityColumnName (null for non-insert chains).
- Emit `unmatchedMethodNames` string array from QueryPlan.UnmatchedMethodNames (null if empty).
- Emit clause `SourceLocation` (file, line, column) for each `ClauseDiagnostic` from the corresponding UsageSiteInfo.
- Emit `ConditionalBitIndex` and `BranchKind` on each conditional `ClauseDiagnostic`.
- For each `DiagnosticParameter` in the active parameter list, include `Value` read from the carrier field (`__c.P0`, `__c.P1`, etc.).

**Tier reason construction algorithm:**
- If tier is PrebuiltDispatch and conditionalBitCount == 0: "unconditional chain, single SQL variant"
- If tier is PrebuiltDispatch and conditionalBitCount > 0: "{N} conditional bits, {M} mask variants"
- If tier is PrequotedFragments: "{N} conditional bits exceeds dispatch threshold"
- If tier is RuntimeBuild: use NotAnalyzableReason from QueryPlan

### 7.2 PrebuiltDispatch Path (Non-Carrier)

`TerminalBodyEmitter.EmitDiagnosticsTerminal` handles non-carrier prebuilt chains. Same additions as 7.1 but parameter values come from the concrete builder state rather than carrier fields. For non-carrier chains, parameters may not be individually accessible, so `Value` is null for parameters that cannot be extracted.

**Additional non-carrier specifics:**
- Emit `carrierIneligibleReason` string from CarrierPlan.IneligibleReason (explains why carrier optimization was not used).
- `IsCarrierOptimized` is false, `CarrierClassName` is null.

### 7.3 RuntimeBuild Path

`TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal` handles chains the compiler could not analyze. Currently delegates entirely to the runtime builder.

**Changes:**
- Still call the runtime builder's `ToDiagnostics()` for SQL generation.
- Wrap the result with compiler-known metadata: `Tier = RuntimeBuild`, `DisqualifyReason` from the QueryPlan (emitted as a string constant), `TierReason = disqualifyReason`.
- `SqlVariants` is null (no prebuilt variants).
- `ConditionalBitCount` is 0.
- `IsCarrierOptimized` is false.

The emitter wraps the runtime result:

```csharp
public static void EmitRuntimeDiagnosticsTerminal(
    StringBuilder sb, UsageSiteInfo site, string methodName,
    string? disqualifyReason)
```

Generated code pattern:
```
var __rt = builder.RuntimeToDiagnostics();
return new QueryDiagnostics(__rt.Sql, __rt.Parameters, ...,
    tier: RuntimeBuild, disqualifyReason: "...", tierReason: "...");
```

This requires a new internal method `RuntimeToDiagnostics()` on the concrete builders that returns the raw runtime diagnostic without going through the interceptor. This prevents infinite recursion since `ToDiagnostics()` is intercepted.

### 7.4 Insert Path

`TerminalBodyEmitter.EmitInsertDiagnosticsTerminal` follows the same pattern as 7.1/7.2. Insert-specific metadata (row count) maps to existing `InsertRowCount` property.

**Additional insert specifics:**
- Emit `identityColumnName` string from InsertInfo.IdentityColumnName (the auto-increment/identity column used in RETURNING/OUTPUT clauses, null if no identity).
- Emit `identityPropertyName` is not surfaced separately — it can be derived from `ProjectionColumns` if needed.

## 8. Data Flow

### 8.1 Compile-Time Flow (New Pipeline)

```
ChainAnalyzer.Analyze()
  -> QueryPlan (tier, tierReason, conditionalTerms, parameters, projection, joins)
    -> SqlAssembler.Assemble()
      -> AssembledPlan (sqlVariants, parameterCount)
        -> CarrierAnalyzer.AnalyzeNew()
          -> CarrierPlan (isEligible, className, fields)
            -> Bridge (EmitFileInterceptorsNewPipeline)
              -> PrebuiltChainInfo (analysis, sqlMap, chainParameters, projectionInfo)
                -> TerminalBodyEmitter.EmitDiagnosticsTerminal()
                  -> Generated interceptor with all diagnostic constants
```

### 8.2 Runtime Flow (Conditional Chain)

1. User calls `.Set(u => u.Name = "x")` -- carrier stores value in field, sets mask bit.
2. User calls `.Where(u => u.IsActive)` inside `if` block -- carrier sets conditional mask bit.
3. User calls `.ToDiagnostics()` -- interceptor runs:
   a. Read `__c.Mask` to get active mask value.
   b. Switch on mask to select SQL variant (compile-time constant strings).
   c. Read parameter values from carrier fields (`__c.P0`, `__c.P1`).
   d. Filter `AllParameters` by active mask bits to populate `Parameters`.
   e. Construct `QueryDiagnostics` with all compile-time constants plus runtime mask/values.

## 9. Trace File Gating

Trace files (`__Trace.*.g.cs`) are retained as an internal debug channel for generator developers. Gated by a compiler flag to avoid noise in production builds.

**Implementation:**
- Add a boolean property `EmitTraceFiles` to the generator options, defaulting to `false`.
- Read from `AnalyzerConfigOptionsProvider` using an MSBuild property: `<QuarryEmitTraceFiles>true</QuarryEmitTraceFiles>`.
- Only emit `__Trace` source files when the flag is true.
- The flag has no effect on `QueryDiagnostics` output.

Files affected:
- `src/Quarry.Generator/QuarryGenerator.cs` -- read the flag, conditionally emit trace files.
- Test projects set `<QuarryEmitTraceFiles>true</QuarryEmitTraceFiles>` in their `.csproj` to retain trace output during development.

## 10. Work Breakdown

### Step 1: Expand QueryDiagnostics Type
Add new properties and constructor parameters to `QueryDiagnostics`. Add `SqlVariantDiagnostic`, `ProjectionColumnDiagnostic`, `JoinDiagnostic`, `ClauseSourceLocation`, `DiagnosticBranchKind` types. Add `PrequotedFragments` to `DiagnosticOptimizationTier`. Expand `DiagnosticParameter` with `TypeMappingClass` and other new fields. Expand `ClauseDiagnostic` with `SourceLocation` (ClauseSourceLocation?), `ConditionalBitIndex` (int?), and `BranchKind` (DiagnosticBranchKind?). Add `IsDistinct`, `Limit`, `Offset`, `IdentityColumnName`, `UnmatchedMethodNames`, `CarrierIneligibleReason`, `ProjectionNonOptimalReason` properties. All new parameters have defaults for backward compatibility.

**Files:** `src/Quarry/Query/QueryDiagnostics.cs`

### Step 2: Add RuntimeToDiagnostics Internal Method
Add `internal QueryDiagnostics RuntimeToDiagnostics()` to concrete builder classes. This is the non-intercepted runtime diagnostic path used by the RuntimeBuild emitter to avoid recursion.

**Files:** `src/Quarry/Query/QueryBuilder.cs`, `src/Quarry/Query/Modification/UpdateBuilder.cs`, `src/Quarry/Query/Modification/DeleteBuilder.cs`, `src/Quarry/Query/Modification/InsertBuilder.cs`

### Step 3: Delegate ToSql() to ToDiagnostics().Sql
Change all `ToSql()` implementations to return `ToDiagnostics().Sql`.

**Files:** Same as Step 2.

### Step 4: Update Carrier ToDiagnostics Emitter
Expand `CarrierEmitter.EmitCarrierToDiagnosticsTerminal` to emit all new diagnostic fields as compile-time constants. Extract parameter values from carrier fields. Emit `sqlVariants` dictionary literal with `SqlVariantDiagnostic` (SQL + parameter count). Emit `activeMask` from carrier. Emit projection metadata (including TypeMapping, FK, enum). Emit join metadata. Emit `isDistinct`, pagination (`limit`/`offset`), `identityColumnName`, `unmatchedMethodNames`, `projectionNonOptimalReason`. Emit clause `SourceLocation`, `ConditionalBitIndex`, `BranchKind`. Emit `TypeMappingClass` per parameter.

**Files:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

### Step 5: Update Non-Carrier ToDiagnostics Emitter
Expand `TerminalBodyEmitter.EmitDiagnosticsTerminal` with the same additions as Step 4. Parameter values may be null for non-carrier chains where individual values are not accessible. Additionally emit `carrierIneligibleReason` from CarrierPlan.IneligibleReason.

**Files:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

### Step 6: Update RuntimeBuild ToDiagnostics Emitter
Change `TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal` to accept `disqualifyReason` from the compiler. Emit wrapper code that calls `RuntimeToDiagnostics()` and augments with compiler metadata.

**Files:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`, `src/Quarry.Generator/CodeGen/FileEmitter.cs` (pass disqualify reason)

### Step 7: Update Insert ToDiagnostics Emitter
Apply the same diagnostic expansion to `TerminalBodyEmitter.EmitInsertDiagnosticsTerminal`.

**Files:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

### Step 8: Pass Compiler Metadata Through Bridge
Ensure the bridge (`EmitFileInterceptorsNewPipeline`) passes all new metadata through to `PrebuiltChainInfo` so the emitters can access them: tier reason, disqualify reason, carrier ineligible reason, projection metadata (including NonOptimalReason), join metadata, IsDistinct, pagination (PaginationPlan), identity column name, unmatched method names, clause source locations, conditional branch kinds, per-parameter TypeMappingClass, per-column TypeMapping/FK metadata, per-variant parameter count.

**Files:** `src/Quarry.Generator/QuarryGenerator.cs`, `src/Quarry.Generator/Models/PrebuiltChainInfo.cs`

### Step 9: Implement Trace File Gating
Add `QuarryEmitTraceFiles` MSBuild property support. Gate trace file emission behind the flag. Set the flag in test projects.

**Files:** `src/Quarry.Generator/QuarryGenerator.cs`, `src/Quarry.Tests/Quarry.Tests.csproj`

### Step 10: Update Tests
Update existing `ToDiagnostics()` test assertions to verify new properties. Add new tests for:
- `TierReason` contains meaningful description for each tier.
- `DisqualifyReason` is populated for RuntimeBuild chains.
- `CarrierIneligibleReason` is populated for non-carrier prebuilt chains, null for carrier chains.
- `SqlVariants` contains all mask-keyed `SqlVariantDiagnostic` entries (SQL + ParameterCount) for conditional chains.
- `ActiveMask` reflects which conditional branches were taken.
- `Parameters` includes values, types, sensitivity flags, and `TypeMappingClass`.
- `AllParameters` vs `Parameters` distinction for conditional chains.
- `ProjectionColumns` matches entity column metadata for identity Select, including TypeMappingClass, IsForeignKey, ForeignKeyEntityName, IsEnum.
- `ProjectionNonOptimalReason` is populated when projection is suboptimal, null otherwise.
- `Joins` contains join metadata for multi-entity chains.
- `IsDistinct` is true for `.Distinct()` chains, false otherwise.
- `Limit` and `Offset` reflect literal pagination values; null when not set.
- `IdentityColumnName` is populated for insert chains with identity columns, null otherwise.
- `UnmatchedMethodNames` lists methods the compiler couldn't intercept; null when all matched.
- `ClauseDiagnostic.SourceLocation` contains file/line/column for each clause.
- `ClauseDiagnostic.ConditionalBitIndex` and `BranchKind` are set for conditional clauses.
- `ToSql()` returns same value as `ToDiagnostics().Sql`.

**Files:** Test files across `src/Quarry.Tests/`

## 11. Migration Notes

- All changes to `QueryDiagnostics` are additive. Existing code that constructs `QueryDiagnostics` continues to work due to default parameter values.
- The `RawState` internal property can be deprecated once all test assertions migrate to the structured properties.
- The `InsertRowCount` internal property can be exposed as a public property if needed.
- Future work: once emitters consume new IR types directly (post bridge removal), the diagnostic metadata flows directly from `AssembledPlan` and `CarrierPlan` without bridge conversion.
