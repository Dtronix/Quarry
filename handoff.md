# Quarry Compiler Migration Handoff

## Key Components

- New Pipeline: RawCallSite -> BoundCallSite -> TranslatedCallSite -> [Collect] -> ChainAnalyzer -> QueryPlan -> SqlAssembler -> AssembledPlan -> CarrierAnalyzer -> CarrierPlan -> FileInterceptorGroup
- Bridge Layer: EmitFileInterceptorsNewPipeline in QuarryGenerator.cs converts new types back to old (UsageSiteInfo, PrebuiltChainInfo) for emitter compatibility. Temporary until emitters consume new types directly.
- ChainId Scoping: ComputeChainId in UsageSiteDiscovery.cs uses method-scope + variable name for builder-type locals so variable-based chains merge correctly. Now includes IDeleteBuilder, IExecutableDeleteBuilder, IUpdateBuilder, IExecutableUpdateBuilder, IInsertBuilder types.
- Trace Logging: PipelineOrchestrator.TraceLog (ThreadStatic StringBuilder) outputs __Trace files during generation.
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
- MySQL InsertExecuteScalar: CarrierAnalyzer.AnalyzeNew gates MySQL InsertExecuteScalar as carrier-ineligible (requires separate SELECT LAST_INSERT_ID() query).
- Two-Arg Set Removed: The Set<TValue>(Expression<Func<T, TValue>>, TValue) overload has been removed from IUpdateBuilder, IExecutableUpdateBuilder, UpdateBuilder, ExecutableUpdateBuilder, and ModificationCarrierBase. Only SetAction (Set(Action<T>)) and SetPoco (Set(T entity)) remain.

## Completions (This Session)

1. d99c9b5: Fix conditional chain grouping by including delete/update/insert builder types in ComputeChainId. 26 tests fixed.
2. 453fbc0: Fix insert RETURNING/OUTPUT clause and per-chain carrier eligibility. MySQL InsertExecuteScalar gate in CarrierAnalyzer.AnalyzeNew. 4 tests fixed.
3. 5284f41: Fix identity Select to render explicit columns instead of SELECT *. ChainAnalyzer enriches identity projections with entity column metadata from EntityRef. 12 tests fixed.
4. a0d35e3: Remove two-arg Set<TValue>(column, value) overload from Update API. All tests converted to SetAction or SetPoco.
5. dad08db: Fix SetAction SQL expectations (literals inlined, not parameterized).

## Previous Session Completions

6. cff5a8e: Fix SetAction boolean literal dialect formatting and SetPoco param indices.
7. 3d53c60: Fix UpdateSetPoco param indices and aggregate column rendering.
8. 300b76f: Fix aggregate SELECT columns and HAVING paren stripping in SqlAssembler.
9. efb48b4: Add UpdateSetPoco handling in ChainAnalyzer.
10. 9849c50: Fix join WHERE/projection, strip redundant WHERE parens, fix boolean rendering.
11. b08217c: Fix join SELECT column quoting with table aliases and dialect awareness.
12. 09675d3: Fix ChainAnalyzer tests, paren stripping, BranchKind, and diagnostics.
13. d4b4eb1: Strip redundant outer parens from WHERE clauses with compound/subquery exprs.
14. 4238e0e: Fix enum constant crash in subquery predicates.
15. 133bf4c: Add subquery support for navigation property collection methods.
16. cdba77c: SqlAssembler skips trivial WHERE conditions.
17. 2576956: Schema name from entry.Context.Schema.
18. 8639e99: Insert RETURNING/OUTPUT clauses.
19. bb47e7d: INNER JOIN keyword, ON clause column resolution.
20. 4f0df05: Set clause parameter index fix.
21. 7ebc1ec: WHERE term parenthesization.
22. fe2d8ec: MySQL parameter format.
23. 6549892: Variable-based chain grouping.

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: 2917 passed, 25 failed, 1 skipped out of 2943 (99.1% pass rate)
- Session start: 2878 passed, 65 failed (97.8%)
- Improvement: +39 tests fixed, 1 test removed (two-arg Set), total 2943
- Branch: feature/compiler-architecture (27 commits ahead of origin)

## Current State

25 remaining failures broken down by category:

- SqlRaw (4): Where_SqlRaw_WithCapturedVariable, Where_SqlRaw_WithColumnReference, Where_SqlRaw_WithLiteralParameter, Where_SqlRaw_WithMultipleColumnReferences. Sql.Raw() in Where clauses not parsed by SqlExprParser. ContainsUnsupportedRawExpr returns true and falls back to null clause.
- QRY001 diagnostics (4): Generator_LocalVariableReceiver_EmitsQRY001, Generator_VariableAssignment_EmitsQRY001, QRY001_HasDescriptiveMessage, QRY001_ReportsCorrectLocation. Diagnostic not emitted from new pipeline for local variable/assignment receivers.
- Join remaining (5): ThreeTableJoin_Where_DeepestTable, Join_FourTable_Select, Join_Where_InClause, Join_Where_OrderBy_Limit_Offset, NavigationJoin_InferredType. Multi-entity joins need CallSiteTranslator to handle 3+ entities. InClause needs runtime collection IN expansion.
- Contains/runtime collection (3): Where_ContainsRuntimeCollection (2), Where_NullCheck_Contains_OrderBy_Limit. Runtime collection IN expansion not supported in SqlExprParser.
- Batch insert (3): ExecuteNonQueryAsync_BatchUsers, ExecuteNonQueryAsync_InsertMany_Users, InsertMany_MultipleEntities_ExecuteNonQueryAsync_Succeeds. Values() method not tracked as InterceptorKind. Carrier throws when Values() is called unintercepted. SQL only renders single VALUES row.
- CarrierGeneration (2): CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch, CarrierGeneration_ForkedChain_EmitsDiagnostic. Carrier-related diagnostic/generation differences.
- SetPoco (1): Update_SetPoco_UpdatesMultipleColumns. Integration test -- SetPoco parameter binding issue at execution time.
- Other (3): Select_MutuallyExclusiveOrderBy_ElseBranch (mutual exclusivity in else branch), Where_CountSubquery_WithEnumPredicate (enum constant renders as CapturedValueExpr).

## Next Work (Priority Order)

### 1. Fix QRY001 diagnostics (~4 failures)
The new pipeline does not emit QRY001 for local variable receivers or variable assignments. The old pipeline detected these patterns in UsageSiteDiscovery and emitted QRY001 directly. The new pipeline needs equivalent detection in the bridge or PipelineOrchestrator. Check EmitFileInterceptorsNewPipeline for where diagnostics are reported and add QRY001 detection for non-analyzable local variable patterns.

### 2. Fix SqlRaw support (~4 failures)
Sql.Raw() expressions in Where clauses are not parsed by SqlExprParser. The parser ContainsUnsupportedRawExpr check returns true for these, causing fallback to null clause (no SQL generated). Need to add a RawSqlExpr node type to SqlExprNodes or a passthrough mechanism that embeds the raw SQL string directly in the expression tree. ATTEMPTED: This was identified in previous sessions but not yet started.

### 3. Fix Contains/runtime collection IN expansion (~3 failures)
Runtime collection parameters (e.g. .Where(u => ids.Contains(u.Id))) need IN clause expansion. The SqlExprParser does not currently handle .Contains() on IEnumerable variables. Need to detect Contains() calls on captured collection variables and generate IN (@p0, @p1, ...) with collection parameter expansion.

### 4. Fix multi-entity joins (~5 failures)
CallSiteTranslator handles 2-entity joins but not 3+ entity joins. ThreeTableJoin and FourTable tests fail because the translator does not set up tableAliases and joinedEntities for the third and fourth entities. NavigationJoin_InferredType needs tuple type inference for joined results.

### 5. Fix batch inserts (~3 failures)
Values() method is not tracked as an InterceptorKind. The carrier throws InvalidOperationException when Values() is called unintercepted. The SQL only renders a single VALUES row. Fix options: (a) make batch insert chains carrier-ineligible by detecting Values() calls during discovery, (b) add Values as an InterceptorKind and generate interceptors for it.

### 6. Fix remaining (~3 failures)
- SetPoco integration: parameter binding issue at execution time
- MutuallyExclusiveOrderBy_ElseBranch: else branch mask logic
- CountSubquery_WithEnumPredicate: enum constant rendering in subquery predicates
