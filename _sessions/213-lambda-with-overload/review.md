# Review: 213-lambda-with-overload

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 6 (set-op lambda tests) deferred -- only analysis infrastructure was implemented, no end-to-end tests | Medium | Plan called for comprehensive set-op lambda tests in Phase 6. Tests were deferred due to the context resolution gap. This is documented in handoff.md as a known deferral. |
| Phase 7 partially completed -- old set-op API retained alongside new lambda API | Medium | Plan Phase 7 called for removing all non-lambda set-op overloads. Old forms were intentionally retained pending the context resolution fix. Documented in handoff.md. |
| Phase 7e (rename identifiers) not performed -- `LambdaInnerSpanStart`, `:lambda-inner:`, `IsLambdaInnerChain` still use lambda-specific names | Low | Plan called for renaming these to generic names (e.g., `InnerChainSpanStart`, `:inner:`). Since old forms coexist, the rename is premature and was correctly deferred. |
| Plan Phase 5b (captured parameter edge cases) partially covered | Low | Plan listed 5 sub-items for captured parameter tests. Tests cover local variable capture, method parameter capture, and no-capture cases. Static field capture and multiple-variable capture tests are missing from LambdaCteTests (though multi-variable capture is tested in CrossDialectCteTests via the migrated multi-CTE test). |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `RawCallSite.WithOperandEntityTypeName()` does not copy `LambdaInnerSpanStart` to the new instance | High | When `CallSiteBinder` normalizes entity types on CTE definition or set-op sites, it creates a copy via `WithOperandEntityTypeName()`. The copy loses `LambdaInnerSpanStart`, causing the lambda detection to silently fall through to the direct (old) path. This would break lambda CTE captured-parameter emission for entities that undergo namespace normalization. Same issue in `WithEntityTypeName()` and `WithResultTypeName()`. All three copy methods in `RawCallSite.cs` (lines 247, 310, 372) must pass `lambdaInnerSpanStart: LambdaInnerSpanStart` to the constructor. |
| Duplicate XML doc `<summary>` blocks on `QuarryContext.With<TDto>(Func<...>)` | Low | Lines 130-149 of `QuarryContext.cs` contain the old `<summary>` block (from the removed `With<TDto>(IQueryBuilder<TDto>)` signature) followed immediately by the new `<summary>` block at lines 150-159. The compiler sees two `<summary>` tags stacked on one method. Only affects generated documentation, not runtime behavior. |
| Broken `<inheritdoc>` cref references | Low | Lines 155 and 168 of `QuarryContext.cs` reference `With{TDto}(IQueryBuilder{TDto})` which no longer exists (old API was removed). The `<inheritdoc>` resolves to nothing. Should reference the lambda-form signature instead or inline the remarks. |
| Lambda-form `With<TEntity, TDto>` inner CTE column reduction not applied | Medium | Known issue (documented in handoff.md). The inner CTE SQL selects all entity columns instead of only the projected ones. Tests were updated to accept the wider column list. This produces correct results but transfers more data than necessary and breaks SQL output parity with the old non-lambda form. |
| Set-op lambda context resolution gap | Medium | Known issue (documented in handoff.md and workflow.md decisions). Lambda inner chain sites inside set-op lambdas get the wrong context class when the entity type is registered in multiple contexts. CTE lambdas are unaffected because `With()` is resolved on the context class. |
| `ConsumedLambdaInnerSiteIds` ThreadStatic cross-invocation accumulation | Low | The `HashSet` is created via `??=` and entries are added during `Analyze()`, but it is only cleared in `PipelineOrchestrator` after use. If `Analyze()` runs multiple times before `PipelineOrchestrator` (e.g., incremental generator reruns), entries from earlier runs accumulate. The `Clear()` in PipelineOrchestrator prevents cross-orchestrator leaks, but accumulated entries within a single orchestrator run could cause over-filtering. In practice this follows the same pattern as the existing `TestCapturedChains` ThreadStatic. |
| Syntactic fallback in `DetectInnerChain` when `parentSymbol == null` | Low | When the semantic model cannot resolve the parent method symbol at all (no symbol, no candidates), the code accepts the syntactic match purely based on method name membership in `InnerChainParentMethods`. This could produce false positives if user code has non-Quarry methods named `With`, `Union`, `Intersect`, or `Except` that accept lambda parameters. The risk is low due to the specific syntactic shape required (lambda as argument to member-access invocation). Documented as an intentional design decision. |

## Security

No concerns. This change is entirely within the source generator and compile-time analysis pipeline. No user input is processed at runtime, no new external dependencies are introduced, and no auth/data-exposure paths are affected.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No set-op lambda end-to-end tests | Medium | The lambda set-op analysis and emission code (Phase 4/6) has no test coverage. This is known/deferred per handoff.md due to the context resolution gap. The emission code paths (`SetOperationBodyEmitter` lambda branch, `CarrierAnalyzer` set-op lambda extraction) are untested. |
| No negative/diagnostic tests for lambda CTE | Low | Plan Phase 5c called for diagnostic tests (QRY080/QRY081/QRY082 equivalents for lambda form). The only diagnostic test present is the migrated `QRY082_DuplicateCteName` which was updated to lambda form. Tests for invalid lambda bodies (QRY080 equivalent) and `CteInnerChainNotAnalyzable` diagnostic are absent. |
| Manifest output tests updated but verify a regression | Low | The manifest `.md` files show the inner CTE removing 5-7 entries (old direct-argument inner chains that are no longer discovered as standalone chains). The `OrderSummaryDto` CTE SQL changed from reduced columns to all columns -- this matches the known column-reduction gap but means the manifest tests now assert the degraded behavior. |
| LambdaCteTests effectively duplicate CrossDialectCteTests | Low | Several test methods in `LambdaCteTests.cs` test the same scenarios as the migrated `CrossDialectCteTests.cs` (e.g., simple filter, captured param, two chained CTEs, DTO). This is acceptable as the new file provides a clear lambda-specific test suite, but there is significant overlap. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `isLambdaInnerChain` boolean parameter on `AnalyzeChainGroup` | Low | The method already has 8+ parameters. Adding a boolean flag for behavioral branching (lines 376-393) is less maintainable than an enum or options object. However, this follows the existing pattern of the `isOperandChain` boolean parameter already present on the same method. |
| `lambdaInnerChainIds` and `lambdaInnerChainGroups` passed as separate nullable dictionaries | Low | `AnalyzeChainGroup` now has 3 new optional dictionary parameters (`lambdaInnerChainIds`, `lambdaInnerChainGroups`, `isLambdaInnerChain`). These could be bundled into a context object. This follows the existing pattern of passing `cteInnerResults` as a nullable dictionary. |
| Consistent use of `EmitLambdaInnerChainCapture` across CTE and set-op emission | None | Good reuse -- both `TransitionBodyEmitter` and `SetOperationBodyEmitter` delegate to `CarrierEmitter.EmitLambdaInnerChainCapture` for the lambda capture pattern. |
| `InnerChainDetection` readonly struct follows existing pattern | None | Good -- mirrors the existing tuple return pattern but with named properties for clarity. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **Breaking**: `With<TDto>(IQueryBuilder<TDto>)` removed from public API | High | The old `With<TDto>(IQueryBuilder<TDto> innerQuery)` and `With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)` overloads were removed from `QuarryContext` and `QuarryContext<TSelf>`. All consumers must migrate to the lambda form `With<TDto>(orders => orders.Where(...))`. This is an intentional breaking change per the plan and workflow decisions. |
| **Non-breaking**: Lambda set-op overloads added to `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>` | None | 18 new lambda-form set-op methods added with default implementations that throw `InvalidOperationException`. Since these are default interface methods, no existing implementations break. |
| Generated CTE shadow methods changed signature | High | `ContextCodeGenerator.GenerateCteMethods()` now emits `Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>>` parameter type instead of `IQueryBuilder<TDto>`. All generated code from this branch will have the new signature, requiring regeneration of all consumer projects. |
| Inner CTE SQL now selects all columns (regression from old form) | Medium | For `With<TEntity, TDto>` with Select projection, the inner CTE SQL now includes all entity columns instead of only the projected columns. This is functionally correct (outer query still binds to DTO properties) but produces wider inner queries. This is a behavioral regression compared to the old API. Known/deferred per handoff.md. |
| Manifest output changes | Low | Discovery count changed (e.g., SQLite 575->565 total discovered, 180->176 consolidated). Old inner chain standalone entries removed from manifest. These are expected side effects of the lambda inner chain model where inner chains are no longer discovered as independent chains. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `RawCallSite` copy methods drop `LambdaInnerSpanStart` | Correctness | A | Fix: add `lambdaInnerSpanStart: LambdaInnerSpanStart` to all 3 copy methods |
| Duplicate XML `<summary>` blocks on `QuarryContext.With<TDto>` | Correctness | A | Fix: remove stale doc block |
| Broken `<inheritdoc>` cref to removed signatures | Correctness | A | Fix: update cref to lambda-form signature |
| Lambda `With<TEntity,TDto>` column reduction gap | Correctness | C | Create tracking issue |
| Set-op lambda context resolution gap | Correctness | C | Create tracking issue |
| Missing negative/diagnostic tests for lambda CTE | Test Quality | C | Create tracking issue |
| Phase 6 set-op tests deferred | Plan Compliance | D | Known deferral, blocked by context resolution gap |
| Phase 7 partial, old set-op API retained | Plan Compliance | D | Known deferral |
| Phase 7e rename deferred | Plan Compliance | D | Premature while old forms coexist |
| Phase 5b captured param edge cases partial | Plan Compliance | D | Multi-var covered in CrossDialectCteTests |
| No set-op lambda e2e tests | Test Quality | D | Same as Phase 6 deferral |
| ThreadStatic accumulation pattern | Correctness | D | Follows existing pattern |
| Syntactic fallback false positive risk | Correctness | D | Documented design decision |

## Issues Created
- #215: Lambda With<TEntity, TDto> inner CTE selects all columns instead of projected subset
- #216: Set-op lambda context resolution gap for multi-context entity types
- #217: Add diagnostic tests for lambda CTE form (QRY080 equivalent)
