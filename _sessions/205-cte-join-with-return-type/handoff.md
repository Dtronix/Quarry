# Work Handoff: 205-cte-join-with-return-type

## Key Components
- `src/Quarry/Context/QuarryContext.cs` — `QuarryContext<TSelf>` generic subclass (committed)
- `src/Quarry.Generator/Models/ContextInfo.cs` — `HasGenericContextBase` flag (committed, but currently unused since conditional emission was reverted)
- `src/Quarry.Generator/Parsing/ContextParser.cs` — generic base detection via `(Inherits, ViaGeneric)` tuple (committed)
- `src/Quarry.Generator/Generation/ContextCodeGenerator.cs` — `GenerateCteMethods` now always emits `With<>` shadows (WIP revert of conditional)
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — CTE candidate disambiguation + FromCte exclusion from DiscoverPostCteSites (WIP)
- `src/Quarry.Generator/QuarryGenerator.cs` — TEMP debug traces (must be removed)
- `src/Quarry.Tests/Samples/CteChainTestDbContext.cs` — `CteDb` test fixture (committed)
- `src/Quarry.Tests/SqlOutput/CteWithEntityAccessorTests.cs` — 7 integration tests (WIP, doesn't compile yet)

## Completions (This Session)
- Phase 1: `QuarryContext<TSelf>` runtime type (committed)
- Phase 2: Generator detection + conditional emission (committed, but conditional emission was reverted in WIP to always-emit)
- Phase 3: `CteDb` test fixture in `Quarry.Tests.Samples.Cte` namespace (committed)
- Phase 4 partial: Root cause identified and fixed — CTE `With<>` discovery now works for generic-base contexts

## Previous Session Completions
- INTAKE, DESIGN, PLAN phases complete
- Baseline: 3012 tests, 0 pre-existing failures

## Progress
- Phases 1-3: committed, all 3012 tests green
- Phase 4: WIP — discovery fix works, but generated interceptor code has namespace qualification issues and test format issues
- Phases 5-6: not started

## Current State

### Discovery fix (WORKING)
The root cause was in `UsageSiteDiscovery.DiscoverRawCallSite` Step 1: when `With<Cte.Order>(inner)` is called on a `QuarryContext<TSelf>` context and `Cte.Order` is a generated (error) type, Roslyn reports BOTH:
- `QuarryContext<TSelf>.With<TDto>` (the `new` shadow from the generic base)  
- `QuarryContext.With<TDto>` (the hidden base method)

as overload-resolution-failure candidates. Both have identical `TypeParameters.Length == 1` (Roslyn returns constructed/reduced candidates). The existing disambiguation code (argCount, lambda params, delegate params) couldn't narrow from 2 to 1, so the method was unresolved and the CTE site was never created.

**Fix**: CTE-specific disambiguation at line ~198-224 that prefers the candidate from the most-derived containing type when the method name is "With" or "FromCte".

### Interceptor emission revert (WORKING)
Phase 2's conditional `if (!context.HasGenericContextBase)` skip was wrong. The generated `With<>` shadow is ALWAYS needed as the interceptor target. The `QuarryContext<TSelf>` generic base makes the typed return visible to DISCOVERY (SemanticModel), while the generated shadow provides the INTERCEPTABLE call target. The `new` keyword is valid in both cases (hides inherited method from either `QuarryContext` or `QuarryContext<TSelf>`).

### Remaining build errors
1. **CS0308** in generated interceptor: `Order` used without namespace qualification, resolving to wrong type. The `Cte` sub-namespace entity types shadow the parent namespace entity types.
2. **QRY032**: Chain analysis classifies CTE+Users+Select as "joined" (expects 2-param lambda). Need to investigate the chain analysis classification.
3. **QRY080**: `With<TEntity, TDto>` projected inner query test — generator can't analyze inner query stored in variable.

## Known Issues / Bugs
- `HasGenericContextBase` flag on `ContextInfo` is now unused (conditional emission reverted). Could be removed, or kept for future use (e.g., diagnostics nudging migration). Decision deferred.
- The debug traces in `QuarryGenerator.cs` (`_withTraces` ConcurrentBag, trace file emission) MUST be removed before any commit.

## Dependencies / Blockers
None — all remaining work is within the generator and test code.

## Architecture Decisions
- **Always emit `With<>` shadow**: The generated shadow is the interceptor target; the generic base is the discovery enabler. Both are needed. `new` is valid because there's always an inherited method to hide.
- **CTE-specific candidate disambiguation**: Scoped to `With`/`FromCte` method names only to avoid affecting other overload resolution paths.
- **`FromCte`/`With` exclusion from `DiscoverPostCteSites`**: Prevents duplicate site creation when these methods are walked through by the post-CTE scanner but also discovered by normal CTE discovery (Step 3b).

## Open Questions
- Why does the chain analysis classify CTE+Users+Select as "joined"? Is this a chain analysis bug or expected behavior requiring different test structure?
- Should `HasGenericContextBase` be removed from `ContextInfo` since conditional emission was reverted? Or kept for future diagnostic use?

## Next Work (Priority Order)
1. Remove debug traces from QuarryGenerator.cs
2. Fix CS0308: namespace qualification in generated interceptor code — investigate `TransitionBodyEmitter`, `CarrierEmitter`, or the entity type name resolution in the emitter pipeline
3. Fix QRY032: investigate chain analysis classification for CTE+EntityAccessor+Select chains
4. Fix QRY080: restructure the `With<TEntity, TDto>` test to inline the inner query
5. Run tests, fix assertion mismatches, commit Phase 4
6. Implement Phase 5 (docs) and Phase 6 (architectural findings)
