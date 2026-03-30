# Generator Consolidation Implementation Plan

## Overview

This document describes the implementation plan for consolidating duplicated and divergent logic across the Quarry source generator. The work is organized into seven execution phases, ordered by dependency and priority. Each phase is scoped to a single pull request.

The plan addresses 16 findings from a comprehensive review of the generator codebase, covering four perspectives: UnsafeAccessor extraction paths, cross-stage shared concepts, emitter duplication, and post-V0.2.0 fix specificity.

### Guiding Principles

- **Single source of truth**: Every classification, resolution, or enrichment decision should live in exactly one place. Downstream consumers read the result; they do not re-derive it.
- **Composition over copy-paste**: When two emitters need the same 10-line block, extract it. When three pipeline stages check "is this type resolved?", centralize the check.
- **Preserve generated output**: Refactoring the generator's internals must not change the C# source it emits. Every phase gates on the existing test suite producing identical diagnostics SQL and interceptor shapes.

### Phase Dependency Graph

```
Phase 1 (TypeClassification)
    |
    v
Phase 2 (SiteParams helper) -----> Phase 5 (Emitter consolidation)
    |
Phase 3 (ConditionalInfo split)
    |
Phase 4 (Enum + Enrichment)
    |
Phase 6 (Pipeline error handling)
    |
Phase 7 (Lower priority cleanup)
```

Phase 1 is foundational because Phases 2-5 reference `TypeClassification` utilities. Phase 3 is independent of Phase 2 but benefits from Phase 1's type resolution unification. Phases 5 and 6 can run in parallel after Phase 4.

---

## Phase 1: TypeClassification Consolidation

**Findings addressed**: #1 (ValueTypes duplication), #3 (GetReaderMethodForType divergence), #5 (IsUnresolvedTypeName divergence), #6 (BuildTupleTypeName divergence)

**Estimated scope**: ~15 files modified, ~200 lines removed, ~120 lines added to `TypeClassification.cs`

### 1.1 Problem Statement

Four independent classification systems have grown organically across the generator. Each was introduced by a different pipeline stage to answer local questions ("is this a value type?", "what reader method?", "is this type resolved?", "build a tuple type string"). Over time they diverged: the `ValueTypes` HashSet in `CarrierAnalyzer` contains 28 entries, `CarrierEmitter` has an identical copy, `TerminalBodyEmitter.IsKnownValueTypeName` uses `is` pattern matching with a different set (includes `nint`/`nuint`, handles tuples, omits BCL names like `Int32`), and `TypeClassification` already centralizes `NeedsSignCast` for a subset of the same types.

The `GetReaderMethodForType` divergence is the most dangerous: `ChainAnalyzer` maps only 12 types, `UsageSiteDiscovery` maps 18 types (including nullable stripping and unsigned), `InterceptorCodeGenerator.Utilities` maps 16 types (but uses `GetValue` for `DateTimeOffset` instead of `GetFieldValue<DateTimeOffset>`), and `ProjectionAnalyzer` maps 6 types. An unsigned column resolved by `ChainAnalyzer` gets `GetValue` (runtime boxing + cast) instead of the correct `GetInt32`.

### 1.2 Target State

A single `TypeClassification` static class in `Utilities/` becomes the authoritative source for all CLR type classification in the generator. Every consumer calls into it. No private `ValueTypes` sets, no per-file `IsUnresolvedTypeName`, no per-stage `GetReaderMethodForType`.

### 1.3 Implementation Steps

#### Step 1.3.1: Extend `TypeClassification` with Value Type Classification

Add a comprehensive `HashSet<string>` that is the union of all four existing lists. Expose it through typed query methods rather than exposing the set directly.

The set must include:
- C# keyword types: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `nint`, `nuint`
- BCL names: `Boolean`, `Byte`, `SByte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, `Decimal`, `Char`
- Date/time types: `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`
- Other value types: `Guid`

Tuple detection (types starting with `(`) is handled as a special case in `IsValueType` before the set lookup because tuples are `ValueTuple<>` structs.

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

```csharp
internal static class TypeClassification
{
    private static readonly HashSet<string> s_valueTypes = new(StringComparer.Ordinal) { /* union set */ };

    public static bool NeedsSignCast(string clrType) => /* existing */;

    public static bool IsValueType(string typeName);
    public static bool IsReferenceType(string typeName);
    public static bool IsNonNullableValueType(string typeName);
}
```

**Algorithm for `IsValueType`**:
1. If `typeName` is null or empty, return false.
2. Strip trailing `?` (nullable annotation) since `int?` is still a value type.
3. If the stripped name starts with `(`, return true (tuple).
4. Return `s_valueTypes.Contains(strippedName)`.

**Algorithm for `IsReferenceType`**:
1. Inverse of `IsValueType` with an additional guard: if the type is unknown (not in the set and not a tuple), default to reference-type semantics (safer for nullable annotation — emitting `= null!` on an unknown type is harmless, but omitting it on a reference type causes CS8618).

**Algorithm for `IsNonNullableValueType`**:
1. Return true only if `IsValueType` is true AND the original `typeName` does not end with `?`.
2. This replaces `CarrierEmitter.IsNonNullableValueType` which currently uses its own private `ValueTypes` set.

#### Step 1.3.2: Add Unified `GetReaderMethod`

Consolidate the four divergent reader-method mappings into a single canonical implementation. The source of truth is the most complete version (`UsageSiteDiscovery`), extended with the `NeedsSignCast` awareness from `InterceptorCodeGenerator.Utilities`.

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

```csharp
internal static class TypeClassification
{
    public static string GetReaderMethod(string clrType);
    public static string GetReaderMethod(string clrType, out bool needsSignCast);
}
```

**Algorithm for `GetReaderMethod`**:
1. Strip nullable suffix (`?`) from `clrType`. Nullable types use the same reader method as their underlying type; the caller handles the nullable wrapping.
2. Switch on the stripped type name:
   - `"int"`, `"Int32"` -> `"GetInt32"`
   - `"long"`, `"Int64"` -> `"GetInt64"`
   - `"short"`, `"Int16"` -> `"GetInt16"`
   - `"byte"`, `"Byte"` -> `"GetByte"`
   - `"decimal"`, `"Decimal"` -> `"GetDecimal"`
   - `"double"`, `"Double"` -> `"GetDouble"`
   - `"float"`, `"Single"` -> `"GetFloat"`
   - `"string"`, `"String"` -> `"GetString"`
   - `"bool"`, `"Boolean"` -> `"GetBoolean"`
   - `"DateTime"` -> `"GetDateTime"`
   - `"DateTimeOffset"` -> `"GetFieldValue<DateTimeOffset>"` (not `GetValue` — this resolves the divergence between `UsageSiteDiscovery` and `InterceptorCodeGenerator.Utilities`)
   - `"Guid"` -> `"GetGuid"`
   - `"char"`, `"Char"` -> `"GetChar"`
   - `"TimeSpan"`, `"DateOnly"`, `"TimeOnly"` -> `"GetFieldValue<{stripped}>"`
   - `"uint"`, `"UInt32"` -> `"GetInt32"` (sign-mismatched; caller must cast)
   - `"ushort"`, `"UInt16"` -> `"GetInt16"` (sign-mismatched; caller must cast)
   - `"ulong"`, `"UInt64"` -> `"GetInt64"` (sign-mismatched; caller must cast)
   - `"sbyte"`, `"SByte"` -> `"GetByte"` (sign-mismatched; caller must cast)
   - `"byte[]"` -> `"GetValue"` (binary data; explicit cast to `byte[]` at call site)
   - Default -> `"GetValue"`
3. The overload with `out bool needsSignCast` sets the flag for unsigned/sbyte types. This replaces the separate `NeedsSignCast` call that some consumers currently make after calling their local `GetReaderMethod`.

**Key design decision**: `DateTimeOffset` uses `GetFieldValue<DateTimeOffset>` universally. This is the ADO.NET-recommended approach and avoids provider-specific quirks. The `InterceptorCodeGenerator.Utilities` version that used `GetValue` was introduced before the `UsageSiteDiscovery` version; the newer version is correct.

#### Step 1.3.3: Add Unified `IsUnresolvedTypeName` and `IsUnresolvedResultType`

Three private implementations exist with different semantics around `"object"`. The `ProjectionAnalyzer` version omits the `"object"` check, which means columns with `ClrType == "object"` pass through projection building as valid when they should be enriched from the entity registry.

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

```csharp
internal static class TypeClassification
{
    public static bool IsUnresolvedTypeName(string? typeName);
    public static bool IsUnresolvedResultType(string? resultTypeName);
}
```

**Algorithm for `IsUnresolvedTypeName`**:
1. Return true if `typeName` is null, empty, or whitespace.
2. Return true if `typeName` is exactly `"?"` (Roslyn error rendering for unresolved types).
3. Return true if `typeName` is exactly `"object"` (semantic model fallback for error types).
4. Return true if `typeName` starts with `"? "` (Roslyn error rendering with trailing info).
5. Return false otherwise.

This is the union of all three existing implementations. The `ProjectionAnalyzer` version's missing `"object"` check is a bug that this consolidation fixes.

**Algorithm for `IsUnresolvedResultType`**:
1. Call `IsUnresolvedTypeName` on the input. If true, return true.
2. If the input starts with `(` (tuple), parse the comma-separated elements and check each with `IsUnresolvedTypeName`. Handle named tuple elements by splitting on space and checking the type portion. Return true if any element is unresolved.
3. Return false otherwise.

This consolidates the tuple-aware logic from `PipelineOrchestrator.IsUnresolvedResultType` (lines 183-227) into the shared utility.

**Note on `IsUnresolvedAggregateType`**: This method in `ChainAnalyzer` (line 1290) is semantically identical to `IsUnresolvedTypeName` and should be replaced by a direct call to `TypeClassification.IsUnresolvedTypeName`.

#### Step 1.3.4: Add Unified `BuildTupleTypeName`

Three implementations exist that build tuple type strings from projected columns. They differ in how they handle unresolved elements: `ChainAnalyzer` returns empty string on failure, while `ProjectionAnalyzer` and `QuarryGenerator` substitute `"object"`. The nullable-suffix logic and `ItemN`-name elision are identical across all three.

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

```csharp
internal static class TypeClassification
{
    public static string BuildTupleTypeName(
        IReadOnlyList<ProjectedColumn> columns,
        bool fallbackToObject = true);
}
```

**Algorithm**:
1. For each column in the list:
   a. Resolve the type name: prefer `ClrType`, fall back to `FullClrType`.
   b. If the resolved type is unresolved (per `IsUnresolvedTypeName`):
      - If `fallbackToObject` is true, use `"object"`.
      - If false, return `""` (signals caller to skip the rebuild).
   c. If the column is nullable and the type name does not already end with `?`, append `?`.
   d. If the column has a named element (from a named tuple in source) and the name is not `ItemN` pattern, include the name after the type.
2. Join all elements with `, ` and wrap in `(` `)`.
3. If only one column, return just the type name (not a tuple).

**Callers and their `fallbackToObject` usage**:
- `ChainAnalyzer.BuildTupleResultTypeName`: passes `fallbackToObject: false` because it uses the empty-string return to skip re-enrichment.
- `ProjectionAnalyzer.BuildTupleTypeName`: passes `fallbackToObject: true` (default).
- `QuarryGenerator.BuildTupleResultTypeName`: passes `fallbackToObject: true` (default).

#### Step 1.3.5: Remove All Duplicated Implementations

After adding the unified methods to `TypeClassification`, systematically replace every call site:

| Original Location | Method | Replacement |
|---|---|---|
| `CarrierAnalyzer.cs:61` | `private static readonly HashSet<string> ValueTypes` | Delete. Use `TypeClassification.IsValueType` |
| `CarrierAnalyzer.NormalizeFieldType` | Inlined value-type check | Call `TypeClassification.IsValueType` and `IsReferenceType` |
| `CarrierAnalyzer.IsReferenceTypeName` | Private method | Delete. Use `TypeClassification.IsReferenceType` |
| `CarrierEmitter.cs:1253` | `private static readonly HashSet<string> ValueTypes` | Delete |
| `CarrierEmitter.IsNonNullableValueType` | Private method | Delete. Use `TypeClassification.IsNonNullableValueType` |
| `TerminalBodyEmitter.IsKnownValueTypeName` | Private method | Delete. Use `TypeClassification.IsValueType` |
| `ChainAnalyzer.GetReaderMethodForType` | Private method (12 types) | Delete. Use `TypeClassification.GetReaderMethod` |
| `UsageSiteDiscovery.GetReaderMethodForType` | Private method (18 types) | Delete. Use `TypeClassification.GetReaderMethod` |
| `InterceptorCodeGenerator.Utilities.GetReaderMethod` | Internal method (16 types) | Delete. Use `TypeClassification.GetReaderMethod` |
| `ProjectionAnalyzer.GetReaderMethodForAggregate` | Private method (6 types) | Delete. Use `TypeClassification.GetReaderMethod` |
| `ColumnInfo.GetReaderMethodFromType` | Method taking `ITypeSymbol` | Keep but delegate to `TypeClassification.GetReaderMethod` after extracting the type name string from the symbol |
| `ChainAnalyzer.IsUnresolvedTypeName` | Private method | Delete. Use `TypeClassification.IsUnresolvedTypeName` |
| `ChainAnalyzer.IsUnresolvedAggregateType` | Private method | Delete. Use `TypeClassification.IsUnresolvedTypeName` |
| `ProjectionAnalyzer.IsUnresolvedTypeName` | Private method | Delete. Use `TypeClassification.IsUnresolvedTypeName` |
| `PipelineOrchestrator.IsUnresolvedResultType` | Private method | Delete. Use `TypeClassification.IsUnresolvedResultType` |
| `ChainAnalyzer.BuildTupleResultTypeName` | Private method | Delete. Call `TypeClassification.BuildTupleTypeName(columns, fallbackToObject: false)` |
| `ProjectionAnalyzer.BuildTupleTypeName` | Private method | Delete. Call `TypeClassification.BuildTupleTypeName(columns)` |
| `QuarryGenerator.BuildTupleResultTypeName` | Private method | Delete. Call `TypeClassification.BuildTupleTypeName(columns)` |

### 1.4 Test Strategy

**Existing test coverage**: The cross-dialect SQL output tests (`SqlOutput/CrossDialect*.cs`, 18+ files) validate that the generated SQL and reader methods are correct for all supported types. These tests are the primary regression gate.

**New tests to add**:
- Unit tests for `TypeClassification.IsValueType` covering all entries plus edge cases: nullable types (`int?`), tuple types (`(int, string)`), unknown types (`MyStruct`), fully-qualified BCL names (`System.Int32`).
- Unit tests for `TypeClassification.GetReaderMethod` covering: all 30+ type mappings, nullable variants, the `needsSignCast` out parameter, and the `DateTimeOffset` -> `GetFieldValue<DateTimeOffset>` resolution.
- Unit tests for `TypeClassification.IsUnresolvedTypeName` covering: `null`, `""`, `"?"`, `"? SomeError"`, `"object"`, `"string"` (resolved), `"MyType"` (resolved).
- Unit tests for `TypeClassification.IsUnresolvedResultType` covering: simple unresolved, tuple with one unresolved element, tuple with named elements, fully resolved tuple.
- Unit tests for `TypeClassification.BuildTupleTypeName` covering: single column, multi-column, nullable columns, named elements, `ItemN` elision, unresolved with fallback, unresolved without fallback.

**Regression verification**: Run the full test suite. The generated interceptor output must not change. If `DateTimeOffset` reader methods change from `GetValue` to `GetFieldValue<DateTimeOffset>` in paths previously using the `InterceptorCodeGenerator.Utilities` version, this is an intentional correctness fix — verify with a targeted integration test that `DateTimeOffset` columns round-trip correctly.

### 1.5 Migration and Breaking Changes

**Generated output changes**: The `DateTimeOffset` reader method change (`GetValue` -> `GetFieldValue<DateTimeOffset>`) affects generated interceptor code for chains where `ChainAnalyzer` or `InterceptorCodeGenerator.Utilities` was the resolver. This is a correctness fix, not a regression. The behavior difference: `GetValue` returns `object` requiring a cast; `GetFieldValue<DateTimeOffset>` returns the typed value directly, which is both faster and more correct across ADO.NET providers.

**Public API changes**: None. `TypeClassification` is `internal`.

**`ProjectionAnalyzer.IsUnresolvedTypeName` behavior change**: Columns with `ClrType == "object"` will now be detected as unresolved and enriched from the entity registry. This may change the projection type names in edge cases where a column was previously left as `"object"`. This is a correctness fix.

---

## Phase 2: Site Parameter Resolution Helper

**Findings addressed**: #2 (Site parameter offset loop, 7 copies), #4 (Divergent offset in BuildParamConditionalMap)

**Estimated scope**: ~6 files modified, ~80 lines removed, ~25 lines added

### 2.1 Problem Statement

The pattern that computes a clause's parameters and their global offset within a chain is copy-pasted in 7 locations across `ClauseBodyEmitter`, `JoinBodyEmitter`, and `CarrierEmitter`. The `CarrierEmitter` version is the most comprehensive — it accounts for three distinct parameter sources (`UpdateSetPoco` columns, standard clause parameters, and `SetActionParameters`). The other 6 copies only account for standard clause parameters.

This divergence is currently safe because joins never co-occur with Set clauses in the same chain. However, this is a latent fragility. More immediately, the 7-way duplication means any bug fix to offset calculation must be applied in 7 places.

### 2.2 Target State

A single `ResolveSiteParams` method on a shared helper class. All 7+ call sites delegate to it. The comprehensive 3-source offset logic from `CarrierEmitter` becomes the canonical implementation.

### 2.3 Implementation Steps

#### Step 2.3.1: Create the Helper Method

Place on `TerminalEmitHelpers` since that class already serves as the shared helper for cross-emitter utilities.

**File**: `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`

```csharp
internal static class TerminalEmitHelpers
{
    internal static (List<QueryParameter> SiteParams, int GlobalOffset) ResolveSiteParams(
        AssembledPlan chain,
        string siteUniqueId);
}
```

**Algorithm**:
1. Initialize `globalParamOffset = 0`.
2. Iterate through `chain.GetClauseEntries()`.
3. For each clause entry:
   a. If `clause.Site.UniqueId == siteUniqueId`, extract this clause's parameters:
      - If `clause.Site.Clause != null`, iterate its parameters and map each to `chain.ChainParameters[globalParamOffset + i]` (with bounds checking).
      - Build the `siteParams` list from these mapped parameters.
      - Return `(siteParams, globalParamOffset)`.
   b. Otherwise, accumulate the offset based on clause type:
      - If `clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null`: add `UpdateInfo.Columns.Count`.
      - Else if `clause.Site.Clause != null`: add `clause.Site.Clause.Parameters.Count`.
      - Else if `clause.Site.Kind == InterceptorKind.UpdateSetAction && clause.Site.Bound.Raw.SetActionParameters != null`: add `SetActionParameters.Count`.
4. If the site is not found, return `(empty list, globalParamOffset)`.

The key difference from the 6 simpler copies: step 3b handles all three parameter source types, not just standard clause parameters.

#### Step 2.3.2: Replace All Call Sites

| Location | Current Code | Replacement |
|---|---|---|
| `ClauseBodyEmitter.EmitSelect` lines 214-227 | Inline loop (clause params only) | `var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);` |
| `JoinBodyEmitter.EmitJoin` lines 78-91 | Inline loop (clause params only) | Same call |
| `JoinBodyEmitter.EmitJoin` lines 128-141 | Inline loop (clause params only) | Same call |
| `JoinBodyEmitter.EmitJoinedWhere` lines 222-235 | Inline loop (clause params only) | Same call |
| `JoinBodyEmitter.EmitJoinedOrderBy` lines 345-358 | Inline loop (clause params only) | Same call |
| `JoinBodyEmitter.EmitJoinedSelect` lines 416-429 | Inline loop (clause params only) | Same call |
| `CarrierEmitter.EmitCarrierClauseBody` lines 371-383 | Inline loop (3-source) | Same call (this was the source of truth) |

Additionally, update the offset computation in:
- `TerminalEmitHelpers.BuildParamConditionalMap` lines 132-139
- `TerminalEmitHelpers.EmitDiagnosticClauseArray` lines 275-281

These two methods compute offsets for diagnostic purposes. They currently only account for clause parameters. After this change, they also correctly account for `UpdateSetPoco` and `UpdateSetAction` parameter counts, which is a correctness improvement for diagnostics on update chains.

### 2.4 Test Strategy

**Existing coverage**: Cross-dialect tests cover Select, Where, OrderBy, GroupBy, Having, Join, Update Set, and Insert chains. The `ResolveSiteParams` extraction is a pure refactor — identical behavior, different code structure.

**New tests**: Add a unit test for `ResolveSiteParams` directly:
- Chain with only standard clause parameters (Where + OrderBy).
- Chain with `UpdateSetPoco` parameters preceding a `Where` clause.
- Chain with `UpdateSetAction` parameters.
- Chain where the target site is the first clause (offset = 0).
- Chain where the target site is not found (returns empty).

**Regression verification**: Run the full test suite. Generated output must not change for any existing test case.

### 2.5 Migration and Breaking Changes

**Generated output changes**: None. The offset computation produces identical results for all existing query kinds.

**Diagnostic output changes**: `BuildParamConditionalMap` and `EmitDiagnosticClauseArray` may produce different (more correct) diagnostic metadata for update chains with conditional Set clauses. This is unlikely to affect any consumer since `ToDiagnostics()` is a debugging aid.

---

## Phase 3: ConditionalInfo Architecture Split

**Findings addressed**: #4 (ConditionalInfo structural flaw — root cause of 3 post-V0.2.0 bugs)

**Estimated scope**: ~5 files modified, ~60 lines added, ~40 lines removed

### 3.1 Problem Statement

`ConditionalInfo` is eagerly attached to every call site inside any `if`/`else`/`try`/`catch` block during Stage 1 (Discovery). Its semantic meaning ("this clause is genuinely conditionally included in the chain") only applies when the clause is deeper than the chain's execution terminal. Three downstream consumers independently re-derive this distinction:

1. **ChainAnalyzer** (commit `a5aaab8`): Uses `baselineDepth` to compute relative nesting depth. Only clauses with `relativeDepth > 0` are conditional.
2. **AssembledPlan.GetClauseEntries** (commit `abb1141`): Uses `bitIndex.HasValue` instead of `ConditionalInfo != null` to determine whether a clause entry is conditional.
3. **UsageSiteDiscovery.ComputeChainId** (commit `88c9eb5`): Uses innermost statement instead of outermost to avoid grouping mutually exclusive branches into the same chain.

Each consumer was fixed only after a user-facing bug was found. Any new consumer of `ConditionalInfo` faces the same trap.

### 3.2 Core Concepts

**Nesting Context**: Structural metadata about where a call site lives in the control flow graph. Always present for sites inside control flow. Contains: nesting depth, containing statement kind (`if`/`else`/`try`/`catch`), condition text, branch kind. This is what `DetectConditionalAncestor` currently computes.

**Conditional Inclusion**: A chain-level determination that a clause is genuinely conditionally included relative to the chain's execution terminal. This can only be determined after all clauses in a chain are known and the execution terminal's depth is established. A clause at or below the terminal's nesting depth is NOT conditional — the entire chain is simply inside nested control flow.

**The fix**: Separate the two concepts so that consumers read an unambiguous `IsGenuinelyConditional` flag instead of re-deriving conditionality from raw nesting metadata.

### 3.3 Implementation Steps

#### Step 3.3.1: Rename Discovery-Time Data

In `RawCallSite`, rename `ConditionalInfo` to `NestingContext`. This is a mechanical rename. The data shape does not change — it still contains `NestingDepth`, `ConditionText`, `BranchKind`, and the containing statement reference.

**File**: `src/Quarry.Generator/IR/RawCallSite.cs`

```csharp
// Before
public ConditionalInfo? ConditionalInfo { get; }

// After
public NestingContext? NestingContext { get; }
```

**File**: `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`

Rename `DetectConditionalAncestor` to `DetectNestingContext`. The method body does not change — it still walks ancestor syntax nodes to find containing control flow statements.

```csharp
// Before
private static ConditionalInfo? DetectConditionalAncestor(SyntaxNode node);

// After
private static NestingContext? DetectNestingContext(SyntaxNode node);
```

#### Step 3.3.2: Add Chain-Level Conditional Resolution

In `ChainAnalyzer`, after grouping sites into chains and identifying the execution terminal, resolve conditionality for each clause. This replaces the current inline `baselineDepth` logic.

**File**: `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

```csharp
private static void ResolveConditionalInclusion(
    IReadOnlyList<TranslatedCallSite> chainSites,
    TranslatedCallSite executionSite);
```

**Algorithm**:
1. Compute `baselineDepth = executionSite.Bound.Raw.NestingContext?.NestingDepth ?? 0`.
2. For each clause site in the chain:
   a. If `site.Bound.Raw.NestingContext == null`, the site is not in control flow. Set `site.IsGenuinelyConditional = false`.
   b. Compute `relativeDepth = site.Bound.Raw.NestingContext.NestingDepth - baselineDepth`.
   c. If `relativeDepth <= 0`, the site is at or above the terminal's depth. Set `site.IsGenuinelyConditional = false`.
   d. If `relativeDepth > 0`, the site is genuinely conditional. Set `site.IsGenuinelyConditional = true`.
3. This single pass replaces the scattered depth-comparison logic.

The `IsGenuinelyConditional` flag is stored on `TranslatedCallSite` (or a new `AnalyzedCallSite` wrapper) and is the single source of truth for all downstream consumers.

#### Step 3.3.3: Update Downstream Consumers

**`ChainAnalyzer` conditional bit assignment** (currently lines 338-347):
- Replace `condInfo.NestingDepth - baselineDepth > 0` check with `site.IsGenuinelyConditional`.
- Remove the inline `baselineDepth` computation.

**`AssembledPlan.GetClauseEntries`** (currently line ~140):
- Currently uses `bitIndex.HasValue` as a proxy for "is conditional". After this change, it can directly read `IsGenuinelyConditional` from the clause site. The `bitIndex` is still assigned by `ChainAnalyzer` for genuinely conditional clauses.
- The `bitIndex.HasValue` check remains correct (it's a consequence of `IsGenuinelyConditional`), but the semantic meaning is clearer.

**`UsageSiteDiscovery.ComputeChainId`** (currently uses innermost statement):
- No change needed. `ComputeChainId` uses `NestingContext` (renamed from `ConditionalInfo`) for scope computation, which is independent of the conditional-inclusion question. The fix in commit `88c9eb5` was about chain grouping, not conditional bit assignment.

#### Step 3.3.4: Rename the Model Type

Rename `ConditionalInfo` class to `NestingContext` to match the renamed property. Update all references.

**File**: `src/Quarry.Generator/Models/ConditionalInfo.cs` (or wherever the type is defined)

```csharp
// Before
internal sealed class ConditionalInfo : IEquatable<ConditionalInfo>
{
    public int NestingDepth { get; }
    public string? ConditionText { get; }
    public BranchKind BranchKind { get; }
    // ...
}

// After
internal sealed class NestingContext : IEquatable<NestingContext>
{
    public int NestingDepth { get; }
    public string? ConditionText { get; }
    public BranchKind BranchKind { get; }
    // ...
}
```

### 3.4 Test Strategy

**Existing coverage**: The regression tests in `SqlOutput/CrossDialect*.cs` include chains with conditional Where clauses (inside `if` blocks). The `ConditionalCarrierTests.cs` file specifically tests mask-gated parameter binding for conditional chains. These are the primary regression gate.

**New tests to add**:
- A test where the execution terminal is inside an `if` block and all clauses are at the same depth. Verify that no clause is treated as conditional (no mask bits assigned). This is the exact scenario that caused the `a5aaab8` false positive.
- A test where a chain spans mutually exclusive `if`/`else` branches. Verify correct chain ID computation (no merge) and correct conditional bit assignment. This covers the `88c9eb5` scenario.
- A test with deeply nested control flow (3+ levels) where only the innermost clause is conditional. Verify exactly one mask bit is assigned.

**Regression verification**: Run full suite. The generated SQL variants and mask values must not change.

### 3.5 Migration and Breaking Changes

**Generated output changes**: None. The conditional bit assignment produces identical results. The refactoring only changes how the generator internally determines conditionality.

**Model rename**: `ConditionalInfo` -> `NestingContext` is an internal type. No public API impact. Incremental generator caching may invalidate for the first build after the rename (different type names in equality checks), but subsequent builds will cache normally.

---

## Phase 4: Enum and Parameter Enrichment Consolidation

**Findings addressed**: #7 (Enum underlying type hardcoding), #8 (Parameter enrichment duplication)

**Estimated scope**: ~3 files modified, ~30 lines changed

### 4.1 Problem Statement

**Enum hardcoding**: `InterceptorCodeGenerator.GetColumnValueExpression` (line 352) hardcodes `(int)` for all enum-to-DB casts. `TerminalEmitHelpers.GetParameterValueExpression` correctly uses `param.EnumUnderlyingType`. An enum with `byte` underlying type gets `(int)entity.Status` in the insert path but `(byte)capturedValue` in the where-clause path. This is a latent bug for enums with non-`int` underlying types.

**Enrichment duplication**: `ChainAnalyzer.EnrichParametersFromColumns` (Where/Having) and `EnrichSetParametersFromColumns` (Set) contain identical 3-line enrichment blocks:
```csharp
var isEnum = col.IsEnum || p.IsEnum;
var isSensitive = col.Modifiers.IsSensitive || p.IsSensitive;
var enumUnderlying = p.EnumUnderlyingType ?? (isEnum && p.EnumUnderlyingType == null ? "int" : null);
```

Adding a new enrichment property (e.g., custom type mapping propagation for parameters) requires updating both methods.

### 4.2 Implementation Steps

#### Step 4.2.1: Fix Enum Underlying Type in Insert Path

Propagate `EnumUnderlyingType` through `InsertColumn` so that `GetColumnValueExpression` can use it instead of hardcoding `"int"`.

**File**: `src/Quarry.Generator/Models/InsertInfo.cs`

Add `EnumUnderlyingType` to `InsertColumn` if not already present. The `SchemaParser` already resolves the underlying type via `ColumnInfo.GetTypeMetadata`. The `InsertInfo.FromEntityInfo` factory needs to propagate this field from `ColumnInfo`.

```csharp
internal sealed class InsertColumn
{
    // existing properties...
    public string? EnumUnderlyingType { get; }
}
```

**File**: `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs`

Update `GetColumnValueExpression` to accept and use the underlying type:

```csharp
internal static string GetColumnValueExpression(
    string entityVar, string propertyName, bool isForeignKey,
    string? customTypeMappingClass, bool isBoolean, bool isEnum,
    bool isNullable, bool convertBool,
    string? enumUnderlyingType = "int");  // new parameter with backward-compatible default
```

**Algorithm change**: Replace `(int)` cast with `({enumUnderlyingType})` cast. The default `"int"` preserves backward compatibility for callers that don't pass the parameter.

Update all call sites in `CarrierEmitter.EmitCarrierInsertTerminal` and `TerminalBodyEmitter.EmitBatchInsertCarrierTerminal` to pass `col.EnumUnderlyingType ?? "int"`.

#### Step 4.2.2: Extract Shared Parameter Enrichment Helper

**File**: `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

```csharp
private static QueryParameter EnrichParameterFromColumn(
    QueryParameter param, ColumnInfo col);
```

**Algorithm**:
1. Merge enum flag: `isEnum = col.IsEnum || param.IsEnum`.
2. Merge sensitive flag: `isSensitive = col.Modifiers.IsSensitive || param.IsSensitive`.
3. Resolve enum underlying type: `enumUnderlying = param.EnumUnderlyingType ?? (isEnum ? "int" : null)`.
4. Return `param.WithEnrichment(isEnum, isSensitive, enumUnderlying)`.

Replace the inline enrichment blocks in both `EnrichParametersFromColumns` and `EnrichSetParametersFromColumns` with calls to this helper. The column-matching logic (SqlExpr tree walking vs SetActionAssignment property name matching) remains in each method — only the per-parameter enrichment is shared.

### 4.3 Test Strategy

**New tests for enum underlying type fix**:
- Add a schema column with `Col<ByteEnum> Status` where `ByteEnum : byte`.
- Verify the generated insert interceptor emits `(byte)entity.Status` instead of `(int)entity.Status`.
- Cross-dialect: verify the correct `DbType` is set for byte-backed enums.

**Regression verification**: Run full suite. Existing enum tests (which all use default `int` underlying type) must produce identical output.

### 4.4 Migration and Breaking Changes

**Generated output changes**: Insert/update interceptors for enums with non-`int` underlying types will change from `(int)` to the correct underlying type cast. This is a correctness fix. Enums with `int` underlying type (the vast majority) are unaffected.

**InsertColumn model change**: Adding `EnumUnderlyingType` changes the `InsertColumn` equality/hash, which may cause incremental generator cache invalidation on the first build. Subsequent builds cache normally.

---

## Phase 5: Emitter Consolidation

**Findings addressed**: #9 (Extraction + binding duplication), #11 (Emitter-specific deduplication)

**Estimated scope**: ~6 files modified, ~150 lines removed, ~40 lines added

### 5.1 Problem Statement

Multiple emitters contain copy-pasted patterns that should be shared helpers. The most impactful are:

1. **Compound expression wrapping**: `ve.Contains(' ') || ve.Contains('(')` appears in `CarrierEmitter.EmitCarrierClauseBody` and `CarrierEmitter.EmitCarrierParamBindings` independently.
2. **Extraction locals + param binding**: `EmitCarrierClauseBody` inlines its own extraction logic instead of delegating to the existing `EmitExtractionLocalsAndBindParams` + `EmitCarrierParamBindings`.
3. **`GetJoinedBuilderTypeName`**: Private copy in `JoinBodyEmitter` duplicates `InterceptorCodeGenerator`'s `internal static` version.
4. **Terminal return type + executor switch**: Identical `site.Kind switch` expressions in `EmitReaderTerminal` and `EmitJoinReaderTerminal`.
5. **`isBrokenTuple` detection**: `resultType.Contains("object") && resultType.StartsWith("(")` repeated 4 times.
6. **Insert parameter binding**: Nearly identical entity-to-parameter binding in single insert (`CarrierEmitter.EmitCarrierInsertTerminal`) and batch insert (`TerminalBodyEmitter.EmitBatchInsertCarrierTerminal`).
7. **Delegate param name sourcing**: `EmitCarrierClauseBody` accepts `delegateParamName` as a method parameter instead of reading from the extraction plan.

### 5.2 Implementation Steps

#### Step 5.2.1: Extract Compound Expression Wrapping Helper

**File**: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

```csharp
private static string FormatCarrierFieldAssignment(int globalIndex, string valueExpression, bool isCaptured);
```

**Algorithm**:
1. If `isCaptured` (extraction locals already typed, no cast needed):
   - If `valueExpression` contains a space or `(`, wrap: `__c.P{globalIndex} = ({valueExpression})!;`
   - Otherwise: `__c.P{globalIndex} = {valueExpression}!;`
2. If not captured, the caller handles casting separately (this helper only covers the captured path).

Replace the duplicated wrapping logic in `EmitCarrierClauseBody` (line 442-448) and `EmitCarrierParamBindings` (line 1129) with calls to this helper.

#### Step 5.2.2: Consolidate Extraction in `EmitCarrierClauseBody`

`EmitCarrierClauseBody` currently inlines ~50 lines of extraction-local emission and parameter binding. After Phase 2 provides `ResolveSiteParams`, this method can delegate to the existing `EmitExtractionLocalsAndBindParams` + `EmitCarrierParamBindings` pipeline.

**Changes to `EmitCarrierClauseBody`**:
1. Replace the inline offset computation with `TerminalEmitHelpers.ResolveSiteParams(chain, site.UniqueId)`.
2. Replace the inline extraction-local emission and param-binding block with a call to `EmitExtractionLocalsAndBindParams(sb, carrier, site, siteParams, globalParamOffset)`.
3. Read `delegateParamName` from the extraction plan (`carrier.GetExtractionPlan(site.UniqueId)?.DelegateParamName ?? "func"`) instead of accepting it as a method parameter. This makes the extraction plan the single source of truth.

**Signature change**:

```csharp
// Before
internal static void EmitCarrierClauseBody(
    StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
    TranslatedCallSite site, int? clauseBit, bool isFirstInChain,
    string concreteBuilderType, string returnInterface,
    bool hasResolvableCapturedParams, List<CachedExtractorField> methodFields,
    string delegateParamName = "func");

// After
internal static void EmitCarrierClauseBody(
    StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
    TranslatedCallSite site, int? clauseBit, bool isFirstInChain,
    string concreteBuilderType, string returnInterface,
    bool hasResolvableCapturedParams, List<CachedExtractorField> methodFields);
```

All callers currently pass `delegateParamName` only for `UpdateSetAction` (passing `"action"`). After this change, the extraction plan's `DelegateParamName` (which is already set to `"action"` for `UpdateSetAction` clauses by `CarrierAnalyzer.GetDelegateParamName`) is used instead.

#### Step 5.2.3: Delete `JoinBodyEmitter.GetJoinedBuilderTypeName`

Delete the private method at `JoinBodyEmitter.cs:24-33`. Replace all calls within `JoinBodyEmitter` with `InterceptorCodeGenerator.GetJoinedBuilderTypeName(entityCount)`, which is already `internal static` and contains identical logic.

#### Step 5.2.4: Extract Terminal Return Type and Executor Helpers

**File**: `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`

```csharp
internal static string ResolveTerminalReturnType(
    InterceptorKind kind, string resultType, string scalarTypeArg,
    bool isValueType);

internal static string ResolveCarrierExecutorMethod(
    InterceptorKind kind, string resultType, string scalarTypeArg);
```

**`ResolveTerminalReturnType` algorithm**:
1. Compute `firstOrDefaultSuffix = isValueType ? "" : "?"`.
2. Switch on `kind`:
   - `ExecuteFetchAll` -> `Task<List<{resultType}>>`
   - `ExecuteFetchFirst` -> `Task<{resultType}>`
   - `ExecuteFetchFirstOrDefault` -> `Task<{resultType}{firstOrDefaultSuffix}>`
   - `ExecuteFetchSingle` -> `Task<{resultType}>`
   - `ExecuteScalar` -> `Task<{scalarTypeArg}>`
   - `ToAsyncEnumerable` -> `IAsyncEnumerable<{resultType}>`

**`ResolveCarrierExecutorMethod` algorithm**:
1. Switch on `kind`:
   - `ExecuteFetchAll` -> `ExecuteCarrierWithCommandAsync<{resultType}>`
   - `ExecuteFetchFirst` -> `ExecuteCarrierFirstWithCommandAsync<{resultType}>`
   - etc.

Replace the duplicated switch expressions in `EmitReaderTerminal` (lines 49-69) and `EmitJoinReaderTerminal` (lines 140-155).

#### Step 5.2.5: Extract `IsBrokenTupleType` Helper

**File**: `src/Quarry.Generator/Generation/InterceptorCodeGenerator.Utilities.cs` (already contains `SanitizeTupleResultType`)

```csharp
internal static bool IsBrokenTupleType(string resultType);
```

**Algorithm**: `return resultType.StartsWith("(") && resultType.Contains("object");`

Replace the 4 inline expressions in:
- `ClauseBodyEmitter.EmitOrderBy` line 111
- `ClauseBodyEmitter.EmitGroupBy` line 265
- `JoinBodyEmitter.EmitJoinedWhere` line 190
- `JoinBodyEmitter.EmitJoinedOrderBy` line 281

#### Step 5.2.6: Extract Insert Column Parameter Binding Helper

**File**: `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`

```csharp
internal static void EmitInsertColumnBinding(
    StringBuilder sb, InsertInfo insertInfo, SqlDialect dialect,
    string entityVar, string indent);
```

**Algorithm**:
1. Compute `convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(dialect)`.
2. For each column in `insertInfo.Columns`:
   a. Compute `needsIntType = col.IsEnum || (col.IsBoolean && convertBool)`.
   b. Compute `valueExpr = InterceptorCodeGenerator.GetColumnValueExpression(entityVar, col.PropertyName, ...)`.
   c. Emit: parameter creation, name assignment, value assignment with null coalescing to `DBNull.Value`, optional `DbType` assignment, parameter add.
3. The `entityVar` parameter allows the caller to specify the entity source (`"__c.Entity!"` for single insert, `"__entity"` for batch insert loop body).
4. The `indent` parameter handles the nesting difference (batch inserts are inside a `for` loop with extra indentation).

Replace the duplicated binding loops in `CarrierEmitter.EmitCarrierInsertTerminal` (lines 993-1005) and `TerminalBodyEmitter.EmitBatchInsertCarrierTerminal` (lines 608-622).

### 5.3 Test Strategy

**Existing coverage**: All emitter changes are covered by the cross-dialect test suite and carrier generation tests. These are pure refactors — the generated C# source must not change.

**Verification approach**: For each step, run the full test suite before and after. Diff the generated interceptor files in the sample webapp to confirm byte-for-byte identical output.

### 5.4 Migration and Breaking Changes

**Generated output changes**: None. All steps are internal refactors.

**Signature change on `EmitCarrierClauseBody`**: Removing the `delegateParamName` parameter is a source-breaking change for any internal callers. All callers are within the generator project, so this is a self-contained change. Verify no callers pass a non-default value for `delegateParamName` that differs from what the extraction plan would provide.

---

## Phase 6: Pipeline Error Handling

**Findings addressed**: #10 (Silent exception swallowing in pipeline stages)

**Estimated scope**: ~1 file modified, ~20 lines changed

### 6.1 Problem Statement

`QuarryGenerator.cs` lines 102-115 contain `catch { return ... }` blocks in the incremental pipeline's `SelectMany`/`Select` lambdas. When `CallSiteBinder.Bind` or `CallSiteTranslator.Translate` throw, the call site is silently dropped or returned with incomplete data. The user sees no error — the chain simply doesn't get intercepted, falling through to the runtime `InvalidOperationException` from the carrier base class.

This made the `abb1141` crash (NullReferenceException in `AssembledPlan.GetClauseEntries`) much harder to diagnose because the stack trace was swallowed.

### 6.2 Core Concepts

**Incremental generator pipeline constraints**: The `SelectMany`/`Select` combinators in `IIncrementalGenerator` don't have access to `SourceProductionContext` (which is needed to report diagnostics). Diagnostics can only be reported in `RegisterSourceOutput`/`RegisterImplementationSourceOutput` callbacks.

**Side-channel pattern**: Capture exceptions during pipeline transforms and carry them as data through the pipeline. Report them as QRY900 diagnostics in the output stage.

### 6.3 Implementation Steps

#### Step 6.3.1: Add Error Carrier to Pipeline Output

Extend the pipeline's data flow to carry exceptions alongside successfully transformed call sites.

**File**: `src/Quarry.Generator/QuarryGenerator.cs`

**Approach**: Wrap the `catch` blocks to capture the exception and produce a diagnostic-carrying result instead of silently returning a fallback.

```csharp
// Current (silent swallow):
catch { return ImmutableArray<BoundCallSite>.Empty; }

// After (capture and carry):
catch (Exception ex)
{
    // Return a sentinel BoundCallSite (or add to a collected errors list)
    // that carries the exception message + stack trace for QRY900 reporting.
    return ImmutableArray<BoundCallSite>.Empty;
    // The exception is captured in a concurrent bag accessible to the output stage.
}
```

**Preferred implementation**: Since the incremental pipeline stages are pure transforms, the cleanest approach is to use a `Result<T, Diagnostic>` pattern where each transform step returns either a successful value or a diagnostic. However, this requires wrapping every `ImmutableArray<T>` in the pipeline with error metadata.

**Simpler alternative**: Keep the `catch` blocks but add `System.Diagnostics.Debug.WriteLine` for development-time visibility, and also capture the exception info into the `RawCallSite` or `BoundCallSite` as an `ErrorMessage` field. When the output stage encounters a site with a non-null `ErrorMessage`, it reports QRY900 with the captured message and stack trace.

```csharp
internal sealed class BoundCallSite
{
    // Existing properties...
    public string? PipelineError { get; }
    public string? PipelineErrorStackTrace { get; }
}
```

#### Step 6.3.2: Report Captured Errors in Output Stage

In `EmitFileInterceptorsNewPipeline` (the `RegisterImplementationSourceOutput` callback), check each site for `PipelineError` and report it as QRY900.

**File**: `src/Quarry.Generator/QuarryGenerator.cs`

```csharp
// In EmitFileInterceptorsNewPipeline, before grouping:
foreach (var site in translatedSites)
{
    if (site.PipelineError != null)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.InternalError,
            site.Location,
            site.PipelineError + Environment.NewLine + site.PipelineErrorStackTrace));
    }
}
```

### 6.4 Test Strategy

**New tests**: This is difficult to unit-test directly because pipeline errors require specific internal failure conditions. Instead:
- Add a test that verifies `BoundCallSite` with a non-null `PipelineError` produces a QRY900 diagnostic in the output stage.
- Optionally, add a test with a deliberately malformed `RawCallSite` that causes `CallSiteBinder.Bind` to throw, and verify the error surfaces as QRY900 rather than being silently swallowed.

**Regression verification**: Run full suite. No new diagnostics should appear (all existing call sites bind and translate successfully).

### 6.5 Migration and Breaking Changes

**Generated output changes**: None for successful compilations. Failed compilations may now show QRY900 diagnostics where previously the chain was silently dropped.

**User-visible behavior change**: This is strictly an improvement. Users who previously saw mysterious "interceptor not generated" runtime failures will now see a compile-time QRY900 diagnostic with a stack trace pointing to the internal error.

---

## Phase 7: Lower Priority Cleanup

**Findings addressed**: #12 (Captured variable type resolver), #13 (SubqueryPredicateParams walker), #14 (Delegate param name sourcing), #15 (Legacy CarrierStrategy), #16 (Appropriate as-is)

**Estimated scope**: Variable per sub-phase. Each can be done independently.

### 7.1 Captured Variable Type Resolver Architecture (#12)

**Status**: The `DisplayClassNameResolver` has grown to ~840 lines with ~550 lines dedicated to type resolution fallbacks added across commits `4778669` and `4fbbe1a`. This is the generator's most complex and fragile subsystem.

**Current architecture**: When the semantic model reports `TypeKind.Error` for a captured variable (because its type depends on a generator-produced entity that doesn't exist yet), `DisplayClassNameResolver` performs recursive syntax walking through variable declarations, initializers, chain invocations, and member accesses to reconstruct the type.

**Proposed improvement (Option B — lower disruption)**:

Extract all `TryResolve*` methods from `DisplayClassNameResolver` into a dedicated `ChainResultTypeResolver` class. Give it explicit access to `EntityRegistry` and the compilation's syntax trees.

**File**: `src/Quarry.Generator/Parsing/ChainResultTypeResolver.cs`

```csharp
internal static class ChainResultTypeResolver
{
    internal static string? TryResolveVariableType(
        ILocalSymbol variable,
        SemanticModel semanticModel,
        EntityRegistry registry,
        Compilation compilation);

    internal static string? TryResolveChainResultType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        EntityRegistry registry);

    internal static string? TryResolveChainInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        EntityRegistry registry);

    internal static string? ResolveProjectionType(
        LambdaExpressionSyntax selectLambda,
        string entityTypeName,
        EntityRegistry registry);
}
```

**Longer-term ideal (Option A — higher disruption, higher payoff)**:

Compute chain result types during chain assembly in `ChainAnalyzer` (which already knows the projection) and store them in a lookup table (`Dictionary<string, string>` mapping chainId to fully-qualified result type). `DisplayClassEnricher` queries this table instead of performing syntax walking.

This inverts the dependency: instead of Discovery trying to predict what Analysis will compute, Analysis computes the result and Discovery looks it up. The challenge is ordering — `DisplayClassEnricher` runs at Stage 2.5 (before Analysis at Stage 4). This requires either a two-pass approach or deferring display class enrichment to post-analysis.

**Recommendation**: Start with Option B (extract to `ChainResultTypeResolver`) as it is a safe refactor. Evaluate Option A as a follow-up if the resolver continues to accumulate fallback cases.

### 7.2 Subquery Predicate Parameter Walker Unification (#13)

**File**: `src/Quarry.Generator/IR/SqlExprClauseTranslator.cs`

`ExtractParameters` and `ExtractSubqueryPredicateParams` are near-duplicate recursive tree walkers. The subquery version intentionally differs in two ways:
1. Does not parameterize string/char literals (they're inlined in subquery predicates).
2. Skips enum/constant member accesses (compile-time constants in subqueries).

But the tree-walking boilerplate (`BinaryOpExpr`, `UnaryOpExpr`, `FunctionCallExpr` traversal) is copy-pasted. The subquery version also misses `InExpr`, `IsNullCheckExpr`, and `RawCallExpr` node types.

**Proposed refactor**: Merge into a single method with a mode parameter.

```csharp
private static SqlExpr ExtractParametersCore(
    SqlExpr expr,
    List<ParameterInfo> parameters,
    ref int paramIndex,
    ExtractionMode mode);

internal enum ExtractionMode
{
    Standard,
    SubqueryPredicate
}
```

**Algorithm changes**:
1. `CapturedValueExpr` handling: In `SubqueryPredicate` mode, skip nodes where `SyntaxText.Contains('.')` (enum/constant detection). In `Standard` mode, always parameterize.
2. `LiteralExpr` handling: In `SubqueryPredicate` mode, leave string/char literals inline. In `Standard` mode, parameterize them.
3. `InExpr`, `IsNullCheckExpr`, `RawCallExpr`: Handle in both modes (the subquery version currently misses these; adding them prevents silent parameter extraction failures if subqueries become more complex).
4. All other node types (`BinaryOpExpr`, `UnaryOpExpr`, `FunctionCallExpr`, `LikeExpr`, `SubqueryExpr`): Identical handling in both modes — recursive traversal.

### 7.3 Legacy CarrierStrategy Cleanup (#15)

**File**: `src/Quarry.Generator/CodeGen/CarrierStrategy.cs`

This file defines `CarrierStrategy` (the old carrier plan model) alongside `CarrierField` (which conflicts with `Models.CarrierField`) and `CarrierStaticField`. The new pipeline uses `CarrierPlan` (in `Models/`) as the carrier analysis output.

`CarrierParameter` in this file is still actively used by both old and new pipelines.

**Steps**:
1. Audit all references to `CarrierStrategy`, `CarrierStrategy.CarrierField`, and `CarrierStaticField`. Determine if any are still in active code paths.
2. If `CarrierStrategy` is only used in `CarrierEmitter.EmitClassDeclaration` (the old emission path), determine if that method is still called. If not, delete both.
3. Move `CarrierParameter` to `Models/CarrierParameter.cs` or a new dedicated file.
4. Delete the orphaned types.

**Note**: This cleanup should only be done after confirming no code path references `CarrierStrategy`. Use `Grep` to find all references before deleting.

### 7.4 Execution/NonQuery Terminal Near-Duplication

**File**: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

`EmitCarrierExecutionTerminal` (line 809) and `EmitCarrierNonQueryTerminal` (line 836) share identical preamble, SQL logging, parameter logging, timeout resolution, and command binding. The only difference is the final executor call line.

**Proposed refactor**: Have `EmitCarrierNonQueryTerminal` call `EmitCarrierExecutionTerminal` with the appropriate executor method and `null` reader expression (which `EmitCarrierExecutionTerminal` already handles by omitting the reader argument).

```csharp
internal static void EmitCarrierNonQueryTerminal(
    StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
{
    EmitCarrierExecutionTerminal(sb, carrier, chain,
        readerExpression: null,
        executorMethod: "ExecuteCarrierNonQueryWithCommandAsync");
}
```

### 7.5 Findings Confirmed as Appropriate (#16)

The following post-V0.2.0 changes were reviewed and found to be well-scoped. No action needed:

- **`d28c862` (IEnumerable collection support)**: Touched 16 files comprehensively across 6+ stages. Added `IsEnumerableOnlyCollection` with proper interface hierarchy checking, `CollectionHelper.Materialize` as runtime fallback, `QueryParameter.WithEnrichment` for clean immutable updates, and empty collection guard. This is the gold standard for how a cross-cutting feature should be added.

- **`421f3f7` / `451721f` (visibility + sign-cast fixes)**: Appropriate narrow fixes. The sign-cast fix was properly deduplicated into `TypeClassification.NeedsSignCast` during code review. Visibility fixes (`[EditorBrowsable(Never)]`) are inherently per-type decisions.

- **`06ddfdb` (FirstOrDefault nullable)**: Added `IsValueTypeResult` flag with dual-strategy (semantic model at discovery, string fallback at emission). Properly scoped to the discovery-emission boundary.

### 7.6 Test Strategy for Phase 7

Each sub-phase has its own test approach:

- **7.1 (ChainResultTypeResolver)**: Existing `DisplayClassEnricherTests.cs` covers the type resolution paths. New tests should cover the extracted methods in isolation: given a chain invocation expression, verify the resolved type matches expectations.
- **7.2 (Subquery walker)**: Add test cases for subquery predicates containing `InExpr` and `RawCallExpr` to verify parameters are correctly extracted after the merge.
- **7.3 (CarrierStrategy cleanup)**: No new tests — pure dead code removal. Run full suite to verify nothing breaks.
- **7.4 (Execution/NonQuery merge)**: Run full suite. Generated output must not change.

### 7.7 Migration and Breaking Changes

All Phase 7 changes are internal refactors with no generated output changes. The `CarrierStrategy` cleanup may affect incremental caching if any remaining code paths reference the removed types, so verify thoroughly before merging.

---

## Appendix A: File Impact Summary

| File | Phases | Changes |
|---|---|---|
| `Utilities/TypeClassification.cs` | 1 | Add IsValueType, IsReferenceType, IsNonNullableValueType, GetReaderMethod, IsUnresolvedTypeName, IsUnresolvedResultType, BuildTupleTypeName |
| `CodeGen/CarrierAnalyzer.cs` | 1 | Remove ValueTypes HashSet, NormalizeFieldType inlined check, IsReferenceTypeName. Use TypeClassification |
| `CodeGen/CarrierEmitter.cs` | 1, 2, 5 | Remove ValueTypes HashSet, IsNonNullableValueType. Extract compound-expression wrapper. Consolidate EmitCarrierClauseBody. Remove delegateParamName param |
| `CodeGen/TerminalBodyEmitter.cs` | 1, 2, 5 | Remove IsKnownValueTypeName. Use ResolveSiteParams. Extract terminal return type helpers |
| `CodeGen/TerminalEmitHelpers.cs` | 2, 5 | Add ResolveSiteParams. Add ResolveTerminalReturnType, ResolveCarrierExecutorMethod, EmitInsertColumnBinding |
| `CodeGen/ClauseBodyEmitter.cs` | 2, 5 | Replace inline siteParams loops. Replace isBrokenTuple inline |
| `CodeGen/JoinBodyEmitter.cs` | 2, 5 | Replace inline siteParams loops. Delete private GetJoinedBuilderTypeName. Replace isBrokenTuple inline |
| `Parsing/ChainAnalyzer.cs` | 1, 3, 4 | Remove GetReaderMethodForType, IsUnresolvedTypeName, IsUnresolvedAggregateType, BuildTupleResultTypeName. Add ResolveConditionalInclusion. Extract EnrichParameterFromColumn |
| `Parsing/UsageSiteDiscovery.cs` | 1, 3 | Remove GetReaderMethodForType. Rename DetectConditionalAncestor |
| `Parsing/DisplayClassNameResolver.cs` | 1, 7 | Remove private type resolution if ChainResultTypeResolver extracted |
| `IR/RawCallSite.cs` | 3 | Rename ConditionalInfo to NestingContext |
| `IR/PipelineOrchestrator.cs` | 1 | Remove IsUnresolvedResultType. Use TypeClassification |
| `IR/AssembledPlan.cs` | 3 | Update GetClauseEntries to use IsGenuinelyConditional |
| `IR/SqlExprClauseTranslator.cs` | 7 | Merge ExtractParameters and ExtractSubqueryPredicateParams |
| `Projection/ProjectionAnalyzer.cs` | 1 | Remove IsUnresolvedTypeName, GetReaderMethodForAggregate, BuildTupleTypeName |
| `Generation/InterceptorCodeGenerator.cs` | 1, 4, 5 | Remove GetReaderMethod. Fix GetColumnValueExpression enum cast. Add IsBrokenTupleType |
| `Models/InsertInfo.cs` | 4 | Add EnumUnderlyingType to InsertColumn |
| `Models/ConditionalInfo.cs` | 3 | Rename to NestingContext |
| `QuarryGenerator.cs` | 1, 6 | Remove BuildTupleResultTypeName. Add pipeline error capture |
| `CodeGen/CarrierStrategy.cs` | 7 | Remove dead types, move CarrierParameter |
| `Models/ColumnInfo.cs` | 1 | Delegate GetReaderMethodFromType to TypeClassification.GetReaderMethod |

## Appendix B: Diagnostic Reference

| ID | Severity | Description | Affected by |
|---|---|---|---|
| QRY032 | Error | Chain not analyzable | Phase 3 (ConditionalInfo split prevents false positives) |
| QRY033 | Error | Forked chain | Phase 3 (NestingContext rename, no behavior change) |
| QRY900 | Error | Internal generator error | Phase 6 (errors now surface instead of being swallowed) |

## Appendix C: Incremental Caching Considerations

Roslyn incremental generators use `IEquatable<T>` equality on pipeline outputs to determine whether to re-run downstream stages. Several phases change model types:

- **Phase 1**: `TypeClassification` is a static utility class, not a model. No caching impact.
- **Phase 3**: Renaming `ConditionalInfo` to `NestingContext` changes the type name but the equality semantics are identical. The first build after the rename will invalidate the cache; subsequent builds cache normally.
- **Phase 4**: Adding `EnumUnderlyingType` to `InsertColumn` changes its equality. First build invalidates; subsequent builds cache normally.
- **Phase 6**: Adding `PipelineError`/`PipelineErrorStackTrace` to `BoundCallSite` changes its equality. These fields are null for successful sites, so equality comparison adds two null checks. First build invalidates; subsequent builds cache normally.

In all cases, the cache miss only affects the first build after the change. Performance impact is negligible.
