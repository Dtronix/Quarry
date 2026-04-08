# Workflow: 213-lambda-with-overload
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #213
pr: #218
session: 3
phases-total: 7
phases-complete: 7
## Problem Statement
Add lambda-form `With<TDto>()` and `With<TEntity, TDto>()` overloads for more ergonomic multi-CTE chains. The lambda receives an injected entity accessor, eliminating context re-references and preventing cross-context mixing at compile time.

Baseline: all 3021 tests pass (97 migration + 103 analyzers + 2821 quarry). No pre-existing failures.
## Decisions
- 2026-04-08: Combined scope — recursive discovery refactor + lambda With<T> overload in one issue. Refactor is motivated by and validated through the lambda overload.
- 2026-04-08: Known-method-driven recursion — recursive descent triggers on known methods (With<T>, set ops). Where predicates etc. stay on SqlExprParser path.
- 2026-04-08: Tree structure in IR — inner chains become children of the call site that owns them. Replaces SpanStart-based dictionary matching. ChainAnalyzer two-pass becomes recursive descent.
- 2026-04-08: Replace non-lambda With<T> forms entirely (breaking). Lambda-only API: With<TDto>(Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>>). Eliminates cross-context mixing at compile time.
- 2026-04-08: Unify set operations (Union/Intersect/Except) into same tree mechanism with lambda API: .Union(orders => orders.Where(...)). IEntityAccessor<T> as lambda parameter type.
- 2026-04-08: Synthesize a virtual ChainRoot site for lambda parameters to serve as inner chain root.
- 2026-04-08: Set-op lambda detection has a context resolution gap — inner chain sites get wrong context when the entity type is registered in multiple contexts. CTE lambdas work because With() is resolved on the context class. Set-op lambdas (Union etc.) on IQueryBuilder have ambiguous resolution during source generation. Defer to follow-up.
- 2026-04-08: Direct capture with DisplayClassEnricher reuse (Option C). No inner carriers, no inner interceptors, no lambda invocation at runtime. The With()/Union() interceptor accesses captured variables via the outer lambda delegate's display class (innerBuilder.Target). Inner chain is purely compile-time: SQL extracted, params mapped to outer carrier at ParameterOffset.
## Suspend State
- Current phase: REVIEW, not yet started
- What is in progress: All 7 IMPLEMENT phases complete. Entering REVIEW.
- WIP commit hash: none (clean state)
- Test status: all 3027 tests passing (97 migration + 103 analyzers + 2827 quarry, including 6 new lambda CTE tests)
- Unrecorded context: Set-op lambda end-to-end tests are deferred due to a context resolution gap — inner chain sites discovered inside set-op lambdas get the wrong context class when entity types are registered in multiple contexts. The emission code (Phase 4) is correct; the issue is in the discovery pipeline's context resolution for lambda parameter-rooted chains. CTE lambdas don't hit this because With() is resolved on the context class (not on IQueryBuilder). Also: lambda-form With<TEntity,TDto> inner CTE SQL selects all entity columns instead of just the projected ones (Select projection not reduced in lambda inner chain).
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT (phase 3/7) | Completed phases 1-3: API overloads, discovery pipeline, ChainAnalyzer tree analysis. Suspended before phase 4 (emission). |
| 2 | IMPLEMENT (phase 4/7) | IMPLEMENT (phase 7/7) | Completed all 7 phases. CTE lambda fully working (6 new tests). Set-op lambda analysis+emission complete but end-to-end tests deferred (context resolution gap). Old CTE API removed, tests migrated. Old set-op API retained. |
| 3 | REVIEW | | Resumed from suspend. 2827 tests passing. Starting review analysis. |
