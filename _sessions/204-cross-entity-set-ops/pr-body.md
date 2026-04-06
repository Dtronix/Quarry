## Summary
- Closes #204
- Wires the existing `Union<TOther>`/`Intersect<TOther>`/`Except<TOther>` (and the `*All` variants) overloads on `IQueryBuilder` through discovery, chain analysis, and code generation so cross-entity set operations are fully supported.
- Removes the `QRY073 CrossEntitySetOperationNotSupported` diagnostic — the feature is no longer rejected.
- Fixes a pre-existing bug in `CallSiteBinder` where any entity with a user-written partial declaration in the schema's namespace pinned interceptor signatures to the wrong type for non-default contexts (PgDb/MyDb/SsDb), surfaced while extending the new tests to all four dialects.

## Reason for Change
Issue #204 reported that the cross-entity set operation overloads on `IQueryBuilder` (`Union<TOther>`, `UnionAll<TOther>`, `Intersect<TOther>`, `IntersectAll<TOther>`, `Except<TOther>`, `ExceptAll<TOther>`) were defined but not wired into discovery or code generation. Users hit the `QRY073` diagnostic at compile time (or `InvalidOperationException` at runtime if the diagnostic was suppressed). Same-entity set operations had been implemented in #181 / #201; this PR extends that work to allow combining queries from different tables that project to the same result type.

## Impact
- Cross-entity set operations (e.g., `Users.Select(u => (u.UserId, u.UserName)).Union(Products.Select(p => (p.ProductId, p.ProductName)))`) now compile and execute.
- Generator interceptor signatures for entities with user-written partial declarations now bind to the per-context generated entity class instead of the schema-namespace type, fixing a latent CS9144 that was previously masked because no test exercised those entities through non-default contexts.
- Removing `QRY073` is a soft break for any downstream that suppressed it via `#pragma warning disable QRY073` or MSBuild `NoWarn`. Such entries become "unknown diagnostic" warnings — harmless but visible.

## Plan items implemented as specified
- **Phase 1**: Added `OperandEntityTypeName` to `RawCallSite` and `SetOperationPlan`, including the `WithResultTypeName` copy method, `Equals`, and `GetHashCode`.
- **Phase 2**: Extracted `TOther` during discovery via `semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol` + `TypeArguments[0].ToDisplayString(FullyQualifiedFormat)` with `global::` prefix stripping.
- **Phase 3**: Propagated `OperandEntityTypeName` from `RawCallSite` through `ChainAnalyzer` into `SetOperationPlan`.
- **Phase 4**: Updated `SetOperationBodyEmitter.EmitSetOperation` to construct the correct operand `argType` when the operand has a different entity type, falling back to same-entity behavior when null.
- **Phase 5**: Removed the `QRY073` diagnostic emission and descriptor; updated the descriptor uniqueness test.
- **Phase 6**: Added cross-entity tests in `CrossDialectSetOperationTests.cs` (Union, UnionAll, Intersect, Except, plus parameters and post-Union OrderBy/Limit) and seeded the products table in `QueryTestHarness`.

## Deviations from plan implemented
- **Multi-dialect coverage achieved in this PR rather than deferred**. Phase 6 of the plan called for asserting against all four dialects, but the initial implementation could only validate the SQLite context because `PgDb.Products()` / `MyDb.Products()` / `SsDb.Products()` produced interceptor signature mismatches. The root cause was a pre-existing source generator bug, not a property of cross-entity set ops, but it was first observable through these tests. After investigation (see "Gaps in original plan implemented" below), the bug was fixed in `CallSiteBinder` and every cross-entity test now asserts across SQLite, PostgreSQL, MySQL, and SQL Server via `AssertDialects`.

## Gaps in original plan implemented
- **`CallSiteBinder` per-context entity type normalization**. The chain root and builder method discovery used `ToFullyQualifiedDisplayString()` on the resolved entity symbol from the user's source. When a user supplies a partial class for an entity in the schema's namespace (e.g., `Quarry.Tests.Samples.Product` annotated with `[EntityReader]`), Roslyn resolves to that user-written class even though `EntityCodeGenerator` separately generates a per-context entity class at `{contextNamespace}.{entityName}`. Downstream interceptor signatures then pinned to the schema-namespace type and failed CS9144 because the context's accessor method actually returns `IEntityAccessor<{contextNamespace}.{entityName}>`. The fix in `CallSiteBinder.Bind` rewrites `RawCallSite.EntityTypeName` (and `OperandEntityTypeName` for cross-entity set ops) to `global::{contextNamespace}.{entityName}` only when the discovery's namespace differs from the context namespace. Simple-name (Error type) and same-namespace cases are left untouched to preserve existing carrier and interceptor output formats. The binder also rebinds entries when `registry.Resolve` returned a foreign-context match because the entity name happened to live in another context's `globalName` index.
- **`CrossEntity_IntersectAll_TupleProjection` and `CrossEntity_ExceptAll_TupleProjection`**. The original plan listed Union/UnionAll/Intersect/Except cases but skipped the `*All` variants. They share a code path with the regular forms but were added for completeness (Pg-only, since QRY070/QRY071 still gate the other dialects).
- **Trailing blank line in `PipelineOrchestrator.CollectPostAnalysisDiagnostics` removed**. Left over from QRY073 deletion.

## Design Notes
- **Strict `TResult` constraint on cross-entity overloads.** Each cross-entity overload is shaped as `Union<TOther>(IQueryBuilder<TOther, TResult>)` — both operands must share the same `TResult`. This is enforced by the C# generic system, not by a runtime check, so column-shape mismatches are caught at compile time before the generator runs.
- **Tradeoff vs raw SQL `UNION`.** SQL's `UNION` (and `INTERSECT`/`EXCEPT`) is column-positional and accepts any column-compatible types — e.g., `INT` can union with `BIGINT` even though the projection types differ. Quarry deliberately does not expose that permissiveness through the chain API, because doing so would require the generator to invent a "common projection" type at parse time and emit positional widening logic, both of which are surprising to a LINQ-style consumer.
- **Explicit-projection escape hatch.** When the source entities have different shapes (or different element types), users project both sides to a common type (anonymous type, named tuple, or DTO) via an explicit `Select` before the set operation. The implemented tests exercise exactly this pattern with `Users.Select(u => (u.UserId, u.UserName))` unioned against `Products.Select(p => (p.ProductId, p.ProductName))` projecting to `(int, string)`.
- **Parity with EF Core / LINQ to SQL.** Both libraries enforce the same compile-time constraint on `Union`/`Intersect`/`Except` — operands must agree on the projected type. Matching this convention keeps Quarry's surface predictable for developers familiar with the existing .NET ORMs.
- **`QRY072` retained as a defensive guard.** The column-count mismatch diagnostic stays in place even though valid C# source cannot reach it through the cross-entity overloads, so future projection-flattening shapes (or generator refactors that bypass the type system) still surface as a clear error rather than a corrupt SQL emit.
- **XML doc updates.** All six cross-entity overloads on `IQueryBuilder` now carry a uniform `<remarks>` block that explains the constraint, points at the explicit-projection escape hatch, and notes the EF Core / LINQ to SQL parity.

## Migration Steps
None for end users. Suppressors of `QRY073` should remove their pragma / `NoWarn` entries to clear the "unknown diagnostic" warning, but this is not a hard requirement.

## Performance Considerations
No runtime impact. The generator changes are confined to parse-time entity type resolution and emit string composition; the `WithEntityTypeName` / `WithOperandEntityTypeName` copy methods on `RawCallSite` only allocate when the resolved namespace actually differs from the context namespace.

## Security Considerations
None. The `OperandEntityTypeName` value flows from Roslyn's semantic model (compiler-verified type resolution), not from user input, and is used only to compose interceptor method signatures. The `CallSiteBinder` rewrite uses the registry-resolved entity name and the call site's own context namespace — no external input.

## Breaking Changes
- **Consumer-facing**: `QRY073 CrossEntitySetOperationNotSupported` is removed. Existing `#pragma warning disable QRY073` or `NoWarn` entries will produce a warning about an unknown diagnostic ID. The warning is harmless and can be cleared by removing the suppression.
- **Internal**: `RawCallSite` and `SetOperationPlan` gained an optional `operandEntityTypeName` parameter (`string? = null`). The change is source-compatible — both classes are `internal`, no external callers.
