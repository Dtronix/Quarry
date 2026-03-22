# Work Handoff

## Key Components

- **New Pipeline**: RawCallSite → BoundCallSite → TranslatedCallSite → [Collect] → ChainAnalyzer → QueryPlan → SqlAssembler → AssembledPlan → CarrierAnalyzer → CarrierPlan → FileInterceptorGroup
- **Bridge Layer**: REMOVED. EmitFileInterceptorsNewPipeline passes new types directly to emitters.
- **FileInterceptorGroup**: Single constructor with new types only (Sites: TranslatedCallSite[], AssembledPlans, ChainMemberSites, CarrierPlans).
- **PipelineOrchestrator**: Static class. Only AnalyzeAndGroupTranslated remains.
- **CarrierPlan**: ClassName/BaseClassName assigned during emission in FileEmitter.
- **AssembledPlan**: Has convenience properties mirroring PrebuiltChainInfo API. ProjectionInfo/TraceLines/JoinedTableInfos set by EmitFileInterceptorsNewPipeline.
- **TranslatedClause**: Has SqlFragment (lazy-rendered) and SetAssignments for SetAction.
- **TestCallSiteBuilder**: New test helper in `src/Quarry.Tests/Testing/TestCallSiteBuilder.cs` for constructing TranslatedCallSite in unit tests.

## Completions (This Session)

1. **Fix ResultTypeName `?` regression** (~160 generated code errors → 0):
   - ClauseBodyEmitter/JoinBodyEmitter: Prefer chain's enriched ProjectionInfo over site's discovery-time projection.
   - Added `ResultTypeName != "?"` guard to Select emitter paths; unresolvable types fall through to generic `<T, TResult>` fallback.

2. **Fix UpdateSetAction interceptor gap** (178 CS9144 errors → 0):
   - CallSiteTranslator: Handle `UpdateSetAction` (Action<T> lambda) before null expression check. Creates TranslatedClause with SetActionAssignments from RawCallSite.
   - Without this fix, SetAction interceptor attributes were orphaned (no method body), causing cascading CS9144 signature mismatches.

3. **Migrate unit tests from UsageSiteInfo to TranslatedCallSite** (~108 errors → 0):
   - Created `TestCallSiteBuilder` fluent helper for test fixture construction.
   - Migrated: ExecutionInterceptorTests, EntityReaderTests, JoinOperationsTests, TypeMappingInterceptorTests, RawSqlInterceptorTests, InlineExtractionGeneratorTests.

4. **Fix entity reader field collection** (14 CS0103 errors → 0):
   - InterceptorCodeGenerator.CollectEntityReaderInstances: Also check chain-level ProjectionInfo for CustomEntityReaderClass.

5. **Fix carrier pagination logging** (10 CS1061 errors → 0):
   - CarrierEmitter.EmitInlineParameterLogging: Use HasCarrierField() instead of checking clause roles. Literal pagination doesn't produce carrier fields.

6. **Fix carrier Select skip** (147 test failures → 39):
   - FileEmitter: Never skip carrier Select sites via ShouldSkipSelectInterceptor — they're needed as cast entry points.

7. **Add SetAction column quoting for standalone path**:
   - ClauseBodyEmitter.EmitUpdateSetAction: Resolve and quote column names using entity metadata for standalone (non-chain) path.

## Previous Session Completions

1-32. See git log for full history. Key milestones:
- New pipeline architecture built (Stages 1-5)
- SqlAssembler, CarrierPlan, ChainAnalyzer rewritten
- All emitters migrated to new types, bridge layer removed
- 2951/2954 tests passing before this session's work

## Progress

- Generator build: Clean (0 errors)
- Test project build: Clean (0 errors)
- Tests: 2914/2954 passing (98.6%), 39 failing, 1 skipped
- Branch: feature/compiler-architecture
- Plan steps 1–8: Complete (previous sessions)
- Plan step 9 (UsageSiteDiscovery rewrite): **Not started** — see Deviations
- Plan step 10 (delete old code): **Blocked** by step 9
- Plan step 11 (fix tests): In progress — 39 failures remain

## Deviations from Original Plan (impl-plan-compiler-bigbang.md)

### Step 9 — NOT done (significant, blocks Step 10)

The plan called for rewriting `UsageSiteDiscovery.DiscoverRawCallSite` to be the sole discovery entry point, removing `DiscoverUsageSite`, removing all `ClauseTranslator` calls, and making SqlExpr the only parsing path. **This was not done.** `DiscoverRawCallSite` still delegates to `DiscoverUsageSite` internally (UsageSiteDiscovery.cs:638), which still calls `ClauseTranslator.TranslateSetAction` for Action<T> lambdas. The old discovery path and old translation infrastructure remain as **active code dependencies**, not dead code.

**Impact**: The old `ClauseTranslator`, `ExpressionSyntaxTranslator`, and related translation code **cannot be deleted in Step 10** until discovery is rewritten. This is the biggest deviation — it blocks Phase 4 (deletion of ~7,800 lines).

### Step 10 — NOT done (blocked by Step 9)

No old code has been deleted. All ~7,800 lines of old pipeline code still exist as live dependencies of the discovery path.

### Step 11 — Partially done, with unplanned fixes

The plan expected test failures to be purely output differences. In practice, the test failures revealed **generator bugs** that required new code not anticipated by the plan:

- **Unplanned: UpdateSetAction interceptor gap** — `CallSiteTranslator` needed explicit Action<T> handling to avoid orphaned `[InterceptsLocation]` attributes. New code path added.
- **Unplanned: Carrier Select skip exemption** — `ShouldSkipSelectInterceptor` incorrectly skipped carrier Select sites. New `!isCarrierSite` guard added.
- **Unplanned: SetAction column quoting in emitter** — The standalone emitter path needed its own column quoting logic (old pipeline handled this via `EnrichSetActionClauseWithMapping`).
- **Planned `TestPlanBuilder` and `TestCarrierPlanBuilder` not created** — Only `TestCallSiteBuilder` was built. The other two are needed if internal unit tests for AssembledPlan/CarrierPlan construction are added.

### Step ordering deviation

The plan specified sequential steps 1→11. This session worked on Step 11 (test fixes) before Steps 9–10 (discovery rewrite and old code deletion), because test failures revealed generator bugs that had to be fixed first. Steps 9–10 remain pending.

## Current State

### 39 test failures remain

The failures fall into these categories:

**SetAction column quoting in chain path (~15 tests)**: `Update_SetAction_*`, `Update_Where_CapturedParameter`, etc. The SetAction SET clause produces unquoted column names in prebuilt SQL: `SET UserName = 'NewName'` instead of `SET "UserName" = 'NewName'`.
- **Root cause**: The `ChainAnalyzer` quotes columns via `SqlFormatting.QuoteIdentifier(site.Bound.Dialect, assignment.ColumnSql)`, but the generated SQL still shows unquoted. The standalone emitter path was fixed (this session), but the chain/SqlAssembler path still produces unquoted columns.
- **What was tried**: Added column quoting in the standalone EmitUpdateSetAction path. This fixes standalone tests but not chain-analyzed tests.
- **What to investigate next**: Check if `ChainAnalyzer` SetAction processing is actually reached (line 343). Verify `raw.SetActionAssignments` is populated. Use `.Trace()` on a failing SetAction chain to see the ChainAnalysis output. The `EnrichSetActionClauseWithMapping` function in the old pipeline (QuarryGenerator.cs:2527) was responsible for quoting — verify its equivalent is called in the new flow.

**Logging tests (~4-5 tests)**: CategoryLevelDebug_SuppressesTraceParameterLogs, FetchAll_LogsSqlAndCompletion, etc. May be related to the carrier pagination logging fix or other emission changes.

**Join unit tests (~6 tests)**: InterceptorCodeGenerator_ChainedJoin_*, InterceptorCodeGenerator_JoinedWhere/OrderBy. The test fixtures were migrated to TestCallSiteBuilder but ClauseInfo data (JoinClauseInfo, OrderByClauseInfo) was dropped. These tests verify generated interceptor code patterns and may need TranslatedClause with appropriate clause data to produce expected output.

**TypeMapping tests (~3-4 tests)**: FallbackPath_*, GenerateInterceptorsFile_SetMappedColumn. The test fixture migration may have lost type mapping information that was previously set on ClauseInfo.

**Navigation join (~2 tests)**: Pre-existing failures from previous session. ChainId computation across type-changing fluent calls.

## Known Issues / Bugs

- **Navigation join inferred type** (2 tests): JoinExecution_NavigationJoin_InferredType_TupleProjection and Traced variant. ChainId computation doesn't link fluent chains across type-changing calls. Pre-existing.
- **Navigation join ON clause table aliases**: Produces `("UserId" = "UserId")` instead of `"t0"."UserId" = "t1"."UserId"` in standalone (non-chain) path. Pre-existing.

## Dependencies / Blockers

- **Step 10 (old code deletion) blocked by Step 9 (discovery rewrite)**: `DiscoverRawCallSite` still delegates to `DiscoverUsageSite` which depends on `ClauseTranslator` and other old translation infrastructure. Until discovery is rewritten to use SqlExpr directly, the old code cannot be deleted.

## Architecture Decisions

- **Big bang over incremental**: Adapter approach was fundamentally broken. Building new system completely and switching in one shot was the only reliable path.
- **CarrierPlan deferred naming**: ClassName/BaseClassName assigned in FileEmitter during carrier class emission, not during CarrierAnalyzer (which lacks file-level context).
- **Chain ProjectionInfo preferred over site**: Discovery-time ProjectionInfo may have `?` ResultTypeName when entity types are generator-produced. EmitSelect now prefers chain's enriched ProjectionInfo.
- **UpdateSetAction pass-through in CallSiteTranslator**: Action<T> lambdas can't be parsed into SqlExpr. The translator creates a TranslatedClause with a placeholder expression and the raw SetActionAssignments. This is a workaround — Step 9 would eliminate the need for this by handling SetAction directly in discovery.
- **ShouldSkipSelectInterceptor exemption for carrier sites**: Carrier Select sites must always be emitted as they serve as Unsafe.As cast entry points, even when the projection fails validation.
- **TestCallSiteBuilder**: Replaces direct UsageSiteInfo construction in tests. Builds the full object graph (RawCallSite → BoundCallSite → TranslatedCallSite) from simplified parameters.

## Open Questions

- **Should Step 9 be done before or after fixing the remaining 39 test failures?** Rewriting discovery to remove the old ClauseTranslator dependency would simplify the SetAction flow (no more pass-through workaround), but could introduce new test regressions. Fixing the 39 failures first gives a stable baseline.
- **SetAction column quoting: is the issue in ChainAnalyzer or SqlAssembler?** The ChainAnalyzer appears to quote correctly, but the rendered SQL is unquoted. A `.Trace()` run would clarify whether the issue is in chain analysis or SQL rendering.

## Next Work (Priority Order)

### 1. Fix SetAction column quoting in chain SQL path (~15 tests)
The ChainAnalyzer should be quoting SetAction columns at line 364, but the generated prebuilt SQL shows unquoted column names. Investigate why the quoting isn't applied in the final SQL output.
- Check if `raw.SetActionAssignments` is populated for the failing test chains
- Use `.Trace()` to inspect ChainAnalysis output for a SetAction chain
- If the issue is in SqlAssembler/SqlExprRenderer, check how ResolvedColumnExpr is rendered

### 2. Fix logging integration tests (~4-5 tests)
CategoryLevelDebug, FetchAll_LogsSql, etc. May be caused by carrier pagination logging changes or other emission path changes.

### 3. Fix join unit test fixtures (~6 tests)
The TestCallSiteBuilder migration dropped ClauseInfo data. These tests need TranslatedClause with join/orderby clause data to produce the expected generated code patterns.

### 4. Fix TypeMapping unit test fixtures (~3-4 tests)
Similar to join tests — need TranslatedClause with proper parameter/clause data for the generated code to match expectations.

### 5. Step 9: Rewrite UsageSiteDiscovery (plan step, not yet started)
Rewrite `DiscoverRawCallSite` to stop delegating to `DiscoverUsageSite`. Parse all clause lambdas directly via SqlExprParser. Handle SetAction/SetPoco directly without ClauseTranslator. This unblocks Step 10.

### 6. Step 10: Delete old pipeline code (~7,800 lines)
Once Step 9 is complete, delete: UsageSiteInfo, ClauseInfo, PendingClauseInfo, ChainAnalysisResult, PrebuiltChainInfo, ChainParameterInfo, CarrierClassInfo, ClauseTranslator, ExpressionSyntaxTranslator, ExpressionTranslationContext, ExpressionTranslationResult, SubqueryScope, CompileTimeSqlBuilder, SqlFragmentTemplate, CarrierClassBuilder, CarrierStrategy, old methods in QuarryGenerator.cs and UsageSiteDiscovery.cs.
