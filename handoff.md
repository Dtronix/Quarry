# Quarry Compiler Migration Handoff

## Key Components

- New Pipeline: RawCallSite -> BoundCallSite -> TranslatedCallSite -> [Collect] -> ChainAnalyzer -> QueryPlan -> SqlAssembler -> AssembledPlan -> CarrierAnalyzer -> CarrierPlan -> FileInterceptorGroup
- Bridge Layer: EmitFileInterceptorsNewPipeline in QuarryGenerator.cs converts new types back to old (UsageSiteInfo, PrebuiltChainInfo) for emitter compatibility. Temporary until emitters consume new types directly.
- ChainId Scoping: ComputeChainId in UsageSiteDiscovery.cs uses method-scope + variable name for builder-type locals so variable-based chains merge correctly. Non-builder locals keep statement-scope.
- Trace Logging: PipelineOrchestrator.TraceLog (ThreadStatic StringBuilder) outputs __Trace files during generation.
- Projection Enrichment: StripNonAggregateSqlExpressions removes SqlExpression from non-aggregate columns for bridge compatibility.
- Subquery Support: SubqueryExpr node type in SqlExprNodes.cs handles correlated subqueries from navigation .Any()/.All()/.Count(). Parser detects subquery methods on ColumnRefExpr. Binder resolves via EntityRegistry with alias tracking (sq0, sq1...). Renderer produces EXISTS/NOT EXISTS/COUNT SQL.
- TestCapturedChains: ThreadStatic hook in ChainAnalyzer.Analyze() captures AnalyzedChain results for test verification. ChainAnalyzerTests use full generator pipeline via this hook.
- Variable-Level Disqualifiers: DetectVariableDisqualifiers in UsageSiteDiscovery walks enclosing method body to find variable references, checking for argument passing, lambda capture, and non-Quarry assignment. Flags stored on RawCallSite (IsPassedAsArgument, IsAssignedFromNonQuarryMethod).
- Join Parameter Mapping: CallSiteTranslator.ResolveJoinParameterMapping matches ColumnRefExpr parameter names to entity columns for WHERE/SELECT/OrderBy clauses after JOIN. Sets up joinedEntities and tableAliases for SqlExprBinder.
- WHERE Paren Stripping: SqlAssembler.RenderWhereCondition strips outer parens from all top-level BinaryOpExpr in WHERE/HAVING context. Tests updated to expect no redundant parens on single conditions.
- Boolean Rendering: SqlExprBinder renders boolean columns as explicit comparisons ("IsActive" = 1 for SQLite/MySQL/SS, "IsActive" = TRUE for PG). Tests updated accordingly.
- UpdateSetPoco: ChainAnalyzer builds SET terms from BoundCallSite.UpdateInfo columns. Each SET value uses ParamSlotExpr with LocalIndex=0 (standalone expression) so SqlAssembler paramBase numbers correctly in SQL output order.
- Aggregate SELECT: SqlAssembler.AppendSelectColumns renders SqlExpression with AS alias for IsAggregateFunction columns.
- Incomplete Tuple Skip: EmitFileInterceptorsNewPipeline allows chains with incomplete tuple result types through if they have a Select clause site (inline projection resolved).

## Completions (This Session)

1. 9849c50: Fix join WHERE/projection, strip redundant WHERE parens, fix boolean rendering. CallSiteTranslator resolves joined entity parameter mapping for non-join clauses. SqlAssembler strips ON clause and WHERE condition outer parens. QuarryGenerator allows assembled plans with Select clause through incomplete tuple filter. 30+ tests fixed via expectation updates.
2. efb48b4: Add UpdateSetPoco handling in ChainAnalyzer. Builds SET terms from BoundCallSite.UpdateInfo columns. 10 tests fixed.
3. 300b76f: Fix aggregate SELECT columns and HAVING paren stripping. AppendSelectColumns renders SqlExpression with AS alias for aggregates. 21 tests fixed.
4. 3d53c60: Fix SetPoco param indices. LocalIndex=0 for standalone SET values.
5. cff5a8e: Fix SetAction boolean literal dialect formatting. Detect "true"/"false" inlined values and use ClrType "bool".

## Previous Session Completions

6. b08217c: Fix join SELECT column quoting with table aliases and dialect awareness.
7. 09675d3: Fix ChainAnalyzer tests, paren stripping, BranchKind, and diagnostics.
8. d4b4eb1: Strip redundant outer parens from WHERE clauses with compound/subquery exprs.
9. 4238e0e: Fix enum constant crash in subquery predicates.
10. 133bf4c: Add subquery support for navigation property collection methods.
11. cdba77c: SqlAssembler skips trivial WHERE conditions.
12. 2576956: Schema name from entry.Context.Schema.
13. 8639e99: Insert RETURNING/OUTPUT clauses.
14. bb47e7d: INNER JOIN keyword, ON clause column resolution.
15. 4f0df05: Set clause parameter index fix.
16. 7ebc1ec: WHERE term parenthesization.
17. fe2d8ec: MySQL parameter format.
18. 6549892: Variable-based chain grouping.

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: 2878 passed, 65 failed, 1 skipped out of 2944 (97.8% pass rate)
- Session start: 2840 passed, 103 failed (96.5%)
- Improvement: +38 tests fixed this session
- Branch: feature/compiler-architecture (22 commits ahead of origin)

## Current State

65 remaining failures broken down by category:

- Conditional chains (20): ConditionalDelete/Update/Select_WithCondition* (7), Delete_ConditionalWhere_* (3), Update_Conditional* (4), Update_SetAction_*Conditional* (6). All fail with EntryPointNotFoundException -- generated interceptor not emitted for conditional chains. Bridge produces chains=0 for conditional chains.
- Insert execution (7): ExecuteNonQueryAsync_SingleUser/Order/Batch (3), ExecuteScalarAsync_Single* (2), InsertMany (1), ExecuteScalar_LogsResult (1). Insert interceptors not generated.
- SetAction/TwoArg remaining (4): Update_SetAction_ChainedWithTwoArgSet, Update_Set_TwoArg_* (2), Update_SetPoco_UpdatesMultipleColumns. Two-arg Set() pattern not handled.
- Join remaining (5): ThreeTableJoin, FourTable, Join_Where_InClause, Join_Where_OrderBy_Limit_Offset, NavigationJoin_InferredType. Multi-entity joins need CallSiteTranslator to handle 3+ entities. InClause needs runtime collection IN expansion.
- Select entity identity (5): Select_Entity_User/Order/Account. InvalidCastException in fallback Select path when no projection is specified.
- SqlRaw (4): Where_SqlRaw_*. Sql.Raw() in Where clauses not parsed by SqlExprParser -- ContainsUnsupportedRawExpr returns true and falls back to null clause.
- QRY001 diagnostics (4): Generator_LocalVariableReceiver/VariableAssignment_EmitsQRY001, QRY001_HasDescriptiveMessage/ReportsCorrectLocation. Diagnostic not emitted from new pipeline for these patterns.
- Variable-based (3): VariableBasedDelete/DeleteAll/Update. EntryPointNotFoundException -- generated interceptor not found.
- Contains (3): Where_ContainsRuntimeCollection (2), Where_NullCheck_Contains_OrderBy_Limit. Runtime collection IN expansion not supported.
- Other (3): CarrierGeneration_* (2), Where_CountSubquery_WithEnumPredicate, Select_MutuallyExclusiveOrderBy_ElseBranch.

## Next Work (Priority Order)

### 1. Fix Conditional chain dispatch (~20 failures)
Tests fail with EntryPointNotFoundException. The bridge in EmitFileInterceptorsNewPipeline produces chains=0 for conditional chains. The issue is that conditional chains need mask-based SQL variants (multiple SQL strings keyed by mask value). The SqlAssembler produces SqlVariants keyed by mask, but the bridge may not be converting them correctly to PrebuiltChainInfo. Check: (a) Are conditional chains getting AssembledPlans with multiple SqlVariants? (b) Is the bridge skipping them due to the incomplete tuple filter or other conditions? (c) Are the clause sites ConditionalInfo being preserved through the bridge? ATTEMPTED: Previous sessions fixed BranchKind detection, nesting depth, and variable disqualifiers for ChainAnalyzer tests. The issue now is in the bridge/emitter path, not in ChainAnalyzer itself.

### 2. Fix Insert execution (~7 failures)
Insert interceptors (ExecuteNonQueryAsync, ExecuteScalarAsync) not being generated. The InsertInfo columns are handled in ChainAnalyzer (insertColumns list), but the generated interceptor may not be emitted. Check if insert chains are classified as RuntimeBuild or if the bridge skips them.

### 3. Fix Select entity identity (~5 failures)
InvalidCastException in fallback Select path. When no explicit Select clause is used, the identity projection should use entity columns. The bridge BuildEntityProjectionFromEntityRef may produce incorrect projections. ATTEMPTED: Previous session added identity projection fallback but it may not handle all entity types (especially those with foreign keys).

### 4. Fix remaining categories
- SqlRaw (4 tests): Need SqlExprParser support for Sql.Raw() expressions, or a passthrough mechanism
- QRY001 diagnostics (4 tests): Bridge needs to emit QRY001 diagnostic for local variable/assignment receivers
- Variable-based operations (3 tests): Similar to conditional -- EntryPointNotFoundException
- Contains/runtime collection (3 tests): Need IN clause expansion for runtime IEnumerable parameters
- Two-arg Set (4 tests): Set(u => u.Col, value) pattern needs SqlAssembler support
- CarrierGeneration (2 tests): Carrier-related diagnostic/generation differences
- Select_MutuallyExclusiveOrderBy (1 test): Mutual exclusivity in else branch
- Where_CountSubquery_WithEnumPredicate (1 test): Enum constant renders as CapturedValueExpr
