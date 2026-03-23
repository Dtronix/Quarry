# Work Handoff

## Key Components

- **New Pipeline**: RawCallSite → BoundCallSite → TranslatedCallSite → [Collect] → ChainAnalyzer → QueryPlan → SqlAssembler → AssembledPlan → CarrierAnalyzer → CarrierPlan → FileInterceptorGroup
- **No Bridge Layer**: EmitFileInterceptorsNewPipeline passes new types directly to emitters.
- **FileInterceptorGroup**: Single constructor with new types only (Sites: TranslatedCallSite[], AssembledPlans, ChainMemberSites, CarrierPlans).
- **PipelineOrchestrator**: Static class. Only AnalyzeAndGroupTranslated remains.
- **CarrierPlan**: ClassName/BaseClassName assigned during emission in FileEmitter.
- **AssembledPlan**: Has convenience properties mirroring old PrebuiltChainInfo API. ProjectionInfo/TraceLines/JoinedTableInfos set by EmitFileInterceptorsNewPipeline.
- **TranslatedClause**: Has SqlFragment (lazy-rendered) and SetAssignments for SetAction.
- **TestCallSiteBuilder**: Test helper in `src/Quarry.Tests/Testing/TestCallSiteBuilder.cs` for constructing TranslatedCallSite in unit tests.
- **DiscoverRawCallSite**: Self-contained discovery method. No delegation to old DiscoverUsageSite. Parses clauses via SqlExprParser, handles SetAction directly.
- **QuarryGenerator.cs**: Reduced from ~3,000 lines to ~1,200 lines. Only contains Initialize(), EmitFileInterceptorsNewPipeline(), context/migration code, and shared utilities.

## Completions (This Session)

1. **Phase 1 — Delete dead old-pipeline methods from QuarryGenerator.cs**: Removed 25 unreachable methods (~1,800 lines) including GetUsageSiteInfo, AnalyzeExecutionChainsWithDiagnostics, BuildPrebuiltChainInfo, EnrichUsageSiteWithEntityInfo, and 21 helper methods. Also removed DiscoverAllUsageSites from UsageSiteDiscovery.cs.

2. **Phase 2 — Rewrite DiscoverRawCallSite to be self-contained**: Inlined symbol resolution, type extraction, InterceptorKind classification, and projection analysis from old DiscoverUsageSite. Added ExtractSetActionAssignments for direct SetAction handling without ClauseTranslator. Replaced DiscoverUsageSite call in DiscoverPostJoinSites with lightweight symbol check. Deleted AnalyzeClause and TrySyntacticAnalysis.

3. **Phase 3 — Delete old pipeline types and translation infrastructure (~6,400 lines)**: Deleted 16 files: UsageSiteInfo, ClauseInfo, PendingClauseInfo, ChainAnalysisResult, PrebuiltChainInfo, ChainParameterInfo, CarrierClassInfo, InterceptorMethodInfo, ClauseTranslator, ExpressionSyntaxTranslator, ExpressionTranslationContext, ExpressionTranslationResult, SubqueryScope, CompileTimeSqlBuilder, SqlFragmentTemplate, CarrierClassBuilder. Moved IsNonNullableValueType to CarrierEmitter. Made SqlExprClauseTranslator static. Extracted ParameterInfo to standalone file. Converted TryDiscoverExecutionSiteSyntactically and DiscoverRawSqlUsageSite to return RawCallSite directly.

4. **Phase 4 — Clean up test files**: Deleted 8 test files (ChainAnalyzerTests, ClauseTranslationTests, CompileTimeSqlBuilderTests, ExpressionPatternTranslationTests, ExpressionTranslationTests, TypeMappingExpressionTests, CompileTimeRuntimeEquivalenceTests, CompileTimeConverter). Fixed CrossDialectTestBase and JoinOperationsTests. Test count reduced from 2949 to 2451 (deleted tests were internal unit tests of removed pipeline code).

## Previous Session Completions

1-32. See git log for full history. Key milestones:
- New pipeline architecture built (Stages 1-5)
- SqlAssembler, CarrierPlan, ChainAnalyzer rewritten
- All emitters migrated to new types, bridge layer removed
- Navigation join forward-scan approach implemented
- All end-to-end tests passing

## Progress

- Generator build: Clean (0 errors, 0 warnings)
- Test project build: Clean (0 errors)
- Tests: 2451/2456 passing (99.8%), 0 failing, 5 skipped
- Branch: feature/compiler-architecture
- Plan steps 1–8: Complete (previous sessions)
- Plan step 9 (UsageSiteDiscovery rewrite): **Complete**
- Plan step 10 (delete old code): **Complete**
- Plan step 11 (fix tests): **Complete**
- **All plan steps complete.**

## Current State

The big-bang compiler switchover is complete. The old pipeline (UsageSiteInfo, ClauseTranslator, ExpressionSyntaxTranslator, CompileTimeSqlBuilder, etc.) has been fully removed. The new IR pipeline is the sole code path.

### What's clean
- Zero references to old types (UsageSiteInfo, ClauseInfo, PrebuiltChainInfo, etc.) in the codebase
- No adapter/conversion layers between pipeline stages
- Pipeline flows: RawCallSite → BoundCallSite → TranslatedCallSite → QueryPlan → AssembledPlan → CarrierPlan → generated source
- QuarryGenerator.cs reduced from ~3,000 to ~1,200 lines
- UsageSiteDiscovery.cs reduced from ~2,900 to ~2,500 lines

### Remaining old code in UsageSiteDiscovery.cs
- `TryDiscoverExecutionSiteSyntactically` (~100 lines): Still constructs an intermediate `RawCallSite` via syntactic-only discovery for Update/Delete execution methods where generated entity types make the entire receiver chain unresolvable. This method works correctly but could be simplified.
- `DiscoverRawSqlUsageSite` + `ResolveRawSqlTypeInfo` (~200 lines): RawSql discovery. Works correctly but still internally structured around the old pattern.

## Known Issues / Bugs

- **Runtime fallback reader delegate** (5 skipped tests): Runtime `Select()` doesn't build reader from expression tree. Only generator interceptors provide readers. This is a runtime limitation, not a generator issue.

## Technical Debt

All four original tech debt items have been resolved:

1. ~~Split responsibility for JoinedEntityTypeNames population~~ — **Resolved.** PipelineOrchestrator.PropagateChainUpdatedSites() now merges chain-updated sites back into the main array after ChainAnalyzer runs. ChainAnalyzer still synthesizes the names (it needs chain context), but the propagation is centralized.

2. ~~FileEmitter site-selection logic~~ — **Resolved.** Conditional site-preference logic removed from FileEmitter. PipelineOrchestrator handles propagation before file grouping, so FileEmitter sees consistent sites.

3. ~~SqlAssembler ResultTypeName fallback~~ — **Resolved.** Extracted duplicated fallback into `ResolveResultTypeName()` helper, computed once and reused for both RuntimeBuild and normal assembly paths.

4. ~~SetAction FormatConstantAsSqlLiteralSimple~~ — **Resolved.** Added backslash escaping via `EscapeSqlString()` helper. Unsupported types (DateTime, Guid, byte[], enums) explicitly documented as falling back to parameter binding. Boolean dialect handling confirmed to be downstream in ChainAnalyzer.

## Dependencies / Blockers

None. All plan steps are complete.

## Architecture Decisions

- **Try/catch NOT a chain disqualifier**: Quarry builder methods don't throw, so prebuilt SQL dispatch works correctly inside try/catch blocks.
- **Foreach collection expression NOT a loop**: `DetectLoopAncestor` checks whether the node is in the loop *body* vs the *collection expression*.
- **Literal pagination inlining**: The new pipeline inlines literal `Limit(10)` values directly into SQL as `LIMIT 10` instead of using a runtime parameter.
- **SetAction column quoting location**: Quoting happens in ChainAnalyzer's `ClauseKind.Set` branch, not the `InterceptorKind.UpdateSetAction` branch.
- **CarrierPlan deferred naming**: ClassName/BaseClassName assigned in FileEmitter during carrier class emission, not during CarrierAnalyzer.
- **Chain ProjectionInfo preferred over site**: Discovery-time ProjectionInfo may have `?` ResultTypeName when entity types are generator-produced. EmitSelect prefers chain's enriched ProjectionInfo.
- **Navigation join forward-scan approach**: Generator forward-scans the syntax tree from Join invocation to discover post-join method calls. Type information propagated from Join's bound data to synthetic post-join sites during chain analysis.
- **Old pipeline unit tests deleted, not rewritten**: The 498 deleted tests tested internals of the old pipeline (ClauseTranslator, ExpressionSyntaxTranslator, CompileTimeSqlBuilder, old ChainAnalyzer). These are covered by the 2451 end-to-end tests that validate generator output. If unit-level coverage of the new IR pipeline is desired, new tests should be written against SqlExprParser, SqlExprBinder, SqlExprRenderer, ChainAnalyzer.Analyze, and SqlAssembler.Assemble.
- **SqlExprClauseTranslator made static**: The instance method `Translate(PendingClauseInfo)` was dead code. Only `ExtractParametersPublic` (static) is used by CallSiteTranslator.

## Open Questions

- **Should unit tests be written for the new IR pipeline internals?** The end-to-end tests provide behavioral coverage, but unit tests for SqlExprParser, ChainAnalyzer.Analyze, SqlAssembler.Assemble etc. would improve debuggability and regression detection.

## Next Work (Priority Order)

### 1. Address technical debt items 1-3
Clean up JoinedEntityTypeNames split population, FileEmitter conditional logic, and SqlAssembler ResultTypeName fallback. These are functional but fragile.

### 2. Write unit tests for new IR pipeline internals
Create targeted tests for SqlExprParser, SqlExprBinder, SqlExprRenderer, ChainAnalyzer.Analyze, SqlAssembler.Assemble to replace the coverage lost from deleted old pipeline tests.

### 3. Fix runtime fallback reader delegate (5 skipped tests)
Requires runtime `Select()` to compile a reader delegate from the selector expression. This is a runtime change, not generator work.
