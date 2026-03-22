# Quarry Compiler Migration Handoff

## Key Components

- New Pipeline: RawCallSite -> BoundCallSite -> TranslatedCallSite -> [Collect] -> ChainAnalyzer -> QueryPlan -> SqlAssembler -> AssembledPlan -> CarrierAnalyzer -> CarrierPlan -> FileInterceptorGroup
- Bridge Layer: EmitFileInterceptorsNewPipeline in QuarryGenerator.cs converts new types back to old (UsageSiteInfo, PrebuiltChainInfo) for emitter compatibility. Temporary until emitters consume new types directly.
- ChainId Scoping: ComputeChainId in UsageSiteDiscovery.cs uses method-scope + variable name for builder-type locals so variable-based chains merge correctly. Now includes IDeleteBuilder, IExecutableDeleteBuilder, IUpdateBuilder, IExecutableUpdateBuilder, IInsertBuilder types.
- Trace Logging: PipelineOrchestrator.TraceLog (ThreadStatic StringBuilder) outputs __Trace files during generation. DO NOT remove trace code.
- Projection Enrichment: StripNonAggregateSqlExpressions removes SqlExpression from non-aggregate columns for bridge compatibility.
- Subquery Support: SubqueryExpr node type in SqlExprNodes.cs handles correlated subqueries from navigation .Any()/.All()/.Count(). Parser detects subquery methods on ColumnRefExpr. Binder resolves via EntityRegistry with alias tracking (sq0, sq1...). Renderer produces EXISTS/NOT EXISTS/COUNT SQL.
- TestCapturedChains: ThreadStatic hook in ChainAnalyzer.Analyze() captures AnalyzedChain results for test verification.
- Variable-Level Disqualifiers: DetectVariableDisqualifiers in UsageSiteDiscovery walks enclosing method body to find variable references.
- Join Parameter Mapping: CallSiteTranslator.ResolveJoinParameterMapping matches ColumnRefExpr parameter names to entity columns for WHERE/SELECT/OrderBy clauses after JOIN.
- WHERE Paren Stripping: SqlAssembler.RenderWhereCondition strips outer parens from all top-level BinaryOpExpr in WHERE/HAVING context.
- Boolean Rendering: SqlExprBinder renders boolean columns as explicit comparisons ("IsActive" = 1 for SQLite/MySQL/SS, "IsActive" = TRUE for PG).
- UpdateSetPoco: ChainAnalyzer builds SET terms from BoundCallSite.UpdateInfo columns. Each SET value uses ParamSlotExpr with LocalIndex=0.
- Aggregate SELECT: SqlAssembler.AppendSelectColumns renders SqlExpression with AS alias for IsAggregateFunction columns.
- Identity Select Enrichment: ChainAnalyzer enriches identity projections (Select(u => u)) with entity column metadata from EntityRef, producing explicit column lists instead of SELECT *. Discovery ProjectionInfo may produce wrong columns (e.g. computed properties), so entity metadata is authoritative. Chains without an explicit Select clause still use SELECT *.
- Per-Chain Carrier Eligibility: Bridge uses index-aligned CarrierPlans for per-chain eligibility instead of group-wide any-eligible check.
- Insert RETURNING: SqlAssembler only strips identity RETURNING/OUTPUT for InsertExecuteNonQuery. InsertExecuteScalar and InsertToDiagnostics retain it.
- MySQL InsertExecuteScalar: SqlAssembler appends "; SELECT LAST_INSERT_ID()" to MySQL INSERT SQL for InsertExecuteScalar and InsertToDiagnostics. Carrier-ineligible gates removed. MySQL insert scalar chains are now carrier-optimized.
- Two-Arg Set Removed: The Set<TValue>(Expression<Func<T, TValue>>, TValue) overload has been removed from IUpdateBuilder, IExecutableUpdateBuilder, UpdateBuilder, ExecutableUpdateBuilder, and ModificationCarrierBase. Only SetAction (Set(Action<T>)) and SetPoco (Set(T entity)) remain.
- Sql.Raw Support: RawCallExpr node type in SqlExprNodes.cs handles Sql.Raw<T>(template, args...) calls. Uses C#-style {0}/{1} placeholders (not @p0) to avoid confusion with dialect-specific SQL parameter syntax. Parser detects Sql.Raw in MapSqlFunction. Binder resolves column refs in arguments. Renderer substitutes placeholders with rendered args in single-pass to avoid re-substitution. Expression path remapping handles params array packaging (Arguments[1].Expressions[N]) in runtime expression trees. QRY029 diagnostic validates placeholder/argument count and sequential indices.
- Diagnostic Source Locations: EmitFileInterceptors combines CompilationProvider to reconstruct proper source locations via SyntaxTree for diagnostic reporting (Location.Create needs SyntaxTree for IsInSource=true).
- SQL Parameter Formats: SQLite uses @p0/@p1, PostgreSQL uses $1/$2 (1-based), MySQL uses ? (positional), SQL Server uses @p0/@p1.
- Contains/IN Expansion: SqlExprParser creates InExpr for collection.Contains(item) patterns. SqlExprClauseTranslator marks collection params with __CONTAINS_COLLECTION__ sentinel ExpressionPath and IsCollection=true. SqlExprAnnotator.AnnotateCapturedTypes enriches CapturedValueExpr CLR types via semantic model at parse time (called from both TryParseLambdaToSqlExpr and TrySyntacticAnalysis). CallSiteTranslator.EnrichCollectionElementTypes infers element type from InExpr operand column metadata. CarrierEmitter.EmitCarrierParamBindings handles collection extraction in join param bindings. Bridge TokenizeCollectionParameters replaces @pN with {__COL_PN__} tokens for runtime carrier expansion. New pipeline bridge in QuarryGenerator now calls TokenizeCollectionParameters before creating PrebuiltChainInfo.
- Multi-Entity Join ON Resolution: RawCallSite.LambdaParameterNames stores ordered lambda parameter names for JOIN sites (ImmutableArray<string>). CallSiteTranslator.ResolveJoinOnParameterMapping uses positional mapping with combined entity list (JoinedEntities + JoinedEntity) to resolve 3+ entity JOIN ON clauses. JoinedEntityTypeNames from discovery contains only receiver entities; the emitter adds the new joined entity separately. Discovery containingType is the method receiver, NOT the return type.

## Completions (This Session)

1. 80f00c8: Add Contains/IN expansion for runtime collection parameters. SqlExprParser creates InExpr, SqlExprClauseTranslator extracts collection params, SqlExprAnnotator enriches types, CallSiteTranslator infers element types from column metadata, CarrierEmitter handles join param bindings, bridge tokenizes collection placeholders. 2 tests fixed.
2. 38f98f1: Add multi-entity join ON clause resolution for 3+ table joins. Store ordered lambda parameter names in RawCallSite, positional entity mapping in CallSiteTranslator. 2 tests fixed.

## Previous Session Completions

3. 34b45ea: Add generator tests for QRY029 Sql.Raw placeholder validation.
4. 76b2c54: Add QRY029 diagnostic for Sql.Raw template placeholder mismatches.
5. 5282512: Add Sql.Raw support with {0}/{1} C#-style template placeholders.
6. f3048fd: Fix QRY001 diagnostics and source locations in new pipeline.
7. 4dfc972: Fix MySQL InsertExecuteScalar to use combined INSERT + SELECT LAST_INSERT_ID().
8. dad08db: Fix SetAction SQL expectations after two-arg Set removal.
9. a0d35e3: Remove two-arg Set<TValue>(column, value) overload from Update API.
10. 5284f41: Fix identity Select to render explicit columns instead of SELECT *.
11. 453fbc0: Fix insert RETURNING/OUTPUT clause and per-chain carrier eligibility.
12. d99c9b5: Fix conditional chain grouping by including delete/update/insert builder types.
13. cff5a8e: Fix SetAction boolean literal dialect formatting and SetPoco param indices.
14. 3d53c60: Fix UpdateSetPoco param indices and aggregate column rendering.
15. 300b76f: Fix aggregate SELECT columns and HAVING paren stripping in SqlAssembler.
16. efb48b4: Add UpdateSetPoco handling in ChainAnalyzer.
17. 9849c50: Fix join WHERE/projection, strip redundant WHERE parens, fix boolean rendering.
18. b08217c: Fix join SELECT column quoting with table aliases and dialect awareness.
19. 09675d3: Fix ChainAnalyzer tests, paren stripping, BranchKind, and diagnostics.
20. d4b4eb1: Strip redundant outer parens from WHERE clauses with compound/subquery exprs.
21. 4238e0e: Fix enum constant crash in subquery predicates.
22. 133bf4c: Add subquery support for navigation property collection methods.
23. cdba77c: SqlAssembler skips trivial WHERE conditions.
24. 2576956: Schema name from entry.Context.Schema.
25. 8639e99: Insert RETURNING/OUTPUT clauses.
26. bb47e7d: INNER JOIN keyword, ON clause column resolution.
27. 4f0df05: Set clause parameter index fix.
28. 7ebc1ec: WHERE term parenthesization.
29. fe2d8ec: MySQL parameter format.
30. 6549892: Variable-based chain grouping.

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: 2934 passed, 13 failed, 1 skipped out of 2947 (99.6% pass rate)
- Session start: 2929 passed, 17 failed (99.4%)
- Improvement: +5 tests fixed this session (+2 Contains/IN, +2 multi-entity join ON, net -1 NullCheck_Contains reclassified as paren issue not Contains)
- Branch: feature/compiler-architecture (34 commits ahead of origin)

## Current State

13 remaining failures broken down by category:

- Multi-entity joins (4): ThreeTableJoin_Where_DeepestTable, Join_Where_InClause, Join_Where_OrderBy_Limit_Offset, NavigationJoin_InferredType.
  ATTEMPTED: ResolveJoinOnParameterMapping with ordered lambda parameter names fixes JOIN ON clause for 3+ entities. Join_ThreeTable_Select and Join_FourTable_Select now pass. ThreeTableJoin_Where_DeepestTable: the WHERE site after 3 joins uses ResolveJoinParameterMapping (non-Join path). The BoundCallSite.JoinedEntities for the WHERE site comes from the receiver builder type (IJoinedQueryBuilder3) which DOES have 3 entities. The issue is likely the WHERE clause SQL rendering: inner comparison parens (see WHERE paren stripping below). Join_Where_InClause: needs join + Contains/IN combination working together. Join_Where_OrderBy_Limit_Offset: inner comparison parens. NavigationJoin_InferredType: tuple type inference for joined result.

- Batch insert (3): ExecuteNonQueryAsync_BatchUsers, ExecuteNonQueryAsync_InsertMany_Users, InsertMany_MultipleEntities_ExecuteNonQueryAsync_Succeeds. Values() method not tracked as InterceptorKind.
  NOT YET ATTEMPTED.

- CarrierGeneration (2): CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch, CarrierGeneration_ForkedChain_EmitsDiagnostic. Carrier-related diagnostic/generation differences.
  NOT YET ATTEMPTED.

- WHERE paren stripping (2): Where_NullCheck_Contains_OrderBy_Limit, Join_Where_OrderBy_Limit_Offset. The SqlExprRenderer.RenderBinary always wraps BinaryOpExpr in parens. The old pipeline's ExpressionSyntaxTranslator rendered comparisons WITHOUT parens. SqlAssembler.RenderWhereCondition strips the outermost paren level, but inner operand comparisons still have parens. Expected: "t1"."Total" > 100 AND "t0"."IsActive" = 1. Actual: ("t1"."Total" > 100) AND "t0"."IsActive" = 1. For WHERE NullCheck_Contains, expected outer parens around AND but SqlAssembler strips them.
  ATTEMPTED AND REVERTED for NullCheck_Contains: Changed RenderWhereCondition to keep AND/OR outer parens, but this broke 5 subquery tests (Where_Any_And_Boolean, Where_Any_Or_Boolean, Where_Boolean_Subquery_OrderBy_Select, Where_Any_And_All_MultipleSubqueries, Where_Multiple_Subqueries_Alias_Monotonicity). All subquery tests expect stripped AND parens. Reverted. Root cause: renderer always wraps BinaryOpExpr in parens. Fix needs: either (a) make RenderBinary not add parens for simple comparisons when in WHERE context, or (b) post-process to strip inner comparison parens from rendered WHERE SQL.

- SetPoco (1): Update_SetPoco_UpdatesMultipleColumns. Integration test, SetPoco parameter binding issue at execution time.
  NOT YET ATTEMPTED.

- MutuallyExclusiveOrderBy (1): Select_MutuallyExclusiveOrderBy_ElseBranch. Else branch mask logic in ChainAnalyzer conditional handling.
  NOT YET ATTEMPTED.

- CountSubquery enum (1): Where_CountSubquery_WithEnumPredicate. Enum constant renders as CapturedValueExpr instead of integer literal in subquery predicates.
  NOT YET ATTEMPTED.

## Next Work (Priority Order)

### 1. Fix WHERE paren stripping (2 failures + helps multi-join tests)
The renderer always wraps BinaryOpExpr in parens. For WHERE context, inner comparison parens need to be stripped. Two sub-issues:
(a) Inner comparison parens: WHERE ("col" > 100) AND ... should be WHERE "col" > 100 AND ...
(b) Outer AND parens for non-subquery cases: WHERE "Email" IS NOT NULL AND ... should be WHERE ("Email" IS NOT NULL AND ...)
Approach: Add a `stripChildBinaryParens` parameter to `RenderBinary` or `Render`. When rendering a WHERE condition that is AND/OR, strip parens from the direct child binary operands (comparisons). For subquery operands, keep them as-is.
NOT YET ATTEMPTED with this approach.

### 2. Fix remaining multi-entity join issues (4 failures)
- ThreeTableJoin_Where_DeepestTable: After fixing WHERE parens, verify this works.
- Join_Where_InClause: After fixing WHERE parens and verifying Contains in join context.
- Join_Where_OrderBy_Limit_Offset: Depends on WHERE paren fix.
- NavigationJoin_InferredType: Needs tuple type inference for joined result. The navigation join (.Join(u => u.Orders)) uses syntactic type resolution where the semantic model cannot determine the joined entity type. InterceptorIntegrationTests.cs line 2058-2072.

### 3. Fix batch inserts (3 failures)
Values() method not tracked as InterceptorKind. Options: (a) make batch insert chains carrier-ineligible by detecting Values() calls during discovery, (b) add Values as an InterceptorKind and generate interceptors.
NOT YET ATTEMPTED.

### 4. Fix remaining 5 failures
- SetPoco: Update_SetPoco_UpdatesMultipleColumns.
- MutuallyExclusiveOrderBy_ElseBranch: else branch mask logic.
- CountSubquery_WithEnumPredicate: enum constant as CapturedValueExpr.
- CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch.
- CarrierGeneration_ForkedChain_EmitsDiagnostic.
