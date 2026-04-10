# Plan: 232-extend-projection-param-merging

## Overview

The `AnalyzeOperandChain` method in `ChainAnalyzer.cs` handles set operation operands (Union, UnionAll, Intersect, Except). It calls `BuildProjection` to build the projection for the operand query, but unlike `AnalyzeChainGroup`, it does not merge projection parameters (window function variable args) into the global parameter list or remap `@__proj{N}` placeholders.

The fix copies the projection parameter merging block from `AnalyzeChainGroup` (lines 1262-1295) into `AnalyzeOperandChain` after the `BuildProjection` call at line 2549. This block:
1. Checks if `raw.ProjectionInfo.ProjectionParameters` has entries.
2. Calls `RemapParameters` to assign global indices.
3. Builds a `localToGlobal` dictionary mapping `@__proj{N}` to `{@globalIndex}`.
4. Adds remapped parameters to the `parameters` list.
5. Iterates `projection.Columns`, replacing `@__proj` placeholders in `SqlExpression` strings.
6. Reconstructs the `SelectProjection` with updated columns.

## Phase 1: Add projection parameter merging to AnalyzeOperandChain

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

After the `BuildProjection` call in the `InterceptorKind.Select` branch of `AnalyzeOperandChain` (line 2549), insert the same merging block that exists in `AnalyzeChainGroup`. The code checks for non-empty `ProjectionParameters`, remaps them, builds a local-to-global dictionary, appends parameters, and replaces `@__proj{N}` placeholders in projected column SQL expressions.

**Tests:** Existing tests remain green (no behavioral change for queries without projection parameters in operands).

## Phase 2: Add cross-dialect test for set operation with variable window function arg

**File:** `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs`

Add a test `UnionAll_WithVariableWindowFunctionArg` that:
- Creates a `UnionAll` query where the operand has a `Select` with `Sql.Ntile(buckets, ...)` using a captured variable.
- Asserts that all four dialects produce SQL with proper parameter placeholders (`@p0`/`$1`/`?`) instead of raw `@__proj0`.
- The main query side uses a Where clause with a parameter, so the operand's window function parameter gets a subsequent global index.

This follows the same pattern as `WindowFunction_Ntile_Variable` in `CrossDialectWindowFunctionTests.cs`.
