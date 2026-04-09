# Plan: 216-set-op-lambda-context-resolution

## Problem

Lambda inner chain sites inside set-op lambdas (Union/Intersect/Except) get the wrong context class when the entity type is registered in multiple contexts. The root cause is in `UsageSiteDiscovery.cs`: when `ResolveContextFromCallSite` is called on a site inside a lambda body (e.g., `x.Where(...)`), it walks the receiver chain to the lambda parameter `x` (type `IEntityAccessor<T>`), which is not a `QuarryContext`, and returns `null`. With null context, `EntityRegistry.Resolve` falls back to first-writer-wins, picking the wrong context entry when the entity exists in multiple contexts.

CTE lambdas happen to work because their tests use namespace-qualified entity types (`Pg.Order`, `My.Order`) that uniquely resolve in the registry by entity type name alone. But CTE lambdas with shared entity types (like `User` which all contexts share) would have the same bug.

## Key Concepts

**InnerChainDetection**: A struct returned by `DetectInnerChain` that indicates whether an invocation is inside a CTE or set-op inner chain. Currently carries `IsInnerChain`, `IsLambdaForm`, and `SpanStart`.

**Parent invocation**: When `DetectInnerChain` identifies a lambda form, it already locates `lambdaParentInv` — the Union/Intersect/Except/With call that contains the lambda. This parent invocation's receiver chain IS rooted on the context (e.g., `db.Users().Union(x => ...)`), so `ResolveContextFromCallSite(lambdaParentInv)` returns the correct context class.

**Fallback pattern**: Instead of changing how clause sites resolve context, we add the parent context as a fallback in the RawCallSite construction — `ResolveContextFromCallSite(invocation) ?? innerChainDetection.ParentContextClassName`.

## Phase 1: Fix context propagation in UsageSiteDiscovery

Changes to `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`:

1. **Extend `InnerChainDetection` struct** (line 3818): Add a `ParentContextClassName` property (string?). Update the constructor to accept it. Update `None` default to leave it null.

2. **Update `DetectInnerChain` method** (line 3836): When detecting a lambda-form inner chain (both the semantic match at line 3863 and the syntactic fallback at line 3870), call `ResolveContextFromCallSite(lambdaParentInv, semanticModel, ct)` on the parent invocation and pass the result to `InnerChainDetection`.

3. **Apply fallback in RawCallSite construction** (line 844): Change the `contextClassName` argument from:
   ```csharp
   contextClassName: ResolveContextFromCallSite(invocation, semanticModel, cancellationToken),
   ```
   to:
   ```csharp
   contextClassName: ResolveContextFromCallSite(invocation, semanticModel, cancellationToken)
       ?? (innerChainDetection.IsLambdaForm ? innerChainDetection.ParentContextClassName : null),
   ```

**Tests**: Run full suite — all 3027 existing tests must still pass (no regressions).

## Phase 2: Add lambda set-op end-to-end tests

Add tests in `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` for lambda-form set operations. These tests exercise the multi-context scenario where `User` is registered in all four contexts (Lite/Pg/My/Ss):

1. **Lambda Union same entity** — `Pg.Users().Where(...).Union(x => x.Where(...)).Prepare()` across all dialects. Verify correct identifier quoting per dialect.

2. **Lambda UnionAll** — same pattern with `UnionAll`.

3. **Lambda Intersect** — same pattern with `Intersect`.

4. **Lambda Except** — same pattern with `Except`.

These tests directly verify the fix: without parent context propagation, the inner chain sites would get null context, and the registry would return the wrong context entry, producing wrong dialect quoting (e.g., MySQL backticks on a PostgreSQL query).

**Tests**: Run full suite — all existing + new tests must pass.
