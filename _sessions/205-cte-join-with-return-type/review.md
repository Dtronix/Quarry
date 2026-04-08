# Review: 205-cte-join-with-return-type

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 2 generator tests not implemented. Plan specified `ContextCodeGeneratorCteShadowTests.cs` with 3 test variants (legacy context emits shadow, generic context omits shadow, both emit FromCte). No test file was created. | Medium | The conditional emission was reverted (shadow is always emitted now), so the "skip emission" tests no longer apply. However, the plan also called for a CS0109 absence test to guard against regressions. The revert means CS0109 is suppressed by `new` hiding `override`, but there is no unit test confirming the generated shadow compiles without warnings on a generic-base context. If someone removes the `new` keyword from the generator template, CS0109 would surface with no test catching it. |
| Phase 4: 5 of 7 planned tests implemented. Tests 6 (FromCte on generic base) and 7 (projected inner query with variable) were dropped. | Low | Both removals are documented as design decisions in workflow.md with clear rationale (entity type mismatch for test 6, QRY080 limitation for test 7). The 5 remaining tests cover the core regression target (With+entity+Join+Select). Acceptable scope reduction. |
| Phase 2 conditional emission reverted; `HasGenericContextBase` flag is computed but never consumed. | Low | Documented in workflow.md (session 2 key decisions). The flag is correctly wired into `ContextInfo.Equals`/`GetHashCode` so it does not cause stale caching. However, it is dead code in the current PR. Either remove it (clean diff) or keep it with a comment noting planned future use. |
| Plan specified `new` keyword for `With<>` on generic subclass; implementation uses `virtual`/`override` with covariant return. | Info | This is a deliberate mid-implementation correction documented in workflow.md (2026-04-07 decision). The plan's `new` approach caused CS9144 errors due to Roslyn candidate ambiguity. `virtual`/`override` with covariant return is the correct fix. The plan deviation is well-justified. |
| 3 WIP commits included in branch history (`d307892`, `994e9a0`, `7bc6580`). | Low | These represent intermediate save points during multi-session development. They should be squashed or cleaned before merging to keep history readable. The final meaningful commits are clean. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `GetExplicitTypeArgumentCount` method added to `UsageSiteDiscovery.cs` (line 2186) but never called. | Low | Dead code. It was likely added during the session 3 work on builder type tracking but superseded by the `virtual`/`override` approach in session 4. Should be removed. |
| `EmitChainRootAfterCte` uses `Unsafe.As<IEntityAccessor<T>>(@this)` -- same proven pattern as `EmitFromCte`. | Info | Correct. The carrier was already created by `EmitCteDefinition`; the chain root is a noop cast. The pattern is well-established. |
| `chainHasCte` linear scan over `carrierChain.ClauseSites` on every ChainRoot emission. | Low | Functionally correct but O(n) per chain root where n = number of clause sites. Chains are typically short (< 10 sites), so this is not a performance concern in practice. A flag on the carrier chain would be cleaner but is not worth changing for this PR. |
| `DiscoverPostCteSites` changed from `continue` to `break` when a method resolves normally (line 1143). | Medium | The new logic assumes that once a method in the chain resolves, all subsequent methods will also resolve. This is true for the generic-base case (entity accessor returns a typed `IEntityAccessor<T>`, so all subsequent builder methods resolve). For legacy contexts, the only resolvable post-CTE methods are `FromCte`/`With` which are `continue`'d before reaching this check, so the `break` is never hit. However, if a future code path produces a partially-resolvable chain (first method resolves, later method doesn't), synthetic sites for the later methods would be lost. The comment documents the assumption well. Acceptable for now given the follow-up plan to delete `DiscoverPostCteSites` entirely. |
| Dedup in `DisplayClassEnricher.EnrichAll` keeps first occurrence. | Medium | Documented in workflow.md as a known issue: "prefers synthetic sites over normal discovery sites, which may have more accurate type info." For the generic-base case this is benign because `DiscoverPostCteSites` breaks early and produces no synthetic sites for resolvable methods. For legacy contexts, the ordering depends on which discovery path runs first. Since tests pass (3017 green), this is not causing issues now, but the "keep first" heuristic is fragile. The workflow correctly identifies this as technical debt to resolve when `DiscoverPostCteSites` is deleted. |
| `virtual` on base `With<>` means subclasses that use `override` (not `new`) would get a different vtable slot. | Info | The generated shadow uses `new`, not `override`, so the virtual dispatch is never exercised at runtime (the `new` method hides it and the interceptor replaces the call). The `virtual` exists solely to enable the covariant `override` in `QuarryContext<TSelf>`. Correct design. |

## Security

No concerns. This is a source generator change. The `With<>` methods throw `NotSupportedException` at runtime (bodies are replaced by interceptors). No user input reaches SQL generation through any new path. The CTE inner query is processed through the existing `IQueryBuilder` pipeline with its parameterization.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| 5 integration tests cover the core chains: Select, Where+Select, Join+Select, Join+Where+Select, Join+Select with parameter. | Info | Good coverage of the primary regression target. Each test validates both SQL output (string assertion on `ToDiagnostics().Sql`) and runtime execution (row counts and values from seeded SQLite data). |
| No negative test for legacy context regression. | Low | The plan notes "All `CrossDialectCteTests.cs` tests against `TestDbContext` (legacy base) must still pass." The 3012 pre-existing tests serve this role implicitly. An explicit test documenting that `With<>.Users()` on a non-generic context is still silently dropped (the known limitation) would be valuable documentation but is not blocking. |
| No unit test for `AnalyzeSingleEntitySyntaxOnly`. | Low | The new `ProjectionAnalyzer` method is exercised indirectly through the integration tests (the non-joined Select in `Cte_Users_Select` and `Cte_Users_Where_Select`). A focused unit test would improve fault isolation but the integration coverage is adequate. |
| No test for multiple CTEs in the same chain (e.g., `.With<A>(...).With<B>(...).Users()`). | Medium | The `EmitChainRootAfterCte` logic checks whether ANY CteDefinition exists in the chain. Multiple CTEs followed by a ChainRoot should work (the carrier is still the one created by the first CTE), but there is no test coverage for this pattern. If the emitter logic later changes, this gap could allow a regression. |
| Tests use `Cte.Order` and `Cte.CteDb` via namespace alias (`using Cte = Quarry.Tests.Samples.Cte`). | Info | Clean pattern to avoid ambiguity with `Quarry.Tests.Samples.Order`. Consistent with how the codebase handles namespace conflicts. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `CteDb` test context has no explicit constructor. | Low | Other test contexts (`TestDbContext`, `PostgreSqlDbContext`, etc.) declare explicit constructors. `CteDb` relies on the generated partial to provide the constructor. This works but is inconsistent with the existing pattern. If the generator's constructor emission changes, `CteDb` would break without an obvious fix in user code. Consider adding an explicit constructor for consistency with other test contexts. |
| `QuarryContext<TSelf>` placed at bottom of `QuarryContext.cs` rather than in a separate file. | Info | Plan noted this as a decision point. Co-locating both types in one file is reasonable given their tight coupling. The file is now ~660 lines, which is within normal bounds for a base class file. |
| The CTE candidate disambiguation block (lines 195-240 in the diff) adds ~45 lines of method-name-specific logic to the general `DiscoverRawCallSites` method. | Low | Scoped to `"With" or "FromCte"` method names, so it won't fire for other methods. But it adds complexity to an already long method. This is acceptable as temporary code that will be removed when `DiscoverPostCteSites` is deleted. |
| XML doc comments on `QuarryContext<TSelf>` and its methods are thorough and explain the "why" (SemanticModel visibility). | Info | Good. Follows the existing documentation style in `QuarryContext`. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `With<>` changed from non-virtual to `virtual` on `QuarryContext`. | Medium | This is a **source-compatible but binary-breaking** change. Existing compiled assemblies that reference `QuarryContext.With<>` will have a call to a non-virtual method; after the update, the method is virtual. At the IL level, `call` vs `callvirt` would differ. However, since these methods always throw `NotSupportedException` (they're replaced by interceptors), the runtime behavior is identical: if an old binary somehow calls the base method, it still throws. New compilations will emit `callvirt` instead of `call`, which is harmless. The practical risk is near-zero because (a) the method body is a throw, (b) all call sites are intercepted, and (c) Quarry is a source-generator-dependent library where recompilation is the normal path. |
| `QuarryContext<TSelf>` is a new public type in the `Quarry` namespace. | Info | Purely additive. No existing code is affected. Users opt in by changing their base class. The CRTP constraint (`where TSelf : QuarryContext<TSelf>`) prevents misuse. |
| Generated `new` shadow on contexts inheriting `QuarryContext<TSelf>` hides the `override` rather than the base `virtual`. | Info | This means the generated `new CteDb With<TDto>(...)` hides `QuarryContext<CteDb>.With<TDto>` (the override), not `QuarryContext.With<TDto>` (the virtual base). The `new` keyword is valid in both cases (it hides the most-derived inherited member). No CS0109 warning because there IS an inherited member to hide. Verified by tests passing without warnings. |
| CTE-to-join table resolution not implemented. | Info | Documented as deferred (workflow.md decision 2026-04-07). `Join<Order>` after `With<Order>` joins the real "orders" table, not the CTE. Test expectations are written accordingly. Not a breaking change -- it's a known limitation of the current CTE+Join feature. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `GetExplicitTypeArgumentCount` is dead code | Correctness | B (should fix) | Flag for removal before merge |
| `HasGenericContextBase` computed but unused | Plan Compliance | C (follow-up) | Keep if planned for future use; otherwise remove |
| Phase 2 generator tests not written | Test Quality | C (follow-up) | Not blocking; conditional emission was reverted. Consider adding a smoke test for CS0109 absence |
| No multi-CTE chain test | Test Quality | B (should fix) | Add a test for `.With<A>(...).With<B>(...).Users()` before or shortly after merge |
| 3 WIP commits in history | Codebase Consistency | B (should fix) | Squash before merge |
| `CteDb` missing explicit constructor | Codebase Consistency | C (follow-up) | Minor consistency issue, not blocking |
| Dedup "keep first" heuristic fragility | Correctness | C (follow-up) | Resolves when `DiscoverPostCteSites` is deleted |
| `virtual` on `With<>` is binary-breaking | Integration | A (accepted risk) | Near-zero practical impact; documented |

## Issues Created

None yet. Recommended issues to file:

1. **Remove dead code from #205 PR**: Delete `GetExplicitTypeArgumentCount` and optionally `HasGenericContextBase` if no immediate follow-up will use it.
2. **Add multi-CTE chain test**: Cover `.With<A>(...).With<B>(...).Users().Select(...)` to validate the `EmitChainRootAfterCte` path with multiple CTE definitions.
3. **Migrate in-repo contexts to `QuarryContext<TSelf>` and delete `DiscoverPostCteSites`** (already identified in plan as follow-up).
4. **Apply typed-base-library principle to `NavigationList<T>`** to eliminate `DiscoverPostJoinSites` (already identified in plan as follow-up).
