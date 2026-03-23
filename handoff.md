# Work Handoff

## Key Components

- **New Pipeline**: RawCallSite → BoundCallSite → TranslatedCallSite → [Collect] → ChainAnalyzer → QueryPlan → SqlAssembler → AssembledPlan → CarrierAnalyzer → CarrierPlan → FileInterceptorGroup
- **Bridge Layer**: REMOVED. EmitFileInterceptorsNewPipeline passes new types directly to emitters.
- **FileInterceptorGroup**: Single constructor with new types only (Sites: TranslatedCallSite[], AssembledPlans, ChainMemberSites, CarrierPlans).
- **PipelineOrchestrator**: Static class. Only AnalyzeAndGroupTranslated remains.
- **CarrierPlan**: ClassName/BaseClassName assigned during emission in FileEmitter.
- **AssembledPlan**: Has convenience properties mirroring PrebuiltChainInfo API. ProjectionInfo/TraceLines/JoinedTableInfos set by EmitFileInterceptorsNewPipeline.
- **TranslatedClause**: Has SqlFragment (lazy-rendered) and SetAssignments for SetAction.
- **TestCallSiteBuilder**: Test helper in `src/Quarry.Tests/Testing/TestCallSiteBuilder.cs` for constructing TranslatedCallSite in unit tests. Has `CreateJoinClause` and `CreateSimpleClause` static helpers.

## Completions (This Session)

1. **Fix navigation join chain discovery** (2 failing tests → 0, +2 bonus):
   - Added `DiscoverRawCallSites` (plural) to `UsageSiteDiscovery.cs` — wraps `DiscoverRawCallSite` and for navigation joins, forward-scans the fluent chain to discover post-join sites (Select, Trace, ToDiagnostics, ExecuteFetchAllAsync, etc.).
   - Added `DiscoverPostJoinSites` — walks UP syntax tree from Join invocation, creates synthetic RawCallSites for each post-join method call.
   - Changed `QuarryGenerator.Initialize()` Stage 2 to use `SelectMany` with the new plural method.
   - Added `TranslatedCallSite.WithJoinedEntityTypeNames()` for immutable propagation.
   - Added `JoinedEntityTypeNames` propagation in `ChainAnalyzer.AnalyzeChainGroup()`.
   - Modified `FileEmitter.Emit()` to prefer chain-updated sites over original sites.
   - `SqlAssembler`: falls back to projection's `ResultTypeName` when execution site lacks it.
   - `CallSiteTranslator.TranslateNavigationJoin`: fixed ON clause to use table-qualified column names (`"t0"."UserId" = "t1"."UserId"` instead of `"UserId" = "UserId"`).

## Previous Session Completions

1-32. See git log for full history. Key milestones:
- New pipeline architecture built (Stages 1-5)
- SqlAssembler, CarrierPlan, ChainAnalyzer rewritten
- All emitters migrated to new types, bridge layer removed
- 2951/2954 tests passing before previous session's work
- Previous session: ResultTypeName regression, UpdateSetAction gap, unit test migration, entity reader fix, carrier pagination logging, carrier Select skip, SetAction column quoting (standalone path)
- Previous session: Fix SetAction column quoting in chain/prebuilt SQL path, join unit test fixtures, logging integration tests, Set type mapping test, carrier diagnostics test

## Progress

- Generator build: Clean (0 errors)
- Test project build: Clean (0 errors)
- Tests: 2949/2954 passing (99.8%), 0 failing, 5 skipped
- Branch: feature/compiler-architecture
- Plan steps 1–8: Complete (previous sessions)
- Plan step 9 (UsageSiteDiscovery rewrite): **Not started** — see Deviations
- Plan step 10 (delete old code): **Blocked** by step 9
- Plan step 11 (fix tests): **Complete** — all non-skipped tests pass

## Current State

All tests pass. 5 tests are skipped (runtime fallback — not a generator issue). Ready for Step 9.

### Root cause of navigation join fix (for reference)

Navigation joins like `.Join(u => u.Orders)` use type inference for `TJoined`. Since `User` is a generated type (from the same source generator's Phase 1), Roslyn's semantic model can't resolve `u.Orders` and type inference fails. The return type becomes `IJoinedQueryBuilder<User, ?>` where `?` is truly unknown. All subsequent method calls (Select, Trace, ToDiagnostics) fail semantic resolution — Roslyn returns no symbol and no candidates. The generator never discovers these call sites.

Explicit joins like `.Join<Order>((u, o) => ...)` work because the `<Order>` type argument is written in source — Roslyn preserves it even as an error type, making the return type `IJoinedQueryBuilder<User, Order>` fully named. Subsequent calls resolve against the interface definition.

The fix forward-scans the syntax tree from the Join invocation to discover post-join method calls, then propagates resolved entity type names during chain analysis.

## Known Issues / Bugs

- **Runtime fallback reader delegate** (5 skipped tests): Runtime `Select()` doesn't build reader from expression tree. Only generator interceptors provide readers. This is a runtime limitation, not a generator issue.

## Technical Debt (from navigation join fix)

The following items work correctly but have suboptimal design. They should be cleaned up, ideally during Step 9 (discovery rewrite).

### 1. Split responsibility for JoinedEntityTypeNames population

**Problem**: For explicit joins (`Join<Order>(...)`), `JoinedEntityTypeNames` is populated during discovery by `ExtractJoinedEntityTypeNames(containingType)` and passed through the binder unchanged. For navigation joins (`Join(u => u.Orders)`), discovery can't resolve the type, so `JoinedEntityTypeNames` is left null on the Join site and on synthetic post-join sites. The ChainAnalyzer then builds it from `BoundCallSite.Entity` + `BoundCallSite.JoinedEntity`.

**Why it matters**: Two different code paths populate the same field, making it hard to reason about when `JoinedEntityTypeNames` is available.

**How to fix**: Have `CallSiteBinder.Bind()` always build `JoinedEntityTypeNames` for Join sites when a `JoinedEntity` is resolved — but under a new field name like `ResolvedJoinedEntityTypeNames` to avoid confusing `JoinBodyEmitter` (which uses `JoinedEntityTypeNames.Count >= 2` to detect chained joins vs first-level joins). Then have ChainAnalyzer propagate from this field. The key constraint: `JoinedEntityTypeNames` must NOT be set on first-level Join sites because `JoinBodyEmitter` at line 65 uses `joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2` to decide if it's a chained join.

### 2. FileEmitter site-selection logic

**Problem**: `FileEmitter.Emit()` (line ~290) builds `chainSites` by looking up each clause site by UniqueId in `siteByUniqueId` (built from `allSitesForGeneration`). For navigation join chains, the original sites lack `JoinedEntityTypeNames`, but the chain-updated sites (from `AssembledPlan.ClauseSites`) have them. The emitter now has conditional logic: use the chain's site when the original lacks `JoinedEntityTypeNames`, otherwise use the original.

**Why it matters**: Fragile conditional logic that can break if other fields are also propagated in the future.

**How to fix**: Have `PipelineOrchestrator.GroupTranslatedIntoFiles()` replace sites in its `allSites` collection with their chain-updated versions before building `FileInterceptorGroup`. This way, `FileInterceptorGroup.Sites` always has the latest data, and the emitter doesn't need the conditional. Specifically: after `SqlAssembler.Assemble()`, build a `Dictionary<string, TranslatedCallSite>` from all chain clause sites and execution sites (keyed by UniqueId), then use it to replace entries in the sites list passed to `FileInterceptorGroup`.

### 3. SqlAssembler ResultTypeName fallback

**Problem**: `SqlAssembler.Assemble()` sets `resultTypeName: executionSite.Bound.Raw.ResultTypeName ?? (plan.Projection?.IsIdentity == false ? plan.Projection.ResultTypeName : null)`. The fallback to projection's ResultTypeName is needed because synthetic post-join execution sites have `Raw.ResultTypeName == null`.

**Why it matters**: The `IsIdentity == false` guard is fragile — it assumes identity projections don't need a ResultTypeName, which may not hold for all query shapes.

**How to fix**: Propagate `ResultTypeName` to the synthetic execution site during ChainAnalyzer analysis (after `BuildProjection` at ~line 472). Add it to `TranslatedCallSite.WithJoinedEntityTypeNames()` or create a more general `WithPropagatedChainData()` method. Then the SqlAssembler fallback becomes unnecessary.

## Dependencies / Blockers

- **Step 10 (old code deletion) blocked by Step 9 (discovery rewrite)**: `DiscoverRawCallSite` still delegates to `DiscoverUsageSite` which depends on `ClauseTranslator` and other old translation infrastructure. Until discovery is rewritten to use SqlExpr directly, the old code cannot be deleted.

## Architecture Decisions

- **Try/catch NOT a chain disqualifier**: Quarry builder methods don't throw, so prebuilt SQL dispatch works correctly inside try/catch blocks.
- **Foreach collection expression NOT a loop**: `DetectLoopAncestor` now checks whether the node is in the loop *body* vs the *collection expression*.
- **Literal pagination inlining**: The new pipeline inlines literal `Limit(10)` values directly into SQL as `LIMIT 10` instead of using a runtime parameter.
- **SetAction column quoting location**: Quoting happens in ChainAnalyzer's `ClauseKind.Set` branch, not the `InterceptorKind.UpdateSetAction` branch.
- **CarrierPlan deferred naming**: ClassName/BaseClassName assigned in FileEmitter during carrier class emission, not during CarrierAnalyzer.
- **Chain ProjectionInfo preferred over site**: Discovery-time ProjectionInfo may have `?` ResultTypeName when entity types are generator-produced. EmitSelect prefers chain's enriched ProjectionInfo.
- **Navigation join forward-scan approach**: Instead of modifying Roslyn's type inference, the generator forward-scans the syntax tree from the Join invocation to discover post-join method calls. Type information (JoinedEntityTypeNames) is propagated from the Join's bound data to synthetic post-join sites during chain analysis. This avoids changing the pipeline's single-site-per-transform architecture by using `SelectMany`.
- **JoinedEntityTypeNames NOT set on Join sites by binder**: Setting it on Join sites confuses JoinBodyEmitter into treating first-level joins as chained joins. Only set on post-join sites via ChainAnalyzer propagation. See Technical Debt #1 for the longer-term fix.

## Open Questions

- **Should Step 9 address the technical debt items above?** The discovery rewrite touches the same files (UsageSiteDiscovery, CallSiteBinder, ChainAnalyzer). It may be the natural place to unify `JoinedEntityTypeNames` population.

## Next Work (Priority Order)

### 1. Step 9: Rewrite UsageSiteDiscovery
Rewrite `DiscoverRawCallSite` to stop delegating to `DiscoverUsageSite`. Parse all clause lambdas directly via SqlExprParser. Handle SetAction/SetPoco directly without ClauseTranslator. This unblocks Step 10. Consider addressing Technical Debt items 1-3 during this rewrite.

### 2. Step 10: Delete old pipeline code (~7,800 lines)
Once Step 9 is complete, delete all old types and translation infrastructure.

### 3. Fix runtime fallback reader delegate (5 skipped tests)
Requires runtime `Select()` to compile a reader delegate from the selector expression. This is a runtime change, not generator work.
