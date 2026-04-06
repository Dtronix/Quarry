# Plan: Cross-Entity Set Operation Support

## Key Concepts

**Cross-entity set operations** allow combining results from queries on different tables (e.g., `Users.Union<Product>(Products)`) as long as both sides project to the same result type (`TResult`). The existing same-entity infrastructure handles operand chain discovery, analysis, carrier generation, SQL assembly, and parameter remapping. Cross-entity support requires threading the operand's entity type through the pipeline so the interceptor method signature uses the correct argument type.

**Operand entity type** (`TOther`) is the C# entity type of the operand query. It's available at the call site as a generic type argument on the method (e.g., `.Union<Product>(...)`). The interceptor must accept `IQueryBuilder<TOther, TResult>` for the operand parameter, not `IQueryBuilder<TEntity, TResult>`.

**QRY073** is the blocking diagnostic that currently rejects cross-entity set operations. It will be removed entirely since the feature will be fully supported.

## Phases

### Phase 1: Add operandEntityTypeName to RawCallSite and SetOperationPlan

Add a `string? operandEntityTypeName` parameter and property to `RawCallSite`. This stores the fully-qualified C# type name of `TOther` when a cross-entity set operation is detected (e.g., `"Quarry.Tests.Samples.Product"`). Same-entity set operations leave this null.

**Files:**
- `src/Quarry.Generator/IR/RawCallSite.cs` — Add constructor parameter, property, include in `Equals`/`GetHashCode`, and `WithResultTypeName` copy method.
- `src/Quarry.Generator/IR/QueryPlan.cs` (`SetOperationPlan`) — Add `string? OperandEntityTypeName` parameter and property. Include in `Equals`/`GetHashCode`.

**Tests to verify:** Existing tests still pass (no behavioral change yet).

### Phase 2: Extract TOther during discovery

In `UsageSiteDiscovery`, when processing a set operation call site, check if the invocation has a generic type argument. If so, use the semantic model to resolve the `TOther` type and pass it as `operandEntityTypeName` to `RawCallSite`.

**Algorithm:** After the existing set operation operand chain extraction (Step 15b), inspect the invocation:
```csharp
var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
if (methodSymbol != null && methodSymbol.TypeArguments.Length > 0)
    operandEntityTypeName = methodSymbol.TypeArguments[0].ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat);
```
Strip the `global::` prefix to match existing entity type name format.

**Files:**
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — Add operandEntityTypeName extraction in Step 15b, pass to RawCallSite constructor.

**Tests to verify:** Existing tests still pass.

### Phase 3: Propagate operandEntityTypeName through ChainAnalyzer to SetOperationPlan

In `ChainAnalyzer`, when building `SetOperationPlan` instances, read the `operandEntityTypeName` from the set operation `RawCallSite` and pass it to the `SetOperationPlan` constructor.

**Algorithm:** In the set operation plan building section (~line 825-878), when creating `new SetOperationPlan(opKind, opPlan, paramGlobalIndex)`, also pass the `raw.OperandEntityTypeName` from the current set operation site.

**Files:**
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — Pass `raw.OperandEntityTypeName` to `SetOperationPlan` constructor.

**Tests to verify:** Existing tests still pass.

### Phase 4: Update SetOperationBodyEmitter for cross-entity arg type

Modify `SetOperationBodyEmitter.EmitSetOperation` to generate the correct argument type when the operand has a different entity type. When `SetOperationPlan.OperandEntityTypeName` is non-null, the `argType` should use the operand entity and the shared result type.

**Algorithm:**
1. After resolving `receiverType`, check if the current set operation has an operand entity type.
2. If so, resolve the result type from the site (using the same logic as `ResolveCarrierReceiverType`).
3. Construct `argType = $"IQueryBuilder<{operandEntityType}, {resultType}>"`.
4. If no operand entity type, fall back to `argType = receiverType` (same-entity behavior).

**Files:**
- `src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs` — Cross-entity arg type resolution.

**Tests to verify:** Existing same-entity tests still pass.

### Phase 5: Remove QRY073 diagnostic

Remove the QRY073 diagnostic emission from `PipelineOrchestrator.CollectPostAnalysisDiagnostics`. Remove the `CrossEntitySetOperationNotSupported` descriptor definition. Remove any analyzer test that verifies QRY073 emission.

**Files:**
- `src/Quarry.Generator/IR/PipelineOrchestrator.cs` — Remove the QRY073 check block (~lines 215-224).
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — Remove the QRY073 descriptor.
- Check for and remove any tests asserting QRY073.

**Tests to verify:** Existing tests pass (minus any removed QRY073 tests).

### Phase 6: Add cross-entity set operation tests

Add comprehensive cross-dialect tests in `CrossDialectSetOperationTests.cs` covering:

1. **CrossEntity_Union_TupleProjection** — `Users.Select(u => (u.UserId, u.UserName)).Union<Product>(Products.Select(p => (p.ProductId, p.ProductName)))`. Verify SQL uses different tables in each SELECT with UNION keyword.

2. **CrossEntity_UnionAll** — Same as above but with UnionAll (keeps duplicates).

3. **CrossEntity_Intersect** — Users intersect Products by projected tuple.

4. **CrossEntity_Except** — Users except Products by projected tuple.

5. **CrossEntity_WithParameters** — Both sides have WHERE clauses with captured variables, verifying parameter remapping works across entity boundaries.

6. **CrossEntity_WithPostUnionOrderBy** — Cross-entity union followed by OrderBy/Limit, verifying subquery wrapping with cross-entity.

Each test verifies SQL output across all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server) and that the SQLite execution succeeds (results are valid).

**Files:**
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — Add new test methods.
- `src/Quarry.Tests/QueryTestHarness.cs` — May need to seed products table if not already seeded.

**Tests to verify:** All new and existing tests pass.
