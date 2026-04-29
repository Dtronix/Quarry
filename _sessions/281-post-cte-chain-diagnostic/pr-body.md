## Summary
- Closes #281

## Reason for Change

`db.With<T>(...).FromCte<T>().OrderBy(...).Select(...).Prepare()` previously emitted a malformed C# 12 interceptor where the entity name appeared as both the receiver and return type (e.g. `Order<Order>`), tripping `CS0308` in the generated `.g.cs` file. Root cause: `IEntityAccessor<T>` did not declare `OrderBy`/`ThenBy`/`Limit`/`Offset`/`Having`/set-ops, so Roslyn could not bind the call. `DiscoverPostCteSites` papered over the unresolved method by synthesizing a site with `currentBuilderTypeName == null`, and the fallback chain in `TranslatedCallSite.BuilderTypeName` returned the entity name verbatim.

## Impact

Approach (b) from the issue: extend `IEntityAccessor<T>` with the missing chain-continuation methods so the natural fluent syntax compiles. Once Roslyn binds the call, the post-CTE walker breaks at its `parentSymbolInfo.Symbol is IMethodSymbol` guard before reaching the bad synthesis path, normal `DiscoverRawCallSite` produces a site with `builderTypeName = containingType.Name = "IEntityAccessor"`, and the existing `IsEntityAccessorType` / `BuildReceiverType` emitter helpers (`InterceptorCodeGenerator.Utilities.cs`) emit a correctly typed `IQueryBuilder<T> X(this IEntityAccessor<T> builder, ...)` interceptor. The bug disappears not by trapping a bad path but by removing it.

## Plan items implemented as specified

- Phase 1 — added 13 chain-continuation methods (`OrderBy`, `ThenBy`, `Limit`, `Offset`, `Having`, 6 direct + 6 lambda set-ops) to `IEntityAccessor<T>` as default-throwing interface methods, mirroring their `IQueryBuilder<T>` counterparts. Updated the `<remarks>` block to describe the new surface.
- Phase 2 — `Cte_FromCte_OrderBy_Select` and `Cte_FromCte_OrderBy_Limit_Offset` in `CrossDialectCteTests.cs` exercise the issue's exact repro shape across all four dialects (SQL string + executed result; SS `OFFSET ... FETCH NEXT` shape covered).
- Phase 3 — `Cte_FromCte_OrderBy_EmitsWellFormedInterceptor` in `CarrierGenerationTests.cs` regression-pins the OrderBy interceptor signature (`IQueryBuilder<Order> OrderBy_X(this IEntityAccessor<Order> builder, ...)`) and that `Order<Order>` never appears.
- Phase 4 — full suite green at every step (baseline 3364 → final 3369).

## Deviations from plan implemented

None. The plan's "no QRY083" decision was honored — no diagnostic descriptor was added; the change is confined to the `Quarry` runtime project plus tests.

## Gaps in original plan implemented

Surfaced and addressed during REVIEW (see `_sessions/281-post-cte-chain-diagnostic/review.md`):

- Replaced the "slim interface" framing in the `IEntityAccessor<T>` `<remarks>` block, which became misleading once the surface roughly doubled.
- Moved `Cte_FromCte_OrderBy_EmitsWellFormedInterceptor` out of the `#region CTE diagnostics (QRY080 / QRY081)` (where it was placed for proximity but didn't fit) into a dedicated `#region Post-CTE chain-continuation interceptor shape (issue #281)`.
- Added a direct `CS0308` recompile check to both #281-region tests: combine the generator output back into the original compilation and assert no `CS0308` in any generated file. The shape regex assertion was indirect; this catches the literal symptom from the bug report.
- Added `Tuple_PostCteWideProjection_OrderBy` (variant of #282's `Tuple_PostCteWideProjection`) — a wide-tuple projection with `OrderBy` directly off `FromCte<T>()`. Uses `OrderBy(o => o.Total, Direction.Descending)` rather than the more obvious `OrderBy(o => o.OrderId)` because the projection contains `OrderId` twice (`OrderId` and `Echo: o.OrderId`) and SS rejects `ORDER BY OrderId` as ambiguous in that case.
- Added `Cte_FromCte_AllChainContinuations_BindAndEmitCleanly` exercising the 10 newly-exposed methods beyond OrderBy/Limit/Offset (ThenBy, Having, all 12 set-op overloads). Combined-compilation check filters to `CS0308` in generated files plus `CS1061`/`CS0117` in the user snippet — the former catches a regression of the malformed-interceptor bug for any of those methods, the latter catches a regression of the new `IEntityAccessor<T>` surface itself.

## Migration Steps

None. Existing carriers inherit the new default-throwing methods automatically (the generator overrides each method only when an interceptable call site is discovered). Existing fluent chains that already worked (`db.Users().Where(...).OrderBy(...)`) continue to bind to `IQueryBuilder<T>.OrderBy` because that's the static type after `Where`.

## Performance Considerations

None. No hot paths touched. The new default-throwing methods are never executed in optimized chains — the generator emits an interceptor that replaces the call. In an unintercepted chain the throw replaces a previous `CS1061` compile error with a deterministic `InvalidOperationException` at runtime, which is the same pattern every other unintercepted IEntityAccessor method uses.

## Security Considerations

None. No data flow, parameter binding, or SQL emission paths changed.

## Breaking Changes

### Consumer-facing
- **New public API surface on `IEntityAccessor<T>`.** Adding default interface methods is binary-additive — no existing consumer breaks at compile or run time. Calls that previously failed with `CS1061` now bind. If a downstream project ships an extension method named `OrderBy`/`ThenBy`/`Limit`/`Offset`/`Having`/`Union`/etc. on `IEntityAccessor<T>`, overload resolution now prefers the interface's default method over the extension. No such extensions exist within Quarry itself; consumers should remove duplicate extensions if any.
- **Documentation change.** The previous `<remarks>` on `IEntityAccessor<T>` told users that chain-continuation methods only appeared after a first clause; that guidance is no longer correct. Workarounds based on the old guidance still compile and behave the same way, but the docs no longer describe them as necessary.

### Internal
- None. No changes under `src/Quarry.Generator/`. No changes to any analyzer, migration, or shared project.

## Follow-ups

- #283 — Analyzer warning for `ThenBy` without prior `OrderBy` and `Having` without prior `GroupBy`. Both are now legal C# but produce surprising SQL semantics. Out of scope for this PR (which is about codegen correctness, not user-source linting); the analyzer needs fluent-chain flow analysis and is better as its own change.
