# Generator Consolidation Phase 2 — Implementation Plan

## Overview

This plan addresses issues identified during code review of the Phase 1 consolidation (7 commits on `feat/generator-consolidation`). It covers correctness fixes, reliability improvements, the remaining Phase 5 emitter sub-steps, and comprehensive unit test coverage for the consolidated `TypeClassification` class.

Work is organized into six execution phases ordered by priority: security/reliability first, then correctness, then deduplication, then tests.

### Phase Dependency Graph

```
Phase A (Pipeline error fixes)
Phase B (IsUnresolvedTypeName rename)
Phase C (Nested tuple parsing fix)
Phase D (DateTimeOffset integration test)
    |
Phase E (Phase 5 remaining emitter sub-steps) — depends on B for IsUnresolvedTypeName references
    |
Phase F (TypeClassification unit tests) — should run last to validate all prior changes
```

Phases A–D are independent and can execute in any order. Phase E depends on B (method renames). Phase F runs last.

---

## Phase A: Pipeline Error Reliability Fixes

**Issues addressed**: A1 (Stage 3 Bind silent drop), B1 (ChainMemberSites not checked), B4 (WithResolvedResultType drops PipelineError), B2 (dead GetReaderMethod overload)

**Estimated scope**: ~4 files modified, ~40 lines added, ~10 lines removed

### A.1 Problem Statement

Three reliability gaps exist in the Phase 6 pipeline error implementation:

1. **Stage 3 (Bind) failures are silently dropped.** The `SelectMany` lambda catches exceptions and returns `ImmutableArray<BoundCallSite>.Empty`. The sites that were being bound simply vanish — no diagnostic, no runtime indication. The user sees a mysterious `InvalidOperationException` at runtime from the carrier base class.

2. **Pipeline error reporting only checks `group.Sites`, not `group.ChainMemberSites`.** The `EmitFileInterceptors` method iterates `group.Sites` for `PipelineError` but `ChainMemberSites` (sites that are part of assembled chains) can also carry pipeline errors. The emission path merges both lists (line 524-526), but the error reporting loop (line 391) misses the chain members.

3. **`WithResolvedResultType` and `WithJoinedEntityTypeNames` drop `PipelineError`.** Both copy methods construct a new `TranslatedCallSite` without forwarding the `pipelineError` parameter. If `PipelineOrchestrator` patches a site that already has a pipeline error, the error is silently discarded.

4. **`GetReaderMethod(string, out bool)` overload is dead code.** Added per the original plan but has zero callers. Should be removed to avoid maintenance burden.

### A.2 Implementation Steps

#### Step A.2.1: Add Side-Channel Error Bag for Stage 3

The incremental pipeline's `SelectMany` combinator cannot carry per-site error metadata because it returns `ImmutableArray<BoundCallSite>`, and a failed Bind produces no `BoundCallSite` to attach errors to.

Use a static `ConcurrentBag` scoped by a generation-unique key. The output stage drains it.

**File**: `src/Quarry.Generator/IR/PipelineErrorBag.cs` (new)

```csharp
internal static class PipelineErrorBag
{
    internal static void Report(string sourceFilePath, int line, int column, string error);
    internal static List<PipelineErrorEntry> DrainErrors();
}

internal readonly struct PipelineErrorEntry
{
    public string SourceFilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string Error { get; }
}
```

**Algorithm**:
- `Report` adds an entry to a static `ConcurrentBag<PipelineErrorEntry>`.
- `DrainErrors` atomically swaps the bag with a fresh one and returns all entries as a list.
- The bag is static because incremental generator pipeline lambdas must be `static` — no instance state is available. Thread safety is provided by `ConcurrentBag`.

**File**: `src/Quarry.Generator/QuarryGenerator.cs`

Update the Stage 3 catch block to call `PipelineErrorBag.Report` with the site's location info from `pair.Left` (the enriched `RawCallSite`):

```csharp
catch (System.Exception ex)
{
    var raw = pair.Left;
    PipelineErrorBag.Report(raw.FilePath, raw.Line, raw.Column,
        $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    return ImmutableArray<IR.BoundCallSite>.Empty;
}
```

In `EmitFileInterceptors`, drain the bag and report each entry as QRY900:

```csharp
foreach (var err in PipelineErrorBag.DrainErrors())
{
    // ... create Location from err.SourceFilePath/Line/Column
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.InternalError, location, err.Error));
}
```

**Limitation**: The bag is global (not per-file), so all errors are reported in the first `EmitFileInterceptors` call that drains them. This is acceptable because QRY900 is an internal error diagnostic — the exact reporting location is less important than the error being visible at all.

#### Step A.2.2: Check ChainMemberSites for Pipeline Errors

**File**: `src/Quarry.Generator/QuarryGenerator.cs`

Extend the pipeline error reporting loop in `EmitFileInterceptors` to also iterate `group.ChainMemberSites`:

```csharp
foreach (var site in group.Sites.Concat(group.ChainMemberSites))
{
    if (site.PipelineError != null) { /* report QRY900 */ }
}
```

Alternatively, since the emission path already merges both lists into `mergedSites` (line 524-526), move the error reporting to after the merge point and iterate `mergedSites` instead.

#### Step A.2.3: Forward PipelineError in Copy Methods

**File**: `src/Quarry.Generator/IR/TranslatedCallSite.cs`

Update `WithResolvedResultType` and `WithJoinedEntityTypeNames` to forward `PipelineError`:

```csharp
internal TranslatedCallSite WithResolvedResultType(string resolvedResultTypeName)
{
    var newRaw = Bound.Raw.WithResultTypeName(resolvedResultTypeName);
    var newBound = Bound.WithRaw(newRaw);
    return new TranslatedCallSite(newBound, Clause, KeyTypeName, ValueTypeName, PipelineError);
}

public TranslatedCallSite WithJoinedEntityTypeNames(
    IReadOnlyList<string> joinedEntityTypeNames,
    IReadOnlyList<EntityRef>? joinedEntities)
{
    var newBound = Bound.WithJoinedEntities(joinedEntityTypeNames, joinedEntities);
    return new TranslatedCallSite(newBound, Clause, KeyTypeName, ValueTypeName, PipelineError);
}
```

#### Step A.2.4: Fix Pipeline Error Location

**File**: `src/Quarry.Generator/QuarryGenerator.cs`

The current error reporting uses `Location.Create(syntaxTree, default)` which points at file start. Use `LinePositionSpan` instead (same pattern as the deferred diagnostics block):

```csharp
var errorLoc = syntaxTree != null && site.Line > 0
    ? Location.Create(site.FilePath, default,
        new LinePositionSpan(
            new LinePosition(site.Line - 1, site.Column - 1),
            new LinePosition(site.Line - 1, site.Column - 1)))
    : Location.None;
```

#### Step A.2.5: Remove Dead GetReaderMethod Overload

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

Delete the `GetReaderMethod(string clrType, out bool needsSignCast)` overload. It has zero callers — all existing call sites call `NeedsSignCast` and `GetReaderMethod` independently, and no call site was wired to use the combined overload.

### A.3 Test Strategy

- Verify QRY900 is reported when a site has `PipelineError` set (existing test infrastructure can create a `TranslatedCallSite` with a non-null `pipelineError`).
- Verify `WithResolvedResultType` preserves `PipelineError` via a unit test that round-trips a site with both a pipeline error and a result type patch.

---

## Phase B: Rename IsUnresolvedTypeName to Eliminate Boolean Parameter Trap

**Issues addressed**: A7 (treatObjectAsUnresolved semantic split)

**Estimated scope**: ~8 files modified, ~20 lines changed (mechanical rename)

### B.1 Problem Statement

`TypeClassification.IsUnresolvedTypeName(string?, bool treatObjectAsUnresolved = true)` has a boolean parameter that controls semantically opposite behavior. During Phase 1 implementation, the wrong default silently broke identity Select projections. A future contributor calling the method without the parameter would get the default `true` behavior, which is wrong for `ProjectionAnalyzer`'s context.

### B.2 Target State

Replace the single method with two explicitly named methods:

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

```csharp
/// <summary>
/// Checks if a type name is unresolved. Treats "object" as unresolved because
/// the semantic model uses "object" for error types on generated entities.
/// Use this in chain analysis and pipeline orchestration.
/// </summary>
public static bool IsUnresolvedTypeName(string? typeName);

/// <summary>
/// Checks if a type name is unresolved, but treats "object" as a valid resolved type.
/// Use this in projection analysis where "object" is a legitimate fallback type
/// that will be enriched later by chain-level analysis.
/// </summary>
public static bool IsUnresolvedTypeNameLenient(string? typeName);
```

**Algorithm for `IsUnresolvedTypeName`** (strict, default):
1. Return true if null, empty, or whitespace.
2. Return true if `"?"` or `"object"`.
3. Return true if starts with `"? "`.
4. Return false otherwise.

**Algorithm for `IsUnresolvedTypeNameLenient`**:
1. Same as above, but skip the `"object"` check (step 2 only checks `"?"`).

### B.3 Implementation Steps

1. Rename all call sites that currently pass `treatObjectAsUnresolved: false` to use `IsUnresolvedTypeNameLenient`. These are exclusively in `ProjectionAnalyzer.cs` (7 call sites).
2. Remove the `treatObjectAsUnresolved` parameter from `IsUnresolvedTypeName`.
3. Update `BuildTupleTypeName` to call `IsUnresolvedTypeNameLenient` (it already passes `false`).
4. Verify all other callers (ChainAnalyzer, PipelineOrchestrator) use the strict version without needing changes.

### B.4 Migration

Purely mechanical rename. No behavior change. All current `treatObjectAsUnresolved: true` callers (explicit or default) continue to call `IsUnresolvedTypeName`. All `treatObjectAsUnresolved: false` callers switch to `IsUnresolvedTypeNameLenient`.

---

## Phase C: Fix IsUnresolvedResultType Nested Tuple Parsing

**Issues addressed**: A3 (nested tuple parsing uses naive Split(','))

**Estimated scope**: 1 file modified, ~20 lines changed

### C.1 Problem Statement

`TypeClassification.IsUnresolvedResultType` uses `string.Split(',')` to parse tuple elements. This fails for nested tuples: `(int, (string, object))` splits into `["int", " (string", " object))"]`. The inner tuple's `"object"` element is never checked against the unresolved-type rules.

### C.2 Core Concept: Depth-Aware Comma Splitting

Tuple type strings can nest: `(int, (string, decimal) Named, bool)`. A correct parser must track parenthesis depth and only split on commas at depth 0 (the outermost tuple level).

### C.3 Implementation Steps

**File**: `src/Quarry.Generator/Utilities/TypeClassification.cs`

Replace the `inner.Split(',')` call in `IsUnresolvedResultType` with a depth-aware element splitter:

```csharp
private static List<string> SplitTupleElements(string inner);
```

**Algorithm**:
1. Initialize `depth = 0`, `start = 0`, `elements = new List<string>()`.
2. Iterate each character in `inner`:
   - If `(`, increment depth.
   - If `)`, decrement depth.
   - If `,` and `depth == 0`, extract `inner[start..i]` as an element, set `start = i + 1`.
3. After the loop, extract the final element `inner[start..]`.
4. Return elements.

Update `IsUnresolvedResultType` to call `SplitTupleElements` instead of `Split(',')`. The per-element analysis logic (space-splitting for named elements, checking for `"object"` and `"?"`) remains unchanged.

Additionally, for recursive correctness: when checking a named element's type part, if the type part itself starts with `(`, recursively call `IsUnresolvedResultType` on it. This handles `(int, (object, string) Nested)` where the inner tuple contains an unresolved element.

### C.4 Test Strategy

Add test cases to the existing `PipelineOrchestratorTests.IsUnresolvedResultType_DetectsPatterns`:
- `"(int, (string, object))"` → true (nested tuple with unresolved inner element)
- `"(int, (string, decimal))"` → false (nested tuple, all resolved)
- `"(int, (string, decimal) Named)"` → false (nested named tuple, all resolved)
- `"((object, int), string)"` → true (unresolved element in first nested tuple)

---

## Phase D: DateTimeOffset GetFieldValue Integration Test

**Issues addressed**: A4 (GetFieldValue<DateTimeOffset> behavioral change needs provider verification)

**Estimated scope**: 1 test file modified, ~20 lines added

### D.1 Problem Statement

`ColumnInfo.GetReaderMethodByTypeName` now delegates to `TypeClassification.GetReaderMethod`, which returns `GetFieldValue<DateTimeOffset>` instead of the previous `GetValue`. While this is correct per ADO.NET spec, it changes the generated interceptor code. SQLite stores `DateTimeOffset` as text, and `GetFieldValue<DateTimeOffset>` may not be supported by all SQLite providers (Microsoft.Data.Sqlite does support it, but the behavior with text-stored values should be verified).

### D.2 Implementation Steps

Add a schema with a `DateTimeOffset` column and a cross-dialect test that:
1. Inserts a row with a known `DateTimeOffset` value.
2. Reads it back via a generated interceptor.
3. Asserts the round-tripped value matches.

If the `QueryTestHarness` 4-dialect infrastructure supports `DateTimeOffset`, add the test there. Otherwise, add a SQLite-specific integration test.

The test should also cover `TimeSpan`, `DateOnly`, and `TimeOnly` if the test schema can accommodate them.

### D.3 Migration and Breaking Changes

If any provider fails with `GetFieldValue<T>`, the fix is to keep `GetValue` for that provider by making `ColumnInfo.GetReaderMethodByTypeName` dialect-aware. The current implementation is dialect-agnostic. This would require threading `SqlDialect` into `ColumnInfo.GetTypeMetadata` — a larger change that should only be done if a provider-specific failure is confirmed.

---

## Phase E: Remaining Phase 5 Emitter Sub-Steps

**Issues addressed**: Handoff items 5.2.1, 5.2.2, 5.2.4, 5.2.6

**Estimated scope**: ~5 files modified, ~120 lines removed, ~50 lines added

### E.1 Step 5.2.1: Extract Compound Expression Wrapping Helper

**Files**: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

Two locations (lines ~353 and ~1027) contain identical logic that wraps a `ValueExpression` in parentheses when it contains a space or opening paren, before applying the null-forgiving `!` operator. This prevents operator-precedence issues in the emitted code (e.g., `__c.P0 = a + b!` vs `__c.P0 = (a + b)!`).

```csharp
private static string FormatCarrierFieldAssignment(
    int globalIndex, string valueExpression);
```

**Algorithm**:
1. Check if `valueExpression` contains `' '` or `'('`.
2. If yes, return `$"__c.P{globalIndex} = ({valueExpression})!;"`.
3. If no, return `$"__c.P{globalIndex} = {valueExpression}!;"`.

Replace both inline occurrences with calls to this helper.

### E.2 Step 5.2.2: Consolidate EmitCarrierClauseBody Extraction

**Files**: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

`EmitCarrierClauseBody` (lines 284-379) contains ~53 lines of inline extraction-local emission and parameter binding that duplicate `EmitExtractionLocalsAndBindParams` (lines 524-541) and `EmitCarrierParamBindings` (lines 1010-1039).

The key difference: `EmitCarrierClauseBody` accepts a `delegateParamName` method parameter (default `"func"`) for the extraction target, while `EmitExtractionLocalsAndBindParams` reads it from `carrier.GetExtractionPlan(site.UniqueId).DelegateParamName`.

**Consolidation approach**:
1. Remove the `delegateParamName` parameter from `EmitCarrierClauseBody`.
2. In `EmitCarrierClauseBody`, after computing `globalParamOffset` via `ResolveSiteParams`, call `EmitExtractionLocalsAndBindParams(sb, carrier, site, siteParams, globalParamOffset)` instead of the inline block.
3. `EmitExtractionLocalsAndBindParams` already reads `DelegateParamName` from the extraction plan, which `CarrierAnalyzer.GetDelegateParamName` sets to `"action"` for `UpdateSetAction` clauses. This matches the value that callers currently pass explicitly.
4. Verify that all callers of `EmitCarrierClauseBody` either pass the default `"func"` or pass a value that matches what the extraction plan provides.

The one caller that passes a non-default `delegateParamName` is in `ClauseBodyEmitter` for SetAction (line ~548-549, passes `actionParamName`). Verify that `CarrierAnalyzer.GetDelegateParamName` returns the same value for this site. If it does, the parameter removal is safe.

**Risk**: If the extraction plan's `DelegateParamName` ever differs from what the caller was passing, the emitted code will reference the wrong variable. Gate on: verify the extraction plan values match for all test cases before removing the parameter.

### E.3 Step 5.2.4: Extract Terminal Return Type and Executor Helpers

**Files**: `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`, `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

Four identical switch expressions exist — two pairs in `EmitReaderTerminal` (lines 61-70, 99-108) and `EmitJoinReaderTerminal` (lines 147-156, 189-198).

```csharp
internal static string ResolveTerminalReturnType(
    InterceptorKind kind, string resultType,
    string scalarTypeArg, bool isValueType);

internal static string ResolveCarrierExecutorMethod(
    InterceptorKind kind, string resultType,
    string scalarTypeArg);
```

**Algorithm for `ResolveTerminalReturnType`**:
1. Compute `firstOrDefaultSuffix = isValueType ? "" : "?"`.
2. Switch on `kind`: `ExecuteFetchAll` → `Task<List<{resultType}>>`, `ExecuteFetchFirst` → `Task<{resultType}>`, `ExecuteFetchFirstOrDefault` → `Task<{resultType}{suffix}>`, `ExecuteFetchSingle` → `Task<{resultType}>`, `ExecuteScalar` → `Task<{scalarTypeArg}>`, `ToAsyncEnumerable` → `IAsyncEnumerable<{resultType}>`, default → `""`.

**Algorithm for `ResolveCarrierExecutorMethod`**:
1. Switch on `kind`: `ExecuteFetchAll` → `ExecuteCarrierWithCommandAsync<{resultType}>`, `ExecuteFetchFirst` → `ExecuteCarrierFirstWithCommandAsync<{resultType}>`, `ExecuteFetchFirstOrDefault` → `ExecuteCarrierFirstOrDefaultWithCommandAsync<{resultType}>`, `ExecuteFetchSingle` → `ExecuteCarrierSingleWithCommandAsync<{resultType}>`, `ExecuteScalar` → `ExecuteCarrierScalarWithCommandAsync<{scalarTypeArg}>`, `ToAsyncEnumerable` → `ToCarrierAsyncEnumerableWithCommandAsync<{resultType}>`, default → `""`.

Replace all four switch expressions in `TerminalBodyEmitter` with calls to these helpers.

### E.4 Step 5.2.6: Extract Insert Column Parameter Binding Helper

**Files**: `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`, `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`, `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

The single-insert binding loop (`CarrierEmitter.EmitCarrierInsertTerminal`, lines 893-904) and the batch-insert binding loop (`TerminalBodyEmitter.EmitBatchInsertCarrierTerminal`, lines 609-623) share the per-column logic: compute `needsIntType`, get `valueExpr` via `GetColumnValueExpression`, create parameter, set name/value/DbType, add to command.

The loops differ in:
- **Entity variable**: `"__c.Entity!"` (single) vs `"__entity"` (batch).
- **Parameter naming**: `"@p{i}"` (single, fixed index) vs `"@p" + __paramIdx` (batch, running counter).
- **Indentation**: 8 spaces (single) vs 12 spaces (batch, inside `for` loop body).
- **Parameter variable**: `__p{i}` (single, named per-column) vs `__p` (batch, reused).

These differences make a fully unified helper difficult without introducing multiple parameters that obscure the code. The recommended approach is to extract only the per-column value expression and DbType computation — the parts that are actually identical:

```csharp
internal static (string ValueExpr, bool NeedsIntType) GetInsertColumnBinding(
    InsertColumnInfo col, string entityVar, bool convertBool);
```

**Algorithm**:
1. `needsIntType = col.IsEnum || (col.IsBoolean && convertBool)`.
2. `valueExpr = InterceptorCodeGenerator.GetColumnValueExpression(entityVar, col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool, col.EnumUnderlyingType ?? "int")`.
3. Return `(valueExpr, needsIntType)`.

Both loops call this helper, keeping their own parameter creation and naming logic. This consolidates the `GetColumnValueExpression` call arguments (9 parameters) into a single source of truth.

### E.5 Test Strategy

All Phase E changes are internal refactors — no generated output changes. Run the full test suite after each step. Diff a generated interceptor file from the sample webapp before and after to confirm byte-for-byte identical output.

---

## Phase F: TypeClassification Unit Test Suite

**Issues addressed**: B3 (no unit tests for consolidated TypeClassification methods)

**Estimated scope**: 1 new test file, ~80-100 test cases

### F.1 Problem Statement

`TypeClassification` is now the single source of truth for all CLR type classification in the generator. A regression in any method silently breaks all generated output. The existing 2259 cross-dialect tests provide end-to-end coverage but cannot pinpoint which classification method failed. Targeted unit tests are needed.

### F.2 Implementation Steps

**File**: `src/Quarry.Tests/Utilities/TypeClassificationTests.cs` (new)

#### F.2.1: IsValueType Tests

Test cases:
- C# keyword types: `"int"` → true, `"string"` → false, `"bool"` → true.
- BCL names: `"Int32"` → true, `"Boolean"` → true, `"String"` → false.
- Nullable types: `"int?"` → true (still a value type), `"string?"` → false.
- Tuple types: `"(int, string)"` → true, `"(int, (bool, decimal))"` → true.
- Qualified names: `"System.Int32"` → true, `"System.String"` → false.
- nint/nuint: `"nint"` → true, `"nuint"` → true.
- Unknown types: `"MyStruct"` → false (defaults to false — no way to know).
- Edge cases: `null` → false, `""` → false, `"object"` → false.
- Date/time types: `"DateTime"` → true, `"DateTimeOffset"` → true, `"TimeSpan"` → true.
- Array: `"byte[]"` → false (arrays are reference types).

#### F.2.2: IsReferenceType Tests

Test cases:
- `"string"` → true, `"int"` → true → false (inverse of IsValueType for known types).
- `"byte[]"` → true (arrays are reference types).
- `"Nullable<int>"` → false, `"System.Nullable<int>"` → false.
- `"(int, string)"` → false (tuples are value types).
- Unknown: `"MyClass"` → true (defaults to reference type for nullable safety).

#### F.2.3: IsNonNullableValueType Tests

Test cases:
- `"int"` → true, `"int?"` → false.
- `"(int, string)"` → true, `"DateTime"` → true.
- `"System.DateTime"` → true (qualified name).
- `"string"` → false (reference type, not in set).

#### F.2.4: GetReaderMethod Tests

Test all 30+ type mappings:
- Signed integers: `"int"` → `"GetInt32"`, `"Int32"` → `"GetInt32"`, `"System.Int32"` → `"GetInt32"`.
- Unsigned with sign cast: `"uint"` → `"GetInt32"`, `"ushort"` → `"GetInt16"`, `"ulong"` → `"GetInt64"`, `"sbyte"` → `"GetByte"`.
- Float/double/decimal: `"float"` → `"GetFloat"`, `"double"` → `"GetDouble"`, `"decimal"` → `"GetDecimal"`.
- String/bool/char: `"string"` → `"GetString"`, `"bool"` → `"GetBoolean"`, `"char"` → `"GetChar"`.
- Special types: `"Guid"` → `"GetGuid"`, `"DateTime"` → `"GetDateTime"`.
- GetFieldValue types: `"DateTimeOffset"` → `"GetFieldValue<DateTimeOffset>"`, `"TimeSpan"` → `"GetFieldValue<TimeSpan>"`, `"DateOnly"` → `"GetFieldValue<DateOnly>"`, `"TimeOnly"` → `"GetFieldValue<TimeOnly>"`.
- Nullable stripping: `"int?"` → `"GetInt32"`, `"DateTimeOffset?"` → `"GetFieldValue<DateTimeOffset>"`.
- Fallback: `"MyCustomType"` → `"GetValue"`, `"byte[]"` → `"GetValue"`.

#### F.2.5: NeedsSignCast Tests

- `"uint"` → true, `"UInt32"` → true, `"System.UInt32"` → true.
- `"int"` → false, `"long"` → false.
- `"sbyte"` → true, `"SByte"` → true.
- `"byte"` → false (no sign mismatch for byte).

#### F.2.6: IsUnresolvedTypeName and IsUnresolvedTypeNameLenient Tests

Strict (`IsUnresolvedTypeName`):
- `null` → true, `""` → true, `" "` → true.
- `"?"` → true, `"object"` → true.
- `"? SomeError"` → true.
- `"string"` → false, `"int"` → false, `"MyType"` → false.

Lenient (`IsUnresolvedTypeNameLenient`):
- Same as strict, except `"object"` → false.

#### F.2.7: IsUnresolvedResultType Tests

- `null` → false (null means "no result type", which is valid).
- `""` → true, `"?"` → true, `"object"` → true.
- `"int"` → false, `"string"` → false, `"UserDto"` → false.
- Simple tuple: `"(int, decimal, OrderPriority)"` → false.
- Unresolved tuple: `"(object, object, object)"` → true.
- Named tuple: `"(int OrderId, string UserName)"` → false.
- Named with unresolved: `"(object OrderId, object UserName)"` → true.
- Empty type parts: `"( OrderId,  Total,  Priority)"` → true.
- **Nested tuples** (new, from Phase C fix):
  - `"(int, (string, object))"` → true.
  - `"(int, (string, decimal))"` → false.
  - `"((object, int), string)"` → true.
  - `"(int, (string, decimal) Named)"` → false.

#### F.2.8: BuildTupleTypeName Tests

- Single column: `[col("int", "Item1", ordinal: 0)]` → `"(int)"`.
- Multi-column: `[col("int"), col("string")]` → `"(int, string)"`.
- Nullable column: `[col("int", nullable: true)]` → `"(int?)"`.
- Named elements: `[col("int", "OrderId", ordinal: 0)]` → `"(int OrderId)"`.
- ItemN elision: `[col("int", "Item1", ordinal: 0)]` → `"(int)"` (name omitted).
- Unresolved with fallback: `[col("?")]` with `fallbackToObject: true` → `"(object)"`.
- Unresolved without fallback: `[col("?")]` with `fallbackToObject: false` → `""`.

---

## Appendix: File Impact Summary

| File | Phase | Changes |
|---|---|---|
| `IR/PipelineErrorBag.cs` | A | New file — side-channel error bag |
| `IR/TranslatedCallSite.cs` | A | Forward PipelineError in copy methods |
| `QuarryGenerator.cs` | A | Bind error capture via bag, drain in output, fix error location |
| `Utilities/TypeClassification.cs` | B, C, A | Rename IsUnresolvedTypeName split, fix nested tuple parsing, remove dead overload |
| `Projection/ProjectionAnalyzer.cs` | B | Rename to IsUnresolvedTypeNameLenient (7 call sites) |
| `CodeGen/CarrierEmitter.cs` | E | Extract compound wrapping helper, consolidate EmitCarrierClauseBody |
| `CodeGen/TerminalBodyEmitter.cs` | E | Extract terminal return type + executor helpers |
| `CodeGen/TerminalEmitHelpers.cs` | E | Add ResolveTerminalReturnType, ResolveCarrierExecutorMethod, GetInsertColumnBinding |
| `Tests/Utilities/TypeClassificationTests.cs` | F | New file — ~80-100 unit tests |
| `Tests/IR/PipelineOrchestratorTests.cs` | C | Add nested tuple test cases |
| Integration test file (TBD) | D | DateTimeOffset round-trip test |
