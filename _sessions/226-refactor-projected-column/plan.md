# Plan: 226-refactor-projected-column

## Key Concepts

**IsExternalInit polyfill**: A small internal class in `System.Runtime.CompilerServices` that enables C# 9 features (`init` setters, records) in netstandard2.0 projects. The compiler checks for this type at compile time; its presence is sufficient.

**Record conversion**: Changing `sealed class` to `sealed record` auto-generates a copy constructor, enabling `with` expressions. Custom `Equals`/`GetHashCode` implementations are preserved — the compiler defers to user-provided implementations. This is important because `NavigationHops` (an `IReadOnlyList<string>?`) requires `EqualityHelpers.SequenceEqual` rather than the reference equality a compiler-generated implementation would use.

**`with` expressions**: For clone-with-modification sites, `existingCol with { SqlExpression = newExpr }` creates a shallow copy with the specified property changed. This replaces 16 sites that currently reconstruct all 18 parameters to change 1-2 fields.

## Phases

### Phase 1: Add IsExternalInit polyfill and convert ProjectedColumn to record

**Files modified:**
- NEW: `src/Quarry.Generator/Utilities/IsExternalInit.cs` — the polyfill class
- `src/Quarry.Generator/Models/ProjectionInfo.cs` — convert `ProjectedColumn`

**Changes:**
1. Create `IsExternalInit.cs` in the Utilities folder (existing folder with other helper types) containing the polyfill class provided by the user.
2. In `ProjectionInfo.cs`:
   - Change `internal sealed class ProjectedColumn : IEquatable<ProjectedColumn>` to `internal sealed record ProjectedColumn : IEquatable<ProjectedColumn>`
   - Change all `{ get; }` property declarations to `{ get; init; }` (18 properties, excluding the computed `EffectivelyNullable` which stays as `=>`)
   - Keep the existing constructor unchanged (it still assigns all values)
   - Keep the existing `Equals(ProjectedColumn?)`, `Equals(object?)`, and `GetHashCode()` unchanged
   - Add the `virtual` modifier to `Equals(ProjectedColumn?)` since records require this for sealed records (the compiler expects it)

**Tests:** Run full suite. No call sites change in this phase — the record is source-compatible with existing constructor calls. All 3062 tests should pass unchanged.

### Phase 2: Convert clone-with-modification sites in ChainAnalyzer.cs to `with` expressions

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

6 clone-with-modification sites to convert:

- **Line ~762**: Copies from `col` with ordinal change → `col with { Ordinal = ... }`
- **Line ~1343**: Copies from `col` adding `TableAlias = "t0"` → `col with { TableAlias = "t0" }`
- **Line ~1819**: Copies from `rawCol` with re-quoted SQL → `rawCol with { SqlExpression = reQuotedSql }`
- **Line ~1835**: Copies from `col` with resolved aggregate types → `col with { ClrType = ..., FullClrType = ..., IsAggregateFunction = true, IsValueType = ..., ReaderMethodName = ... }`
- **Line ~1899**: Copies from `col` with navigation enrichment → `col with { ColumnName = ..., ClrType = ..., ... }`  
- **Line ~1923**: Copies from `col` adding join-nullable → `col with { IsJoinNullable = true }`

Also convert the 2 mixed positional/named sites (lines ~762, ~1819) to fully named if they remain as constructor calls (they won't — they become `with` expressions).

**Tests:** Full suite, all 3062 should pass.

### Phase 3: Convert clone-with-modification sites in QuarryGenerator.cs

**File:** `src/Quarry.Generator/QuarryGenerator.cs`

1 clone-with-modification site:

- **Line ~835** (`StripNonAggregateSqlExpressions`): Copies from `col` setting `sqlExpression: null` and `isAggregateFunction: false` → `col with { SqlExpression = null, IsAggregateFunction = false }`

**Tests:** Full suite, all 3062 should pass.

### Phase 4: Convert clone-with-modification sites in ProjectionAnalyzer.cs to `with` expressions

**File:** `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

Review all 21 call sites. The 9 clone-like sites where values are copied from an existing `ProjectedColumn` variable (not from a `ColumnInfo` lookup) are candidates for `with`. Sites constructing from `ColumnInfo` metadata (a different type) are not clones and stay as constructor calls.

Specifically, sites that copy from an existing `ProjectedColumn` and change a subset of fields should become `with` expressions. Sites constructing from `ColumnInfo` or other source types remain as named constructor calls (they are already using named parameters).

**Tests:** Full suite, all 3062 should pass.

### Phase 5: Ensure all remaining constructor sites use named parameters + update test files

**Files:**
- `src/Quarry.Tests/EntityReaderTests.cs`
- `src/Quarry.Tests/IR/PipelineOrchestratorTests.cs`  
- `src/Quarry.Tests/TypeMappingProjectionTests.cs`
- `src/Quarry.Tests/Utilities/TypeClassificationTests.cs`
- `src/Quarry.Tests/SignCastReaderTests.cs`
- Any other test files with `new ProjectedColumn(` calls

Audit each remaining constructor call (production and test) to ensure fully named parameters. Convert any positional-only or mixed calls.

**Tests:** Full suite, all 3062 should pass.
