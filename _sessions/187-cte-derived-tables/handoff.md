# Work Handoff: 187-cte-derived-tables

## Key Components
- **CteDef / CteColumn** (`src/Quarry.Generator/IR/CteDef.cs`): CTE definition in the query plan
- **QueryPlan.CteDefinitions** (`src/Quarry.Generator/IR/QueryPlan.cs`): List of CTEs attached to a query plan
- **CteDtoResolver** (`src/Quarry.Generator/IR/CteDtoResolver.cs`): Resolves INamedTypeSymbol → EntityInfo/CteColumn. `Resolve()` is dead code (TODO for CTE+Join).
- **QuarryContext CTE methods** (`src/Quarry/Context/QuarryContext.cs`): With<TDto>(), With<TEntity, TDto>(), FromCte<TDto>()
- **Context CTE methods** (`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`): `new` overrides returning concrete type
- **InterceptorKind.CteDefinition / FromCte** (`src/Quarry.Generator/Models/InterceptorKind.cs`)
- **CTE discovery** (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`): DetectCteInnerChain (candidate fallback), DiscoverCteSite, TryResolveViaChainRootContext, DiscoverPostCteSites, DiscoverPreparedTerminalsForCteChain
- **Two-pass chain analysis** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`)
- **WITH clause rendering** (`src/Quarry.Generator/IR/SqlAssembler.cs`)
- **CTE interceptor bodies** (`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`): EmitCteDefinition (with builderTypeName for 2-arg overload), EmitFromCte

## Completions (This Session)
- Fixed DetectCteInnerChain candidate symbols fallback for multi-dialect support
- Expanded CrossDialectCteTests to 4-dialect SQL verification
- Added TryResolveViaChainRootContext, DiscoverPostCteSites, DiscoverPreparedTerminalsForCteChain
- Fixed With<TEntity,TDto> interceptor signature (stores param type via builderTypeName)
- Marked CteDtoResolver.Resolve() with TODO
- Created tracking issues: #205 (CTE+Join blocker), #206 (carrier conflict), #207 (boilerplate)
- Completed review analysis and classification

## Previous Session Completions
- Phases 1-8 complete (IR, API, discovery, binding, chain analysis, SQL assembly, code gen)
- Session 3: Pipeline fixes, SQLite CTE test passing

## Progress
- 8/9 phases complete + Phase 9 partial (1 test, 4 dialects)
- Review complete, (A)/(B) fixes committed, (C) issues created
- 16 commits on branch (pre-rebase)
- All 2780 tests passing

## Current State
CTE FromCte pattern works end-to-end for all 4 dialects. Review completed and fixes applied. Branch needs rebase on master before PR creation.

## Known Issues / Bugs
1. **CTE+Join chain cascade** (#205): With() returns QuarryContext during source generation, blocking Users() resolution
2. **Carrier conflict for multiple CTEs** (#206): Each With() creates new carrier, discarding previous
3. **Discovery boilerplate duplication** (#207): DiscoverPostCteSites/DiscoverPreparedTerminalsForCteChain duplicate patterns

## Next Work (Priority Order)
1. **Rebase on origin/master** — conflicts in InterceptorKind.cs, QueryPlan.cs, RawCallSite.cs, UsageSiteDiscovery.cs (all additive — keep both sides). Run tests after rebase.
2. **Push and create PR** — use PR Body Template with all session artifacts
3. **Wait for CI** → finalize and merge

## Rebase Conflict Resolution Guide
All conflicts are additive (master added set operations, branch added CTE). For each file:
- `InterceptorKind.cs`: Master added Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll before Unknown. Branch added CteDefinition/FromCte. Keep both — place CTE values after set operation values.
- `QueryPlan.cs`: Master added setOperations/postUnion* constructor params. Branch added cteDefinitions param. Keep both — add cteDefinitions after the master params.
- `RawCallSite.cs`: Similar additive pattern — keep both sets of new fields.
- `UsageSiteDiscovery.cs`: Large file — conflicts likely in InterceptableMethods dictionary and method dispatch. Keep both set operation entries and CTE entries.
- `OptimizationTier.cs`: Master added SetOperation to ClauseRole. Branch added CteDefinition/FromCte. Keep both.
