# Code Review: generator-review-fixes

## Plan Compliance

All 8 planned phases are implemented and each commit maps 1:1 to a plan phase:

| Plan Phase | Commit | Status | Notes |
|---|---|---|---|
| Phase 1: Fix SubqueryExpr traversal (C1+C2+C5) | `ab50772` | Implemented as specified | Matches plan pseudocode exactly |
| Phase 2: Fix equality gaps (C3+C4) | `33bf360` | Implemented as specified | All 8 AssembledPlan + 4 QueryParameter properties added |
| Phase 3: Cache GetClauseEntries (H1) | `e464d55` | Implemented as specified | Lazy init with `_clauseEntries` field |
| Phase 4: Pre-compute site params + conditional map (H2+H3) | `0a678f5` | Implementation differs from plan | See finding below |
| Phase 5: Fix SqlExpr node hashes (H7) | `c243833` | Implemented as specified | All 3 node types (ParamSlot, Like, CapturedValue) updated |
| Phase 6: Unify terminal eligibility (H8) | `20eba10` | Implemented as specified | FileEmitter delegates to CarrierEmitter |
| Phase 7: Extract shared patterns (M2+M5) | `0b8ff61` | Implemented as specified | RemapProjectionParameters + SqlLikeHelpers.FormatConstantAsSqlLiteral |
| Phase 8: Small improvements (M8+M10+M11) | `3a77770` | Implemented as specified | RenderTo overload, catch(Exception), ternary marker |

| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 4 approach differs from plan: plan specified a `BuildSiteParamsMap` helper in `TerminalEmitHelpers` with callers receiving the pre-computed map as a parameter. Actual implementation moves the logic into `AssembledPlan` with lazy-cached `GetSiteParams`/`GetParamConditionalMap` methods, keeping `ResolveSiteParams`/`BuildParamConditionalMap` in `TerminalEmitHelpers` as thin wrappers. | Low | The actual approach is arguably better: it localizes caching near the data owner and requires zero call-site changes. The plan's threading-a-map-through-8-callers approach would have been more invasive. No design principle violated. |
| Plan Phase 1 specified adding new tests for subquery selector parameter extraction. No new tests were added. | Medium | The plan explicitly called for tests exercising aggregate subqueries with captured variables in selectors, and subqueries with both predicate and selector containing parameters. These would validate the C1/C2/C5 bug fixes directly rather than relying on existing tests that may not exercise these specific code paths. |

## Correctness

### SubqueryExpr reconstruction in SqlExprClauseTranslator

| Finding | Severity | Why It Matters |
|---|---|---|
| No concerns with the four-case matrix. | -- | When both predicate and selector are null: `predicateChanged` and `selectorChanged` are both false, returns original `sub`. When only predicate is non-null: processes predicate, `selectorChanged` is false (both null), reconstructs only if predicate changed, passes `selector: null`. When only selector is non-null: processes selector, `predicateChanged` is false (both null), reconstructs only if selector changed, passes `newPredicate` (which is null). When both present: processes both, reconstructs if either changed. All four cases are correct. |
| ImplicitJoins are correctly preserved via `sub.ImplicitJoins != null ? result.WithImplicitJoins(sub.ImplicitJoins) : result`. | -- | The plan identified C2 (ImplicitJoins dropped during reconstruction) and the fix correctly re-attaches them after constructing the new SubqueryExpr. |

### AssembledPlan equality

| Finding | Severity | Why It Matters |
|---|---|---|
| `SequenceEqual(ClauseSites, ...)` is order-dependent, which is correct here since clause order is semantically significant (determines SQL clause ordering). | -- | `ClauseSites` is constructed in call-site order and never reordered. Order-dependent comparison is the right choice. |
| `Equals(PrepareSite, other.PrepareSite)` uses `object.Equals` which correctly handles null on either side and dispatches to `TranslatedCallSite.Equals`. Same for `InsertInfo`. | -- | Both types implement `IEquatable<T>` with proper `Equals(object?)` overrides. |
| The `_clauseEntries`, `_siteParamsMap`, and `_paramConditionalMap` cache fields are correctly excluded from equality (they are private fields, and `Equals` only checks public/internal properties). | -- | Derived caches must not participate in equality or the incremental pipeline would compare stale cached data. |

### Cached GetSiteParams / GetParamConditionalMap thread safety

| Finding | Severity | Why It Matters |
|---|---|---|
| The lazy caching uses simple null-check-then-assign without any locking or `Interlocked` operations. This is safe in the source generator context. | -- | Roslyn source generators execute on a single thread per `GeneratorExecutionContext`. The `AssembledPlan` instances are created and consumed within a single pipeline execution. Even if two threads raced, the worst case is redundant dictionary construction (both would produce identical results from immutable inputs), and the final assignment is a reference write which is atomic on .NET. No correctness risk. |
| `GetSiteParams` returns `(new List<QueryParameter>(), 0)` for not-found site IDs, while the old `ResolveSiteParams` returned `(new List<QueryParameter>(), totalGlobalOffset)`. | Low | The not-found fallback path should never be reached in practice (callers always pass IDs from the chain's own clause list). If it were reached, the offset difference (0 vs total) could produce incorrect parameter indexing. However, this would indicate a pre-existing bug in the caller, not a regression from this change. |

### Other correctness items

| Finding | Severity | Why It Matters |
|---|---|---|
| No concerns with SqlExprRenderer.CollectParamsRecursive additions. | -- | `SubqueryExpr.Selector` and `RawCallExpr.Arguments` traversal correctly recurses into all child nodes that may contain `ParamSlotExpr`. |
| No concerns with SqlExprParser ternary change. | -- | Replacing `conditional.ToString()` (raw C# syntax) with `/* unsupported: C# ternary expression */` is strictly better: the old output would produce invalid SQL silently, while the new output produces a visible SQL comment marker. |

## Security

No concerns. This is a compile-time source generator operating on the developer's own code. There are no user-input-to-SQL paths; the SQL literal formatter operates on compile-time constants only.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| No new tests were added for the SubqueryExpr traversal fixes (C1/C2/C5). | Medium | These are correctness bug fixes where the plan explicitly specified adding tests for: (1) aggregate subquery with captured variable in selector, (2) subquery with both predicate and selector parameters, (3) verification of proper `@p` placeholders. Without these, the fixes are validated only by the existing 3,236-test regression suite, which may not exercise the specific SubqueryExpr-selector-with-captured-values code path that was broken. |
| The `FormatConstantTests.InvokeEscape` helper was updated from reflection to direct call, which is an improvement. | -- | Direct calls are faster, compile-time checked, and won't break if the method is renamed. Good change. |
| The `FormatConstantTests.InvokeFormat` helper still uses reflection to call the private `FormatConstantAsSqlLiteralSimple`. | Low | This is now a thin wrapper around the public `SqlLikeHelpers.FormatConstantAsSqlLiteral`. The test could call the public method directly for the same benefits as the escape helper. Minor inconsistency. |
| No tests validate the cached `GetSiteParams`/`GetParamConditionalMap` produce identical results to the old uncached implementations. | Low | The existing 3,236 tests serve as the regression gate and the logic was moved (not rewritten). But a direct unit test comparing old vs new would catch any subtle port errors. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| No concerns with code style or patterns. | -- | The new code follows existing conventions: `internal static` helpers, XML doc comments, `IEquatable<T>` patterns, `HashCode.Combine` / builder pattern for hashing, lazy field caching via null-check. |
| The `SqlLikeHelpers` placement in `Translation/` namespace is consistent with existing translation utilities. | -- | Reuses the existing `SqlLikeHelpers` file rather than creating a new utility class. |
| The `RenderTo` overload follows the existing `Render` method signature pattern with an added `StringBuilder` parameter. | -- | The overload is public but no callers use it yet (only the existing `Render` delegates to it). This is future-facing infrastructure. |
| Backslash escaping was added to `ProjectionAnalyzer.FormatConstantForSql` via the shared formatter. | Low | The old `ProjectionAnalyzer` code only escaped single quotes (`s.Replace("'", "''")`). The new shared formatter also escapes backslashes. Additionally, the old code did not handle `char` as a SQL string literal; the new code does. These are behavioral differences, though they are arguably improvements (more robust escaping). The risk is minimal since compile-time string constants rarely contain backslashes, and `char` constants in SQL projections are uncommon. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| `SqlLikeHelpers.FormatConstantAsSqlLiteral` and `EscapeSqlStringLiteral` are `public`. | Low | These are `public` methods on an `internal` class, so they are only accessible within the generator assembly. No external API surface change. |
| `SqlExprRenderer.RenderTo` is a new `public static` method on an `internal` class. | -- | Same as above: no external API surface impact. |
| `CarrierEmitter.WouldExecutionTerminalBeEmitted` was already `internal static` on master. The branch does not change its signature. | -- | FileEmitter now calls it, but both are in the same assembly. No breaking change. |
| The `AssembledPlan.Equals` expansion adds 8 new property comparisons. This means plans that previously compared as equal (due to missing fields) will now correctly compare as unequal when those fields differ. | -- | This is the intended fix (C3). It will cause the incremental pipeline to regenerate code in cases where it previously served stale cache. This is a correctness improvement, not a breaking change. The same applies to `QueryParameter.Equals` (C4). |
| The `GetHashCode` changes for `ParamSlotExpr`, `LikeExpr`, and `CapturedValueExpr` will change hash distribution. | -- | Hash changes don't affect equality semantics. They improve cache bucket distribution, reducing collision-driven `DeepEquals` calls. No functional impact. |
| The ternary expression handling change (`SqlExprParser`) changes generated SQL output from raw C# syntax (e.g., `x > 0 ? a : b`) to `/* unsupported: C# ternary expression */`. | Low | Any user code relying on the old raw-C#-in-SQL behavior (which would have produced invalid SQL anyway) will see different output. This is intentionally a better diagnostic experience. |

## Summary

The branch is well-structured with clean 1:1 commit-to-phase mapping and no scope creep. The correctness fixes (SubqueryExpr traversal, equality gaps) are implemented correctly. The performance optimizations (caching) are sound and safe for the single-threaded source generator context. The consolidation work (shared formatters, unified eligibility) reduces duplication without introducing regressions.

The primary gap is the absence of the unit tests specified in the plan for Phase 1 (SubqueryExpr selector parameter extraction). These tests would provide direct validation of the C1/C2/C5 bug fixes rather than relying on indirect coverage from the existing test suite.
