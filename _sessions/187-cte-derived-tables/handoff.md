# Work Handoff: 187-cte-derived-tables

## Key Components
- **CteDef / CteColumn** (`src/Quarry.Generator/IR/CteDef.cs`): CTE definition in the query plan — name, inner SQL, parameters, columns
- **QueryPlan.CteDefinitions** (`src/Quarry.Generator/IR/QueryPlan.cs`): List of CTEs attached to a query plan
- **CteDtoResolver** (`src/Quarry.Generator/IR/CteDtoResolver.cs`): Resolves INamedTypeSymbol → EntityInfo/CteColumn from public properties
- **Context CTE methods** (`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`): With<TDto>(), With<TEntity, TDto>(), FromCte<TDto>()
- **InterceptorKind.CteDefinition / FromCte** (`src/Quarry.Generator/Models/InterceptorKind.cs`)
- **ClauseRole.CteDefinition / FromCte** (`src/Quarry.Generator/Models/OptimizationTier.cs`)

## Completions (This Session)
- Phase 1: IR foundation types (CteDef, CteColumn, QueryPlan extension, enum values)
- Phase 2: Runtime API (With/FromCte methods on generated context, non-partial with throw bodies)
- Phase 3: CTE DTO resolver (CteDtoResolver.cs — builds pseudo-EntityInfo from DTO properties)

## Previous Session Completions
(none — first session)

## Progress
- 3 of 9 phases complete
- 3 commits on branch: 51c94ba, 509d225, 116fb81
- All 2779 tests passing
- Working tree is clean

## Current State
Foundation types and API surface are in place. The remaining work is the core source generator pipeline — teaching the 6-stage generator to discover, bind, analyze, assemble, and emit CTE chains.

## Known Issues / Bugs
- None. All existing tests pass.

## Dependencies / Blockers
- None. The remaining phases only modify generator-internal code.

## Architecture Decisions
1. **With<TDto>() returns the context type** — avoids needing per-context ICteScope interfaces. The compiler sees `db.With<A>(inner).Users()` as valid because With returns the context, and Users is a context method. Non-partial methods (not `partial`) — no defining declaration needed.

2. **With() is the CTE chain root** — creates the carrier. Users()/FromCte() are clause methods that set the primary table (no-ops at runtime since the table is compile-time known).

3. **Nested chain analysis** — the inner chain (argument to With) is discovered and analyzed as a separate chain, SQL captured, then composed into the outer chain. Inner chain's carrier is suppressed.

4. **Two-pass chain analysis** — inner chains must be analyzed/assembled before outer chains so the inner SQL is available. ChainAnalyzer.Analyze() needs ordering.

5. **Parameter merging** — CTE inner params are prepended to the outer chain's parameter list (CTE SQL renders first).

6. **CTE name = DTO class name** — no user-specified string. SQL: `WITH "OrderCountDto" AS (...)`.

## Open Questions
- How exactly to identify inner chain sites during discovery? The inner chain is the argument to With<TDto>(invocation). Need to trace the invocation argument syntax tree to find all chain sites that belong to it, and tag them with `IsCteInnerChain = true`.
- How to handle the return type of With<TDto>() in the interceptor — it returns the context but the carrier-based interceptor needs to create and return the carrier (cast as context type via Unsafe.As).

## Next Work (Priority Order)

### Phase 4: Discovery (UsageSiteDiscovery.cs)
- Add "With" and "FromCte" to InterceptableMethods dictionary
- Handle With() as a context method (not a builder method) — it's on the context class, not on IQueryBuilder
- Extract TDto type argument from With<TDto>() and FromCte<TDto>()
- Identify the inner chain argument (the InvocationExpressionSyntax passed to With)
- Tag inner chain sites with IsCteInnerChain = true
- Add CteEntityTypeName to RawCallSite for DTO type tracking

### Phase 5: Binding (CallSiteBinder.cs)
- When binding a CteDefinition site, use CteDtoResolver to build pseudo-EntityInfo
- Register CTE DTOs so Join<CteDto>() binding resolves correctly
- Handle FromCte — set entity to the CTE DTO pseudo-entity

### Phase 6: Chain Analysis (ChainAnalyzer.cs)
- Two-pass analysis: separate inner chains (IsCteInnerChain) from outer chains
- Analyze inner chains first, assemble SQL
- When processing outer chains, find CteDefinition sites, match to analyzed inner chains
- Build CteDef objects from inner chain assembly results
- Wire into QueryPlan.CteDefinitions
- Merge inner chain parameters into outer chain's global parameter list
- Suppress inner chains from carrier emission

### Phase 7: SQL Assembly (SqlAssembler.cs)
- In RenderSelectSql: if plan.CteDefinitions is non-empty, prepend `WITH "Name" AS (innerSql)[, ...]`
- Handle CTE name as table reference (no schema prefix)
- Adjust paramIndex to account for CTE parameters

### Phase 8: Code Generation (CarrierEmitter.cs, FileEmitter.cs)
- Route CteDefinition interceptor: create carrier, extract inner params, return as context type
- Route FromCte interceptor: no-op type transition
- Suppress inner chain carrier emission in FileEmitter
- Include CTE SQL in carrier's static SQL field (already handled by SqlAssembler embedding it in the rendered SQL)

### Phase 9: Tests (CrossDialectCteTests.cs)
- CTE join, FromCte, multiple CTEs, parameters, aggregates — all 4 dialects
