# Quarry Compiler Migration Handoff

## Key Components

- **New Pipeline**: RawCallSite → BoundCallSite → TranslatedCallSite → [Collect] → ChainAnalyzer → QueryPlan → SqlAssembler → AssembledPlan → CarrierAnalyzer → CarrierPlan → FileInterceptorGroup
- **Bridge Layer**: REMOVED. EmitFileInterceptorsNewPipeline now passes new types (TranslatedCallSite, AssembledPlan, CarrierPlan) directly to emitters. No more UsageSiteInfo/PrebuiltChainInfo conversion.
- **FileInterceptorGroup**: Simplified to single constructor with new types only (Sites: TranslatedCallSite[], AssembledPlans, ChainMemberSites, CarrierPlans). Old dual-type design removed.
- **PipelineOrchestrator**: Converted to static class. Old instance methods (AnalyzeAndGroup, GroupIntoFiles) removed. Only AnalyzeAndGroupTranslated remains.
- **CarrierPlan**: ClassName and BaseClassName now assigned during emission in FileEmitter (not during analysis). CarrierAnalyzer.AnalyzeNew sets them to null/empty, FileEmitter assigns "Chain_N" names.
- **AssembledPlan**: Added convenience properties mirroring PrebuiltChainInfo API (QueryKind, TableName, SchemaName, IsJoinChain, ChainParameters, UnmatchedMethodNames, Tier, etc.). GetClauseEntries() replaces chain.Analysis.Clauses. ProjectionInfo/TraceLines/JoinedTableInfos set by EmitFileInterceptorsNewPipeline.
- **TranslatedClause**: Added SqlFragment property (lazy-rendered from ResolvedExpression) and ColumnSql alias. These replace ClauseInfo.SqlFragment for emitters that need pre-rendered SQL.
- **ChainId Scoping**: Unchanged from previous session.
- **Trace Logging**: TraceCapture data now flows to AssembledPlan.TraceLines directly (set in EmitFileInterceptorsNewPipeline). FileEmitter reads chain.TraceLines from AssembledPlan. Trace format unchanged.
- **Subquery Support**: Unchanged.
- **TestCapturedChains**: Unchanged.

## Completions (This Session)

1. Phase 1: Verified shared enums/types already extracted to standalone files in Models/ — no work needed.
2. Phase 2: Migrated ALL emitters to consume new pipeline types directly:
   - RawSqlBodyEmitter: UsageSiteInfo → TranslatedCallSite
   - TransitionBodyEmitter: UsageSiteInfo → TranslatedCallSite, CarrierClassInfo → CarrierPlan, PrebuiltChainInfo → AssembledPlan
   - ClauseBodyEmitter: Full migration including ClauseInfo → TranslatedClause property renames, ChainParameterInfo → QueryParameter property renames (.Index→.GlobalIndex, .TypeName→.ClrType, .TypeMapping→.TypeMappingClass)
   - JoinBodyEmitter: Full migration including JoinClauseInfo removal (OnConditionSql → SqlFragment, JoinedEntityName → site.JoinedEntityTypeName)
   - TerminalBodyEmitter: Full migration including chain.SqlMap → chain.SqlVariants
   - CarrierEmitter: Full migration including CarrierClassInfo → CarrierPlan, ChainParameterInfo → QueryParameter, chain.Analysis.* flattening
   - InterceptorCodeGenerator: Full migration of all utility methods
   - FileEmitter: Rewritten to take TranslatedCallSite/AssembledPlan/CarrierPlan directly, build carrier lookups from passed-in CarrierPlans instead of CarrierClassBuilder
3. Phase 2b: Removed bridge layer from QuarryGenerator:
   - EmitFileInterceptorsNewPipeline now enriches AssembledPlan directly (ProjectionInfo, ReaderDelegateCode, JoinedTableInfos, TraceLines) and passes new types to FileEmitter
   - Removed EmitFileInterceptorsLegacy
   - Removed GroupByFileAndProcess (old pipeline entry point)
   - Removed GroupByFileAndProcessTranslated (intermediate bridge)
   - Removed old PipelineOrchestrator instance methods
   - Updated TokenizeCollectionParameters to use AssembledSqlVariant/QueryParameter
4. Fixed CarrierPlan.ClassName assignment: deferred to FileEmitter emission phase (Chain_0, Chain_1, etc.)

## Previous Session Completions

1-32. See git log for full history. Key milestones:
- New pipeline architecture built (Stages 1-5)
- SqlAssembler, CarrierPlan, ChainAnalyzer rewritten
- 2951/2954 tests passing before this session's migration

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: Pending verification after emitter migration (running now)
- Branch: feature/compiler-architecture
- Phase 1 (extract enums): Complete ✓
- Phase 2 (migrate emitters): Complete ✓
- Phase 3 (rewire QuarryGenerator): Partially complete (bridge removed, old dead code remains)
- Phase 4 (delete old code): Not started
- Phase 5 (fix tests): Not started

## Current State

### Emitter migration complete, tests pending
- All emitters now consume TranslatedCallSite, AssembledPlan, CarrierPlan directly
- Bridge layer fully removed from EmitFileInterceptorsNewPipeline
- Old pipeline code still exists as dead code (UsageSiteInfo, PrebuiltChainInfo, etc.) — will be deleted in Phase 4
- CarrierPlan ClassName/BaseClassName now assigned in FileEmitter during carrier class emission

### Known regressions from migration (test project fails to compile with 214 errors)
1. **Result type `?` in generated Select interceptors (~120 errors)**: The old bridge enriched Select clause sites with `projectionOverride` which fixed broken `ResultTypeName`. With the bridge removed, Select sites keep discovery-time `ResultTypeName` which is `?` when the semantic model can't resolve generated entity types. Fix: emitters should use `chain.ProjectionInfo.ResultTypeName` or `chain.ResultTypeName` instead of `site.ResultTypeName` for Select sites in chains. ClauseBodyEmitter.EmitSelect already receives the `prebuiltChain` (now `AssembledPlan`) parameter.
2. **Test code uses old types (~54 errors)**: Test files directly construct `FileEmitter`/`FileInterceptorGroup` with `UsageSiteInfo`/`PrebuiltChainInfo`. Need updating to use new types or the old test helpers need replacement with TestCallSiteBuilder/TestPlanBuilder as described in the plan.
3. **Missing entity reader fields (~7 errors)**: `_entityReader_X` fields not generated for some custom entity reader classes.
4. **CarrierField namespace collision**: `CodeGen.CarrierField` (2-arg: Name, Type) and `Models.CarrierField` (3-arg: Name, TypeName, Role) coexist. Fixed by using fully-qualified `Models.CarrierField` in CarrierPlan and CarrierAnalyzer. Will be fully resolved when `CodeGen.CarrierStrategy.cs` is deleted in Phase 4.
5. TranslatedClause.SqlFragment renders using PostgreSQL dialect with generic param format — used only for standalone clause rendering, not chain SQL.
6. JoinBodyEmitter.EmitJoin: `clauseInfo.JoinedEntityName` replaced with `InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName ?? site.EntityTypeName)` — verify for all join patterns.

## Known Issues / Bugs

- **Navigation join inferred type (2 test failures from previous session)**: JoinExecution_NavigationJoin_InferredType_TupleProjection and Traced variant. ChainId computation doesn't link fluent chains across type-changing calls. See previous handoff for full details.
- **Navigation join ON clause table aliases**: Produces `("UserId" = "UserId")` instead of `"t0"."UserId" = "t1"."UserId"` in standalone (non-chain) path. Only affects non-chain path.

## Architecture Decisions

- **Big bang over incremental**: Adapter approach was fundamentally broken. Building new system completely and switching in one shot was the only reliable path.
- **Bridge removal strategy**: Changed emitter type signatures first (mechanical), then fixed property access changes. Used parallel agents for independent emitter files.
- **CarrierPlan deferred naming**: ClassName and BaseClassName can't be determined during CarrierAnalyzer (which runs per-chain without file-level context). Deferred to FileEmitter which knows the carrier index within the file.
- **AssembledPlan convenience properties**: Added properties like QueryKind, TableName, IsJoinChain that delegate to Plan/ExecutionSite. Reduces verbosity in emitters without adding new data.
- **GetClauseEntries() method**: Replaces chain.Analysis.Clauses pattern. Builds ChainClauseEntry list with conditional bit indices computed from ConditionalTerms alignment. Called per-access (not cached) since it's used at most once per chain emission.
- **TranslatedClause.SqlFragment**: Lazy-rendered from ResolvedExpression using PostgreSQL dialect + generic param format. Matches old ClauseInfo.SqlFragment behavior. Used by emitters for standalone clause SQL rendering (non-carrier path).
- **Trace system integration**: TraceCapture data flows to AssembledPlan.TraceLines in EmitFileInterceptorsNewPipeline (same location as before, just no type conversion). FileEmitter reads TraceLines directly from AssembledPlan.

## Next Work (Priority Order)

### 1. Run tests and fix any regressions from emitter migration
Tests are currently running. Expected: most tests should pass since the emitter logic is unchanged, just consuming new types. Fix any property access mismatches or missing data.

### 2. Phase 4: Delete old pipeline code
Delete all dead code now that emitters consume new types directly:
- `Models/UsageSiteInfo.cs`, `Models/ClauseInfo.cs`, `Models/PendingClauseInfo.cs`
- `Models/ChainAnalysisResult.cs`, `Models/PrebuiltChainInfo.cs`, `Models/ChainParameterInfo.cs`
- `Models/CarrierClassInfo.cs`
- `Translation/ClauseTranslator.cs`, `Translation/ExpressionSyntaxTranslator.cs`
- `Translation/ExpressionTranslationContext.cs`, `Translation/ExpressionTranslationResult.cs`
- `Translation/SubqueryScope.cs`
- `Sql/CompileTimeSqlBuilder.cs`, `Sql/SqlFragmentTemplate.cs`
- `Generation/CarrierClassBuilder.cs`
- Old methods in QuarryGenerator.cs (BuildPrebuiltChainInfo, BuildPrebuiltChainInfoForJoin, EnrichUsageSiteWithEntityInfo, etc.)
- Old methods in UsageSiteDiscovery.cs (DiscoverUsageSite, old discovery paths)
- `CodeGen/CarrierStrategy.cs` (old carrier types)

### 3. Phase 5: Fix remaining test failures
- Fix 2 navigation join inferred type failures (from previous sessions)
- Fix any new test failures from the emitter migration
- Rewrite internal unit tests that reference deleted old types

### 4. Clean up UsageSiteDiscovery (Phase 3 remainder)
- Remove DiscoverUsageSite and old ClauseTranslator calls
- Make DiscoverRawCallSite the sole entry point
