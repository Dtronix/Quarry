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

1. **Navigation join forward-scan discovery (in progress)**:
   - Added `DiscoverRawCallSites` (plural) to `UsageSiteDiscovery.cs` — wraps `DiscoverRawCallSite` and for navigation joins, forward-scans the fluent chain to discover post-join sites (Select, Trace, ToDiagnostics, ExecuteFetchAllAsync, etc.).
   - Added `DiscoverPostJoinSites` method that walks UP the syntax tree from the Join invocation to find each subsequent method call, creating synthetic RawCallSites.
   - Changed `QuarryGenerator.Initialize()` Stage 2 to use `SelectMany` with the new plural method.
   - Added `TranslatedCallSite.WithJoinedEntityTypeNames()` — creates a copy with updated JoinedEntityTypeNames (since TranslatedCallSite/BoundCallSite are immutable).
   - Added `JoinedEntityTypeNames` propagation in `ChainAnalyzer.AnalyzeChainGroup()` — when a chain has a Join clause site with resolved JoinedEntity, it propagates the entity type names to all post-join sites that lack them.
   - Modified `FileEmitter.Emit()` to prefer chain-updated sites over original sites (for JoinedEntityTypeNames propagation).
   - **Remaining issue**: `ResultTypeName` is not propagated to synthetic execution sites. The `ToDiagnostics` emitter generates `IJoinedQueryBuilder<User, Order>` instead of `IJoinedQueryBuilder<User, Order, (string, decimal)>` because the synthetic ToDiagnostics RawCallSite has `resultTypeName: null`. Need to either extract ResultTypeName from the Select site's ProjectionInfo or propagate it during chain analysis.

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
- Test project build: 5 errors (all `ToDiagnostics` signature mismatch on navigation join chains — see Current State)
- Tests: 2947/2954 passing (99.8%) at baseline, 2 failing, 5 skipped
- Branch: feature/compiler-architecture
- Plan steps 1–8: Complete (previous sessions)
- Plan step 9 (UsageSiteDiscovery rewrite): **Not started** — see Deviations
- Plan step 10 (delete old code): **Blocked** by step 9
- Plan step 11 (fix tests): **In progress** — navigation join fix underway

## Current State

### Navigation join forward-scan fix (5 compilation errors remaining)

The forward-scan discovery is working — synthetic post-join sites are now discovered, bound, translated, chain-analyzed, and assembled. The remaining issue is **ResultTypeName propagation** for the execution site.

**What was tried and current approach:**
1. **First attempt**: Syntactic fallback in `DiscoverUsageSite` (`TryDiscoverJoinedBuilderSiteSyntactically`) — created UsageSiteInfo for post-join sites. **Failed**: downstream pipeline (binder/translator/emitter) crashed with null references because the synthetic sites had incomplete data (no JoinedEntityTypeNames, missing entity metadata). Approach abandoned.
2. **Second attempt (current)**: Forward-scan from the navigation join's `DiscoverRawCallSite`. Walk UP the syntax tree from the Join invocation, create RawCallSites for each post-join method. Then in ChainAnalyzer, propagate `JoinedEntityTypeNames` from the Join clause site to all post-join sites. This approach is mostly working but has a remaining ResultTypeName issue.

**What's left to resolve:**
- The synthetic ToDiagnostics/ExecuteFetchAllAsync sites have `resultTypeName: null` because Roslyn can't resolve the `Select` return type (the tuple result type). The emitter needs the ResultTypeName to generate the correct this-parameter type (e.g., `IJoinedQueryBuilder<User, Order, (string, decimal)>`).
- **Suggested fix**: In ChainAnalyzer, after building the `SelectProjection`, propagate the `ResultTypeName` from the projection to the execution site. Or: when building `AssembledPlan`, set `ResultTypeName` from the chain's projection. The `EmitDiagnosticsTerminal` at line 363 already checks `chain.ResultTypeName` — the chain's `ResultTypeName` should be correct if the Select projection was analyzed properly.

### Root cause analysis (for reference)

Navigation joins like `.Join(u => u.Orders)` use type inference for `TJoined`. Since `User` is a generated type (from the same source generator's Phase 1), Roslyn's semantic model can't resolve `u.Orders` and type inference fails. The return type becomes `IJoinedQueryBuilder<User, ?>` where `?` is truly unknown. All subsequent method calls (Select, Trace, ToDiagnostics) fail semantic resolution — Roslyn returns no symbol and no candidates. The generator never discovers these call sites.

Explicit joins like `.Join<Order>((u, o) => ...)` work because the `<Order>` type argument is written in source — Roslyn preserves it even as an error type, making the return type `IJoinedQueryBuilder<User, Order>` fully named. Subsequent calls resolve against the interface definition.

### 5 skipped tests (runtime fallback)

These deliberately bypass the generator by casting to `QueryBuilder<T, TResult>` and calling `AddWhereClause()` directly. The runtime `Select()` doesn't create a reader delegate. This is a runtime limitation, not a generator issue.

## Known Issues / Bugs

- **Navigation join ResultTypeName** (5 compile errors): Synthetic post-join execution sites don't carry the result type from the Select projection. See Current State above.
- **Navigation join ON clause table aliases** (pre-existing): Produces `("UserId" = "UserId")` instead of `"t0"."UserId" = "t1"."UserId"` in standalone (non-chain) path.
- **Runtime fallback reader delegate** (5 skipped tests): Runtime `Select()` doesn't build reader from expression tree. Only generator interceptors provide readers.
- **Dead code**: `TryDiscoverJoinedBuilderSiteSyntactically` and `TryExtractNavigationJoinEntityType` methods in UsageSiteDiscovery.cs are unreachable (from first failed approach). Should be removed.

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
- **JoinedEntityTypeNames NOT set on Join sites by binder**: Setting it on Join sites confuses JoinBodyEmitter into treating first-level joins as chained joins. Only set on post-join sites via ChainAnalyzer propagation.

## Open Questions

- **Should ResultTypeName propagation happen in ChainAnalyzer or SqlAssembler?** The chain's `SelectProjection` has the result type info — the question is where to attach it to the execution site.

## Next Work (Priority Order)

### 1. Fix navigation join ResultTypeName (5 compile errors)
The synthetic ToDiagnostics site needs `ResultTypeName`. The chain's `SelectProjection.ResultTypeName` has the correct value. Propagate it to the execution site in ChainAnalyzer (after `BuildProjection` at ~line 433) or in SqlAssembler. Then update the FileEmitter to use the chain's execution site (already partially done).

### 2. Verify all tests pass after nav join fix
Run the full 2954-test suite. Fix any remaining type propagation or emission issues.

### 3. Step 9: Rewrite UsageSiteDiscovery
Rewrite `DiscoverRawCallSite` to stop delegating to `DiscoverUsageSite`. Parse all clause lambdas directly via SqlExprParser. Handle SetAction/SetPoco directly without ClauseTranslator. This unblocks Step 10.

### 4. Step 10: Delete old pipeline code (~7,800 lines)
Once Step 9 is complete, delete all old types and translation infrastructure.

### 5. Clean up dead code
Remove `TryDiscoverJoinedBuilderSiteSyntactically` and `TryExtractNavigationJoinEntityType` from UsageSiteDiscovery.cs.
