# Workflow: 205-cte-join-with-return-type
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: suspended
issue: #205
pr:
session: 2
phases-total: 6
phases-complete: 3
## Problem Statement
CTE chains that use context-specific methods after `With()` (e.g., `db.With<A>(inner).Users().Join<A>(...).Select(...)`) fail during source generation because `QuarryContext.With<TDto>()` returns the base `QuarryContext` type, and methods like `Users()` only exist on the derived (generated) context class.

Cascade effect during semantic model analysis in `Quarry.Generator`:
1. `.Users()` fails to resolve on the base `QuarryContext`
2. All subsequent chain methods cascade into error types
3. The chain is silently dropped (no interceptors generated)

`FromCte()` works because it is defined on the base `QuarryContext`. The breakage is specific to entity-accessor methods that only exist on the generated derived context.

Key files referenced in the issue:
- `src/Quarry/Context/QuarryContext.cs` — base class `With()` return type
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — discovery cascade failure
- `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — `EmitCteDefinition`

Approaches identified in the issue:
1. Self-referencing generic `QuarryContext<TSelf>` where `With()` returns `TSelf` (clean, but public API change)
2. Expand `DiscoverPostCteSites` with full entity-type tracking through the chain
3. `RegisterPostInitializationOutput` for CTE methods (limited by needing context class names at compile time)

Prior attempts noted in the issue:
- `TryResolveViaChainRootContext` — resolves individual methods against the concrete context but doesn't fix cascading type errors
- `DiscoverPostCteSites` — forward-scans from `With()` to create synthetic sites but generates incorrect builder types for Join/Select

Baseline: 3012 tests pass (97 Migration + 103 Analyzers + 2812 main). No pre-existing failures.
## Decisions
- 2026-04-06: **Approach: Option A (Hybrid) — add `QuarryContext<TSelf>` as a new generic subclass of the non-generic `QuarryContext`.** Non-breaking. Internals (`QueryExecutor`, carriers, `IEntityAccessor<T>`) continue referencing the non-generic base. Users migrate by changing inheritance to `class MyDb : QuarryContext<MyDb>`. Rejected Option B (full breaking conversion) and Option C (discovery-only workaround expansion).
- 2026-04-06: **Generator integration: Path 2 — conditional emission.** `ContextParser` detects whether the user inherits the generic base and records it on `ContextInfo`. `ContextCodeGenerator.GenerateCteMethods` skips the `new With<TDto>` / `new With<TEntity, TDto>` shadow when the base is already typed. Rejected Path 1 (always emit shadow + `#pragma warning disable CS0109`) because cleaner emitted output is worth ~10 extra lines.
- 2026-04-06: **FromCte shadow unchanged.** `FromCte<TDto>` already returns `IEntityAccessor<TDto>` (a base-library type), no drift, no change needed.
- 2026-04-06: **`DiscoverPostCteSites` stays as-is.** It self-skips invocations that resolve normally (UsageSiteDiscovery.cs:1078-1085), so it becomes a harmless no-op for migrated users. Not deleted in this PR — deletion requires migrating all in-repo contexts, filed as follow-up.
- 2026-04-06: **Legacy contexts remain supported.** Users inheriting the non-generic `QuarryContext` keep current behavior: `With/FromCte` chains work, `With/entity-accessor` chains silently drop as today. A diagnostic nudging migration is a future improvement, not part of this PR.
- 2026-04-06: **Architectural principle to be captured in PR body:** "Typed API declarations belong in the hand-written base library so the source generator's own discovery SemanticModel can see them. Implementations belong in generator output." The codebase already follows this for entity accessors (user writes `partial IEntityAccessor<User> Users();`); `With<TDto>` violated it by putting the typed return in generator output only. This pattern should guide future chain-method additions and a follow-up cleanup of `NavigationList<T>` declaration (which would dissolve `DiscoverPostJoinSites` the same way).
- 2026-04-06: **Follow-up issues to file during REMEDIATE (class C):** (1) migrate all in-repo contexts to `QuarryContext<TSelf>` and delete `DiscoverPostCteSites`; (2) apply the same principle to `NavigationList<T>` (user-declared partial navigation properties, generator emits body) to eliminate `DiscoverPostJoinSites`.
- 2026-04-06: **New test context rather than migrating existing ones.** Add `CteChainTestDbContext : QuarryContext<CteChainTestDbContext>` reusing existing entity types. Keeps blast radius on existing test fixtures to zero and isolates the new-path tests.

## Suspend State
- Current phase: **IMPLEMENT**, Phase 4 of 6, mid-phase
- Sub-step: Phase 4 integration tests — generator discovery is working, remaining issues are in generated code and test format

### Completed commits on this branch
1. `aeb93f1` Phase 1: `QuarryContext<TSelf>` generic subclass added to `src/Quarry/Context/QuarryContext.cs`
2. `c88d071` Phase 2: `HasGenericContextBase` flag on `ContextInfo`, `ContextParser` generic detection, conditional `With<>` emission in `ContextCodeGenerator` (NOTE: conditional emission was REVERTED in WIP — see below)
3. `8cb2c3f` Phase 3: `CteDb` test fixture in `Quarry.Tests.Samples.Cte` namespace

### WIP state (uncommitted changes)
**Root cause found and fixed:** The `With<>` call on `QuarryContext<TSelf>` contexts was not being discovered by the generator because Roslyn reports BOTH `QuarryContext<TSelf>.With<TDto>` (new) AND `QuarryContext.With<TDto>` (hidden base) as overload-resolution-failure candidates when the type argument is an error type. The disambiguation code in `UsageSiteDiscovery.cs` at Step 1 (line ~185) couldn't narrow from 2 to 1 candidate, so the discovery returned null.

**Fix applied in `UsageSiteDiscovery.cs`** (line ~198-224): Added a CTE-specific disambiguation for the `matchCount > 1` case that prefers the candidate from the most-derived containing type (i.e., `QuarryContext<TSelf>.With<TDto>` over `QuarryContext.With<TDto>`). This correctly narrows to 1 candidate, allowing `DiscoverCteSite` to proceed.

**Fix applied in `UsageSiteDiscovery.cs` `DiscoverPostCteSites`** (line ~1128-1134): Added `FromCte`/`With` exclusion before the entity-accessor fallback to prevent duplicate site creation (QRY900).

**Fix applied in `ContextCodeGenerator.cs`**: Reverted Phase 2's conditional `With<>` shadow emission — the shadow is ALWAYS emitted (even for generic-base contexts) because it's needed as the interceptor target. The `new` keyword is valid in both cases (hides inherited method). The generic base's purpose is for DISCOVERY (makes typed return visible to SemanticModel), while the generated shadow is for INTERCEPTION.

**Temporary debug code in `QuarryGenerator.cs`**: `_withTraces` ConcurrentBag and trace emission via `RegisterImplementationSourceOutput` — MUST be removed before committing.

### Current build status (with WIP changes)
Build fails with 17 errors, all in generated interceptor code:
1. **CS0308** ("non-generic type 'Order' cannot be used with type arguments") — 6 errors in `CteDb.Interceptors.*.g.cs`. The generated interceptor references `Order` without namespace qualification, resolving to the wrong `Order` type. The generator's emitter needs to use fully-qualified or namespace-prefixed entity type names for the `Cte` namespace.
2. **QRY032** ("Joined Select() argument must be a parenthesized lambda") — 4 errors in test file. Tests 1-2 (`Cte_Users_Select`, `Cte_Users_Where_Select`) and test 6 (`Cte_FromCte_StillWorks_OnGenericBase`) have Select lambdas that the chain analysis classifies as "joined" because of the CTE context. Need to investigate whether the lambda format needs changing or the chain analysis classification is wrong.
3. **QRY080** ("could not analyze inner query for With<OrderSummaryDto>") — 1 error in test 7. The `With<TEntity, TDto>` overload with projected inner query isn't handled correctly. May need to pass the inner query differently or this test may need restructuring.

### Immediate next steps on resume
1. **Remove debug traces** from `QuarryGenerator.cs` (_withTraces, trace emission)
2. **Fix CS0308**: Investigate how the interceptor emitter resolves entity type names. The `Cte` sub-namespace creates ambiguity with entity types in the parent namespace. May need to use fully-qualified names in generated code. Look at `TransitionBodyEmitter.EmitCteDefinition` and `CarrierEmitter`.
3. **Fix QRY032**: Check why CTE+Users+Select chains are classified as "joined" by the chain analysis. Compare with how CTE+FromCte+Select works. The issue may be in how `DiscoverPostCteSites` classifies the entity accessor as part of the chain.
4. **Fix QRY080**: The `With<TEntity, TDto>` overload's inner query is a projected query stored in a variable (`var innerQuery = ...`). The generator may require the inner query to be inline. Consider restructuring the test.
5. After all build errors fixed, run tests, fix assertion mismatches, then commit Phase 4.
6. Phases 5-6 are straightforward (docs + artifact).

### Test status
3012 tests pass on committed code (phases 1-3). WIP changes don't compile yet due to the above errors.

### Key decisions made during this session
- **`With<>` shadow must always be emitted** regardless of `HasGenericContextBase`. The generic base provides typed return for DISCOVERY; the generated shadow provides the INTERCEPTOR TARGET. Both are needed. Phase 2's conditional emission was a premature optimization.
- **CTE methods (With/FromCte) must be excluded from `DiscoverPostCteSites`'s entity-accessor fallback** to prevent duplicate site creation.
- **CTE-specific candidate disambiguation** is needed in Step 1 of discovery when `QuarryContext<TSelf>` introduces `new` methods that Roslyn reports alongside the hidden base methods as separate overload-resolution-failure candidates.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #205 loaded, worktree created, baseline green (3012 tests) |
| 1 | DESIGN | PLAN | Design approved: Option A (QuarryContext<TSelf> generic subclass) + Path 2 (conditional generator emission). Principle captured for PR body. |
| 1 | PLAN | PLAN | plan.md written (6 phases). Suspended by user before explicit plan approval; session pushed to remote for handoff. |
| 2 | PLAN | IMPLEMENT | Resumed. Plan approved. Phases 1-3 committed. Phase 4 in progress — core discovery fix working, remaining build errors in generated interceptors and test format. |
