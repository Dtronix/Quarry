# Quarry Compiler Migration Handoff

## Key Components

- New Pipeline: RawCallSite -> BoundCallSite -> TranslatedCallSite -> [Collect] -> ChainAnalyzer -> QueryPlan -> SqlAssembler -> AssembledPlan -> CarrierAnalyzer -> CarrierPlan -> FileInterceptorGroup
- Bridge Layer: EmitFileInterceptorsNewPipeline in QuarryGenerator.cs converts new types back to old (UsageSiteInfo, PrebuiltChainInfo) for emitter compatibility. Temporary until emitters consume new types directly.
- ChainId Scoping: ComputeChainId in UsageSiteDiscovery.cs uses method-scope + variable name for builder-type locals so variable-based chains merge correctly. Now includes IDeleteBuilder, IExecutableDeleteBuilder, IUpdateBuilder, IExecutableUpdateBuilder, IInsertBuilder types.
- Trace Logging: PipelineOrchestrator.TraceLog (ThreadStatic StringBuilder) outputs __Trace files during generation. DO NOT remove trace code. Additionally, the .Trace() chain method produces per-chain [Trace] comments in generated interceptors -- see llm-trace.md for usage guide.
- Projection Enrichment: StripNonAggregateSqlExpressions removes SqlExpression from non-aggregate columns for bridge compatibility.
- Subquery Support: SubqueryExpr node type in SqlExprNodes.cs handles correlated subqueries from navigation .Any()/.All()/.Count(). Parser detects subquery methods on ColumnRefExpr. Binder resolves via EntityRegistry with alias tracking (sq0, sq1...). Renderer produces EXISTS/NOT EXISTS/COUNT SQL.
- TestCapturedChains: ThreadStatic hook in ChainAnalyzer.Analyze() captures AnalyzedChain results for test verification.
- Variable-Level Disqualifiers: DetectVariableDisqualifiers in UsageSiteDiscovery walks enclosing method body to find variable references.
- Join Parameter Mapping: CallSiteTranslator.ResolveJoinParameterMapping matches ColumnRefExpr parameter names to entity columns for WHERE/SELECT/OrderBy clauses after JOIN.
- WHERE Paren Stripping: SqlAssembler.RenderWhereCondition recursively flattens AND/OR expressions and strips outer parens from comparison children. Handles OR-inside-AND precedence. Multi-term WHERE assembler wraps each term in parens when count > 1.
- Boolean Rendering: SqlExprBinder renders boolean columns as explicit comparisons ("IsActive" = 1 for SQLite/MySQL/SS, "IsActive" = TRUE for PG). Uses SqlRawExpr to avoid extra parens. In AND/OR compound expressions, boolean columns render as bare column names (inBooleanContext=false for BinaryOpExpr children).
- UpdateSetPoco: ChainAnalyzer builds SET terms from BoundCallSite.UpdateInfo columns. Each SET value uses ParamSlotExpr with LocalIndex=0. Parameters now include entityPropertyExpression for carrier extraction.
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
- Constant Array Inlining: SqlExprAnnotator.InlineConstantCollections resolves local variables and static readonly/const fields with constant array initializers to inline LiteralExpr values in InExpr. Called after AnnotateCapturedTypes in both TryParseLambdaToSqlExpr and TrySyntacticAnalysis. For local variables, uses semantic model symbol resolution with fallback to syntactic block-walking (matching old pipeline TryResolveVariableCollectionLiterals). For fields, uses IFieldSymbol.DeclaringSyntaxReferences. SqlExprClauseTranslator.ExtractParameters preserves LiteralExpr inside InExpr values (does not convert to ParamSlotExpr).
- Numeric Literal Suffix Stripping: SqlExprParser.ParseLiteral strips C# type suffixes (m/M, f/F, d/D, l/L) from numeric literal text to produce clean SQL (50.00 not 50.00m).
- Multi-Entity Join ON Resolution: RawCallSite.LambdaParameterNames stores ordered lambda parameter names for JOIN sites (ImmutableArray<string>). CallSiteTranslator.ResolveJoinOnParameterMapping uses positional mapping with combined entity list (JoinedEntities + JoinedEntity) to resolve 3+ entity JOIN ON clauses. JoinedEntityTypeNames from discovery contains only receiver entities; the emitter adds the new joined entity separately. Discovery containingType is the method receiver, NOT the return type.
- Batch Insert Detection: RawCallSite.IsBatchInsert flag, set during discovery by DetectBatchInsertInChain() walking receiver chain syntax for Values()/InsertMany(). ChainAnalyzer sets unmatchedMethodNames for batch chains, making CarrierAnalyzer mark them ineligible.
- Enum Constant Folding: SqlExprAnnotator.AnnotateCapturedTypes now collects constant values from MemberAccessExpressionSyntax nodes (e.g., OrderPriority.Urgent) via SemanticModel.GetConstantValue, converting CapturedValueExpr to LiteralExpr with the integer value. Also recurses into SubqueryExpr predicates.
- Forked Chain Detection: ChainAnalyzer detects multiple execution terminals sharing one ChainId. Extracts variable name from ChainId, creates RuntimeBuild chain with ForkedVariableName. QuarryGenerator reports QRY033 diagnostic.
- Navigation Join ON Clause: CallSiteTranslator.TranslateNavigationJoin synthesizes ON clause from entity NavigationInfo FK metadata. Detects IsNavigationJoin and builds BinaryOpExpr from parent PK and child FK columns.

## Completions (This Session)

1. cacba62: Fix batch insert carrier ineligibility — IsBatchInsert flag on RawCallSite, DetectBatchInsertInChain() in discovery, unmatchedMethodNames in ChainAnalyzer. 3 tests fixed.
2. 2029162: Add cross-dialect traced diagnostic tests (TracedFailureDiagnosticTests.cs) with .Trace() for all remaining failure patterns.
3. 450b692: Fix conditional clause bit assignment — consumedConditionalTerms tracking in ChainAnalyzer so each conditional clause gets unique bit. 1 test fixed.
4. bf7b799: Fix enum constant resolution in subquery predicates — SubqueryExpr handling in ApplyCapturedTypes, constant value collection from MemberAccessExpressionSyntax, folding to LiteralExpr. 1 test fixed.
5. f543544: Fix CarrierGeneration tests — forked chain detection (QueryPlan.ForkedVariableName, QRY033), trivial ToDiagnostics carrier ineligibility. 2 tests fixed.
6. 8401d86: Fix SetPoco carrier parameter binding — pass EntityPropertyExpression in QueryParameter for entity-sourced params. 1 test fixed.
7. 5db318f: Add navigation join ON clause resolution — TranslateNavigationJoin in CallSiteTranslator synthesizes ON from NavigationInfo FK. Partial fix.

## Previous Session Completions

1. e05b773: Fix WHERE paren stripping for inner comparisons in compound expressions.
2. cb5e34c: Fix multi-entity join issues: decimal literal suffix stripping, IN clause constant inlining.
3. 38f98f1: Add multi-entity join ON clause resolution for 3+ table joins.
4. 80f00c8: Add Contains/IN expansion for runtime collection parameters.
5. 4dfc972: Fix MySQL InsertExecuteScalar to use combined INSERT + SELECT LAST_INSERT_ID().
6. 34b45ea: Add generator tests for QRY029 Sql.Raw placeholder validation.
7. 76b2c54: Add QRY029 diagnostic for Sql.Raw template placeholder mismatches.
8. 5282512: Add Sql.Raw support with {0}/{1} C#-style template placeholders.
9. f3048fd: Fix QRY001 diagnostics and source locations in new pipeline.
10. dad08db: Fix SetAction SQL expectations after two-arg Set removal.
11. a0d35e3: Remove two-arg Set<TValue>(column, value) overload from Update API.
12. 5284f41: Fix identity Select to render explicit columns instead of SELECT *.
13. 453fbc0: Fix insert RETURNING/OUTPUT clause and per-chain carrier eligibility.
14. d99c9b5: Fix conditional chain grouping by including delete/update/insert builder types.
15. cff5a8e: Fix SetAction boolean literal dialect formatting and SetPoco param indices.
16. 3d53c60: Fix UpdateSetPoco param indices and aggregate column rendering.
17. 300b76f: Fix aggregate SELECT columns and HAVING paren stripping in SqlAssembler.
18. efb48b4: Add UpdateSetPoco handling in ChainAnalyzer.
19. 9849c50: Fix join WHERE/projection, strip redundant WHERE parens, fix boolean rendering.
20. b08217c: Fix join SELECT column quoting with table aliases and dialect awareness.
21. 09675d3: Fix ChainAnalyzer tests, paren stripping, BranchKind, and diagnostics.
22. d4b4eb1: Strip redundant outer parens from WHERE clauses with compound/subquery exprs.
23. 4238e0e: Fix enum constant crash in subquery predicates.
24. 133bf4c: Add subquery support for navigation property collection methods.
25. cdba77c: SqlAssembler skips trivial WHERE conditions.
26. 2576956: Schema name from entry.Context.Schema.
27. 8639e99: Insert RETURNING/OUTPUT clauses.
28. bb47e7d: INNER JOIN keyword, ON clause column resolution.
29. 4f0df05: Set clause parameter index fix.
30. 7ebc1ec: WHERE term parenthesization.
31. fe2d8ec: MySQL parameter format.
32. 6549892: Variable-based chain grouping.

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: 2951 passed, 2 failed, 1 skipped out of 2954 (99.9% pass rate)
- Session start: 2940 passed, 9 failed (99.7%)
- Improvement: +11 tests passed this session (+3 batch insert, +1 conditional OrderBy, +1 enum subquery, +2 carrier generation, +1 SetPoco, +3 new traced tests now pass)
- Branch: feature/compiler-architecture

## Current State

2 remaining failures — both navigation join inferred type:

- **JoinExecution_NavigationJoin_InferredType_TupleProjection_GeneratesPrebuiltSql** (InterceptorIntegrationTests.cs:2058): Integration test, navigation join `.Join(u => u.Orders)` with tuple Select projection.
- **Traced_NavigationJoin_InferredType_TupleProjection** (TracedFailureDiagnosticTests.cs): Cross-dialect traced version of the same pattern.

### What was tried:
- Added TranslateNavigationJoin in CallSiteTranslator to synthesize ON clause from NavigationInfo FK metadata. The ON clause now resolves correctly.
- **Why it's still failing**: The Join site is a standalone interceptor, not part of the chain. ChainId computation doesn't link EntityAccessor (pre-Join) → JoinedQueryBuilder (post-Join) sites because they have different receiver types. The `ComputeChainId` function keys on the variable name, but in a fluent chain like `Lite.Users().Join(u => u.Orders).Select(...)`, there's no variable — it's direct method chaining. Each call gets a different ChainId because `ComputeChainId` falls back to a site-specific ID for non-variable chains.
- Even if chain grouping worked, the tuple projection `Select((u, o) => (u.UserName, o.Total))` needs cross-entity column resolution for joined queries, which requires all sites to be in the same chain.

### What's left to resolve:
1. Fix ChainId computation to link fluent chains across type-changing calls (EntityAccessor → JoinedQueryBuilder). The explicit join test works because `.Join<Order>((u, o) => ...)` preserves the same builder type variable.
2. Ensure the joined query projection analysis resolves cross-entity tuple columns with table aliases.

## Known Issues / Bugs

- Navigation join ON clause renders without table aliases in standalone interceptor path (produces `("UserId" = "UserId")` instead of `"t0"."UserId" = "t1"."UserId"`). Only affects standalone (non-chain) path.

## Architecture Decisions

- **Big bang over incremental**: The adapter approach (converting TranslatedCallSite → UsageSiteInfo → re-enrichment) is fundamentally broken due to different data shapes, nullability contracts, and enrichment timing. Building the new system completely and switching in one shot is the only reliable path.
- **Trace-driven debugging**: Added TracedFailureDiagnosticTests.cs with cross-dialect tests using .Trace() to inspect generator output. This pattern proved highly effective for diagnosing issues.
- **Enum constant folding at annotation time**: Rather than deferring enum resolution to translation, we fold constants during AnnotateCapturedTypes using SemanticModel.GetConstantValue on MemberAccessExpressionSyntax nodes. This handles enum constants in subquery predicates where the inner lambda body is only accessible at parse time.
- **Forked chain detection via execution count**: ChainAnalyzer counts execution terminals per ChainId group. Multiple terminals → forked chain → RuntimeBuild tier with QRY033 diagnostic.
- **Batch insert carrier ineligibility**: Rather than adding Values/InsertMany as InterceptorKinds with generated interceptors, batch insert chains are made carrier-ineligible via IsBatchInsert flag + unmatchedMethodNames.

## Next Work (Priority Order)

### 1. Fix navigation join chain grouping (2 failures)
The core issue is that `ComputeChainId` in UsageSiteDiscovery doesn't link fluent chains across type-changing calls. In `Lite.Users().Join(u => u.Orders).Select(...).ToDiagnostics()`, the Join site gets a different ChainId than Select/ToDiagnostics.

**Suggested approach**: Extend `ComputeChainId` to detect when the receiver of a call is itself an invocation (fluent chain) and inherit the ChainId from the outermost call. Alternatively, walk the invocation chain syntax upward to find the root and use that for all sites in the fluent chain.

**Complication**: The receiver type changes from `IEntityAccessor<User>` to `IJoinedQueryBuilder<User, Order>`, which means the ChainId variable-name approach doesn't apply. Fluent chains need a different ChainId strategy — perhaps based on the root invocation's location.
