# Plan: 281-post-cte-chain-diagnostic

## Background

Issue #281: `db.With<T>(...).FromCte<T>().OrderBy(...).Select(...).Prepare()` produces a malformed C# 12 interceptor where the entity name appears as both the receiver and return type (e.g., `Order<Order>`), triggering `CS0308`. Root cause: `IEntityAccessor<T>` does not declare `OrderBy`/`ThenBy`/`Limit`/`Offset`/`Having`/set-ops. Roslyn cannot bind the call. `DiscoverPostCteSites` (the post-CTE walker that papers over unresolvable methods) synthesizes a site with `currentBuilderTypeName == null`. Downstream the fallback chain in `TranslatedCallSite.BuilderTypeName` returns the entity name, and `ClauseBodyEmitter` writes it into the generic receiver/return positions verbatim.

We are taking approach (b) from the issue — extend the chain so the user-level syntax is valid C#. Concretely: add the missing chain-continuation methods to `IEntityAccessor<T>` as default-throwing interface methods. Once Roslyn can bind the call, three things happen automatically: (1) the user gets no compile error; (2) the post-CTE walker breaks at the `parentSymbolInfo.Symbol is IMethodSymbol` check and never synthesizes the bad site; (3) normal `DiscoverRawCallSite` produces a site with `builderTypeName = containingType.Name = "IEntityAccessor"` and the existing `IsEntityAccessorType` / `BuildReceiverType` helpers emit `IQueryBuilder<T> OrderBy(this IEntityAccessor<T> builder, ...)`. The bug disappears not by trapping a bad path but by removing it.

## Key concepts

**Default interface methods.** `IEntityAccessor<T>` is a slim interface whose default implementations throw `InvalidOperationException("...not intercepted in this optimized chain. This indicates a code generation bug.")`. Carriers implement the interface; the source generator overrides the default for every interceptable call, and unintercepted calls hit the default body. Adding more default-throwing methods does not break existing carriers — they continue to inherit defaults for methods they don't intercept. The existing `Distinct()` and `WithTimeout()` methods on `IEntityAccessor<T>` already follow this exact pattern and prove the round-trip works (they compile, their interceptors emit cleanly via `BuildReceiverType` with `thisType == "IEntityAccessor"`).

**Receiver type plumbing in the emitter.** `InterceptorCodeGenerator.IsEntityAccessorType("IEntityAccessor") => true` and `BuildReceiverType("IEntityAccessor", entity, _) => "IEntityAccessor<entity>"` already exist. The struct-unbox helper `EntityAccessorToQueryBuilder("T") => "((EntityAccessor<T>)(object)builder).CreateQueryBuilder()"` is in place for terminal/transition emitters that need to call into the optimized chain after intercepting on `IEntityAccessor<T>`. No emitter changes are required to make this work — the path just isn't reached today because Roslyn fails to bind.

**Why the post-CTE walker still matters.** `DiscoverPostCteSites` exists to paper over a different kind of unresolvable call: methods on the chain root (`Users()`, `Orders()`) where `With<T>(...)` returned the `QuarryContext` base class so subsequent context-specific methods aren't on the static type. That use case is unrelated to this fix and stays as-is. After this change, the only thing that disappears is the `OrderBy/ThenBy/Limit/...` synthesis path (it was always wrong for non-IEntityAccessor builder types in this codepath; it just happened to land near the CTE walker because IEntityAccessor was the only type missing those methods).

## Phases

### Phase 1 — Add chain-continuation methods to `IEntityAccessor<T>`

Edit `src/Quarry/Query/IEntityAccessor.cs`. Add the following default-throwing methods, each returning `IQueryBuilder<T>` (mirror the corresponding signatures on `IQueryBuilder<T>` from `src/Quarry/Query/IQueryBuilder.cs`):

- `OrderBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)`
- `ThenBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)`
- `Limit(int count)`
- `Offset(int count)`
- `Having(Func<T, bool> predicate)`
- Set-op direct form: `Union(IQueryBuilder<T> other)`, `UnionAll(...)`, `Intersect(...)`, `IntersectAll(...)`, `Except(...)`, `ExceptAll(...)`
- Set-op lambda form: `Union(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)`, plus `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll` lambda variants

Each body throws `InvalidOperationException("Carrier method IEntityAccessor.<Name> is not intercepted in this optimized chain. This indicates a code generation bug.")` — mirror the wording used by the existing methods on `IEntityAccessor`.

Update the doc-comment on `IEntityAccessor<T>` (the `<remarks>` block at lines 7–14) which currently reads "It does NOT extend `IQueryBuilder<T>`. Chain-continuation methods (OrderBy, Limit, GroupBy, Having, Offset, ThenBy) only appear after the first clause...". That statement is no longer true. Reword to: chain-continuation methods are available on `IEntityAccessor<T>` directly so the natural fluent syntax compiles in chains where the static type is `IEntityAccessor<T>` (e.g., `FromCte<T>()`); calling them transitions to `IQueryBuilder<T>` for the rest of the chain.

**Tests for Phase 1:** none yet — Phase 1 is the source change. Verify by building the solution (it must build) and running the existing test suite (must remain green; existing chains starting with `Users()` etc. don't hit the new defaults because their interceptors override them).

### Phase 2 — Add cross-dialect SQL output coverage for post-CTE chain-continuation

Edit `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` (the file the issue's repro test was being added to in #282).

Add tests that exercise the bug's exact shape — a CTE chain with `OrderBy`/`Limit`/`Offset` directly on the `FromCte<T>()` accessor — for each registered dialect (Sqlite/Pg/My/Ss). Match the file's existing pattern for cross-dialect assertions (look at any existing `[Theory]` test in the file; one assertion per dialect covering SQL shape). Cover at minimum:

```csharp
db.With<Order>(orders => orders.Where(o => o.Total > 100))
  .FromCte<Order>()
  .OrderBy(o => o.OrderId)
  .Select(o => (o.OrderId, o.Total))
  .Prepare();
```

and a variant with `.Limit(10).Offset(5)` after `.OrderBy(...)`. Assert the generated SQL has `ORDER BY` / `LIMIT` / `OFFSET` (or dialect equivalents) and the build succeeds — the latter is the load-bearing assertion since the bug was a build failure. If `CrossDialectCteTests` already has a helper for "build, capture diagnostics, assert SQL shape" use it; otherwise mirror what neighboring tests do.

Also add a tuple-projection variant equivalent to #282's `Tuple_PostCteWideProjection` test that uses `OrderBy` in the chain instead of sorting client-side. The existing test (per #282) explicitly avoided `OrderBy` to dodge the bug; this new variant proves the workaround is no longer needed.

**Tests for Phase 2:** the new tests above.

### Phase 3 — Verify carrier/interceptor generation explicitly

Edit `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` (the file currently hosting QRY080/081/082 tests at line 2363+). Add a test in the CTE region that:

1. Compiles a snippet containing `db.With<Order>(...).FromCte<Order>().OrderBy(...).Select(...).Prepare()`.
2. Inspects the generated interceptor source for the `OrderBy` method.
3. Asserts the receiver type contains `IEntityAccessor<` (not the bare entity name) and the return type contains `IQueryBuilder<` — i.e., that the malformed `Order<Order>` shape from issue #281 does not regress.

Mirror the structure of `Cte_With_NonInlineInnerArgument_EmitsQRY080` (the existing pattern for compiling a snippet and inspecting generator output). The exact API for capturing generated source should be visible from the surrounding tests; if not, follow whatever `CarrierGenerationTests` does to capture diagnostics for QRY080 and use the source-driver hook beside it.

**Tests for Phase 3:** the new generator-output assertion test.

### Phase 4 — Run full suite, fix any fallout, commit

Run the full solution test suite (`dotnet test Quarry.sln`). All 3364 baseline tests must remain green plus the new tests pass. Watch for:

- Existing CTE chains (CrossDialectCteTests, CarrierGenerationTests CTE region) — should still pass; the IEntityAccessor surface only grew, didn't change.
- Generated `*.Interceptors.*.g.cs` files for tests that touch CTE — re-inspect a couple to make sure the new methods don't cause double discovery (post-CTE walker creating one site, normal discovery creating another for the same `OrderBy`). If duplicates appear, the post-CTE walker's break logic may need a small adjustment.
- `IEntityAccessor<T>` is a public interface in the `Quarry` namespace. Adding default interface methods is binary-compatible for consumers that depend on Quarry but a NEW public API surface — call this out in the PR notes.

**Commit message (single squash-target):** `Extend IEntityAccessor<T> with chain-continuation methods so post-CTE OrderBy/Limit/etc. emit valid interceptors (#281)`.

## Phase dependencies

Strictly sequential. Phase 1 must land before Phase 2/3 tests can compile (they exercise the new surface). Phase 4 is the final gate before review.

## Files touched (estimate)

- `src/Quarry/Query/IEntityAccessor.cs` — Phase 1 (one edit, ~80 lines added)
- `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — Phase 2 (~2 new tests)
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` — Phase 3 (~1 new test)

No changes expected in `Quarry.Generator` itself. If exploration during Phase 4 turns up a generator change is needed (e.g., post-CTE walker double-discovery), that becomes a Phase 5 sub-step inside this same plan.
