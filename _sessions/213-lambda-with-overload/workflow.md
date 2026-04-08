# Workflow: 213-lambda-with-overload
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #213
pr:
session: 2
phases-total: 7
phases-complete: 4
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
- 2026-04-08: Direct capture with DisplayClassEnricher reuse (Option C). No inner carriers, no inner interceptors, no lambda invocation at runtime. The With()/Union() interceptor accesses captured variables via the outer lambda delegate's display class (innerBuilder.Target). Inner chain is purely compile-time: SQL extracted, params mapped to outer carrier at ParameterOffset.
## Suspend State
- Current phase: IMPLEMENT phase 4/7 (Carrier + Emission direct capture), not yet started
- What is in progress: Phases 1-3 complete and committed. Phase 4 is next.
- WIP commit hash: none (clean state)
- Test status: all 3021 tests passing
- Unrecorded context: Phase 4 requires changes to CarrierAnalyzer (build extraction plans for inner chain params), CarrierEmitter (UnsafeAccessor methods), TransitionBodyEmitter (lambda-form CTE emission using innerBuilder.Target), SetOperationBodyEmitter (lambda-form set-op emission), and InterceptorCodeGenerator (skip inner carrier/interceptor generation for lambda inner chains). The direct capture model means: no inner carriers, no inner interceptors, no lambda invocation — the With()/Union() interceptor extracts captured variables directly from the lambda delegate's display class via UnsafeAccessor.
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT (phase 3/7) | Completed phases 1-3: API overloads, discovery pipeline, ChainAnalyzer tree analysis. Suspended before phase 4 (emission). |
| 2 | IMPLEMENT (phase 4/7) | | Resumed. Baseline: 3021 tests passing. |
