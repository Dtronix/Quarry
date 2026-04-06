# Work Handoff: 187-cte-derived-tables

## Key Components
- **CteDef / CteColumn** (`src/Quarry.Generator/IR/CteDef.cs`): CTE definition in the query plan — name, inner SQL, parameters, columns
- **QueryPlan.CteDefinitions** (`src/Quarry.Generator/IR/QueryPlan.cs`): List of CTEs attached to a query plan
- **CteDtoResolver** (`src/Quarry.Generator/IR/CteDtoResolver.cs`): Resolves INamedTypeSymbol → EntityInfo/CteColumn from public properties
- **QuarryContext CTE methods** (`src/Quarry/Context/QuarryContext.cs`): With<TDto>(), With<TEntity, TDto>(), FromCte<TDto>() on base class
- **Context CTE methods** (`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`): `new` overrides on generated context returning concrete type
- **InterceptorKind.CteDefinition / FromCte** (`src/Quarry.Generator/Models/InterceptorKind.cs`)
- **ClauseRole.CteDefinition / FromCte** (`src/Quarry.Generator/Models/OptimizationTier.cs`)
- **RawCallSite CTE fields** (`src/Quarry.Generator/IR/RawCallSite.cs`): CteEntityTypeName, IsCteInnerChain, CteInnerArgSpanStart, CteColumns
- **CTE discovery** (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`): DiscoverCteSite, DetectCteInnerChain (with candidate fallback), TryResolveViaChainRootContext, DiscoverPostCteSites, DiscoverPreparedTerminalsForCteChain
- **Two-pass chain analysis** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Inner chains analyzed first, outer chains compose CteDefinitions
- **WITH clause rendering** (`src/Quarry.Generator/IR/SqlAssembler.cs`): WITH "name" AS (inner_sql) prefix
- **CTE interceptor dispatch** (`src/Quarry.Generator/CodeGen/FileEmitter.cs`): Routes CteDefinition/FromCte to TransitionBodyEmitter
- **CTE interceptor bodies** (`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`): EmitCteDefinition (Unsafe.As carrier→context), EmitFromCte
- **Inner chain carrier generation** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Inner chains added to results for carrier/interceptor generation

## Completions (This Session)
- Fixed DetectCteInnerChain candidate symbols fallback for multi-dialect support
- Expanded CrossDialectCteTests to verify 4-dialect FROM CTE SQL
- Added TryResolveViaChainRootContext for post-CTE context method resolution
- Added DiscoverPostCteSites for syntactic forward-walk from With()
- Added DiscoverPreparedTerminalsForCteChain for prepared terminal discovery

## Previous Session Completions
- Phase 1: IR foundation types (CteDef, CteColumn, QueryPlan extension, enum values)
- Phase 2: Runtime API (With/FromCte methods on generated context)
- Phase 3: CTE DTO resolver (CteDtoResolver.cs)
- Phase 4: CTE chain discovery — With/FromCte recognized, inner chains detected and tagged, ChainId differentiated
- Phase 5: CTE binding — CteColumns resolved during discovery, carried through pipeline
- Phase 6: Two-pass chain analysis — inner chains analyzed/assembled first, CteDefinitions built for outer QueryPlan
- Phase 7: WITH clause SQL rendering — prepended before SELECT with parameter offset
- Phase 8: Code generation — CteDefinition/FromCte interceptor dispatch, inner chain suppression
- Session 3: Pipeline fixes (6 fixes), SQLite CTE test passing

## Progress
- 8 of 9 phases complete + Phase 9 partially complete
- 12 commits on branch
- All 2780 tests passing (1 CTE test with 4-dialect verification + 2779 existing)

## Current State
CTE FromCte pattern works end-to-end for all 4 dialects. The discovery → binding → chain analysis → SQL assembly → code generation → runtime execution pipeline is proven. The WITH clause is correctly generated and queries execute against SQLite.

CTE+Join pattern (With→Users→Join→Select) is blocked by a fundamental semantic model limitation during source generation.

## Known Issues / Bugs
1. **CTE+Join chain cascade** (BLOCKING for Join/captured var/multiple CTE tests):
   - `QuarryContext.With<TDto>()` returns `QuarryContext` during source generation
   - `.Users()` can't be resolved on `QuarryContext` → cascading error type for entire chain
   - `DiscoverPostCteSites` partially addresses but generates incorrect builder types
   - **Root cause**: Generated context class (with `new T With<TDto>()` returning concrete type) isn't available to the generator's semantic model
   - **Potential fixes**:
     a. Self-referencing generic: `QuarryContext<TSelf>` where `With()` returns `TSelf` — clean but changes public API
     b. Full syntactic chain discovery: expand `DiscoverPostCteSites` with entity type tracking through chain — complex but API-stable
     c. `RegisterPostInitializationOutput` for CTE methods — limited by needing context class names at compile time

2. **Carrier creation conflict** (not yet triggered): When a CTE chain has both CteDefinition and ChainRoot (e.g., `db.With<A>(inner).Users()`), both sites try to create carriers.

3. **CTE inner parameter extraction** (not yet triggered): Inner chain captured variables don't flow into outer carrier.

4. **CTE join column resolution** (not yet triggered): `Join<CteDto>()` can't resolve CTE DTO columns during binding/translation.

## Dependencies / Blockers
- Issue #1 blocks all remaining Phase 9 test cases (Join, captured var, multiple CTE)
- Issues #2-4 are secondary blockers once #1 is resolved

## Architecture Decisions
1. **CTE methods on base class**: With/FromCte on QuarryContext base class for discovery visibility. Generated context shadows with `new` for concrete return types.
2. **Inner chains get carriers**: Inner CTE chains flow through full carrier pipeline.
3. **Unsafe.As for CTE carrier returns**: EmitCteDefinition uses `Unsafe.As<ContextClass>()` to type-convert carrier.
4. **Candidate symbols fallback**: DetectCteInnerChain falls back to CandidateSymbols when primary symbol resolution fails (fixes non-SQLite dialects).
5. **Post-CTE syntactic discovery**: DiscoverPostCteSites walks the chain forward from With() to discover methods that can't resolve via the semantic model.

## Open Questions
- Which approach for fixing the CTE+Join cascade: self-referencing generic (a), syntactic discovery (b), or post-init output (c)?
- Should CteDefinition create the carrier (current) or should ChainRoot?

## Next Work (Priority Order)
1. **Fix CTE+Join chain cascade** — Choose and implement one of the three approaches
2. **Fix carrier creation conflict** — ChainRoot after CteDefinition must not create new carrier
3. **Add CTE+Join test** — `With().Users().Join().Select()`
4. **Add captured variable test** — Inner query with captured var parameter
5. **Add multiple CTE test** — `With().With().Users().Join().Join()`
6. **Verify all 4 dialects produce correct CTE SQL** for Join scenarios
