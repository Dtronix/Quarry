## Summary
- Closes #187
- Adds CTE (Common Table Expression) and derived table support via `With<TDto>()` / `FromCte<TDto>()` API
- Tracking issues for known follow-up work: #205 (CTE+Join chains), #206 (carrier conflict for multiple CTEs), #207 (discovery boilerplate refactor)

## Reason for Change
CTEs and derived tables are required for wrapping subquery results (e.g., window functions from #186 that need `WHERE rn = 1`). This adds "query-as-entity" support where a subquery's projection defines a virtual table, using a DTO class to describe the CTE column shape.

## Impact
New public API on `QuarryContext`:
- `With<TDto>(IQueryBuilder<TDto> innerQuery)` — defines a CTE from an inner query chain
- `With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)` — defines a CTE from an inner chain with Select projection
- `FromCte<TDto>()` — sets the CTE as the primary FROM source (derived table pattern)

The `FromCte` pattern (CTE as primary FROM source) works end-to-end across all 4 SQL dialects. The CTE+Join pattern (joining a CTE to a real table) is blocked by a semantic model limitation (#205) and will be addressed in a follow-up.

## Plan items implemented as specified
- **Phase 1**: IR foundation — `CteDef`, `CteColumn` types, `InterceptorKind.CteDefinition`/`FromCte`, `ClauseRole` extensions, `QueryPlan.CteDefinitions`
- **Phase 2**: Runtime API — `With<TDto>()`, `With<TEntity, TDto>()`, `FromCte<TDto>()` on generated context class with `new` keyword shadowing
- **Phase 3**: CTE DTO resolution — `CteDtoResolver` resolves `INamedTypeSymbol` to `CteColumn[]` from public properties
- **Phase 4**: Discovery — CTE chain recognition in `UsageSiteDiscovery`, inner chain tagging via `IsCteInnerChain`, `DetectCteInnerChain` with candidate symbol fallback for multi-dialect support
- **Phase 5**: Binding — CTE DTO column resolution during discovery, columns stored on `RawCallSite.CteColumns`
- **Phase 6**: Chain analysis — Two-pass analysis (inner chains first, outer chains second), CTE composition in `ChainAnalyzer`, parameter merging
- **Phase 7**: SQL assembly — `WITH "Name" AS (inner_sql)` prefix rendering in `SqlAssembler`, CTE parameter index offset handling
- **Phase 8**: Code generation — `EmitCteDefinition` and `EmitFromCte` interceptor bodies in `TransitionBodyEmitter`, inner chain carrier suppression, `builderTypeName` for two-type-arg overload

## Deviations from plan implemented
- **Phase 5**: Instead of registering CTE DTOs in `EntityRegistry` as pseudo-entities, CTE columns are resolved directly during discovery and stored on `RawCallSite.CteColumns`. This avoids polluting the entity registry with non-schema types.
- **Phase 6**: Inner chain matching uses `CteInnerArgSpanStart` (syntax span position) rather than terminal syntax node references, which proved more reliable across incremental compilation.
- **Phase 8**: `With()` creates the carrier (not `Users()`/`FromCte()` as originally considered). `FromCte()` is a no-op type transition via `Unsafe.As<IEntityAccessor<TDto>>`.
- **Phase 9**: Only 1 of 8 planned test cases implemented (FromCte with simple filter). CTE+Join tests are blocked by #205.

## Gaps in original plan implemented
- `DiscoverPostCteSites` — forward-scans the chain after `FromCte()` to discover clause/terminal methods that Roslyn cannot resolve (mirrors the existing post-navigation-join discovery pattern)
- `DiscoverPreparedTerminalsForCteChain` — handles `.Prepare()` terminals on CTE chains
- `TryResolveViaChainRootContext` — resolves context class/namespace from chain root for CTE chains where the root is a `With()` call (not a standard entity accessor)
- Multi-dialect `DetectCteInnerChain` candidate symbols fallback — handles cases where `GetSymbolInfo` returns candidates instead of a resolved symbol

## Migration Steps
None — purely additive. No schema changes, no breaking changes to existing APIs.

## Performance Considerations
- CTE SQL is assembled at compile time and embedded as a static string constant. No runtime overhead for CTE prefix rendering.
- Inner chain parameters are merged into the outer carrier's parameter list at compile time. Parameter extraction at runtime uses the same field-access pattern as regular parameters.

## Security Considerations
- CTE names are derived from DTO class names (compile-time constants) and quoted through `SqlFormatting.QuoteIdentifier`. No user-supplied strings flow into SQL identifiers.
- Inner CTE SQL is fully assembled by the generator pipeline — no injection vectors.

## Breaking Changes
- Consumer-facing: None. New methods on `QuarryContext` throw `NotSupportedException` without interception, consistent with all other Quarry methods.
- Internal: `InterceptorKind` and `ClauseRole` enums gain new values. `QueryPlan` and `RawCallSite` constructors gain new optional parameters (all default to null/false). No existing code affected.
