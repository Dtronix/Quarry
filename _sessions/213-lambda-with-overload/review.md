# Review: 213-lambda-with-overload

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 6 (set-op lambda tests) deferred to follow-up due to context resolution gap (#216). Plan called for end-to-end set-op tests in this branch. | Low | Tracked as #216. The analysis + emission code for set-op lambdas IS present and structurally correct. Only the test infrastructure can't exercise it yet due to multi-context entity resolution ambiguity in the discovery pipeline. |
| Phase 7 partial: old set-op API retained (plan called for removal). Lambda set-op overloads added alongside, not replacing. | Low | Correct decision given the deferred set-op tests — removing the old API without end-to-end validation of the new one would be risky. The old API can be removed once #216 is fixed. |
| Phase 7e (rename lambda-specific identifiers) not done. `LambdaInnerSpanStart`, `:lambda-inner:`, `isLambdaInnerChain` still use lambda-specific names. | Low | Plan said to rename after old forms are removed. Since old CTE forms are removed but old set-op forms remain, keeping the lambda-prefixed names is reasonable. Can be renamed when set-op old forms are also removed. |
| Column reduction gap in lambda-form `With<TEntity, TDto>` — inner CTE SQL selects all entity columns instead of just the projected ones. | Medium | Tracked as #215. Functionally correct (outer query still binds to DTO properties), but the inner CTE transfers more columns than necessary. Documented in test assertions with comments explaining the gap. |
| Diagnostic tests for lambda-specific errors (plan phase 5c) not added. | Low | Tracked as #217. The existing QRY082 (duplicate CTE name) test was migrated to lambda form and works. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| RawCallSite copy methods: all four copy methods (`WithOperandEntityTypeName`, `WithEntityTypeName`, `WithResultTypeName`, `WithInterceptableLocation`) now correctly propagate `LambdaInnerSpanStart`. | Verified Fix | Previously dropped during copy, causing lambda CTE sites to lose their inner chain linkage after pipeline transformations. Fix confirmed correct by reading all four copy method implementations. |
| RawCallSite `Equals` includes `LambdaInnerSpanStart`. `GetHashCode` does not, but this follows the existing pattern (hash uses only identity fields). | OK | Consistent with pre-existing design: `GetHashCode` is a fast approximation, `Equals` is exhaustive. No correctness issue. |
| XML doc fix: `QuarryContext.With<TDto>` has full docs, `With<TEntity, TDto>` uses `<inheritdoc cref="..." path="/remarks"/>` (no duplicate summary). `QuarryContext<TSelf>` overrides use single `<inheritdoc cref="..."/>`. Cref uses curly-brace syntax for type params. | Verified Fix | Prior round found duplicate `<summary>` blocks and broken `<inheritdoc>` crefs. Both issues are now resolved. |
| `ConsumedLambdaInnerSiteIds` uses `??=` (null-coalescing assignment) in `ChainAnalyzer.AnalyzeChains`. If a prior run's IDs were not `Clear()`-ed (e.g., due to an error between `Analyze()` and `PipelineOrchestrator`), stale IDs would accumulate. | Low | In practice, `PipelineOrchestrator` always runs after `Analyze()` and calls `Clear()`. The risk is theoretical — a pipeline crash between the two would leave stale IDs that could incorrectly filter out sites in the next incremental run. A defensive `Clear()` at the start of `AnalyzeChains` would be more robust. |
| `DetectInnerChain` syntactic fallback: when the semantic model can't resolve the parent `With`/`Union` call, the code accepts the syntactic match (`parentSymbol == null`). If a non-Quarry method named "Union" with a lambda argument existed, it would be falsely detected as an inner chain. | Low | The `InnerChainParentMethods` set (`With`, `Union`, `UnionAll`, etc.) is specific enough that false positives are extremely unlikely in practice. A user would need a non-Quarry fluent API method with one of these exact names taking a lambda argument on a type that the semantic model can't resolve. |
| Lambda inner chain `executionSite` assignment: `clauseSites[0]` is used as the execution site BUT remains in `clauseSites` (unlike direct-form CTE inner chains where ChainRoot is removed). This means the first clause site serves double duty. | OK | This is intentional and correct. Lambda inner chains have no ChainRoot, so the first clause site (e.g., `Where`) provides entity/table resolution info and must also be processed as a SQL clause. The downstream code that uses `executionSite` only reads entity type and table name from it — it doesn't generate execution-specific SQL from it. |
| `CarrierAnalyzer.BuildExtractionPlans`: set-op sites (both lambda and direct form) are handled by a blanket `if (IsSetOperationKind)` guard that always `continue`s. For direct-form set-ops, this skips the regular clause extraction path. | OK | Verified against master: direct-form set-ops never had extraction plans in `CarrierAnalyzer`. They use carrier-to-carrier P-field copy in `SetOperationBodyEmitter`. The `continue` is correct for both forms. |
| `CarrierAnalyzer.setOpIndex` is incremented for ALL set-op sites (both lambda and direct), regardless of whether extraction was performed. This ensures correct index-to-`SetOperationPlan` alignment. | OK | Correct behavior: `setOpIndex` must track position in `assembled.Plan.SetOperations` regardless of form. |

## Security

No concerns. The changes are in the source generator (compile-time only) and runtime API surface. No user input parsing, no network calls, no auth changes. The `UnsafeAccessor` pattern for display class field extraction is the same pattern already used throughout the codebase for captured variable binding.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| CTE lambda tests cover: simple filter (no params), captured param (with live re-capture verification), two chained CTEs with captured params, DTO projection, entity accessor without params, entity accessor with captured param. 6 tests across 4 dialects. | OK | Good coverage of the CTE lambda path. Each test validates SQL output AND runtime execution results. |
| All existing CTE tests migrated from direct-argument to lambda form: CrossDialectCteTests (29 call sites), CteWithEntityAccessorTests (5 call sites), CarrierGenerationTests (2 call sites for QRY082). | OK | Mechanical migration ensures no regression in existing test scenarios. |
| Set-op lambda tests entirely absent (deferred to #216). | Medium | Tracked. The set-op lambda analysis and emission code is present but has no end-to-end test coverage. The context resolution gap blocks these tests. |
| No test for lambda CTE with multiple captured variables of different types in a single lambda. | Low | The multi-CTE test captures different variables per CTE (`orderCutoff: decimal`, `activeFilter: bool`), but no test captures multiple variables within a single lambda body. The extraction pipeline handles this (dedup by `CapturedFieldName`), but it's untested. |
| No test for lambda CTE where the inner chain has zero clause sites (e.g., `With<Order>(orders => orders)` — identity select). | Low | Edge case. Would result in a lambda inner chain group with only the parameter root site and no clauses. Likely produces valid SQL (SELECT all columns), but untested. |
| Diagnostic tests (#217): QRY082 (duplicate CTE name) migrated to lambda form. No new lambda-specific diagnostic tests. | Low | Tracked as #217. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `ConsumedLambdaInnerSiteIds` follows the same `[ThreadStatic]` pattern as `TestCapturedChains`. | OK | Consistent with existing codebase pattern for cross-method state passing in the generator pipeline. |
| `InnerChainDetection` struct follows the same readonly struct pattern used elsewhere in the codebase. `InnerChainDetection.None` as a static default mirrors similar sentinel patterns. | OK | |
| `EmitLambdaInnerChainCapture` in `CarrierEmitter` reuses `EmitCarrierParamBindings` for the P-field binding step, avoiding code duplication. | OK | Good reuse of existing infrastructure. |
| `BuildLambdaInnerExtractionPlanFromParams` in `CarrierAnalyzer` mirrors the structure of the existing clause extraction plan builder, using the same `CapturedVariableExtractor` model. | OK | Consistent with existing patterns. |
| Lambda set-op overloads on `IQueryBuilder<TEntity, TResult>` include both same-entity and cross-entity variants (12 methods), matching the existing direct-form pattern exactly. | OK | |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **Breaking**: `With<TDto>(IQueryBuilder<TDto>)` and `With<TEntity, TDto>(IQueryBuilder<TEntity, TDto>)` removed from `QuarryContext`, `QuarryContext<TSelf>`, and generated context classes. Replaced by lambda overloads. | High (intentional) | Per design decision: lambda-only API. Any consumer using the old direct-argument form must migrate to lambda form: `db.With<T>(db.Orders().Where(...))` becomes `db.With<T>(orders => orders.Where(...))`. This is a compile-time break — migration is mechanical. |
| Old set-op direct-argument API (`Union(IQueryBuilder<T>)` etc.) retained alongside lambda overloads. | OK | No break for set-op consumers. Both forms coexist until #216 is resolved. |
| Lambda set-op overloads added as default interface methods on `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>` with `InvalidOperationException` defaults. | OK | Additive change. Existing implementations don't need to implement these methods (they use default interface methods). The source generator intercepts them at compile time. |
| Generated context code (`ContextCodeGenerator.GenerateCteMethods`) now emits lambda-form `With` methods only. | OK | Matches the runtime API change. Interceptor signatures in `TransitionBodyEmitter` correctly use `Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>>` for lambda form and `IQueryBuilder<TDto>` for direct form (via `isLambdaForm` flag). |
| Manifest file changes: discovery count drops by 7-10 per dialect (e.g., SQLite 575->565). Inner chain sites are no longer separately intercepted. | OK | Expected consequence of lambda inner chain consumption. Inner chain SQL is embedded at compile time — no runtime interceptors needed. |
| Inner CTE SQL for `With<TEntity, TDto>` lambda form selects all entity columns (no column reduction). | Medium | Tracked as #215. Functionally correct but generates wider SQL than the old form for projected CTEs. Not a break per se, but a behavioral difference consumers should be aware of. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| RawCallSite copy methods fixed | Correctness | Verified | Fixed in prior round — confirmed correct |
| XML doc fix | Correctness | Verified | Fixed in prior round — confirmed correct |
| ConsumedLambdaInnerSiteIds stale state | Correctness | D | Follows existing ThreadStatic pattern |
| DetectInnerChain syntactic fallback | Correctness | D | Extremely unlikely false positives |
| No multi-capture-in-single-lambda test | Test Quality | D | Dedup logic tested indirectly via multi-CTE test |
| No identity lambda test | Test Quality | D | Edge case, low risk |
| Phase 6 set-op tests deferred | Plan Compliance | D | Tracked as #216 |
| Phase 7 partial (old set-op API retained) | Plan Compliance | D | Blocked by #216 |
| Phase 7e rename deferred | Plan Compliance | D | Premature while old set-op forms exist |
| Column reduction gap | Plan Compliance | D | Tracked as #215 |
| Diagnostic tests not added | Plan Compliance | D | Tracked as #217 |
| Set-op lambda tests absent | Test Quality | D | Tracked as #216 |
| Breaking: old CTE API removed | Integration | D | Intentional per design decision |
| Inner CTE wider SQL for projected CTEs | Integration | D | Tracked as #215 |

## Issues Created
- #215: Lambda With<TEntity, TDto> inner CTE selects all columns instead of projected subset
- #216: Set-op lambda context resolution gap for multi-context entity types
- #217: Add diagnostic tests for lambda CTE form (QRY080 equivalent)
