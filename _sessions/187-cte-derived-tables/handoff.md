# Work Handoff: 187-cte-derived-tables

## Key Components
- **CteDef / CteColumn** (`src/Quarry.Generator/IR/CteDef.cs`): CTE definition in the query plan — name, inner SQL, parameters, columns
- **QueryPlan.CteDefinitions** (`src/Quarry.Generator/IR/QueryPlan.cs`): List of CTEs attached to a query plan
- **CteDtoResolver** (`src/Quarry.Generator/IR/CteDtoResolver.cs`): Resolves INamedTypeSymbol → EntityInfo/CteColumn from public properties
- **Context CTE methods** (`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`): With<TDto>(), With<TEntity, TDto>(), FromCte<TDto>()
- **InterceptorKind.CteDefinition / FromCte** (`src/Quarry.Generator/Models/InterceptorKind.cs`)
- **ClauseRole.CteDefinition / FromCte** (`src/Quarry.Generator/Models/OptimizationTier.cs`)
- **RawCallSite CTE fields** (`src/Quarry.Generator/IR/RawCallSite.cs`): CteEntityTypeName, IsCteInnerChain, CteInnerArgSpanStart, CteColumns
- **CTE discovery** (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`): DiscoverCteSite, DetectCteInnerChain
- **Two-pass chain analysis** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Inner chains analyzed first, outer chains compose CteDefinitions
- **WITH clause rendering** (`src/Quarry.Generator/IR/SqlAssembler.cs`): WITH "name" AS (inner_sql) prefix
- **CTE interceptor dispatch** (`src/Quarry.Generator/CodeGen/FileEmitter.cs`): Routes CteDefinition/FromCte to TransitionBodyEmitter
- **CTE interceptor bodies** (`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`): EmitCteDefinition, EmitFromCte
- **Inner chain suppression** (`src/Quarry.Generator/IR/PipelineOrchestrator.cs`): Filters IsCteInnerChain from file groups

## Completions (This Session)
- Phase 4: CTE chain discovery — With/FromCte recognized, inner chains detected and tagged, ChainId differentiated
- Phase 5: CTE binding — CteColumns resolved during discovery, carried through pipeline
- Phase 6: Two-pass chain analysis — inner chains analyzed/assembled first, CteDefinitions built for outer QueryPlan
- Phase 7: WITH clause SQL rendering — prepended before SELECT with parameter offset
- Phase 8: Code generation — CteDefinition/FromCte interceptor dispatch, inner chain suppression

## Previous Session Completions
- Phase 1: IR foundation types (CteDef, CteColumn, QueryPlan extension, enum values)
- Phase 2: Runtime API (With/FromCte methods on generated context)
- Phase 3: CTE DTO resolver (CteDtoResolver.cs)

## Progress
- 8 of 9 phases complete
- 8 commits on branch: 51c94ba, 509d225, 116fb81, 220ea18, 9f91fc6, 9f6032a, 6f65f42, 7627c99, e3b0970
- All 2779 tests passing
- Working tree is clean

## Current State
Full pipeline infrastructure is in place: discovery → binding → chain analysis → SQL assembly → code generation. The remaining work is Phase 9 (end-to-end tests) which will likely surface issues in the carrier creation and parameter handling for CTE chains.

## Known Issues / Bugs
1. **Carrier creation conflict**: When a chain has both CteDefinition (`With<A>()`) and ChainRoot (`.Users()`), both try to create the carrier. Fix: CteDefinition should create carrier; ChainRoot following CteDefinition should be a noop transition.
2. **CTE inner parameter extraction**: Inner chain captured variables (e.g., `db.Orders().Where(o => o.Date > cutoffVar)`) don't flow into the outer carrier. The EmitCteDefinition body currently ignores the inner query argument.
3. **CTE join column resolution**: `Join<CteDto>()` can't resolve CTE DTO columns during binding/translation because CTE DTOs aren't in the EntityRegistry. Need retranslation in ChainAnalyzer using CteColumns from the CteDefinition site.

## Dependencies / Blockers
- None. The remaining work is incremental bug fixes driven by test failures.

## Architecture Decisions
1. **Two-pass chain analysis**: Inner CTE chains (tagged with IsCteInnerChain, differentiated ChainId with ":cte-inner:XXX" suffix) are analyzed and assembled to SQL before outer chains. Inner chain SQL is embedded in CteDef objects in the outer chain's QueryPlan.
2. **Inner chains use chain root as virtual execution site**: CTE inner chains have no execution terminal (no ExecuteFetchAllAsync). The chain root (e.g., `.Orders()`) serves as the virtual execution site to provide entity/table/dialect info.
3. **CteDefinition stays in clauseSites**: CteDefinition and FromCte are not removed from clauseSites — they remain for carrier/interceptor emission while also being processed for CteDefinitions during analysis.
4. **CTE name = DTO class short name**: Extracted via GetShortTypeName() from the fully qualified CteEntityTypeName.
5. **CTE columns resolved during discovery**: CteDtoResolver.ResolveColumns is called during DiscoverCteSite so column metadata flows through the incremental pipeline without type symbols.
6. **Parameter re-indexing**: CTE inner parameters are prepended to the outer chain's parameter list with re-indexed GlobalIndex values. The SQL assembler offsets paramIndex by the CTE parameter count.

## Open Questions
- Should CteDefinition create the carrier (chain root for CTE chains) or should it always be a noop, with a separate mechanism for carrier creation?
- How to handle the `With<TEntity, TDto>` overload in the interceptor signature (two type arguments)?
- How to handle CTE joins where the ON clause references CTE columns (need EntityInfo for CTE DTO in binding/translation)?

## Next Work (Priority Order)

### Phase 9: Cross-Dialect CTE Tests
1. Define OrderCountDto and similar DTO classes in test project
2. Start with simplest test: CTE with FromCte (no join, no captured vars)
3. Fix carrier creation conflict (CteDefinition vs ChainRoot)
4. Add CTE join test (requires CTE DTO entity resolution during retranslation)
5. Add captured variable test (requires inner parameter extraction)
6. Add multiple CTE test
7. Verify all 4 dialects
