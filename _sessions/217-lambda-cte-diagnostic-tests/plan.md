# Plan: 217-lambda-cte-diagnostic-tests

## Overview

Add diagnostic test coverage for invalid lambda CTE bodies in `CarrierGenerationTests.cs`. The existing QRY080 diagnostic (`CteInnerChainNotAnalyzable`) already fires for these cases — the generator doesn't detect a lambda inner chain, so the outer chain's CteDefinition site falls through to QRY080 emission. We're adding tests to prove this behavior and prevent regressions.

All tests follow the established pattern:
1. Build source with an invalid lambda CTE body
2. Run the generator via `RunGeneratorWithDiagnostics`
3. Assert QRY080 is emitted
4. Assert QRY900 (InternalError) is NOT emitted

## Phase 1: Add lambda-form QRY080 diagnostic tests

**File:** `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`
**Location:** Inside the `#region CTE diagnostics (QRY080 / QRY081)` region, after the existing `Cte_With_NonInlineInnerArgument_EmitsQRY080` test.

### Test 1: `Cte_LambdaWith_IdentityLambda_EmitsQRY080`
The lambda body just returns the parameter (`orders => orders`) without building a chain. The discovery phase won't detect an inner chain, so QRY080 fires.

```csharp
await db.With<Order>(orders => orders)
    .FromCte<Order>()
    .Select(o => (o.OrderId, o.Total))
    .ExecuteFetchAllAsync();
```

### Test 2: `Cte_LambdaWith_VariableReturn_EmitsQRY080`
The lambda body returns an external variable instead of building a chain on the lambda parameter. Discovery won't classify this as a lambda inner chain.

```csharp
IQueryBuilder<Order> external = null!;
await db.With<Order>(orders => external)
    .FromCte<Order>()
    .Select(o => (o.OrderId, o.Total))
    .ExecuteFetchAllAsync();
```

### Test 3: `Cte_LambdaWith_NonQuarryMethodCall_EmitsQRY080`
The lambda body calls a non-Quarry method on the parameter. The fluent chain won't be recognized as a Quarry query chain.

```csharp
await db.With<Order>(orders => orders.ToString())
    .FromCte<Order>()
    .Select(o => (o.OrderId, o.Total))
    .ExecuteFetchAllAsync();
```

Note: This test may need adjustment depending on whether the compiler allows this (type mismatch). If `ToString()` doesn't type-check against the expected return type, we may need a different non-Quarry method or accept that the compilation has errors that prevent generator execution. Will verify during implementation.

### Tests to add/modify
- Add 3 new test methods as described above
- No existing tests modified

### Commit
Single commit: "Add lambda-form QRY080 diagnostic tests (#217)"
