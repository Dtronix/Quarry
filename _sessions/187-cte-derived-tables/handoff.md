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
- **CTE discovery** (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`): DiscoverCteSite, DetectCteInnerChain
- **Two-pass chain analysis** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Inner chains analyzed first, outer chains compose CteDefinitions
- **WITH clause rendering** (`src/Quarry.Generator/IR/SqlAssembler.cs`): WITH "name" AS (inner_sql) prefix
- **CTE interceptor dispatch** (`src/Quarry.Generator/CodeGen/FileEmitter.cs`): Routes CteDefinition/FromCte to TransitionBodyEmitter
- **CTE interceptor bodies** (`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`): EmitCteDefinition (Unsafe.As carrier→context), EmitFromCte
- **Inner chain carrier generation** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Inner chains added to results for carrier/interceptor generation

## Completions (This Session)
- Added With/FromCte to QuarryContext base class (discovery visibility fix)
- Fixed EmitCteDefinition return type with Unsafe.As
- Fixed DiscoverCteSite to use concrete receiver context type
- Added inner CTE chains to ChainAnalyzer results
- Removed inner chain suppression from PipelineOrchestrator
- Created CrossDialectCteTests.cs with passing SQLite FromCte test

## Previous Session Completions
- Phase 1: IR foundation types (CteDef, CteColumn, QueryPlan extension, enum values)
- Phase 2: Runtime API (With/FromCte methods on generated context)
- Phase 3: CTE DTO resolver (CteDtoResolver.cs)
- Phase 4: CTE chain discovery — With/FromCte recognized, inner chains detected and tagged, ChainId differentiated
- Phase 5: CTE binding — CteColumns resolved during discovery, carried through pipeline
- Phase 6: Two-pass chain analysis — inner chains analyzed/assembled first, CteDefinitions built for outer QueryPlan
- Phase 7: WITH clause SQL rendering — prepended before SELECT with parameter offset
- Phase 8: Code generation — CteDefinition/FromCte interceptor dispatch, inner chain suppression

## Progress
- 8 of 9 phases complete + Phase 9 partially complete
- 10 commits on branch (8 phases + session artifacts + WIP)
- All 2780 tests passing (1 new CTE test + 2779 existing)
- Working tree has uncommitted session artifact changes only

## Current State
Full pipeline infrastructure is in place and the simplest CTE test (FromCte with SQLite) passes end-to-end: discovery → binding → chain analysis → SQL assembly → code generation → runtime execution. The CTE WITH clause is correctly generated and executes against SQLite.

## Known Issues / Bugs
1. **Multi-dialect inner chain detection** (BLOCKING): `DetectCteInnerChain` returns false for Pg/My/Ss inner chain sites. For TestDbContext (SQLite), detection works correctly — inner chain gets `:cte-inner:` suffix on ChainId. For PgDb/MyDb/SsDb, the inner chain sites are NOT tagged, causing them to merge with the outer chain group. This breaks CTE composition for non-SQLite dialects.
   - **Symptoms**: Chain group for pg/my/ss has 6-7 sites including Where+ChainRoot mixed with CteDefinition+FromCte+Select+Prepare
   - **Likely cause**: The semantic model resolves `Pg.With<Pg.Order>(...)` differently for PgDb than for TestDbContext. Possibly the `GetSymbolInfo` for the parent invocation returns different results, or `IsQuarryContextType` fails for the derived context types through the base class resolution.
   - **Investigation approach**: Add targeted logging in DetectCteInnerChain to compare `parentSymbol.ContainingType` and `IsQuarryContextType` results for each dialect's inner chain invocations.

2. **Carrier creation conflict** (not yet triggered): When a CTE chain has both CteDefinition and ChainRoot (e.g., `db.With<A>(inner).Users()`), both create carriers. The CteDefinition interceptor now creates the carrier via Unsafe.As. ChainRoot also creates a carrier. Fix needed: ChainRoot should be noop when following CteDefinition.

3. **CTE inner parameter extraction** (not yet triggered): Inner chain captured variables don't flow into outer carrier. The EmitCteDefinition body ignores the inner query argument at runtime.

4. **CTE join column resolution** (not yet triggered): `Join<CteDto>()` can't resolve CTE DTO columns during binding/translation.

## Dependencies / Blockers
- Issue #1 (multi-dialect detection) blocks cross-dialect test expansion
- Issues #2-4 block more complex CTE test cases (joins, captured vars)

## Architecture Decisions
1. **CTE methods on base class**: With/FromCte moved from generated-only to QuarryContext base class. This is required because the incremental generator's discovery phase runs before generated code exists — the semantic model needs the base class methods to resolve call sites. Generated context classes shadow with `new` for concrete return types.
2. **Inner chains get carriers**: Inner CTE chains are added to ChainAnalyzer results and flow through the full carrier pipeline. At runtime, the inner chain methods create a carrier that's passed to With() and ignored. This avoids the original design's assumption that inner chain interceptors could be suppressed (they can't — the inner chain expression is evaluated as a C# argument before With() is called).
3. **Unsafe.As for CTE carrier returns**: EmitCteDefinition uses `Unsafe.As<ContextClass>(new Carrier { Ctx = @this })` to return the carrier typed as the context class, since carriers don't inherit from context classes.

## Open Questions
- Why does DetectCteInnerChain fail for PgDb/MyDb/SsDb but work for TestDbContext?
- Should CteDefinition create the carrier (current approach) or should ChainRoot?

## Next Work (Priority Order)
1. **Fix multi-dialect inner chain detection** — Debug DetectCteInnerChain for PgDb/MyDb/SsDb
2. **Expand tests to 4 dialects** — Restore AssertDialects pattern in CrossDialectCteTests
3. **Add CTE+Join test** — Requires fixing carrier conflict (#2) and CTE DTO entity resolution (#4)
4. **Add captured variable test** — Requires inner parameter extraction (#3)
5. **Add multiple CTE test**
