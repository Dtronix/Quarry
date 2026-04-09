# Plan: 223-over-clause-failure-tests

## Overview

Add 4 test methods to `CarrierGenerationTests.cs` that verify the generator's behavior when the OVER clause lambda in a window function call is malformed. Three tests verify graceful degradation (no crash, fallback to RuntimeBuild), and one verifies that an empty OVER clause (`over => over`) actually succeeds, producing `ROW_NUMBER() OVER ()`.

## Key Concepts

- **ParseOverClause / WalkOverChain**: These methods in `ProjectionAnalyzer.cs` parse the OVER clause lambda body. They return null/false for malformed lambdas (block bodies, unknown methods, non-fluent expressions). The empty case (`over => over`) succeeds with empty partition/order lists.
- **RuntimeBuild fallback**: When OVER parsing fails, `GetWindowFunctionInfo` returns `(null, null)`, the projection returns `CreateFailed`, and the chain degrades to `OptimizationTier.RuntimeBuild`. No QRY diagnostic is emitted.
- **Test pattern**: CarrierGenerationTests uses inline C# source compiled via Roslyn's `CreateCompilation`, runs the generator with `RunGeneratorWithDiagnostics`, and asserts on generated code content and/or diagnostics.

## Phase 1: Add all 4 test methods

All tests go in `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`, using the existing `SharedSchema` (which has `OrderSchema` with `OrderId`, `UserId`, `Total` columns).

### Test 1: `CarrierGeneration_WindowFunction_BlockBodyLambda_FallsToRuntimeBuild`

Source uses `Sql.RowNumber(over => { return over.OrderBy(o.Total); })` inside a Select tuple projection on `Orders()`. This compiles cleanly because the block body returns `IOverClause`.

Assertions:
- No QRY900 diagnostic (generator didn't crash)
- Interceptors file IS generated (chain handled)
- Generated code does NOT contain `ROW_NUMBER()` (window function SQL not emitted — RuntimeBuild has no prebuilt SQL)

### Test 2: `CarrierGeneration_WindowFunction_UnknownMethodInChain_FallsToRuntimeBuild`

Source uses `Sql.RowNumber(over => over.ToString())`. This has a compilation error (ToString returns string, not IOverClause). The generator should still process the syntax tree and handle it gracefully.

Assertions:
- No QRY900 diagnostic (generator didn't crash)
- Generator produces no QRY-prefixed errors

### Test 3: `CarrierGeneration_WindowFunction_EmptyOverClause_ProducesValidSql`

Source uses `Sql.RowNumber(over => over)`. This compiles cleanly and actually succeeds — WalkOverChain returns true with empty lists, producing `ROW_NUMBER() OVER ()`.

Assertions:
- No QRY errors
- Interceptors file IS generated
- Generated code contains `ROW_NUMBER() OVER ()` (the SQL is valid and emitted)

### Test 4: `CarrierGeneration_WindowFunction_NonFluentChainExpression_FallsToRuntimeBuild`

Source uses `Sql.RowNumber(over => SomeMethod(over))` where `SomeMethod` is undefined. This has a compilation error. The generator should handle it gracefully.

Assertions:
- No QRY900 diagnostic (generator didn't crash)
- Generator produces no QRY-prefixed errors

### Implementation Notes

- All test sources use `SharedSchema` + a `TestDbContext` with `Orders()` accessor
- Tests use `ExecuteFetchAllAsync()` as the terminal to trigger the generator
- For tests with intentional compilation errors (2, 4), filter diagnostics to only check QRY-prefixed ones
- Place all 4 tests together in the file, near the end (before closing brace), grouped under a comment region for window function failure tests
