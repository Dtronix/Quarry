# Work Handoff: 205-cte-join-with-return-type

## Key Components
- `src/Quarry/Context/QuarryContext.cs` — `QuarryContext<TSelf>` generic subclass (committed)
- `src/Quarry.Generator/Models/ContextInfo.cs` — `HasGenericContextBase` flag (committed, currently unused since conditional emission was reverted)
- `src/Quarry.Generator/Parsing/ContextParser.cs` — generic base detection via `(Inherits, ViaGeneric)` tuple (committed)
- `src/Quarry.Generator/Generation/ContextCodeGenerator.cs` — `GenerateCteMethods` always emits `With<>` shadows (WIP revert of conditional)
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — CTE candidate disambiguation + DiscoverPostCteSites enhancements (WIP)
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — `AnalyzeSingleEntitySyntaxOnly` for simple lambda support (WIP)
- `src/Quarry.Generator/Parsing/DisplayClassEnricher.cs` — InterceptableLocationData deduplication (WIP)
- `src/Quarry.Generator/QuarryGenerator.cs` — Debug traces removed (WIP)
- `src/Quarry.Tests/Samples/CteChainTestDbContext.cs` — `CteDb` test fixture (committed)
- `src/Quarry.Tests/SqlOutput/CteWithEntityAccessorTests.cs` — 5 integration tests (WIP, 16 CS9144 errors)

## Completions (This Session)
- Removed debug traces from QuarryGenerator.cs
- Fixed CS0308: added `builderTypeName` tracking in DiscoverPostCteSites
- Fixed QRY032: added `AnalyzeSingleEntitySyntaxOnly` for single-entity syntax-only projection analysis
- Added dedup by InterceptableLocationData in DisplayClassEnricher.EnrichAll
- Removed test 6 (FromCte on generic base — entity type mismatch)
- Removed test 7 (projected inner query — QRY080 limitation)
- Fixed entity type tracking: primaryEntityTypeName flows through post-CTE and prepared terminal sites

## Previous Session Completions
- Phase 1: QuarryContext<TSelf> runtime type (committed)
- Phase 2: Generator detection + conditional emission (committed, conditional reverted)
- Phase 3: CteDb test fixture (committed)
- Phase 4 partial: CTE disambiguation, FromCte/With exclusion from DiscoverPostCteSites

## Progress
- Phases 1-3: committed, all 3012 tests green
- Phase 4: WIP — 16 CS9144 errors remaining (interceptor signature mismatches)
- Phases 5-6: not started

## Current State

### What works
- CTE discovery via disambiguation (With<> resolves to correct method)
- Entity type tracking through post-CTE chains (User instead of Order)
- Single-entity projection analysis (AnalyzeSingleEntitySyntaxOnly)
- Deduplication of duplicate discovery sites
- Non-joined chains compile (Select, Where signatures correct)
- All existing tests (CrossDialectCteTests etc.) still pass

### What doesn't work
16 CS9144 errors: interceptor signature mismatches. The generated interceptor method's `this` parameter type doesn't match the actual call site's receiver type.

Examples:
- `Prepare()` interceptor has `this IEntityAccessor<User>` but actual call is on `IQueryBuilder<User, (int, string)>` (result of Select)
- `Select()` interceptor has `this IJoinedQueryBuilder<User, Order>` but actual call is on `IEntityAccessor<User>` after Join resolution

### Root cause
`DiscoverPostCteSites` creates synthetic sites with `builderTypeName` that approximates the receiver type using a state machine. But the state machine doesn't account for:
1. **Result type argument**: `Select()` changes `IQueryBuilder<T>` to `IQueryBuilder<T, R>`. The `R` comes from projection analysis. The `builderTypeName` doesn't include the result type argument count.
2. **Post-transition types**: The dedup keeps the first-discovered site (from DiscoverPostCteSites), which has approximate types. Normal discovery would produce exact types but its sites are deduplicated away.
3. **Chain position**: Each method in the chain changes the builder type. The state machine needs to output the RECEIVER type (pre-transition), not the return type (post-transition).

## Known Issues / Bugs
- `HasGenericContextBase` flag on `ContextInfo` is now unused (conditional emission reverted). Could be removed, or kept for future use.
- The dedup in DisplayClassEnricher keeps the FIRST occurrence. This prefers synthetic sites over normal discovery sites, which may have more accurate type info.

## Dependencies / Blockers
None — all remaining work is within the generator and test code.

## Architecture Decisions
- **Always emit `With<>` shadow**: The generated shadow is the interceptor target; the generic base is the discovery enabler. Both are needed.
- **CTE-specific candidate disambiguation**: Scoped to `With`/`FromCte` method names only.
- **Dedup by InterceptableLocationData**: Necessary because the same call site can be discovered by both post-CTE/post-join discovery and normal discovery.

## Open Questions
- **Should DiscoverPostCteSites set builderTypeName at all?** Setting it to null would let the TranslatedCallSite.BuilderTypeName fallback chain handle it (Entity?.EntityName → EntityTypeName). But this was the original CS0308 problem.
- **Can the emitter infer the correct receiver type from chain context?** The emitter has the full chain — it could derive the receiver type for each site from the chain progression.
- **Would Option C (supplemental compilation enhancement) bypass the whole issue?** If DisplayClassEnricher's supplemental compilation included QuarryContext<TSelf> with typed With<>, Roslyn could resolve the chain natively during enrichment, and normal discovery would work.

## Next Work (Priority Order)
1. **Fix CS9144 interceptor signature mismatches** — try Option A first: set builderTypeName=null and let fallback handle it. If that causes CS0308 again, try Option B (emitter-level fix) or Option C (supplemental compilation).
2. After all build errors fixed, run tests, fix assertion mismatches, commit Phase 4.
3. Implement Phase 5 (docs) and Phase 6 (architectural findings).
