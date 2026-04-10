# Plan: 227-joined-over-failure-tests

## Overview

Add failure-mode tests for `ParseJoinedOverClause` in `CarrierGenerationTests.cs`. These mirror the four existing single-entity tests from #223 but use a two-entity join pattern so the generator routes through the joined code path (`ParseJoinedOverClause` → `WalkOverChain` with `GetJoinedColumnSql`).

## Key Concepts

**Joined vs single-entity code path:** The generator decides which OVER parser to call based on the Select lambda arity. A single-parameter Select (`o => ...`) uses `ParseOverClause`; a multi-parameter Select (`(u, o) => ...`) uses `ParseJoinedOverClause`. Both ultimately call the shared `WalkOverChain` method but with different column resolution delegates.

**Test source pattern:** Each test provides raw C# source containing a `TestDbContext` with both `Users()` and `Orders()` entity accessors. The query chains `db.Users().Join<Order>((u, o) => u.UserId == o.UserId).Select((u, o) => ...)` with a `Sql.RowNumber(over => ...)` call in the projection.

## Phase 1: Add all 4 joined failure-mode tests (single commit)

This is a small, cohesive change — all 4 tests follow the same template and belong together.

**File:** `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`

**Location:** Add a new region `#region Joined window function OVER clause failure modes (#227)` immediately after the existing `#endregion` for the #223 tests (line 2873), before the closing brace of the test class.

### Test 1: `CarrierGeneration_JoinedWindowFunction_BlockBodyLambda_FallsToRuntimeBuild`

Source uses `over => { return over.OrderBy(o.Total); }` — a block body lambda rather than an expression body. `ParseJoinedOverClause` extracts the body using `lambda.Body as ExpressionSyntax`, which returns null for block bodies. The method returns null, and the generator falls back to RuntimeBuild.

Assertions:
- No QRY900 diagnostic (generator doesn't crash)
- Interceptors file is still generated
- Generated code does NOT contain `ROW_NUMBER()` (fell to RuntimeBuild)

### Test 2: `CarrierGeneration_JoinedWindowFunction_UnknownMethodInChain_FallsToRuntimeBuild`

Source uses `over => over.ToString()` — `ToString` is not a recognized OVER chain method. `WalkOverChain` hits the `default` case in the switch and returns false. `ParseJoinedOverClause` returns null.

Assertions:
- No QRY900 diagnostic
- No QRY error diagnostics
- Generated code (if interceptors exist) does NOT contain `ROW_NUMBER()`

### Test 3: `CarrierGeneration_JoinedWindowFunction_EmptyOverClause_ProducesValidSql`

Source uses `over => over` — bare parameter reference. `WalkOverChain` hits the `IdentifierNameSyntax` base case and returns true immediately with empty partition/order lists. `BuildOverClauseString` produces `OVER ()`.

Assertions:
- No QRY error diagnostics
- Interceptors file is generated
- Generated code CONTAINS `ROW_NUMBER() OVER ()` (valid empty OVER clause)

### Test 4: `CarrierGeneration_JoinedWindowFunction_NonFluentChainExpression_FallsToRuntimeBuild`

Source uses `over => SomeMethod(over)` — not a fluent chain. `WalkOverChain` receives an `InvocationExpressionSyntax` but its Expression is an `IdentifierNameSyntax` (the method name), not a `MemberAccessExpressionSyntax`. The `expression is not InvocationExpressionSyntax` check on the inner expression fails during recursion. Returns false.

Assertions:
- No QRY900 diagnostic
- No QRY error diagnostics
- Generated code (if interceptors exist) does NOT contain `ROW_NUMBER()`

### Tests to verify

Run full test suite. All existing tests plus the 4 new ones must pass.
