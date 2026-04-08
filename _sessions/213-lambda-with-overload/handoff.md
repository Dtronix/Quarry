# Work Handoff: 213-lambda-with-overload

## Key Components
- `src/Quarry/Context/QuarryContext.cs` — Runtime With<T> lambda overloads (base + QuarryContext<TSelf>)
- `src/Quarry/Query/IQueryBuilder.cs` — Lambda set-op overloads on IQueryBuilder<T> and IQueryBuilder<TEntity, TResult>
- `src/Quarry.Generator/Generation/ContextCodeGenerator.cs` — Generated With<T> lambda shadow methods
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — DetectInnerChain (extended), lambda-form CTE/set-op detection, ComputeChainId lambda parameter scope
- `src/Quarry.Generator/IR/RawCallSite.cs` — LambdaInnerSpanStart property
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — Lambda inner chain group separation, recursive AnalyzeChainGroup for lambda CTE definitions

## Completions (This Session)
- **Phase 1**: Added lambda API overloads on QuarryContext, QuarryContext<TSelf>, IQueryBuilder<T>, IQueryBuilder<TEntity,TResult>, ContextCodeGenerator. All additive, existing tests green.
- **Phase 2**: Extended DetectCteInnerChain → DetectInnerChain to detect lambda-form inner chains. Added :lambda-inner: ChainId suffix. ComputeChainId uses lambda SpanStart as scope key for IParameterSymbol roots. Added LambdaInnerSpanStart to RawCallSite. Set EnrichmentLambda on CTE/set-op sites with lambda arguments.
- **Phase 3**: ChainAnalyzer.AnalyzeChains now separates :lambda-inner: chain groups. Builds lambdaSpanStart→ChainId lookup. CTE definition processing has a lambda-form branch that recursively calls AnalyzeChainGroup, assembles SQL, creates CteDef with parameter offset mapping.

## Previous Session Completions
(none — first session)

## Progress
Phases 1-3 of 7 complete. Phases 4-7 remain.

## Current State
The discovery and analysis pipeline is complete for CTE lambda form. The emission pipeline (Phase 4) has NOT been touched. No lambda-form tests exist yet (Phase 5). Set-op lambda analysis is partially done in ChainAnalyzer for CTE but the same pattern needs extension for set-op sites.

## Known Issues / Bugs
- ChainAnalyzer Phase 3 only added the lambda path for CTE definitions. Set-op sites with LambdaInnerSpanStart need the same recursive analysis treatment (part of Phase 4 or Phase 6).
- Synthesized ChainRoot for lambda inner chains is NOT yet implemented. Lambda inner chains that have no ChainRoot site (because the lambda parameter is not a method call) will fail in AnalyzeChainGroup. This needs to be handled before Phase 5 tests can work. The plan says to inject a synthetic ChainRoot when a lambda inner chain group has no ChainRoot site.

## Dependencies / Blockers
None.

## Architecture Decisions
- **Direct capture model**: No inner carriers, no inner interceptors, no lambda invocation. The With()/Union() interceptor extracts captured variables via innerBuilder.Target (display class) using UnsafeAccessor. DisplayClassEnricher already processes the EnrichmentLambda set on the site.
- **Tree-based analysis**: Lambda inner chains are analyzed on-demand via recursive AnalyzeChainGroup calls. The existing :cte-inner: flat two-pass approach remains for the old direct-argument form.
- **ChainId scoping**: Lambda parameter roots use lambda.SpanStart as scope key to prevent collisions.

## Open Questions
- How will the CarrierAnalyzer build extraction plans for inner chain parameters accessed via the outer lambda's display class? Need to trace through the enrichment data from DisplayClassEnricher to map inner chain CapturedFieldNames to the outer lambda's display class fields.
- TransitionBodyEmitter.EmitCteDefinition currently uses Unsafe.As to cast innerQuery to inner carrier and copies P-fields. For lambda form, it needs to emit UnsafeAccessor extraction from innerBuilder.Target. The exact integration with CarrierEmitter.EmitExtractionLocalsAndBindParams needs investigation.

## Next Work (Priority Order)
1. **Phase 4: Carrier + Emission** — Implement direct capture in TransitionBodyEmitter and SetOperationBodyEmitter. Modify CarrierAnalyzer to include inner chain params as extractors on the outer carrier. Modify InterceptorCodeGenerator to skip inner carrier/interceptor generation for lambda inner chains. Add synthesized ChainRoot injection in AnalyzeChainGroup for lambda inner chains.
2. **Phase 5: CTE lambda tests** — First end-to-end validation. Test single CTE, multi-CTE, captured params, diagnostics.
3. **Phase 6: Set-op lambda tests** — Extend set-op analysis in ChainAnalyzer, test same-entity and cross-entity lambda set ops.
4. **Phase 7: Remove old forms, migrate tests** — Remove old overloads, old detection machinery, migrate all tests to lambda form.
