# Quarry Generator: Compiler Architecture Completion Plan

## 1. Executive Summary

The Quarry source generator's codegen layer has been fully restructured. `FileEmitter` orchestrates per-file output, routing each call site through `InterceptorRouter` to one of six focused body emitters (`RawSqlBodyEmitter`, `TransitionBodyEmitter`, `ClauseBodyEmitter`, `JoinBodyEmitter`, `TerminalBodyEmitter`, `CarrierEmitter`). These emitters are live and producing all interceptor output today. The old `InterceptorCodeGenerator` partial files (`.Query.cs`, `.Execution.cs`, `.Joins.cs`, `.Modifications.cs`, `.RawSql.cs`, `.Carrier.cs`) have been deleted; only the slim entry-point shell and `.Utilities.cs` remain. On the IR side, all new types have been built and tested: `SqlExpr` (with `SqlExprParser`, `SqlExprAnnotator`, `SqlExprBinder`, `SqlExprRenderer`), layered call sites (`RawCallSite`, `BoundCallSite`, `TranslatedCallSite`), `QueryPlan`, `AssembledPlan`, `EntityRegistry`, and `FileOutputGroup`. However, none of the new IR types are wired into the live pipeline. The old types -- `UsageSiteInfo` (519 lines, 25+ nullable properties), `PrebuiltChainInfo`, `ClauseInfo`, `CarrierClassInfo` -- still drive every stage from discovery through codegen. The dual expression translators (`ExpressionSyntaxTranslator` and `ClauseTranslator` with its syntactic fallback path) remain the primary translation route. `CompileTimeSqlBuilder` still assembles SQL. `ChainAnalyzer` still operates on `UsageSiteInfo`. The `.Collect()` bottleneck in `QuarryGenerator.GroupByFileAndProcess()` still collapses all sites into a single incremental node.

Three phases of work remain. Phase 4 (Pipeline Restructure) rewires `QuarryGenerator.Initialize()` so that per-site binding and translation happen before `.Collect()`, enabling per-site incremental caching. This is the phase that delivers the performance improvement: editing one `.Where()` lambda will no longer re-translate every query site in the compilation. Phase 5 (Chain Analysis + SQL Assembly + Carrier Redesign) replaces `ChainAnalyzer`'s dependency on `UsageSiteInfo` with `TranslatedCallSite`, introduces `QueryPlan`-based SQL assembly via a new `SqlAssembler` (replacing `CompileTimeSqlBuilder`), and consolidates carrier eligibility logic into `CarrierAnalyzer` consuming `AssembledPlan`. Phase 6B (Codegen Completion) updates the body emitters to consume `TranslatedCallSite` and `AssembledPlan` directly instead of `UsageSiteInfo` and `PrebuiltChainInfo`, then deletes all old types.

The end state is a clean compiler pipeline: `RawCallSite` (discovery) -> `BoundCallSite` (binding) -> `TranslatedCallSite` (translation) -> `QueryPlan` (chain analysis) -> `AssembledPlan` (SQL assembly) -> `CarrierPlan` (carrier optimization) -> generated C# source. Each stage is a pure transform with well-typed input and output. Per-site incremental caching eliminates the O(N) re-enrichment on every keystroke. No god objects, no dual translators, no `.Collect()` bottleneck on enrichment. The old types (`UsageSiteInfo`, `PrebuiltChainInfo`, `ClauseInfo`, `CarrierClassInfo`, `PendingClauseInfo`, `ChainParameterInfo`, `SyntacticExpression`) are deleted entirely.

Scope: approximately 13,100 lines of old pipeline code to be replaced or deleted across ~20 files (`QuarryGenerator.cs`, `UsageSiteDiscovery.cs`, `ExpressionSyntaxTranslator.cs`, `ClauseTranslator.cs`, `ChainAnalyzer.cs`, `CompileTimeSqlBuilder.cs`, `CarrierClassBuilder.cs`, and the old model types). The new IR and CodeGen files already total ~9,000 lines and will grow by an estimated 1,500-2,500 lines as the pipeline wiring and adapter removal are completed. Net line count is expected to decrease as dead code and duplication are eliminated.

---

## 2. Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Old translator fate | Delete entirely | `SqlExpr` handles 100% of expression translation. No fallback path retained. `ExpressionSyntaxTranslator`, `SyntacticExpressionParser`, and `SyntacticClauseTranslator` are all removed. |
| Translation failure policy | Skip + diagnostic | When `SqlExprBinder` or `SqlExprClauseTranslator` cannot translate a clause, the site is skipped with a QRY019 diagnostic. The runtime `SqlBuilder` method runs instead. No regression from current behavior. |
| Navigation join timing | Phase 4 (binding) | `CallSiteBinder` resolves navigation joins via `SelectMany` on the entity metadata. Implicit join sites are synthesized during binding rather than discovered in a separate post-enrichment pass. Pipeline stays clean. |
| ChainAnalyzer approach | Rewrite to produce `QueryPlan` | Single pass over `TranslatedCallSite` chain members, producing `QueryPlan` directly. No intermediate `ChainAnalysisResult` adapter. Cleanest architecture with fewest conversion steps. |
| SQL assembly approach | New `SqlAssembler` (clean-room) | Renders SQL from `QueryPlan` via `SqlExprRenderer`. `CompileTimeSqlBuilder` deleted. The new assembler consumes `QueryPlan` terms and conditional bitmask metadata without touching `ClauseInfo` or `PrebuiltChainInfo`. |
| `CarrierPlan` timing | Phase 5 | Natural fit alongside carrier redesign. `CarrierAnalyzer` already exists and consumes the old types; updating it to consume `AssembledPlan` avoids double churn. |
| Emitter API strategy | New types flow end-to-end | `TranslatedCallSite`, `AssembledPlan`, and `CarrierPlan` are consumed directly by emitters. No adapter layer between new IR and codegen. Old types are removed, not wrapped. |
| Emitter migration order | One at a time, smallest first | RawSql -> Transition -> Clause -> Join -> Terminal -> FileEmitter. Each commit leaves tests green. This order is already complete for Phase 6A. |
| Test migration | Alongside emitter migration | Each emitter step migrates its tests. No separate test phase. Existing tests validate behavioral equivalence continuously. |
| Old type cleanup | Delete entirely | `UsageSiteInfo`, `PrebuiltChainInfo`, `ClauseInfo` variants, `CarrierClassInfo`, `PendingClauseInfo`, `ChainParameterInfo` all removed. Zero dead code in the final state. |
| `CarrierClassInfo` fate | Replace with `CarrierPlan` | `CarrierStrategy` (already built) captures eligibility and field layout. `CarrierPlan` extends this with the full carrier optimization decision. Better long-term flexibility for struct carriers or pooled carriers. |

---

## 3. Phase Dependency Order

1. **Phase 4: Pipeline Restructure** -- Rewires `QuarryGenerator.Initialize()` to perform per-site binding (`CallSiteBinder`) and translation (`CallSiteTranslator`) before `.Collect()`, using `EntityRegistry` as the shared metadata source. No prior phase dependency beyond the already-completed IR types and codegen emitters.

2. **Phase 5: Chain Analysis + SQL Assembly + Carrier Redesign** -- Replaces `ChainAnalyzer`'s `UsageSiteInfo` input with `TranslatedCallSite`, introduces `SqlAssembler` to produce `AssembledPlan` from `QueryPlan`, and updates `CarrierAnalyzer` to consume `AssembledPlan`. Depends on Phase 4 (translated call sites must be flowing through the pipeline).

3. **Phase 6B: Codegen Completion** -- Updates all body emitters to consume `TranslatedCallSite` and `AssembledPlan` directly, removes all adapter code, and deletes the old types (`UsageSiteInfo`, `PrebuiltChainInfo`, `ClauseInfo`, `CarrierClassInfo`). Depends on Phase 5 (assembled plans must be available for emitter consumption).

---

## 4. Constraints

- **netstandard2.0 target**: No `record` types, no `init` properties, no `Span<T>`, no `required` members. All IR types are sealed classes with manual `IEquatable<T>`.
- **Roslyn 5.0 API surface**: Pipeline primitives limited to `CreateSyntaxProvider`, `Combine`, `Collect`, `SelectMany`, `Select`, `RegisterSourceOutput`.
- **All types must implement `IEquatable<T>`**: The incremental pipeline uses equality to detect cache hits. Every type flowing through the pipeline must have correct, deterministic `Equals()` and `GetHashCode()`. Fields compared in order of most likely to differ first.
- **Transforms must be pure functions**: `Select` and `SelectMany` transforms must be deterministic. No mutable shared state across invocations.
- **No `SyntaxNode` in cached values**: Roslyn syntax nodes hold references to the full syntax tree. Storing them in pipeline values prevents GC and causes unbounded memory growth. Call site locations are stored as value types (`DiagnosticLocation`).

---

## 5. Test Strategy

- **All 2,929 existing tests must pass after each phase.** No phase is allowed to break any existing test, even transiently between commits within that phase. Every commit is green.
- **Generated output must be byte-identical** for non-subquery cases (behavioral equivalence). The existing test suite validates end-to-end correctness by comparing generated interceptor source against approved baselines. Any change to generated output requires explicit baseline update with justification.
- **New unit tests for new pipeline components.** `CallSiteBinder`, `CallSiteTranslator` (the pipeline wiring around `SqlExprClauseTranslator`), `SqlAssembler`, and `CarrierPlan` each get dedicated unit tests verifying their transform logic in isolation. The 40 IR pipeline stress tests already committed cover `SqlExpr` round-tripping and raw SQL string verification.
- **Each commit within a phase must leave tests green.** Adapter patterns are used at phase boundaries: when wiring new types upstream, temporary adapters convert back to old types for downstream consumers that have not yet been migrated. Adapters are removed only after their downstream consumers are updated. No commit introduces a half-wired state.

---

## 6. Phase 4: Pipeline Restructure

### Objective

Phase 4 eliminates the `.Collect()` bottleneck by moving per-site binding and translation into individually-cached incremental pipeline stages that execute before collection. After this phase, editing a single query expression only re-binds and re-translates that one call site; all other sites return their cached `TranslatedCallSite` values. The `.Collect()` call remains but operates on fully-translated sites where the expensive work (entity lookup, column resolution, SqlExpr binding, SQL rendering, parameter extraction) has already been done, leaving only chain analysis and file grouping in the collected transform.

### New Pipeline Architecture

The restructured pipeline has five incremental stages. Each stage produces an output type with a correct `IEquatable<T>` implementation so the Roslyn incremental driver can detect when a stage's output has not changed and skip downstream re-execution.

**Stage 1: Context Discovery**

- Input: `ClassDeclarationSyntax` nodes with `[QuarryContext]` attribute (via `CreateSyntaxProvider`).
- Output: `ContextInfo`.
- Caching: Per-context. Each `ContextInfo` is independently cached by the incremental driver. Changing one context class does not invalidate others.
- Re-executes when: The source text of a class with `[QuarryContext]` changes.
- This stage is unchanged from the current pipeline. The `EntityRegistry` is derived from collected `ContextInfo` values via `contextDeclarations.Collect().Select(EntityRegistry.Build)`. Because schema classes change infrequently compared to query code, this collected value is effectively stable during normal development and serves as the shared binding context for all call sites.

**Stage 2: Raw Call Site Discovery**

- Input: `InvocationExpressionSyntax` nodes matching Quarry method names (via `CreateSyntaxProvider`).
- Output: `RawCallSite`.
- Caching: Per-site. Each `RawCallSite` is individually cached by its content equality (which prioritizes `UniqueId`, derived from file path, line, column, method name).
- Re-executes when: The syntax of the invocation expression changes (lambda body edited, arguments changed, method name changed) or when the file is re-parsed.
- This stage replaces `GetUsageSiteInfo` returning `UsageSiteInfo`. The critical difference is that `RawCallSite` contains a parsed `SqlExpr` tree (from `SqlExprParser.ParseWithPathTracking`) but no semantic-model-derived clause translation. No `ClauseTranslator` calls occur during discovery. No `PendingClauseInfo` wrapping is needed because the `SqlExpr` is stored directly on `RawCallSite.Expression`.

**Stage 3: Per-Site Binding**

- Input: `(RawCallSite, EntityRegistry)` via `.Combine(entityRegistry)`.
- Output: `ImmutableArray<BoundCallSite>` (one or more per raw site, via `SelectMany`).
- Caching: Per-site. The incremental driver caches each `(RawCallSite, EntityRegistry)` pair. If neither the raw site nor the entity registry has changed, the bound result is reused without re-executing the transform.
- Re-executes when: The `RawCallSite` changes (query code edited) OR the `EntityRegistry` changes (schema class edited -- rare during typical development).
- This stage replaces `EnrichUsageSiteWithEntityInfo`. It resolves the entity type against the registry, populates table name, schema name, dialect, `InsertInfo`, `UpdateInfo`, `EntityRef`, and joined entity metadata. Navigation joins produce additional `BoundCallSite` entries via `SelectMany` (see Step 4.6), replacing the separate `DiscoverNavigationJoinChainMembers` post-enrichment pass.

**Stage 4: Per-Site Translation**

- Input: `BoundCallSite`.
- Output: `TranslatedCallSite`.
- Caching: Per-site. Each `BoundCallSite` is individually cached. If the bound site has not changed, translation is skipped entirely.
- Re-executes when: The `BoundCallSite` changes, which happens when either the raw site or the entity registry changes.
- This stage replaces `TranslatePendingClause` and the `SqlExprClauseTranslator` fallback path inside `EnrichUsageSiteWithEntityInfo`. It runs `SqlExprBinder.Bind` for column resolution, `SqlExprClauseTranslator.ExtractParameters` for parameter extraction, and `SqlExprRenderer.Render` for SQL generation. Sites that cannot be translated produce a `TranslatedCallSite` with `Clause = null`; a QRY019 diagnostic is reported in the collected stage.

**Stage 5: Collected Analysis**

- Input: `(Compilation, EntityRegistry, ImmutableArray<TranslatedCallSite>)` via `.Collect()` on translated sites.
- Output: `ImmutableArray<FileInterceptorGroup>` via `SelectMany`.
- Caching: The entire collected array is compared. If any single `TranslatedCallSite` in the array differs, this stage re-executes. However, because Stages 3--4 handle the expensive per-site work, this stage only performs chain analysis, diagnostic collection, and file grouping -- all lightweight operations.
- Re-executes when: Any translated site changes or the compilation reference changes.
- This stage replaces `GroupByFileAndProcess` but does significantly less work. It no longer calls `EnrichUsageSiteWithEntityInfo`, `TranslatePendingClause`, or `DiscoverNavigationJoinChainMembers`. It retains chain analysis (`ChainAnalyzer`), diagnostic reporting (QRY005, QRY006, QRY015, QRY016, QRY019), and file grouping into `FileInterceptorGroup` values.

### Step 4.1: Convert Discovery to RawCallSite

`UsageSiteDiscovery.DiscoverUsageSite` currently returns `UsageSiteInfo` with up to 25 populated fields, including translated `ClauseInfo` from semantic analysis and `PendingClauseInfo` from the syntactic fallback. This step changes its return type to `RawCallSite` and strips out all enrichment and translation logic that occurs during discovery.

**Method signature change:**

```csharp
// BEFORE
public static UsageSiteInfo? DiscoverUsageSite(
    InvocationExpressionSyntax invocation,
    SemanticModel semanticModel,
    CancellationToken ct)

// AFTER
public static RawCallSite? DiscoverUsageSite(
    InvocationExpressionSyntax invocation,
    SemanticModel semanticModel,
    CancellationToken ct)
```

**What gets removed from discovery:**

- All `ClauseTranslator.TranslateWhere`, `TranslateOrderBy`, `TranslateGroupBy`, `TranslateHaving`, `TranslateSet`, `TranslateSetAction` calls in the `AnalyzeClause` method (lines 721--730 of `UsageSiteDiscovery.cs`). These semantic-model-based translation attempts are deleted entirely because the SqlExpr pipeline handles 100% of translation in Stage 4.
- The `TrySyntacticAnalysis` fallback path (lines 755--809) that wraps a parsed `SqlExpr` in `PendingClauseInfo`. The `PendingClauseInfo` type becomes unnecessary because `RawCallSite.Expression` holds the `SqlExpr` directly.
- The `AnalyzeClause` method itself is simplified: instead of attempting translation via `ClauseTranslator` and falling back to syntactic analysis, it parses the lambda to `SqlExpr` via `SqlExprParser.ParseWithPathTracking()` and stores the result on `RawCallSite.Expression`. The `ClauseKind` is derived from the `InterceptorKind` mapping (the same switch logic in the current `TrySyntacticAnalysis`, lines 790--803) and stored on `RawCallSite.ClauseKind`.
- The `ClauseTranslator.TranslateJoin` call for join sites (line 521). Join condition parsing still happens syntactically during discovery (the ON condition lambda is parsed to `SqlExpr`), but column resolution and SQL rendering are deferred to binding and translation.

**The key change in the clause analysis path:**

The `AnalyzeClause` method currently returns `(ClauseInfo?, PendingClauseInfo?)` where one of the two is non-null. After this step, it is simplified to populate `SqlExpr` and `ClauseKind` fields directly. The method can be inlined into the main discovery flow or retained as a helper returning `(SqlExpr?, ClauseKind?, bool isDescending)`:

```csharp
// AnalyzeClause now:
//   1. Extract lambda body and parameter names
//   2. Call SqlExprParser.ParseWithPathTracking(body, parameterNames)
//   3. Return (sqlExpr, clauseKind, isDescending)
//   No translation attempt, no PendingClauseInfo wrapper
```

`SqlExprParser.ParseWithPathTracking()` is already called in the current `TrySyntacticAnalysis` fallback path (line 778). The change is that this becomes the only parsing path, not a fallback. It is called unconditionally for all clause-bearing sites (Where, OrderBy, GroupBy, Having, Set, Join conditions).

**What remains in discovery unchanged:**

- Syntax filtering (`IsQuarryMethodCandidate`) -- the predicate is unchanged.
- Method symbol resolution via semantic model -- unchanged. This is needed to determine `InterceptorKind`, `BuilderKind`, entity type name, result type name, and joined entity type names.
- Interceptable location resolution (`GetInterceptableLocation`) -- unchanged.
- `UniqueId` generation from file path, line, column, method name -- unchanged.
- `ProjectionInfo` analysis for Select sites -- unchanged, this is syntactic analysis that does not depend on entity metadata.
- Navigation join detection (`IsNavigationJoin` flag) -- unchanged.
- `ConstantIntValue` extraction for Limit/Offset sites -- unchanged.

**SyntaxNode references:** The `SyntaxNode invocationSyntax` field on `UsageSiteInfo` is NOT carried to `RawCallSite`. Storing `SyntaxNode` references in cached pipeline values prevents garbage collection and causes unbounded memory growth. The `RawCallSite` stores only value-type location data (`FilePath`, `Line`, `Column`, `InterceptableLocationData`, `DiagnosticLocation`). Any downstream logic that needs the original syntax node must either operate on the `DiagnosticLocation` value or obtain it from the compilation in the collected stage where `SyntaxNode` references are short-lived.

### Step 4.2: Create CallSiteBinder

`CallSiteBinder` is a new static class in the `IR` namespace that resolves a `RawCallSite` against the `EntityRegistry` to produce one or more `BoundCallSite` values.

**File:** `src/Quarry.Generator/IR/CallSiteBinder.cs`

**Primary method signature:**

```csharp
internal static class CallSiteBinder
{
    /// <summary>
    /// Binds a raw call site against the entity registry to produce bound call sites.
    /// Returns one element for most sites; may return multiple for navigation joins
    /// that discover additional chain members.
    /// </summary>
    public static ImmutableArray<BoundCallSite> Bind(
        RawCallSite raw,
        EntityRegistry registry,
        CancellationToken ct)
}
```

The return type is `ImmutableArray<BoundCallSite>` rather than a single `BoundCallSite` because navigation join resolution can produce additional bound sites for chain members discovered during binding (see Step 4.6). For non-navigation-join sites, the array contains exactly one element. The pipeline uses `SelectMany` to flatten these arrays into the values stream.

**Algorithm:**

1. **Entity resolution.** Call `registry.Resolve(raw.EntityTypeName, raw.ContextClassName, out bool isAmbiguous)` to find the `EntityRegistryEntry`. If resolution fails (entity type not found in any context), return an array containing a single `BoundCallSite` with a null `Entity` -- this site will be skipped during translation and marked non-analyzable. If the resolution is ambiguous (multiple contexts, no `ContextClassName` to disambiguate), the first entry is used (matching current first-writer-wins behavior) and the ambiguity flag is set on the `BoundCallSite` for QRY015 diagnostic reporting in Stage 5.

2. **Context binding.** Extract `ContextClassName`, `ContextNamespace`, `Dialect` from the resolved `EntityRegistryEntry`. If the `RawCallSite` already has `ContextClassName` and `ContextNamespace` (populated during discovery for chain root sites), prefer those values; otherwise populate from the registry entry. Extract `TableName` and `SchemaName` from the resolved entity.

3. **EntityRef construction.** Build an `EntityRef` from the resolved `EntityInfo` via `EntityRef.FromEntityInfo(entry.Entity)`. The `EntityRef` carries column metadata and navigation properties needed for translation without the full weight of `EntityInfo` (which includes `Location` and other heavy fields not needed post-binding).

4. **InsertInfo construction.** For insert-related sites (`InsertExecuteNonQuery`, `InsertExecuteScalar`, `InsertToDiagnostics`), call `InsertInfo.FromEntityInfo(entity, dialect, raw.InitializedPropertyNames)` to build insert column metadata. This logic moves from `EnrichUsageSiteWithEntityInfo` lines 1300--1307 of `QuarryGenerator.cs`.

5. **UpdateInfo construction.** For `UpdateSetPoco` sites, call `InsertInfo.FromEntityInfo(entity, dialect, raw.InitializedPropertyNames)` to build update POCO column metadata. This logic moves from lines 1310--1317 of `QuarryGenerator.cs`.

6. **Joined entity resolution.** For join sites (`Join`, `LeftJoin`, `RightJoin`) with a `JoinedEntityTypeName`, resolve the joined entity from the registry via `registry.Resolve(raw.JoinedEntityTypeName)`. Build a `JoinedEntity` `EntityRef` from the result. For navigation joins where the joined entity type name is unresolved (generic type parameter not yet available to the semantic model), call `ResolveNavigationJoinEntityType` to look up the navigation property in the primary entity's `NavigationInfo` list and find the `RelatedEntityName`. This logic moves from `QuarryGenerator.ResolveNavigationJoinEntityType` (line 1643).

7. **RawSqlTypeInfo enrichment.** For `RawSqlAsync` and `RawSqlScalarAsync` sites where `RawSqlTypeInfo.TypeKind == Dto`, check if the DTO type matches a known entity in the registry and enrich with entity column metadata. This logic moves from `QuarryGenerator.EnrichRawSqlTypeInfoWithEntity`.

8. **Navigation join chain member discovery.** If `raw.IsNavigationJoin` is true and the joined entity was successfully resolved, produce additional `BoundCallSite` entries for downstream chain members. See Step 4.6 for the detailed algorithm.

**What moves here from `EnrichUsageSiteWithEntityInfo`:**

- Entity registry lookup and context resolution (lines 1258--1263)
- InsertInfo construction (lines 1300--1307)
- UpdateInfo construction (lines 1310--1317)
- Join entity resolution and navigation join entity type resolution (lines 1320--1343)
- RawSql type enrichment (lines 1404--1408)
- Context class/namespace assignment from resolved entry (lines 1424--1425)

**What does NOT move here (deferred to `CallSiteTranslator`):**

- `TranslatePendingClause` (clause translation)
- `TryTranslateJoinClause` and `TryTranslateJoinedClause` (join condition SQL rendering)
- `EnrichProjectionWithEntityInfo` (projection column enrichment)
- `EnrichSetClauseWithMapping` and `EnrichSetActionClauseWithMapping` (Set type mapping)
- `ResolveSetValueTypeFromEntity` (Set value type resolution)
- KeyTypeName/ValueTypeName resolution

### Step 4.3: Create CallSiteTranslator

`CallSiteTranslator` is a new static class in the `IR` namespace that translates a `BoundCallSite` into a `TranslatedCallSite` by running the SqlExpr pipeline (bind columns, extract parameters, render SQL) and resolving clause-specific metadata.

**File:** `src/Quarry.Generator/IR/CallSiteTranslator.cs`

**Primary method signature:**

```csharp
internal static class CallSiteTranslator
{
    /// <summary>
    /// Translates a bound call site into a fully-translated call site
    /// with resolved SQL expression, parameters, and type metadata.
    /// </summary>
    public static TranslatedCallSite Translate(
        BoundCallSite bound,
        CancellationToken ct)
}
```

**Algorithm:**

1. **Non-clause sites.** If the site does not carry a clause expression (`bound.Raw.Expression == null` and the site's `InterceptorKind` is not a clause-bearing method), return a `TranslatedCallSite` with `Clause = null`. This covers Limit, Offset, Distinct, WithTimeout, chain roots, execution terminals, and insert/delete transitions. These sites carry their semantics in the `InterceptorKind` and `ConstantIntValue` fields and need no SQL translation.

2. **Projection enrichment.** For Select sites with `bound.Raw.ProjectionInfo != null`, call `EnrichProjectionWithEntityInfo` using the resolved `EntityRef` column metadata and dialect. This populates correct column names, CLR types, custom type mappings, reader method names, and foreign key metadata from the entity schema. For entity-kind projections where discovery produced zero columns (because the entity type is generated by this same source generator), rebuild the full column list from `EntityRef.Columns`. This logic moves from `QuarryGenerator.EnrichProjectionWithEntityInfo` (lines 1820--1880+). The enriched `ProjectionInfo` is stored on the `TranslatedCallSite`.

3. **SqlExpr binding.** Call `SqlExprBinder.Bind(expression, entityInfo, dialect, lambdaParameterName, inBooleanContext)` where:
   - `expression` is `bound.Raw.Expression`
   - `entityInfo` is reconstructed from `bound.Entity` columns
   - `dialect` is `bound.Dialect`
   - `lambdaParameterName` is the first lambda parameter name, derivable from `ColumnRefExpr` nodes in the `SqlExpr` tree or stored during parsing
   - `inBooleanContext` is `true` for Where and Having clause kinds

   For join clause sites with multiple entities (methods on `IJoinedQueryBuilder`), the binder handles multi-entity column resolution using all entity metadata from `BoundCallSite.Entity`, `BoundCallSite.JoinedEntity`, and `BoundCallSite.JoinedEntityTypeNames`. Column references prefixed with different lambda parameter names are resolved to their respective entity's columns with appropriate table alias qualification.

4. **Parameter extraction.** Walk the bound `SqlExpr` tree and replace `CapturedValueExpr` and string/char `LiteralExpr` nodes with `ParamSlotExpr`, collecting `ParameterInfo` for each parameter. This is the same `ExtractParameters` algorithm currently in `SqlExprClauseTranslator` (line 152 of `SqlExprClauseTranslator.cs`). The algorithm assigns sequential parameter indices (`@p0`, `@p1`, ...) and captures the source expression text for code generation.

5. **SQL rendering.** Call `SqlExprRenderer.Render(boundExpr, dialect, useGenericParamFormat: true)` to produce the SQL fragment string. Generic parameter format (`@p{n}`) is used because dialect-specific parameter formatting (`$1` for PostgreSQL, `?` for MySQL) is applied later during SQL assembly by `SqlFragmentTemplate`.

6. **KeyTypeName resolution.** For OrderBy, ThenBy, and GroupBy clauses, resolve the CLR type of the key expression from the `SqlExpr` tree. If the root expression is a `ColumnRefExpr`, look up the column's `FullClrType` from the entity metadata via a column name lookup dictionary. This logic moves from `SqlExprClauseTranslator.ResolveKeyTypeFromExpr` (line 253 of `SqlExprClauseTranslator.cs`).

7. **ValueTypeName resolution.** For Set and UpdateSet clauses, resolve the value type from entity column metadata. If the Set expression's column reference resolves to a known column, use that column's `FullClrType`. If discovery could not resolve the value type (because the entity type is generated by this generator), resolve it now from the bound entity metadata. This logic moves from `QuarryGenerator.ResolveSetValueTypeFromEntity` (lines 1373--1389). Additionally, apply custom type mapping enrichment for Set clauses: if the target column has a `CustomTypeMappingClass`, propagate it to the `TranslatedClause`. This logic moves from `QuarryGenerator.EnrichSetClauseWithMapping` (lines 1364--1367).

8. **SetAction enrichment.** For UpdateSetAction sites where the clause contains `SetActionAssignment` entries, resolve custom type mappings, value types, and proper dialect-specific column quoting from entity metadata. This logic moves from `QuarryGenerator.EnrichSetActionClauseWithMapping` (lines 1398--1401).

9. **Join condition translation.** For join sites, the `SqlExpr` representing the ON condition is bound and rendered using both the left (primary) and right (joined) entity metadata from `BoundCallSite.Entity` and `BoundCallSite.JoinedEntity`. The `JoinClauseKind` (Inner, Left, Right) is determined from the `InterceptorKind`. The joined table name and schema name come from the resolved `JoinedEntity`. For navigation joins, the ON condition `SqlExpr` was synthesized during binding from foreign key metadata rather than parsed from a user lambda, so the binding step here resolves column names to their SQL equivalents. For chained joins (3-way, 4-way), all prior entity columns are available through the `JoinedEntityTypeNames` list, enabling alias-qualified column resolution. This replaces `TryTranslateJoinClause` (lines 1452--1517) and `TryTranslateJoinedClause` in `QuarryGenerator.cs`.

10. **Joined clause translation.** For clause methods on joined builders (Where, OrderBy, GroupBy, Having on `IJoinedQueryBuilder`), the `SqlExpr` is bound with multi-entity column resolution using all joined entity metadata. Each lambda parameter (e.g., `(u, o)` in a 2-table join) maps to a specific entity's columns. The binder resolves `u.Name` to the first entity's `Name` column and `o.Status` to the second entity's `Status` column, with appropriate table alias prefixes. This replaces `TryTranslateJoinedClause` in `QuarryGenerator.cs` (lines 1348--1353).

11. **Joined Select projection analysis.** For Select sites on joined builders, analyze the joined projection using entity metadata from all joined entities. This moves from `TryAnalyzeJoinedProjection` (lines 1356--1361).

**Error handling -- skip and diagnose:**

When translation fails (unsupported `SqlRawExpr` nodes in the tree, `SqlExprBinder` failure, `SqlExprRenderer` producing empty output), `CallSiteTranslator` produces a `TranslatedCallSite` with `Clause = null`. It does NOT attempt a fallback to the old semantic `ClauseTranslator` path -- those translators are deleted in Step 4.5.

The collected analysis stage (Stage 5) detects untranslated clause sites by checking whether `Clause` is null for sites whose `InterceptorKind` is a clause-bearing method. For each such site, a QRY019 diagnostic is reported with the clause kind name (e.g., "Where", "OrderBy"). The generated interceptor for that site delegates to the runtime method implementation, which evaluates the expression dynamically. The user sees the diagnostic in their IDE, indicating that compile-time SQL optimization was not applied for that particular expression, but the generated code is still functionally correct.

**What moves here from `EnrichUsageSiteWithEntityInfo` and `TranslatePendingClause`:**

- `TranslatePendingClause` call and `SqlExprClauseTranslator` instantiation (lines 1282--1293)
- `TryTranslateJoinClause` logic (lines 1321--1343, 1452--1517)
- `TryTranslateJoinedClause` logic (lines 1348--1353)
- `TryAnalyzeJoinedProjection` (lines 1356--1361)
- `EnrichProjectionWithEntityInfo` (lines 1267--1273, 1820--1880+)
- `EnrichSetClauseWithMapping` (lines 1364--1367)
- `EnrichSetActionClauseWithMapping` (lines 1398--1401)
- `ResolveSetValueTypeFromEntity` (lines 1373--1389)
- KeyTypeName extraction (lines 1437--1438)
- ValueTypeName extraction (lines 1442--1446)

### Step 4.4: Rewire QuarryGenerator.Initialize()

The `Initialize()` method in `QuarryGenerator.cs` is restructured to implement the five-stage pipeline. The current method has three pipeline branches (context discovery, usage site discovery, migration discovery). Only the usage site discovery branch changes.

**New Initialize() structure:**

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // === Stage 1: Context Discovery (UNCHANGED) ===
    var contextDeclarations = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => IsContextCandidate(node),
            transform: static (ctx, ct) => GetContextInfo(ctx, ct))
        .Where(static info => info is not null)
        .Select(static (info, _) => info!);

    context.RegisterSourceOutput(contextDeclarations,
        static (spc, contextInfo) => GenerateEntityAndContextCode(contextInfo, spc));

    context.RegisterSourceOutput(contextDeclarations.Collect(),
        static (spc, contexts) => CheckDuplicateTypeMappings(contexts, spc));

    // === NEW: Build EntityRegistry from collected contexts ===
    var entityRegistry = contextDeclarations.Collect()
        .Select(static (contexts, ct) => EntityRegistry.Build(contexts, ct));

    // === Stage 2: Raw Call Site Discovery (CHANGED: returns RawCallSite) ===
    var rawCallSites = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => UsageSiteDiscovery.IsQuarryMethodCandidate(node),
            transform: static (ctx, ct) => DiscoverRawCallSite(ctx, ct))
        .Where(static site => site is not null)
        .Select(static (site, _) => site!);

    // === Stage 3: Per-Site Binding (NEW — individually cached) ===
    var boundCallSites = rawCallSites
        .Combine(entityRegistry)
        .SelectMany(static (pair, ct) =>
            CallSiteBinder.Bind(pair.Left, pair.Right, ct));

    // === Stage 4: Per-Site Translation (NEW — individually cached) ===
    var translatedCallSites = boundCallSites
        .Select(static (bound, ct) =>
            CallSiteTranslator.Translate(bound, ct));

    // === Stage 5: Collected Analysis + File Grouping (SIMPLIFIED) ===
    var perFileGroups = context.CompilationProvider
        .Combine(entityRegistry)
        .Combine(translatedCallSites.Collect())
        .SelectMany(static (data, ct) =>
            AnalyzeAndGroup(data.Left.Left, data.Left.Right, data.Right, ct));

    context.RegisterSourceOutput(perFileGroups,
        static (spc, group) => EmitFileInterceptors(spc, group));

    // === Migration Discovery (UNCHANGED) ===
    // ...
}
```

**How EntityRegistry is built and combined:**

The `EntityRegistry` is built from `contextDeclarations.Collect()` via a `Select` transform that calls `EntityRegistry.Build(contexts, ct)`. This produces an `IncrementalValueProvider<EntityRegistry>` (singular, not plural) that changes only when any `ContextInfo` changes. The entity registry is then combined with raw call sites via `rawCallSites.Combine(entityRegistry)`, creating `(RawCallSite, EntityRegistry)` pairs for each site. Because the registry is a single shared value provider, the Roslyn incremental driver efficiently pairs it with each per-site value without duplicating it. When the registry has not changed, the `(RawCallSite, EntityRegistry)` pair for a given site only differs if the `RawCallSite` itself changed.

**How rawCallSites flow through binding and translation:**

`rawCallSites.Combine(entityRegistry)` produces `IncrementalValuesProvider<(RawCallSite, EntityRegistry)>`. The `.SelectMany()` invokes `CallSiteBinder.Bind` which returns `ImmutableArray<BoundCallSite>` -- one element for most sites, potentially multiple for navigation joins (see Step 4.6). The `SelectMany` flattens these into `IncrementalValuesProvider<BoundCallSite>`. Then `.Select()` invokes `CallSiteTranslator.Translate` on each `BoundCallSite` to produce `IncrementalValuesProvider<TranslatedCallSite>`. Each of these per-site transforms is independently cached by the incremental driver.

**Where .Collect() now sits:**

The `.Collect()` call moves from raw usage sites (the current `usageSites.Collect()` at line 85 of `QuarryGenerator.cs`) to translated call sites (`translatedCallSites.Collect()`). This is the critical architectural change. Instead of collecting raw/partially-translated sites and performing all enrichment, translation, and analysis in the collected transform, the expensive per-site binding and translation happen in individually-cached stages before collection. The collected transform receives `ImmutableArray<TranslatedCallSite>` where each element already has its SQL fragment, parameters, key/value type names, and projection info fully resolved.

**What GroupByFileAndProcess becomes:**

The current `GroupByFileAndProcess` method (lines 305--368 of `QuarryGenerator.cs`) is replaced by a simplified `AnalyzeAndGroup` method. The following logic is removed from the collected transform:

- `EntityRegistry.Build(contexts, ct)` call (the registry is pre-built, passed in as a parameter)
- The `EnrichUsageSiteWithEntityInfo` loop over all analyzable sites (lines 345--348)
- `TranslatePendingClause` calls within enrichment (lines 1282--1293)
- `DiscoverNavigationJoinChainMembers` post-enrichment pass (lines 351--356)
- `TryTranslateJoinClause` / `TryTranslateJoinedClause` within enrichment
- All projection, Set, and SetAction enrichment calls

The following logic remains in the collected transform:

- Pre-analysis diagnostic collection: QRY005 (anonymous type not supported), QRY006 (query not analyzable) from `TranslatedCallSite` flags
- Ambiguity diagnostics (QRY015): checking sites where entity resolution was flagged ambiguous during binding
- Unbound parameter diagnostics (QRY016): scanning translated SQL fragments for `@p{n}` placeholders without matching `ParameterInfo` entries
- Clause-not-translatable diagnostics (QRY019): checking for clause-bearing sites where `Clause` is null
- Chain analysis via `ChainAnalyzer` (still requires the full site array to identify execution chains)
- File grouping into `FileInterceptorGroup` via `PipelineOrchestrator.GroupIntoFiles`

**New AnalyzeAndGroup signature:**

```csharp
private static ImmutableArray<FileInterceptorGroup> AnalyzeAndGroup(
    Compilation compilation,
    EntityRegistry registry,
    ImmutableArray<TranslatedCallSite> translatedSites,
    CancellationToken ct)
```

**Adapting PipelineOrchestrator:**

The existing `PipelineOrchestrator` class is updated to accept `TranslatedCallSite` instead of `UsageSiteInfo`. Its constructor drops the `ImmutableArray<UsageSiteInfo> _originalUsageSites` field. The `AnalyzeAndGroup` method signature changes:

- The `enrichSite` delegate parameter is removed (no enrichment needed).
- The `navJoinChainSites` parameter is removed (navigation join sites are already in the `TranslatedCallSite` array, added during binding via SelectMany).
- The `getNonTranslatableClauseKind` function is adapted to check `site.Clause == null` on the `TranslatedCallSite` for clause-bearing `InterceptorKind` values, instead of checking `site.ClauseInfo` on `UsageSiteInfo`.

The chain analysis path must be adapted to work with `TranslatedCallSite` instead of `UsageSiteInfo`. The `ChainAnalyzer` currently takes `List<UsageSiteInfo>`. During this phase, a lightweight adapter method converts each `TranslatedCallSite` into the fields `ChainAnalyzer` needs (UniqueId, FilePath, Line, Column, Kind, clause SQL, context class name, etc.) by constructing temporary `UsageSiteInfo` instances. This adapter is explicitly temporary -- Phase 5 rewrites `ChainAnalyzer` to consume `TranslatedCallSite` directly. The adapter is acceptable here because it introduces no behavioral change and keeps the phase scope manageable.

### Step 4.5: Delete Old Translators

With the SqlExpr pipeline handling 100% of expression translation and `CallSiteTranslator` serving as the translation entry point, the old semantic and syntactic translators are deleted.

**Files deleted:**

| File | Lines | Reason |
|---|---|---|
| `Translation/ClauseTranslator.cs` | 1,067 | Semantic clause-to-SQL translation via `ExpressionSyntaxTranslator`. All call sites removed: `UsageSiteDiscovery` lines 521, 721--730 (Step 4.1) and `QuarryGenerator` lines 1477, 1498, 1506 (Steps 4.2--4.3). |
| `Translation/ExpressionSyntaxTranslator.cs` | 1,569 | Core semantic expression translator. Called exclusively by `ClauseTranslator`. No direct external callers remain after `ClauseTranslator` deletion. |
| `Translation/ExpressionTranslationContext.cs` | 386 | Translation context type. Referenced only by `ExpressionSyntaxTranslator` and `ClauseTranslator`. Zero references remain after those files are deleted. |
| `Translation/ExpressionTranslationResult.cs` | ~170 | Result type. Referenced only by `ExpressionSyntaxTranslator`. Zero references remain. |
| `Translation/SubqueryScope.cs` | ~50 | Subquery scope type. Referenced only by `ExpressionTranslationContext`. Zero references remain. |

**Files retained in `Translation/`:**

| File | Lines | Reason |
|---|---|---|
| `Translation/SqlLikeHelpers.cs` | ~50 | LIKE pattern helpers (`EscapeLikePattern`, `GetLikeEscapeClause`). Referenced by `SqlExprParser` in the IR layer for LIKE expression construction. May be relocated to `IR/` in a later cleanup but does not block deletion of the translators. |

**Total line count reduction:** Approximately 3,240 lines deleted across the 5 files.

**Verification procedure:**

1. After deletion, build `Quarry.Generator`. Any remaining references to deleted types produce compiler errors (CS0103 name not found, CS0234 namespace missing type). Fix any straggling references by ensuring the corresponding logic has been migrated to `CallSiteBinder` or `CallSiteTranslator`.

2. Search for string references to deleted type names across the entire solution:
   - `ClauseTranslator` -- should only appear in test files, comments, and this implementation plan. Test files that directly instantiate `ClauseTranslator` (e.g., `ClauseTranslationTests.cs`) are migrated to test the new pipeline via end-to-end generator tests or dedicated `CallSiteTranslator` unit tests.
   - `ExpressionSyntaxTranslator` -- should have zero remaining code references.
   - `ExpressionTranslationContext` -- should have zero remaining code references.
   - `ExpressionTranslationResult` -- should have zero remaining code references.
   - `SubqueryScope` -- should have zero remaining code references.

3. The `PendingClauseInfo` model class (`Models/PendingClauseInfo.cs`) can also be deleted after verifying no code creates or consumes it. The `UsageSiteInfo.PendingClauseInfo` property becomes unused once `UsageSiteInfo` is no longer produced by discovery, but `UsageSiteInfo` itself may remain temporarily as an adapter type for `ChainAnalyzer` compatibility (see Step 4.4). If the adapter constructs `UsageSiteInfo` without populating `PendingClauseInfo`, the property can be removed and the model slimmed down.

4. Run the full test suite. All 2,929 tests must pass. Pay particular attention to `ClauseTranslationTests` (31 tests) and `ExpressionTranslationTests` (48 tests) which may have directly tested the deleted translators and need migration to the new pipeline entry points.

### Step 4.6: Navigation Join Resolution

Navigation joins require special handling during binding because a single navigation join `RawCallSite` can produce additional bound sites for downstream chain members (Select, Where, Execute, etc.) that the initial syntax scan could not discover. The semantic model during discovery cannot resolve downstream method calls when the joined entity type parameter `TJoined` is a generic type parameter that the compiler has not yet inferred.

**Current behavior (in `DiscoverNavigationJoinChainMembers`, lines 1524--1636 of `QuarryGenerator.cs`):**

The current implementation iterates enriched sites after the `.Collect()` call, looking for navigation joins where the joined entity type was successfully resolved during enrichment. For each such site, it walks up the syntax tree from the join `InvocationExpressionSyntax` to find parent `MemberAccessExpression` / `InvocationExpression` pairs that represent downstream fluent chain calls (e.g., `.Join(u => u.Orders).Select(...).ExecuteFetchAllAsync(...)`). For each call not already in the discovered set (checked via a `locationKey` hash set), it constructs a new `UsageSiteInfo` with the resolved joined entity type names, enriches it via `EnrichUsageSiteWithEntityInfo`, resolves its interceptable location, and adds it to the result list.

**New behavior (Approach A -- discovery-time chain member emission):**

The preferred approach moves the syntax tree walk into Stage 2 (discovery) where the syntax tree is naturally available, rather than requiring syntax tree access during binding.

When `UsageSiteDiscovery.DiscoverUsageSite` detects a navigation join (lambda body is a simple member access like `u => u.Orders` rather than a binary condition), it performs the syntax tree walk immediately:

1. **Identify the navigation join.** After determining `IsNavigationJoin = true`, the discovery method walks up the parent syntax tree from the current `InvocationExpressionSyntax`.

2. **For each parent invocation in the fluent chain**, check if the method name is in `InterceptableMethods`. If so, emit an additional `RawCallSite` for that call site with:
   - `EntityTypeName` set to the primary entity (same as the join site)
   - `JoinedEntityTypeName` set to the navigation property's unresolved type (it will be resolved during binding)
   - `BuilderKind` set to `BuilderKind.JoinedQuery`
   - `IsNavigationJoin = false` (the chain member itself is not a navigation join, only the originating join is)
   - Its own `UniqueId`, `InterceptableLocationData`, and `DiagnosticLocation`
   - The parsed `SqlExpr` from its lambda (if it has one, e.g., a `.Where()` after the join)

3. **Return mechanism.** Since `DiscoverUsageSite` currently returns a single `RawCallSite?`, it must be changed to return `ImmutableArray<RawCallSite>` (or the caller must use `SelectMany`). The simplest approach is to change the `CreateSyntaxProvider` transform to return `ImmutableArray<RawCallSite>` and use `SelectMany` on the syntax provider output:

```csharp
var rawCallSites = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: static (node, _) => UsageSiteDiscovery.IsQuarryMethodCandidate(node),
        transform: static (ctx, ct) => DiscoverRawCallSites(ctx, ct))
    .SelectMany(static (sites, _) => sites);
```

This keeps each raw site individually cached after the `SelectMany` flattening.

4. **During binding**, `CallSiteBinder.Bind` handles these additional chain member `RawCallSite` entries by resolving their `JoinedEntityTypeName` against the registry (now possible because the binder has the `EntityRegistry` with all navigation metadata). For each chain member, the binder populates `JoinedEntityTypeNames` with the full list of joined entity types, the correct `BuilderKind`, and all context metadata from the resolved navigation join. The binder returns a single `BoundCallSite` per raw site -- no `SelectMany` expansion is needed at the binding stage when using Approach A.

**What moves from `DiscoverNavigationJoinChainMembers`:**

- Navigation property name extraction from the join lambda syntax
- Syntax tree walk for undiscovered chain members (lines 1547--1632): moves to `UsageSiteDiscovery`
- `UsageSiteInfo` construction for discovered members (lines 1602--1619): becomes `RawCallSite` construction in discovery
- Interceptable location resolution for chain members (lines 1586--1600): stays in discovery where the semantic model is available
- Enrichment of discovered chain members (line 1622): becomes binding in `CallSiteBinder`, happening naturally in Stage 3
- The `discoveredLocations` hash set deduplication logic (lines 1551, 1565): becomes unnecessary because the `CreateSyntaxProvider` already deduplicates by invocation syntax node -- each `InvocationExpressionSyntax` is visited exactly once. The chain member `RawCallSite` entries are emitted from the join site's discovery call, so they are associated with that specific syntax node and will not be re-emitted if the join site has not changed.

### Validation

**Test suite:** All 2,929 existing tests must pass with zero failures after Phase 4 is complete. This includes:

- `ExpressionTranslationTests` (48 tests) and `ExpressionPatternTranslationTests` (16 tests): End-to-end expression translation through the generator. These verify that the SqlExpr pipeline produces identical SQL output to the deleted translators.
- `ClauseTranslationTests` (31 tests): Clause-level translation tests. Tests that directly instantiate `ClauseTranslator` (now deleted) must be migrated to test through the generator pipeline or through `CallSiteTranslator` directly.
- `CompileTimeRuntimeEquivalenceTests` (49 test cases): Verify that compile-time SQL matches runtime SQL across dialects. These are the most critical correctness tests.
- `IncrementalCachingTests` (4 tests): Verify incremental caching behavior. Must continue to pass and should be extended with new tests verifying per-site caching.
- `Samples/InterceptorIntegrationTests` (126 tests): Compile real query code and verify the generated interceptor output byte-for-byte against approved baselines.
- All `SqlOutput/` tests (190+ tests across 20 files): End-to-end SQL output verification across PostgreSQL, MySQL, SQL Server, and SQLite dialects.
- `JoinOperationsTests` (53 tests) and `NavigationListTests` (13 tests): Join and navigation join behavior.
- `IR/EntityRegistryTests` (8 tests) and `IR/CallSiteTests` (10 tests): IR type unit tests.

**Byte-identical output:** The generated `.g.cs` source files must be byte-identical before and after the restructure for all non-subquery cases. Phase 4 introduces no behavioral changes -- it is purely a pipeline restructure. The same source code inputs must produce the same generated outputs character-for-character. Any divergence indicates a translation regression in the SqlExpr pipeline.

To verify byte-identical output, rely on the snapshot comparison tests (`CompileTimeRuntimeEquivalenceTests`, `InterceptorIntegrationTests`). These compile source code, run the generator, and compare the generated output against stored baselines. Any difference causes a test failure with a diff showing exactly where the output diverged.

**Performance validation:** After the restructure, verify the incremental caching improvement using Roslyn's `GeneratorDriver` test infrastructure:

- Use `GeneratorDriver.RunGeneratorsAndUpdateCompilation` with `GeneratorDriverRunResult.Results[0].TrackedSteps` to inspect which pipeline stages re-executed on an incremental run.
- Modify a single `.Where()` lambda in a multi-query source file.
- Confirm that only the modified site's binding and translation stages show `IncrementalStepRunReason.Modified`. All other sites should show `IncrementalStepRunReason.Cached` or `IncrementalStepRunReason.Unchanged`.
- Confirm that Stage 5 (collected analysis) re-executes but processes pre-translated sites without redundant enrichment or translation work.

**Memory validation:** Verify that no `SyntaxNode` references persist in cached pipeline values. `RawCallSite`, `BoundCallSite`, and `TranslatedCallSite` must not hold `SyntaxNode` or `SyntaxTree` references. After running the generator, force a GC collection and confirm that syntax trees from previous compilation snapshots are eligible for collection.

### Files Changed

| Path | Action | Description |
|---|---|---|
| `src/Quarry.Generator/IR/CallSiteBinder.cs` | Create | Static class that binds `RawCallSite` + `EntityRegistry` to produce `BoundCallSite`. Contains entity resolution, InsertInfo/UpdateInfo construction, join entity resolution, navigation join entity type resolution, and RawSql type enrichment. |
| `src/Quarry.Generator/IR/CallSiteTranslator.cs` | Create | Static class that translates `BoundCallSite` to `TranslatedCallSite`. Wraps SqlExprBinder/SqlExprRenderer pipeline; handles parameter extraction, KeyTypeName/ValueTypeName resolution, projection enrichment, join condition translation, joined clause translation, and Set/SetAction mapping enrichment. |
| `src/Quarry.Generator/QuarryGenerator.cs` | Modify | Restructure `Initialize()` to implement 5-stage pipeline. Add `EntityRegistry` construction from collected contexts. Replace `GroupByFileAndProcess` with simplified `AnalyzeAndGroup` accepting `TranslatedCallSite`. Remove `EnrichUsageSiteWithEntityInfo` (~225 lines), `TranslatePendingClause` (~30 lines), `TryTranslateJoinClause` (~65 lines), `DiscoverNavigationJoinChainMembers` (~110 lines), `ResolveNavigationJoinEntityType`, `EnrichProjectionWithEntityInfo` (~60 lines), `EnrichSetClauseWithMapping`, `EnrichSetActionClauseWithMapping`, `ResolveSetValueTypeFromEntity`, `EnrichRawSqlTypeInfoWithEntity`, `GetNonTranslatableClauseKind`. Add `DiscoverRawCallSite` wrapper. |
| `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` | Modify | Change `DiscoverUsageSite` return type from `UsageSiteInfo` to `RawCallSite` (or `ImmutableArray<RawCallSite>` for navigation join chain member emission). Remove all `ClauseTranslator` calls from `AnalyzeClause`. Remove `TrySyntacticAnalysis` method. Call `SqlExprParser.ParseWithPathTracking()` unconditionally for all clause-bearing sites. Add navigation join chain member discovery to emit additional `RawCallSite` entries for downstream fluent calls. |
| `src/Quarry.Generator/IR/PipelineOrchestrator.cs` | Modify | Update to accept `ImmutableArray<TranslatedCallSite>` instead of `List<UsageSiteInfo>`. Remove `enrichSite` delegate, `navJoinChainSites` parameter, and `_originalUsageSites` field. Adapt diagnostic collection and chain analysis adapter to operate on `TranslatedCallSite`. |
| `src/Quarry.Generator/IR/RawCallSite.cs` | Modify | Minor: may need field additions if navigation join chain member emission requires extra context. No structural changes to existing fields. |
| `src/Quarry.Generator/IR/BoundCallSite.cs` | Modify | Add `bool IsAmbiguous` field for QRY015 diagnostic deferred reporting in Stage 5. |
| `src/Quarry.Generator/IR/TranslatedCallSite.cs` | Modify | Add `ProjectionInfo` field for enriched projection data. May add `bool IsNonAnalyzable` and `string? NonAnalyzableReason` for QRY006 diagnostic reporting. |
| `src/Quarry.Generator/IR/SqlExprClauseTranslator.cs` | Modify | Core logic absorbed into `CallSiteTranslator`. `Translate(PendingClauseInfo)` signature changes to accept `(SqlExpr, ClauseKind, bool isDescending, EntityInfo, SqlDialect)` directly. May be retained as internal helper or inlined entirely. |
| `src/Quarry.Generator/Models/FileInterceptorGroup.cs` | Modify | Update site lists to hold `TranslatedCallSite` instead of `UsageSiteInfo`. Add adapter properties if needed during transition. |
| `src/Quarry.Generator/Models/UsageSiteInfo.cs` | Modify | Remove `PendingClauseInfo` property. Retained temporarily as adapter type for `ChainAnalyzer` compatibility. Add static `FromTranslatedCallSite(TranslatedCallSite)` factory. Full removal deferred to Phase 5/6B. |
| `src/Quarry.Generator/Models/PendingClauseInfo.cs` | Delete | No longer produced or consumed. `RawCallSite.Expression` stores `SqlExpr` directly. |
| `src/Quarry.Generator/Translation/ClauseTranslator.cs` | Delete | 1,067 lines. Semantic clause-to-SQL translation replaced by SqlExpr pipeline via `CallSiteTranslator`. |
| `src/Quarry.Generator/Translation/ExpressionSyntaxTranslator.cs` | Delete | 1,569 lines. Core semantic expression translator replaced by SqlExprParser + SqlExprBinder + SqlExprRenderer. |
| `src/Quarry.Generator/Translation/ExpressionTranslationContext.cs` | Delete | 386 lines. Context type used exclusively by deleted translators. |
| `src/Quarry.Generator/Translation/ExpressionTranslationResult.cs` | Delete | ~170 lines. Result type used exclusively by `ExpressionSyntaxTranslator`. |
| `src/Quarry.Generator/Translation/SubqueryScope.cs` | Delete | ~50 lines. Subquery scope used exclusively by `ExpressionTranslationContext`. |
| `src/Quarry.Generator/IR/EntityRegistry.cs` | Modify | No structural changes. May add convenience methods for batch resolution if needed by `CallSiteBinder`. |

---

## 7. Phase 5: Chain Analysis, SQL Assembly & Carrier Redesign

### Objective

This phase replaces three major subsystems -- ChainAnalyzer, CompileTimeSqlBuilder, and CarrierClassBuilder -- with clean-room implementations that operate on the new IR types established in Phases 1-4. ChainAnalyzer is rewritten to accept `TranslatedCallSite[]` (produced by the Phase 4 pipeline) and emit `QueryPlan[]` directly, eliminating the intermediate `ChainAnalysisResult` and `PrebuiltChainInfo` types. A new `SqlAssembler` renders SQL from `QueryPlan` using `SqlExprRenderer`, replacing `CompileTimeSqlBuilder` and its `SqlFragmentTemplate` machinery. Finally, `CarrierPlan` replaces `CarrierClassInfo` as the carrier representation, produced by a revised `CarrierAnalyzer` that consumes `AssembledPlan` instead of `PrebuiltChainInfo`.

---

### Step 5.1: Rewrite ChainAnalyzer

**New signature:**

```csharp
internal static class ChainAnalyzer
{
    public static IReadOnlyList<QueryPlan> Analyze(
        ImmutableArray<TranslatedCallSite> sites,
        EntityRegistry registry,
        CancellationToken ct);
}
```

**Input change.** The current `ChainAnalyzer.AnalyzeChain()` takes a `UsageSiteInfo` execution site and an `IReadOnlyList<UsageSiteInfo>` of all sites in the method, then performs Roslyn `SemanticModel` dataflow analysis (variable tracking, `IfStatementSyntax` nesting, `ILocalSymbol` resolution). The rewritten analyzer takes `TranslatedCallSite[]` -- sites that already carry fully translated `SqlExpr` trees and resolved parameters. The Roslyn semantic model is no longer available at this stage because translation completed before `.Collect()`. The chain grouping algorithm must therefore rely on location metadata embedded in `TranslatedCallSite.Bound.Raw` (file path, method span, source location) rather than walking syntax trees.

**Algorithm -- chain grouping.** The analyzer groups `TranslatedCallSite` values into chains, where each chain consists of one execution terminal and zero or more clause sites. Grouping proceeds as follows:

1. **Identify execution terminals.** Scan the input array for sites where `Bound.Raw.Kind` is an execution kind (`ExecuteFetchAll`, `ExecuteFetchFirst`, `ExecuteFetchFirstOrDefault`, `ExecuteFetchSingle`, `ExecuteScalar`, `ExecuteNonQuery`, `ToAsyncEnumerable`, `ToDiagnostics`, `InsertExecuteNonQuery`, `InsertExecuteScalar`, `InsertToDiagnostics`). Each execution terminal anchors one chain.

2. **Group by method scope.** Partition all sites by their containing method span (file path + method start/end offsets, stored on `RawCallSite`). Within each method scope, match clause sites to their execution terminal using the chain link metadata established during the binding phase. `TranslatedCallSite.Bound.Raw` carries a `ChainId` field (assigned during discovery when the per-site binding resolves the fluent chain or variable assignment chain). All sites sharing the same `ChainId` within a method scope belong to the same chain.

3. **Order clauses.** Within each chain, order clause sites by their source location offset (ascending). The execution terminal is always last. This preserves the original execution flow order needed for parameter index assignment and SQL clause ordering.

4. **Detect conditional clauses.** Each `TranslatedCallSite` carries a `ConditionalInfo` property (populated during the Phase 4 per-site binding pass) that records whether the site is inside an `if` block, its nesting depth, and its branch classification (`Independent` or `MutuallyExclusive`). The analyzer reads these flags to assign bit indices rather than performing its own syntax tree walk. For sites with `ConditionalInfo != null`, a monotonically increasing bit index is assigned per chain.

**Tier classification.** After grouping and conditional detection, the analyzer classifies each chain into one of three optimization tiers:

- **PrebuiltDispatch (Tier 1):** All clause sites within the chain have successfully translated `SqlExpr` expressions (every `TranslatedCallSite.Clause` is non-null with `IsSuccess == true`), and the number of conditional bits is at most `MaxTier1Bits` (4). This means at most 16 mask variants. The chain qualifies for a compile-time dispatch table of prebuilt SQL strings.

- **PrequotedFragments (Tier 2):** All clauses translated successfully, but the conditional bit count exceeds `MaxTier1Bits`. Too many dispatch variants for a static table. The emitted code concatenates pre-quoted SQL fragments at runtime based on the mask value.

- **RuntimeBuild (Tier 3):** One or more clause translations failed (`Clause == null` or `Clause.IsSuccess == false`), the chain contains unmatched method invocations, the chain was detected as forked, or a disqualifying pattern is present (loop assignment, try/catch, lambda capture, opaque assignment). The generator emits no execution interceptor; the existing runtime `SqlBuilder` path handles SQL construction.

**Disqualifier detection** is simplified compared to the current implementation. The current analyzer walks the method body's `BlockSyntax` to detect loops, try/catch blocks, lambda captures, and opaque assignments. In the rewritten version, disqualifying conditions are detected earlier during the Phase 4 per-site binding pass and recorded on `RawCallSite` as flags (`IsInsideLoop`, `IsInsideTryCatch`, `IsCapturedInLambda`). The chain analyzer checks these flags rather than re-walking syntax. If any clause site in a chain has a disqualifying flag, the entire chain is downgraded to `RuntimeBuild`.

**Conditional mask computation.** For Tier 1 chains, the analyzer enumerates all possible mask values using the same combinatorial algorithm as the current implementation: independent conditional bits contribute `2^N` combinations (each bit on or off), while mutually exclusive groups (if/else branches) contribute one-of-N selections. The algorithm builds the mask list iteratively by doubling the mask set for each independent bit and multiplying by the group size for each exclusive group. The resulting `IReadOnlyList<ulong>` is stored on `QueryPlan.PossibleMasks`.

**Building QueryPlan terms from TranslatedCallSite.Clause.** For each clause site in a chain, the analyzer maps `TranslatedClause` data into QueryPlan terms:

- `ClauseKind.Where` / `ClauseKind.DeleteWhere` / `ClauseKind.UpdateWhere` produces a `WhereTerm` with `Condition = clause.ResolvedExpression` and `BitIndex` from the conditional assignment (null if unconditional).
- `ClauseKind.OrderBy` / `ClauseKind.ThenBy` produces an `OrderTerm` with `Expression = clause.ResolvedExpression`, `IsDescending = clause.IsDescending`, and optional `BitIndex`.
- `ClauseKind.Join` / `ClauseKind.LeftJoin` / `ClauseKind.RightJoin` produces a `JoinPlan` with `Kind` mapped from `clause.JoinKind`, `Table = new TableRef(clause.JoinedTableName, clause.JoinedSchemaName, clause.TableAlias)`, and `OnCondition = clause.ResolvedExpression`.
- `ClauseKind.GroupBy` appends `clause.ResolvedExpression` to `QueryPlan.GroupByExprs`.
- `ClauseKind.Having` appends `clause.ResolvedExpression` to `QueryPlan.HavingExprs`.
- `ClauseKind.Set` / `ClauseKind.UpdateSet` / `ClauseKind.UpdateSetAction` / `ClauseKind.UpdateSetPoco` produces one or more `SetTerm` values. For `SetActionAssignment` lists (from `clause.SetAssignments`), each assignment produces a separate `SetTerm` with `Column` and `Value` expressions. For `UpdateSetPoco`, the insert-style column list from `BoundCallSite.UpdateInfo` is expanded into per-column `SetTerm` values.
- `ClauseKind.Select` populates `QueryPlan.Projection` via a `SelectProjection` built from `TranslatedCallSite.Bound.Raw.ProjectionInfo`.
- `ClauseKind.Limit` / `ClauseKind.Offset` are recorded in `QueryPlan.Pagination`. Literal values (constant int arguments) produce `PaginationPlan.LiteralLimit` / `LiteralOffset`. Parameterized values produce parameter index references.
- `ClauseKind.Distinct` sets `QueryPlan.IsDistinct = true`.
- Insert columns (from `BoundCallSite.InsertInfo`) produce `InsertColumn` values on `QueryPlan.InsertColumns`.

**Global parameter extraction with remapped indices.** Each `TranslatedClause` carries its own `Parameters` list with clause-local indices. The analyzer walks all clause sites in execution order, collects every `ParameterInfo` from every `TranslatedClause.Parameters`, and assigns a globally unique `GlobalIndex` (0, 1, 2, ...) to each. This produces the `QueryPlan.Parameters` list of `QueryParameter` values. The `SqlExpr` trees within `WhereTerm.Condition`, `OrderTerm.Expression`, etc. contain `ParamSlotExpr` nodes with clause-local slot indices. These are not remapped at this stage -- the slot indices refer to the clause-local parameter list, and the `SqlAssembler` (Step 5.2) handles the remapping to global indices during rendering. Each `QueryParameter` records the full extraction metadata: `ClrType`, `ValueExpression`, `IsCaptured`, `IsCollection`, `ElementTypeName`, `TypeMappingClass`, `IsEnum`, `EnumUnderlyingType`, `IsSensitive`, `EntityPropertyExpression`, `NeedsFieldInfoCache`, `IsDirectAccessible`, and `CollectionAccessExpression`.

**Unmatched method detection.** During the Phase 4 per-site binding pass, if a fluent chain invocation cannot be matched to any interceptable method, its name is recorded on the `RawCallSite` (e.g., `AddWhereClause`, custom extension methods). The chain analyzer collects these into `QueryPlan.UnmatchedMethodNames`. When this list is non-null, the chain is ineligible for carrier optimization and no execution interceptor is emitted.

**Output.** The method returns one `QueryPlan` per detected chain. Chains whose execution terminal has no matched clause sites (standalone execution calls) still produce a `QueryPlan` with empty clause lists. The `QueryPlan.Tier` field carries the classified optimization tier. The `QueryPlan.NotAnalyzableReason` field is set for `RuntimeBuild` chains.

---

### Step 5.2: Create SqlAssembler

**Signature:**

```csharp
internal static class SqlAssembler
{
    public static AssembledPlan Assemble(
        QueryPlan plan,
        EntityRegistry registry,
        SqlDialect dialect,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        string entityTypeName,
        string? resultTypeName,
        string? entitySchemaNamespace);
}
```

**Purpose.** `SqlAssembler` is a clean-room replacement for `CompileTimeSqlBuilder`. It takes a dialect-agnostic `QueryPlan` and materializes concrete SQL strings for every possible mask value, producing an `AssembledPlan`. The critical difference from `CompileTimeSqlBuilder` is the input format: instead of operating on `ChainedClauseSite` with `ClauseInfo.SqlFragment` strings and `SqlFragmentTemplate` objects, `SqlAssembler` operates on `SqlExpr` expression trees and delegates rendering to `SqlExprRenderer`.

**Algorithm -- per-mask SQL generation.** For Tier 1 plans (`PrebuiltDispatch`), `SqlAssembler` iterates over `QueryPlan.PossibleMasks`. For each mask value `m`:

1. **Determine active terms.** A `WhereTerm` is active if it is unconditional (`BitIndex == null`) or its bit is set in the mask (`(m & (1UL << BitIndex)) != 0`). The same logic applies to `OrderTerm`, `SetTerm`, and `JoinPlan` terms that carry a `BitIndex`. All unconditional terms (joins, group-by, having, projection, pagination) are always active.

2. **Compute parameter base offsets.** Walk the active terms in execution order (the same order as `QueryPlan.Parameters`). For each active clause's parameter slice, assign a contiguous range of global parameter indices starting from the running total. This mirrors the current `ComputeParameterBaseOffsets` logic but operates on `QueryParameter` objects rather than `SqlFragmentTemplate` slots. The output is a mapping from each active clause's first parameter `GlobalIndex` to its remapped base offset in this particular mask variant.

3. **Render SQL by query kind.** Dispatch to a per-kind rendering method based on `QueryPlan.Kind`:

   - **Select:** Emit `SELECT` keyword. If `QueryPlan.IsDistinct`, append `DISTINCT`. Render the projection columns from `QueryPlan.Projection`: for `ProjectionKind.AllColumns` emit `*`; for explicit projections, emit each `ProjectedColumn` with dialect-quoted column names, table alias prefixes, SQL expressions, and aliases. Emit `FROM` with the primary table formatted via `SqlFormatting.FormatTableName(dialect, plan.PrimaryTable.TableName, plan.PrimaryTable.SchemaName)` and optional alias. For each active `JoinPlan`, emit the join keyword (`INNER JOIN`, `LEFT JOIN`, `RIGHT JOIN`), the joined table reference with alias, `ON`, and the on-condition rendered via `SqlExprRenderer.Render(join.OnCondition, dialect, paramBase)`. For active `WhereTerm` values, emit `WHERE` followed by AND-joined conditions, each rendered via `SqlExprRenderer.Render(term.Condition, dialect, paramBase)`. Constant-true tautologies (where the rendered expression equals `"TRUE"`, `"1"`, or `"true"` and has no parameters) are elided. For `GroupByExprs`, emit `GROUP BY` with comma-separated rendered expressions. For `HavingExprs`, emit `HAVING` with AND-joined rendered expressions. For active `OrderTerm` values, emit `ORDER BY` with comma-separated `{rendered_expr} ASC|DESC`. For pagination, emit dialect-specific syntax.

   - **Update:** Emit `UPDATE {table}`. Emit `SET` with comma-separated `{column} = {value}` pairs from active `SetTerm` values. Each `SetTerm.Column` is rendered as a quoted column name; each `SetTerm.Value` is rendered via `SqlExprRenderer`. For `SetPoco`-sourced terms, expand all columns from the update info. Emit `WHERE` from active `WhereTerm` values with `UpdateWhere` role.

   - **Delete:** Emit `DELETE FROM {table}`. Emit `WHERE` from active `WhereTerm` values with `DeleteWhere` role.

   - **Insert:** Emit `INSERT INTO {table}` with column list from `QueryPlan.InsertColumns`. Emit `VALUES` with parameter placeholders formatted via `SqlFormatting.FormatParameter(dialect, index)`. Emit the `RETURNING` clause (PostgreSQL, SQLite) or note the need for a separate `SELECT LAST_INSERT_ID()` query (MySQL) via `SqlFormatting.FormatReturningClause`.

4. **Record the variant.** Store the rendered SQL string and its parameter count in an `AssembledSqlVariant`, keyed by the mask value in the `AssembledPlan.SqlVariants` dictionary.

**Expression rendering via SqlExprRenderer.** All SQL fragment rendering goes through `SqlExprRenderer.Render(SqlExpr, SqlDialect, parameterBaseIndex)`. This replaces the `SqlFragmentTemplate.RenderTo()` mechanism. `SqlExprRenderer` walks the `SqlExpr` tree recursively: `ResolvedColumnExpr` emits the pre-quoted column name, `ParamSlotExpr` emits a dialect-formatted parameter placeholder (`@p{base + slot}` for SqlServer, `$N` for PostgreSQL, `?` for SQLite/MySQL), `BinaryOpExpr` emits `({left} {op} {right})`, `FunctionCallExpr` emits `{funcName}({args})`, `InExpr` emits `{operand} IN ({values})`, `LikeExpr` emits `{operand} LIKE {pattern}`, `LiteralExpr` emits the literal value, and `IsNullCheckExpr` emits `{operand} IS [NOT] NULL`. For collection parameters (where the `ParamSlotExpr` corresponds to a `QueryParameter` with `IsCollection == true`), the renderer emits the expansion token `{__COL_P{index}__}` instead of a standard placeholder, enabling runtime expansion into multiple parameters.

**Dialect-specific formatting.** Parameter format differences are handled by `SqlFormatting.FormatParameter(dialect, index)`: SqlServer uses `@p{n}`, PostgreSQL uses `${n+1}` (1-based), MySQL and SQLite use `@p{n}`. Column quoting uses `SqlFormatting.QuoteIdentifier(dialect, name)`: SqlServer uses `[name]`, PostgreSQL uses `"name"`, MySQL uses `` `name` ``, SQLite uses `"name"`. Pagination syntax differs by dialect: SQLite/PostgreSQL/MySQL emit `LIMIT {n} OFFSET {m}`, SqlServer emits `OFFSET {m} ROWS FETCH NEXT {n} ROWS ONLY` (requiring an `ORDER BY` clause; if none exists, `ORDER BY (SELECT NULL)` is synthesized).

**Reader delegate code generation.** For Select queries, `SqlAssembler` calls into `ReaderCodeGenerator` (unchanged from the current implementation) to produce the reader delegate code string from `QueryPlan.Projection`. This string is stored in `AssembledPlan.ReaderDelegateCode`.

**MaxParameterCount computation.** After rendering all variants, compute `MaxParameterCount` as the maximum `ParameterCount` across all `AssembledSqlVariant` values. This is stored on `AssembledPlan` for pre-allocation in emitted interceptor code.

**Output.** The method returns an `AssembledPlan` containing the original `QueryPlan`, the `SqlVariants` dictionary, reader delegate code, parameter counts, and metadata (entity type, result type, dialect, clause sites, execution site, schema namespace).

---

### Step 5.3: Create CarrierPlan

**Type definition:**

```csharp
internal sealed class CarrierPlan : IEquatable<CarrierPlan>
{
    public CarrierPlan(
        string className,
        string baseClassName,
        bool isEligible,
        string? ineligibleReason,
        IReadOnlyList<CarrierField> fields,
        IReadOnlyList<CarrierStaticField> staticFields,
        IReadOnlyList<CarrierParameter> parameters,
        string maskType,
        int maskBitCount);

    public string ClassName { get; }
    public string BaseClassName { get; }
    public bool IsEligible { get; }
    public string? IneligibleReason { get; }
    public IReadOnlyList<CarrierField> Fields { get; }
    public IReadOnlyList<CarrierStaticField> StaticFields { get; }
    public IReadOnlyList<CarrierParameter> Parameters { get; }
    public string MaskType { get; }
    public int MaskBitCount { get; }
}
```

`CarrierPlan` replaces both `CarrierClassInfo` and `CarrierStrategy` with a single type that carries all information needed by the emitter. It includes the class name (`Chain_0`, `Chain_1`, ...), base class name (resolved from query kind and join shape), eligibility status, instance fields, static fields, parameter metadata, and mask metadata. Unlike `CarrierClassInfo`, which required the emitter to separately consult `PrebuiltChainInfo` for parameter extraction details, `CarrierPlan` is self-contained: the `CarrierParameter` list includes extraction code, binding code, type mapping class, and collection/sensitivity flags.

**Revised CarrierAnalyzer signature:**

```csharp
internal static class CarrierAnalyzer
{
    public static CarrierPlan Analyze(
        AssembledPlan assembled,
        int chainIndex);
}
```

**Eligibility algorithm.** The analyzer applies the following gates in order, returning an ineligible `CarrierPlan` (with `IsEligible = false` and a reason string) at the first failure:

1. **Tier gate.** The plan must be `PrebuiltDispatch` (Tier 1). Tier 2 and Tier 3 chains cannot use carrier optimization because carrier interceptors require a compile-time dispatch table of SQL variants.

2. **Unmatched method gate.** `QueryPlan.UnmatchedMethodNames` must be null. Unmatched methods (e.g., custom extension methods, `AddWhereClause`) indicate that the chain shape is not fully captured by the analyzer. Emitting carrier clause interceptors without a corresponding terminal to consume them would cause runtime failures.

3. **Parameter resolution gate.** Every `QueryParameter` in `QueryPlan.Parameters` must have a resolved `ClrType` (not `"object?"` or `"?"`). Collection parameters must have a resolved `ElementTypeName`. Without resolved types, the carrier cannot declare properly typed fields.

4. **Terminal emittability gate.** The execution terminal must pass emission checks based on its kind:
   - Reader terminals (`ExecuteFetchAll`, `ExecuteFetchFirst`, `ExecuteFetchFirstOrDefault`, `ExecuteFetchSingle`, `ToAsyncEnumerable`): require a resolved result type and non-null `AssembledPlan.ReaderDelegateCode`.
   - Scalar terminals (`ExecuteScalar`): require a resolved result type.
   - Non-query terminals (`ExecuteNonQuery`): require non-empty, well-formed SQL across all variants (no `"SET  "` gaps in UPDATE queries).
   - Insert terminals (`InsertExecuteNonQuery`, `InsertExecuteScalar`, `InsertToDiagnostics`): require non-empty SQL. Additionally, `InsertExecuteScalar` on MySQL is ineligible because MySQL lacks `RETURNING` and requires a separate `SELECT LAST_INSERT_ID()` query that the carrier path cannot issue.

5. **SQL validity gate.** All `AssembledSqlVariant` values in `AssembledPlan.SqlVariants` must have non-empty, non-whitespace SQL strings.

**Field construction.** When all gates pass, the analyzer builds the carrier fields:

- **Parameter fields.** For each `QueryParameter` in `QueryPlan.Parameters` where `EntityPropertyExpression == null` (non-entity-sourced): if `IsCollection` and `ElementTypeName != null`, create a field with type `System.Collections.Generic.IReadOnlyList<{normalized_element_type}>` and role `Collection`. Otherwise, create a field with the normalized CLR type and role `Parameter`. The field name is `P{GlobalIndex}`. Type normalization converts `System.Nullable<T>` to `T?`, appends `?` to reference types that lack it, and passes value types through unchanged. Entity-sourced parameters (from `UpdateSetPoco`) produce `CarrierParameter` entries with `IsEntitySourced = true` but no carrier field, since their values are extracted from the `Entity` field at terminal time.

- **ClauseMask field.** If `QueryPlan.ConditionalTerms` is non-empty, add a `Mask` field. The type is `byte` for up to 8 conditional bits, `ushort` for 9-16, `uint` for 17-32. The `MaskType` and `MaskBitCount` are stored on `CarrierPlan` for the emitter.

- **Limit field.** If `QueryPlan.Pagination` has a non-null `LimitParamIndex`, add a `Limit` field of type `int` with role `Limit`.

- **Offset field.** If `QueryPlan.Pagination` has a non-null `OffsetParamIndex`, add an `Offset` field of type `int` with role `Offset`.

- **Timeout field.** If any clause site in the chain has `ClauseKind.WithTimeout`, add a `Timeout` field of type `TimeSpan?` with role `Timeout`.

- **Entity field.** For `QueryKind.Insert` chains, or `QueryKind.Update` chains with `SetPoco`-sourced parameters, add an `Entity` field typed as `{ShortEntityTypeName}?` with role `Entity`. This field stores the entity object passed to `.Insert()` or `.Set(entity)`.

**Static field caches.** For each `QueryParameter` where `NeedsFieldInfoCache == true` (captured closure variables requiring expression tree extraction at runtime), add a static field `F{GlobalIndex}` of type `FieldInfo?`. These are used by the carrier's parameter extraction code to cache `System.Reflection.FieldInfo` lookups.

**Base class resolution.** The base class is resolved from the query kind, join shape, and projection:

- `QueryKind.Delete` uses `DeleteCarrierBase<TEntity>`.
- `QueryKind.Update` uses `UpdateCarrierBase<TEntity>`.
- `QueryKind.Insert` uses `InsertCarrierBase<TEntity>`.
- `QueryKind.Select` without joins: `CarrierBase<TEntity>` (no projection) or `CarrierBase<TEntity, TResult>` (with projection).
- `QueryKind.Select` with joins: `JoinedCarrierBase<T1, T2[, TResult]>`, `JoinedCarrierBase3<T1, T2, T3[, TResult]>`, `JoinedCarrierBase4<T1, T2, T3, T4[, TResult]>` depending on entity count. The result type, when present, is sanitized for tuple element names.

**CarrierParameter construction.** For each `QueryParameter`, the analyzer builds a `CarrierParameter` with: `GlobalIndex`, `FieldName` (`P{n}`), `FieldType` (normalized), `ExtractionCode` (the `ValueExpression` from `QueryParameter`), `BindingCode` (null; generated by the emitter), `TypeMappingClass`, `IsCollection`, `IsSensitive`, and `IsEntitySourced` (true when `EntityPropertyExpression != null`).

---

### Step 5.4: Update CarrierEmitter

Change `CarrierEmitter` to consume `CarrierPlan` instead of `CarrierStrategy` + `PrebuiltChainInfo`. The current `CarrierEmitter` takes `CarrierStrategy` (which itself was computed from `PrebuiltChainInfo`) and the `PrebuiltChainInfo` for SQL map access. After this step, `CarrierEmitter` takes `CarrierPlan` and `AssembledPlan`, which together provide all needed information.

**Method signature changes:**

```csharp
internal static class CarrierEmitter
{
    // Before: EmitClassDeclaration(sb, CarrierStrategy, className)
    // After:
    public static void EmitClassDeclaration(
        StringBuilder sb, CarrierPlan plan);

    // Before: EmitChainRootBody(sb, className, entityType, contextClass)
    // After:
    public static void EmitChainRootBody(
        StringBuilder sb, CarrierPlan plan, string entityType, string contextClass);

    // Before: EmitClauseBody(sb, className, CarrierStrategy, clauseParameters, ...)
    // After:
    public static void EmitClauseBody(
        StringBuilder sb,
        CarrierPlan plan,
        IReadOnlyList<CarrierParameter> clauseParameters,
        int? clauseBit,
        bool isFirstInChain,
        string concreteBuilderType,
        string returnInterface);

    // Before: EmitTerminalPreamble(sb, className, maskType, maskBitCount)
    // After:
    public static void EmitTerminalPreamble(
        StringBuilder sb, CarrierPlan plan);

    // Before: EmitParameterLocals(sb, IReadOnlyList<CarrierParameter>)
    // After:
    public static void EmitParameterLocals(
        StringBuilder sb, CarrierPlan plan);

    // Before: ResolveCarrierBaseClass(PrebuiltChainInfo)
    // After: removed (base class is on CarrierPlan.BaseClassName)

    // Before: WouldExecutionTerminalBeEmitted(PrebuiltChainInfo)
    // After: removed (eligibility is on CarrierPlan.IsEligible)

    // Before: ResolveCarrierReceiverType(UsageSiteInfo, entityType, PrebuiltChainInfo?)
    // After:
    public static string ResolveCarrierReceiverType(
        TranslatedCallSite site, string entityType, AssembledPlan? plan);
}
```

**Carrier class emission adaptation.** `EmitClassDeclaration` reads all data from `CarrierPlan` directly: `plan.ClassName` for the class name, `plan.BaseClassName` for the inheritance declaration, `plan.Fields` for instance field declarations, `plan.StaticFields` for static field declarations. No lookup into `PrebuiltChainInfo` or `ChainAnalysisResult` is needed.

**Clause body emission adaptation.** `EmitClauseBody` receives the mask type and bit count from `CarrierPlan.MaskType` and `CarrierPlan.MaskBitCount` instead of computing them from `ChainAnalysisResult.ConditionalClauses.Count`. Parameter extraction code comes from `CarrierParameter.ExtractionCode` (sourced from `QueryParameter.ValueExpression` through `CarrierPlan.Parameters`).

**Terminal emission adaptation.** `EmitTerminalPreamble` reads `plan.MaskType` and `plan.MaskBitCount` for the `__opId` computation. SQL variant dispatch reads from `AssembledPlan.SqlVariants` (which contains `AssembledSqlVariant` values) instead of `PrebuiltChainInfo.SqlMap` (which contained `PrebuiltSqlResult` values). The SQL string content is identical; only the container type changes. `EmitParameterLocals` iterates `plan.Parameters` instead of receiving a separate parameter list.

**Removed methods.** `ResolveCarrierBaseClass` is deleted because the base class is pre-computed on `CarrierPlan.BaseClassName`. `WouldExecutionTerminalBeEmitted` is deleted because eligibility is pre-computed as `CarrierPlan.IsEligible`. The helper methods `CanEmitReaderTerminal`, `CanEmitScalarTerminal`, `CanEmitNonQueryTerminal`, `CanEmitInsertTerminal`, and `GetJoinedConcreteBuilderTypeName` are either deleted or moved to `CarrierAnalyzer` as private helpers used during eligibility checks.

---

### Step 5.5: Update PipelineOrchestrator

The collected pipeline stage in `PipelineOrchestrator.AnalyzeAndGroup()` changes to operate on the new types. The current flow is:

1. Receive `UsageSiteInfo[]` (post-Collect).
2. Call legacy `ChainAnalyzer.AnalyzeChain()` per execution site, producing `ChainAnalysisResult[]`.
3. Build `PrebuiltChainInfo` from each `ChainAnalysisResult` using `CompileTimeSqlBuilder`.
4. Build `CarrierClassInfo` from each `PrebuiltChainInfo` using `CarrierClassBuilder`.
5. Group into `FileInterceptorGroup` containing `PrebuiltChainInfo[]`.

The new flow is:

1. Receive `TranslatedCallSite[]` (post-Collect).
2. Call `ChainAnalyzer.Analyze(sites, registry, ct)` producing `QueryPlan[]`.
3. For each `QueryPlan`, call `SqlAssembler.Assemble(plan, registry, dialect, ...)` producing `AssembledPlan`.
4. For each `AssembledPlan`, call `CarrierAnalyzer.Analyze(assembled, chainIndex)` producing `CarrierPlan`.
5. Group into `FileInterceptorGroup` containing `AssembledPlan[]` and `CarrierPlan[]`.

**Revised `AnalyzeAndGroup` signature:**

```csharp
public ImmutableArray<FileInterceptorGroup> AnalyzeAndGroup(
    ImmutableArray<TranslatedCallSite> sites);
```

The method no longer accepts callback delegates for enrichment or chain analysis. The `EntityRegistry` and `Compilation` are constructor-injected (as they are today). The `analyzeChains` delegate parameter is removed because chain analysis is now called directly.

**FileInterceptorGroup changes.** The `Chains` property type changes from `IReadOnlyList<PrebuiltChainInfo>` to `IReadOnlyList<AssembledPlan>`. A new `CarrierPlans` property of type `IReadOnlyList<CarrierPlan>` is added, indexed parallel to `Chains` (i.e., `CarrierPlans[i]` is the carrier plan for `Chains[i]`). The `Sites` property type changes from `IReadOnlyList<UsageSiteInfo>` to `IReadOnlyList<TranslatedCallSite>`. The `ChainMemberSites` property similarly becomes `IReadOnlyList<TranslatedCallSite>`.

**Diagnostic collection.** The diagnostic collection methods (`CollectAmbiguityDiagnostics`, `CollectUnboundParameterDiagnostics`, `CollectClauseNotTranslatableDiagnostics`) are adapted to read from `TranslatedCallSite` properties. Ambiguity diagnostics read `site.Bound.Raw.EntityTypeName`. Unbound parameter diagnostics inspect `site.Clause.Parameters` and `site.Clause.ResolvedExpression`. Non-translatable clause diagnostics check `site.Clause.IsSuccess`.

**Chain member site merging.** The chain member merge logic (adding non-analyzable clause sites pulled in by chain analysis) operates on `TranslatedCallSite` values and their `Bound.Raw.UniqueId` for identity.

---

### Step 5.6: Delete Old Types

The following types are deleted in this phase, with their files removed from the project:

| Type | File | Lines | Reason |
|---|---|---|---|
| `ChainAnalysisResult` | `Models/ChainAnalysisResult.cs` | 264 | Replaced by `QueryPlan.Tier`, `QueryPlan.ConditionalTerms`, `QueryPlan.PossibleMasks` |
| `ChainedClauseSite` | `Models/ChainAnalysisResult.cs` | (included above) | Clause sites are now `TranslatedCallSite` values with `BitIndex` metadata |
| `ConditionalClause` (in Models) | `Models/ChainAnalysisResult.cs` | (included above) | Replaced by `ConditionalTerm` on `QueryPlan` |
| `PrebuiltChainInfo` | `Models/PrebuiltChainInfo.cs` | 177 | Replaced by `AssembledPlan` |
| `ChainParameterInfo` | `Models/ChainParameterInfo.cs` | 141 | Replaced by `QueryParameter` on `QueryPlan` and `CarrierParameter` on `CarrierPlan` |
| `CarrierClassInfo` | `Models/CarrierClassInfo.cs` | 219 | Replaced by `CarrierPlan` |
| `CarrierField` (in Models) | `Models/CarrierClassInfo.cs` | (included above) | Replaced by `CarrierField` in `CodeGen/CarrierStrategy.cs` (already exists) |
| `CarrierInterfaceStub` | `Models/CarrierClassInfo.cs` | (included above) | Dead method stubs no longer needed; carrier classes do not implement builder interfaces |
| `CarrierStaticField` (in Models) | `Models/CarrierClassInfo.cs` | (included above) | Replaced by `CarrierStaticField` in `CodeGen/CarrierStrategy.cs` (already exists) |
| `CompileTimeSqlBuilder` | `Sql/CompileTimeSqlBuilder.cs` | 976 | Replaced by `SqlAssembler` |
| `PrebuiltSqlResult` | `Sql/CompileTimeSqlBuilder.cs` | (included above) | Replaced by `AssembledSqlVariant` |
| `InsertSqlResult` | `Sql/CompileTimeSqlBuilder.cs` | (included above) | Insert SQL rendering absorbed into `SqlAssembler` |
| `SqlFragmentTemplate` | `Sql/SqlFragmentTemplate.cs` | 217 | No longer needed; `SqlExprRenderer` renders expressions directly |
| `CarrierClassBuilder` | `Generation/CarrierClassBuilder.cs` | 200 | Replaced by `CarrierAnalyzer.Analyze()` producing `CarrierPlan` |

**Total lines removed:** approximately 2,194 (264 + 177 + 141 + 219 + 976 + 217 + 200 from the file-level deletions). Additional dead code removed from `PipelineOrchestrator` (the `analyzeChains` delegate parameter and associated callback infrastructure, approximately 50 lines) and from `FileInterceptorGroup` (the `PrebuiltChainInfo`-typed properties, approximately 20 lines).

**Net line count change.** The new files created are `IR/SqlAssembler.cs` (estimated 450-550 lines) and `CodeGen/CarrierPlan.cs` (estimated 120-150 lines). The rewritten `ChainAnalyzer.cs` is estimated at 500-600 lines (down from 1,159 lines), because syntax tree walking, `SemanticModel` dataflow analysis, `MethodBodyCache`, `VariableFlowGraph`, `FlowNode`, and `BranchPoint` internal types are eliminated -- conditional detection is moved to the per-site binding phase. Net reduction is approximately 1,000-1,300 lines.

---

### Validation

All 2,929 existing tests must continue to pass with byte-identical generated output. The test suite covers the full range of query patterns: single-table selects, multi-table joins (inner, left, right), conditional clauses (if/else branching), multi-mask dispatch, delete/update/insert operations, collection parameters with IN expansion, enum parameters with underlying type casts, type-mapped columns, pagination (literal and parameterized), scalar execution, async enumerable streaming, diagnostics output, raw SQL passthrough, and carrier-optimized paths.

Specific validation points:

- **SQL output equivalence.** Every `AssembledSqlVariant.Sql` produced by `SqlAssembler` must be character-identical to the corresponding `PrebuiltSqlResult.Sql` produced by `CompileTimeSqlBuilder` for the same chain and mask value. This is verified by the `CompileTimeRuntimeEquivalenceTests` and the cross-dialect SQL output tests covering SqlServer, PostgreSQL, MySQL, and SQLite dialects.

- **Carrier optimization preserved.** Every chain that was previously carrier-eligible must remain carrier-eligible. Every chain that was previously ineligible must remain ineligible for the same reason. This is verified by the `CarrierStrategyTests` and by comparing the generated interceptor code for carrier-path chains (the clause body, terminal body, and class declaration must produce functionally equivalent output).

- **Tier classification preserved.** The tier assigned to each chain by the rewritten `ChainAnalyzer` must match the tier assigned by the current implementation. Tier 1 chains remain Tier 1; chains downgraded to Tier 2 or Tier 3 remain at the same tier. This is verified by the `ChainAnalyzerTests`.

- **Mask enumeration preserved.** The set of possible mask values for each Tier 1 chain must be identical. The `ChainAnalyzerTests` and `ExecutionInterceptorTests` validate correct mask combinations for independent and mutually exclusive conditional clauses.

- **Parameter index mapping preserved.** The global parameter indices assigned by the rewritten analyzer must produce the same parameter binding order as the current implementation. This is critical for SQL output correctness and is covered by `ParameterTests`, `EnumParameterTests`, and `TypeMappingExpressionTests`.

- **Incremental caching correctness.** All new types (`CarrierPlan`, modified `FileInterceptorGroup`) implement `IEquatable<T>` with correct `Equals()` and `GetHashCode()`. The `IncrementalCachingTests` verify that the pipeline correctly identifies cache hits when input values are structurally equal.

---

### Files Changed

| File | Action | Estimated Lines |
|---|---|---|
| `Parsing/ChainAnalyzer.cs` | **Rewrite** -- new algorithm operating on `TranslatedCallSite[]`, producing `QueryPlan[]` | ~550 (was 1,159) |
| `IR/SqlAssembler.cs` | **New** -- renders SQL from `QueryPlan` using `SqlExprRenderer` | ~500 |
| `CodeGen/CarrierPlan.cs` | **New** -- `CarrierPlan` type definition with `IEquatable<CarrierPlan>` | ~130 |
| `CodeGen/CarrierAnalyzer.cs` | **Rewrite** -- consumes `AssembledPlan`, produces `CarrierPlan` | ~250 (was 243) |
| `CodeGen/CarrierEmitter.cs` | **Modify** -- consume `CarrierPlan` instead of `CarrierStrategy` + `PrebuiltChainInfo` | ~1,000 (was 1,039) |
| `IR/PipelineOrchestrator.cs` | **Modify** -- new pipeline flow with `TranslatedCallSite[]` input | ~250 (was 257) |
| `Models/FileInterceptorGroup.cs` | **Modify** -- `Chains` becomes `AssembledPlan[]`, add `CarrierPlans`, `Sites` becomes `TranslatedCallSite[]` | ~100 (was 93) |
| `Models/ChainAnalysisResult.cs` | **Delete** | -264 |
| `Models/PrebuiltChainInfo.cs` | **Delete** | -177 |
| `Models/ChainParameterInfo.cs` | **Delete** | -141 |
| `Models/CarrierClassInfo.cs` | **Delete** | -219 |
| `Sql/CompileTimeSqlBuilder.cs` | **Delete** | -976 |
| `Sql/SqlFragmentTemplate.cs` | **Delete** | -217 |
| `Generation/CarrierClassBuilder.cs` | **Delete** | -200 |
| `Tests/ChainAnalyzerTests.cs` | **Modify** -- update to new `ChainAnalyzer.Analyze()` signature and `QueryPlan` assertions | modified |
| `Tests/CompileTimeSqlBuilderTests.cs` | **Replace** -- becomes `SqlAssemblerTests.cs` testing `SqlAssembler.Assemble()` | replaced |
| `Tests/IR/CarrierStrategyTests.cs` | **Modify** -- update to assert `CarrierPlan` output | modified |

---

## 8. Phase 6B: Codegen Completion

### 8.1 Objective

Phase 6B rewrites the six body emitters and the `FileEmitter` orchestrator to consume the new IR types (`TranslatedCallSite`, `AssembledPlan`, `CarrierPlan`) directly, eliminating the conversion layer that currently maps new types back to `UsageSiteInfo`, `PrebuiltChainInfo`, and `CarrierClassInfo` at the pipeline boundary. Once all emitters consume the new types, the old types are deleted entirely, completing the compiler architecture migration. This is the final phase -- after 6B, no old god-object types remain in the codebase.

### 8.2 Migration Order and Rationale

The emitters are migrated one at a time, smallest first, so that each step is a self-contained green commit with minimal blast radius.

1. **RawSqlBodyEmitter** (165 lines, 2 public methods). Simplest emitter. No dependency on `PrebuiltChainInfo` or `CarrierClassInfo` -- reads only `UsageSiteInfo.RawSqlTypeInfo`. Maps to `TranslatedCallSite.Bound.RawSqlTypeInfo` mechanically. ~4 tests. Low-risk proof-of-concept for the migration pattern.

2. **TransitionBodyEmitter** (175 lines, 6 public methods). Reads `UsageSiteInfo.EntityTypeName`, `.Kind`, `.BuilderKind`, `.ContextClassName`. Two methods accept `CarrierClassInfo`, three accept both `CarrierClassInfo` and `PrebuiltChainInfo`. No clause translation or SQL rendering involved -- validates carrier type migration without clause complexity.

3. **ClauseBodyEmitter** (1,244 lines, 10 public methods). Largest emitter, first to exercise clause data migration. Every method reads `UsageSiteInfo.ClauseInfo` or subclass fields. On new types, clause data is `TranslatedCallSite.Clause` (`TranslatedClause`), and SQL is rendered from `TranslatedClause.ResolvedExpression` via `SqlExprRenderer.Render()` instead of pre-rendered string. Validates the core clause data mapping.

4. **JoinBodyEmitter** (770 lines, 4 public methods). Builds on clause migration pattern from step 3, adds join-specific data: `JoinClauseInfo.JoinedEntityName`, `.JoinedTableName`, `.OnConditionSql`, `UsageSiteInfo.JoinedEntityTypeNames`. Maps to `TranslatedClause.JoinKind`, `.JoinedTableName`, rendered `ResolvedExpression`, and `TranslatedCallSite.Bound.JoinedEntityTypeNames`.

5. **TerminalBodyEmitter** (666 lines, 8 public methods). Heaviest consumer of `PrebuiltChainInfo`. Reads `.SqlMap`, `.ReaderDelegateCode`, `.MaxParameterCount`, `.EntityTypeName`, `.ResultTypeName`, `.QueryKind`, `.Dialect`, `.IsJoinChain`. Maps to `AssembledPlan.SqlVariants`, `.ReaderDelegateCode`, `.MaxParameterCount`, `.EntityTypeName`, `.ResultTypeName`, `.Plan.Kind`, `.Dialect`, `.Plan.Joins.Count >= 1`. Deferred until after ClauseBodyEmitter because terminal methods also delegate to `CarrierEmitter` methods needing both old types updated simultaneously.

6. **FileEmitter** (683 lines, 1 public entry point). Migrated last because it constructs the lookup dictionaries threading old types into every body emitter. Rewritten to construct lookups from `TranslatedCallSite[]`, `AssembledPlan[]`, and `CarrierPlan[]`. Final step removing all old type imports from codegen.

Each step produces a green commit. The conversion layer narrows after each step and is deleted after step 6.

### 8.3 Step 6B.1: Migrate RawSqlBodyEmitter

**Current signatures:**

```csharp
public static void EmitRawSqlAsync(StringBuilder sb, UsageSiteInfo site, string methodName)
public static void EmitRawSqlScalarAsync(StringBuilder sb, UsageSiteInfo site, string methodName)
```

**New signatures:**

```csharp
public static void EmitRawSqlAsync(StringBuilder sb, TranslatedCallSite site, string methodName)
public static void EmitRawSqlScalarAsync(StringBuilder sb, TranslatedCallSite site, string methodName)
```

**Field access changes.** `site.RawSqlTypeInfo` becomes `site.Bound.RawSqlTypeInfo`. The `RawSqlTypeInfo` type is unchanged; it already lives on `BoundCallSite.RawSqlTypeInfo`. Private helpers `GeneratePropertyAssignment` and `GenerateScalarConverter` are unchanged since they operate on `RawSqlPropertyInfo`.

**Call site update in FileEmitter.** During this step, `FileEmitter` continues to hold `UsageSiteInfo` internally but constructs a `TranslatedCallSite` wrapper for the two RawSql dispatch cases only. This temporary bridge is removed in step 6B.6.

**Test migration.** A test helper `CreateRawSqlCallSite(RawSqlTypeInfo info, InterceptorKind kind)` is introduced that populates the three-layer IR with sensible defaults.

**Commit scope:** `RawSqlBodyEmitter.cs`, test files, temporary bridge in `FileEmitter` dispatch.

### 8.4 Step 6B.2: Migrate TransitionBodyEmitter

**Signature changes:**

| Method | Old `site` type | New `site` type | Old carrier | New carrier | Old chain | New chain |
|---|---|---|---|---|---|---|
| `EmitChainRoot` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo` | `CarrierPlan` | -- | -- |
| `EmitPagination` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo` | `CarrierPlan` | `PrebuiltChainInfo` | `AssembledPlan` |
| `EmitDistinct` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo` | `CarrierPlan` | `PrebuiltChainInfo?` | `AssembledPlan?` |
| `EmitWithTimeout` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo` | `CarrierPlan` | `PrebuiltChainInfo?` | `AssembledPlan?` |
| `EmitDeleteUpdateTransition` | `UsageSiteInfo` | `TranslatedCallSite` | -- | -- | -- | -- |
| `EmitInsertTransition` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo` | `CarrierPlan` | -- | -- |
| `EmitAllTransition` | `UsageSiteInfo` | `TranslatedCallSite` | `CarrierClassInfo?` | `CarrierPlan?` | -- | -- |

**Field access changes.** `site.EntityTypeName` -> `site.Bound.Raw.EntityTypeName`. `site.Kind` -> `site.Bound.Raw.Kind`. `site.BuilderKind` -> `site.Bound.Raw.BuilderKind`. `site.BuilderTypeName` -> `site.Bound.Raw.BuilderTypeName`. `site.ContextClassName` -> `site.Bound.ContextClassName`.

**CarrierPlan replaces CarrierClassInfo.** `CarrierPlan` wraps the same data (class name, interfaces, fields, dead methods, static fields). Field checks like `CarrierEmitter.HasCarrierField(carrier, FieldRole.Limit)` work identically because `CarrierPlan.Fields` contains the same `CarrierField` objects.

**AssembledPlan replaces PrebuiltChainInfo.** `CarrierEmitter.ResolveCarrierReceiverType` is updated: `chain.QueryKind` -> `plan.Plan.Kind`, `chain.IsJoinChain` -> `plan.Plan.Joins.Count >= 1`.

**Commit scope:** `TransitionBodyEmitter.cs`, `CarrierEmitter.ResolveCarrierReceiverType`, test files, `FileEmitter` bridge.

### 8.5 Step 6B.3: Migrate ClauseBodyEmitter

**Signature pattern (all 10 methods follow this):**

```csharp
// Old: (StringBuilder sb, UsageSiteInfo site, ..., PrebuiltChainInfo? prebuiltChain, ..., CarrierClassInfo? carrier)
// New: (StringBuilder sb, TranslatedCallSite site, ..., AssembledPlan? plan, ..., CarrierPlan? carrier)
```

Applies to: `EmitWhere`, `EmitOrderBy`, `EmitSelect`, `EmitGroupBy`, `EmitHaving`, `EmitSet`, `EmitModificationWhere`, `EmitUpdateSet`, `EmitUpdateSetAction`, `EmitUpdateSetPoco`.

**TranslatedClause replaces ClauseInfo.** `site.ClauseInfo` -> `site.Clause`. `clauseInfo.IsSuccess` -> `site.Clause.IsSuccess`. `clauseInfo.Kind` -> `site.Clause.Kind`. `clauseInfo.Parameters` -> `site.Clause.Parameters` (same `ParameterInfo` type).

**SqlFragment rendered from ResolvedExpression.** Most significant change. Current emitters read `clauseInfo.SqlFragment` (pre-rendered string). New emitters call `SqlExprRenderer.Render(site.Clause.ResolvedExpression, site.Bound.Dialect)`. A private helper is introduced:

```csharp
private static string RenderClauseSql(TranslatedClause clause, SqlDialect dialect)
```

**Subclass-specific mappings.** `OrderByClauseInfo`: `.ColumnSql` -> render `ResolvedExpression`, `.IsDescending` -> `site.Clause.IsDescending`, `.KeyTypeName` -> `site.KeyTypeName`. `SetClauseInfo`: `.ColumnSql` -> render column portion, `.ParameterIndex` -> `site.Clause.Parameters[0].Index`, `.CustomTypeMappingClass` -> `site.Clause.CustomTypeMappingClass`, `.ValueTypeName` -> `site.ValueTypeName`. `SetActionClauseInfo`: `.Assignments` -> `site.Clause.SetAssignments`.

**CachedExtractorField.** `CollectStaticFields` updated to iterate `TranslatedCallSite` and read `site.Clause.Parameters`. Type itself unchanged.

**Parameter access patterns unchanged.** `TranslatedClause.Parameters` contains same `ParameterInfo` objects.

**PrebuiltChainInfo -> AssembledPlan.** `prebuiltChain.MaxParameterCount` -> `plan.MaxParameterCount`. `prebuiltChain.ChainParameters` -> derived from `plan.Plan.Parameters`. `prebuiltChain.Analysis.Clauses` iteration -> `plan.ClauseSites` iteration.

**Commit scope:** `ClauseBodyEmitter.cs` (10 methods), `RenderClauseSql` helper, static field collection update, ~25 tests, `FileEmitter` bridge.

### 8.6 Step 6B.4: Migrate JoinBodyEmitter

**Signature pattern (all 4 methods):**

```csharp
// Old: (StringBuilder sb, UsageSiteInfo site, ..., PrebuiltChainInfo? prebuiltChain, ..., CarrierClassInfo? carrier)
// New: (StringBuilder sb, TranslatedCallSite site, ..., AssembledPlan? plan, ..., CarrierPlan? carrier)
```

Applies to: `EmitJoin`, `EmitJoinedWhere`, `EmitJoinedOrderBy`, `EmitJoinedSelect`.

**Join-specific mappings.** `site.JoinedEntityTypeNames` -> `site.Bound.JoinedEntityTypeNames`. `site.JoinedEntityTypeName` -> `site.Bound.Raw.JoinedEntityTypeName`. `site.IsNavigationJoin` -> `site.Bound.Raw.IsNavigationJoin`. `site.ClauseInfo as JoinClauseInfo` -> `site.Clause` (where `Kind == ClauseKind.Join`). `joinClause.JoinedEntityName` -> `site.Bound.Raw.JoinedEntityTypeName`. `joinClause.JoinedTableName` -> `site.Clause.JoinedTableName`. `joinClause.JoinedSchemaName` -> `site.Clause.JoinedSchemaName`. `joinClause.TableAlias` -> `site.Clause.TableAlias`. `joinClause.OnConditionSql` -> `SqlExprRenderer.Render(site.Clause.ResolvedExpression, site.Bound.Dialect)`. `joinClause.JoinKind` -> `site.Clause.JoinKind`.

**EmitJoin code paths.** `prebuiltChain.Analysis.Clauses` iteration for parameter offsets -> `plan.ClauseSites` iteration. `clause.Site.ClauseInfo.Parameters.Count` -> `clauseSite.Clause.Parameters.Count`. `site.BuilderTypeName` -> `site.Bound.Raw.BuilderTypeName`.

**EmitJoinedWhere/OrderBy/Select.** Follow clause migration pattern from step 6B.3. Carrier-path code shifts from `prebuiltChain.Analysis.Clauses` to `plan.ClauseSites`.

**Commit scope:** `JoinBodyEmitter.cs` (4 methods), ~10 tests, `FileEmitter` bridge.

### 8.7 Step 6B.5: Migrate TerminalBodyEmitter

**Signature pattern (all 8 methods):**

```csharp
// Old: (StringBuilder sb, UsageSiteInfo site, ..., PrebuiltChainInfo chain, CarrierClassInfo? carrier)
// New: (StringBuilder sb, TranslatedCallSite site, ..., AssembledPlan plan, CarrierPlan? carrier)
```

Applies to: `EmitReaderTerminal`, `EmitJoinReaderTerminal`, `EmitNonQueryTerminal`, `EmitDiagnosticsTerminal`, `EmitRuntimeDiagnosticsTerminal`, `EmitInsertNonQueryTerminal`, `EmitInsertScalarTerminal`, `EmitInsertDiagnosticsTerminal`.

**SqlVariants replaces SqlMap.** `chain.SqlMap` (`Dictionary<ulong, PrebuiltSqlResult>`) -> `plan.SqlVariants` (`Dictionary<ulong, AssembledSqlVariant>`). Same `Sql` and `ParameterCount` fields. `InterceptorCodeGenerator.GenerateDispatchTable` parameter type updated.

**Other mappings.** `chain.ReaderDelegateCode` -> `plan.ReaderDelegateCode`. `chain.EntityTypeName` -> `plan.EntityTypeName`. `chain.ResultTypeName` -> `plan.ResultTypeName`. `chain.MaxParameterCount` -> `plan.MaxParameterCount`. `chain.QueryKind` -> `plan.Plan.Kind`. `chain.Dialect` -> `plan.Dialect`. `chain.TableName` -> `plan.Plan.PrimaryTable.TableName`. `chain.IsJoinChain` -> `plan.Plan.Joins.Count >= 1`. `chain.Analysis.UnmatchedMethodNames` -> `plan.Plan.UnmatchedMethodNames`. `chain.Analysis.Tier` -> `plan.Plan.Tier`.

**UsageSiteInfo mappings.** `site.Kind` -> `site.Bound.Raw.Kind`. `site.BuilderTypeName` -> `site.Bound.Raw.BuilderTypeName`. `site.ResultTypeName` -> `site.Bound.Raw.ResultTypeName`. `site.EntityTypeName` -> `site.Bound.Raw.EntityTypeName`. `site.BuilderKind` -> `site.Bound.Raw.BuilderKind`. `site.InsertInfo` -> `site.Bound.InsertInfo`. `site.JoinedEntityTypeNames` -> `site.Bound.JoinedEntityTypeNames`.

**CarrierEmitter delegation.** `EmitCarrierExecutionTerminal`, `EmitCarrierNonQueryTerminal`, `EmitCarrierInsertTerminal`, `EmitCarrierToDiagnosticsTerminal`, `EmitCarrierInsertToDiagnosticsTerminal` updated to accept `CarrierPlan` and `AssembledPlan`.

**Commit scope:** `TerminalBodyEmitter.cs` (8 methods), affected `CarrierEmitter` methods, `GenerateDispatchTable` update, ~15 tests, `FileEmitter` bridge.

### 8.8 Step 6B.6: Migrate FileEmitter

**Constructor change:**

```csharp
// Old
public FileEmitter(string contextClassName, string? contextNamespace, string fileTag,
    IReadOnlyList<UsageSiteInfo> sites, IReadOnlyList<PrebuiltChainInfo>? chains)
// New
public FileEmitter(string contextClassName, string? contextNamespace, string fileTag,
    IReadOnlyList<TranslatedCallSite> sites, IReadOnlyList<AssembledPlan>? plans)
```

**Lookup type changes.** `chainLookup`: `Dictionary<string, PrebuiltChainInfo>` -> `Dictionary<string, AssembledPlan>`. `carrierLookup`: `Dictionary<string, (CarrierClassInfo, PrebuiltChainInfo)>` -> `Dictionary<string, (CarrierPlan, AssembledPlan)>`.

**Field accesses in EmitInterceptorMethod.** `site.UniqueId` -> `site.Bound.Raw.UniqueId`. `site.Kind` -> `site.Bound.Raw.Kind`. `site.MethodName` -> `site.Bound.Raw.MethodName`. `site.FilePath` -> `site.Bound.Raw.FilePath`. `site.Line`/`.Column` -> `site.Bound.Raw.Line`/`.Column`. `site.IsAnalyzable` -> `site.Bound.Raw.IsAnalyzable`. `site.InterceptableLocationData`/`Version` -> `site.Bound.Raw.InterceptableLocationData`/`Version`. `site.JoinedEntityTypeNames` -> `site.Bound.JoinedEntityTypeNames`. `site.ResultTypeName` -> `site.Bound.Raw.ResultTypeName`. `site.BuilderKind` -> `site.Bound.Raw.BuilderKind`. `site.ProjectionInfo` -> `site.Bound.Raw.ProjectionInfo`.

**Static field collection.** `CollectStaticFields`, `CollectMappingInstances`, `CollectEntityReaderInstances` updated to accept `IReadOnlyList<TranslatedCallSite>`.

**Chain grouping.** `chain.Analysis.Clauses` -> `plan.ClauseSites`. `chain.Analysis.ExecutionSite.UniqueId` -> `plan.ExecutionSite.Bound.Raw.UniqueId`.

**Carrier lookup construction.** Loop iterates `_plans`, checks `CarrierAnalyzer.IsEligible(plan)`, calls `CarrierPlanBuilder.Build(plan, carrierIndex, resolvedBase)`.

**Conversion layer removal.** `PipelineOrchestrator` conversion layer deleted. Pipeline flows directly: `TranslatedCallSite[]` -> `AssembledPlan[]` -> `FileEmitter`.

**Commit scope:** `FileEmitter.cs`, `PipelineOrchestrator` conversion deletion, utility method updates, test files.

### 8.9 Step 6B.7: Delete Old Types

| Type | File | Lines |
|---|---|---|
| `UsageSiteInfo` | `Models/UsageSiteInfo.cs` | 519 |
| `PrebuiltChainInfo` | `Models/PrebuiltChainInfo.cs` | 177 |
| `ChainAnalysisResult`, `ChainedClauseSite`, `ConditionalClause` | `Models/ChainAnalysisResult.cs` | 264 |
| `ClauseInfo`, `OrderByClauseInfo`, `SetClauseInfo`, `SetActionClauseInfo`, `JoinClauseInfo` | `Models/ClauseInfo.cs` | 425 |
| `PendingClauseInfo` | `Models/PendingClauseInfo.cs` | 61 |
| `ChainParameterInfo` | `Models/ChainParameterInfo.cs` | 141 |
| `PrebuiltSqlResult` | `Sql/CompileTimeSqlBuilder.cs` | ~30 |
| `CarrierClassInfo` | `Models/CarrierClassInfo.cs` | 219 |

**Enums extracted before deletion.** `InterceptorKind`, `BuilderKind` -> `Models/InterceptorKind.cs`. `QueryKind` -> `Models/QueryKind.cs`. `OptimizationTier`, `ClauseRole`, `BranchKind` -> `Models/OptimizationTier.cs`. `ClauseKind`, `JoinClauseKind` -> `Models/ClauseKind.cs`.

**Types extracted before deletion.** `CarrierField`, `CarrierInterfaceStub`, `CarrierStaticField`, `FieldRole` -> `Models/CarrierField.cs`. `SetActionAssignment` -> `Models/SetActionAssignment.cs`.

**Net reduction.** ~1,836 lines of old model types + ~100-150 lines of conversion layer = ~1,950-2,000 lines removed.

### 8.10 Step 6B.8: Test Migration Pattern

**TestCallSiteBuilder (fluent API hiding IR layering):**

```csharp
internal sealed class TestCallSiteBuilder
{
    public static TestCallSiteBuilder WhereClause(string entityTypeName)
    public static TestCallSiteBuilder OrderByClause(string entityTypeName)
    public static TestCallSiteBuilder SelectClause(string entityTypeName)
    public static TestCallSiteBuilder RawSql(RawSqlTypeInfo rawSqlInfo)
    public static TestCallSiteBuilder Terminal(InterceptorKind kind, string entityTypeName)
    public static TestCallSiteBuilder Transition(InterceptorKind kind, string entityTypeName)
    public TestCallSiteBuilder WithKind(InterceptorKind kind)
    public TestCallSiteBuilder WithBuilderKind(BuilderKind kind)
    public TestCallSiteBuilder WithResultType(string resultTypeName)
    public TestCallSiteBuilder WithClause(TranslatedClause clause)
    public TestCallSiteBuilder WithDialect(SqlDialect dialect)
    public TestCallSiteBuilder WithBuilderTypeName(string typeName)
    public TestCallSiteBuilder WithContextClassName(string className)
    public TestCallSiteBuilder WithJoinedEntityTypeNames(IReadOnlyList<string> names)
    public TestCallSiteBuilder WithInsertInfo(InsertInfo info)
    public TestCallSiteBuilder WithProjectionInfo(ProjectionInfo info)
    public TestCallSiteBuilder WithKeyTypeName(string keyType)
    public TestCallSiteBuilder WithValueTypeName(string valueType)
    public TestCallSiteBuilder IsNavigationJoin(bool value)
    public TranslatedCallSite Build()
}
```

`Build()` constructs `RawCallSite` (file `"test.cs"`, line 1, column 1, auto-ID, placeholder location data) -> `BoundCallSite` (`SqlDialect.PostgreSQL`, table `"test_table"`, dummy `EntityRef`) -> `TranslatedCallSite` with configured clause.

**TestPlanBuilder:**

```csharp
internal sealed class TestPlanBuilder
{
    public static TestPlanBuilder ForQuery(QueryKind kind, string entityTypeName)
    public TestPlanBuilder WithSqlVariant(ulong mask, string sql, int paramCount)
    public TestPlanBuilder WithReaderDelegate(string code)
    public TestPlanBuilder WithMaxParameterCount(int count)
    public TestPlanBuilder WithResultType(string resultTypeName)
    public TestPlanBuilder WithDialect(SqlDialect dialect)
    public TestPlanBuilder WithJoins(IReadOnlyList<JoinPlan> joins)
    public AssembledPlan Build()
}
```

**Migration example (Where clause).** Old: `new UsageSiteInfo(..., clauseInfo: ClauseInfo.Success(ClauseKind.Where, "\"Name\" = @p0", params))`. New: `TestCallSiteBuilder.WhereClause("User").WithClause(new TranslatedClause(ClauseKind.Where, new BinaryOpExpr(Equal, new ResolvedColumnExpr("\"Name\""), new ParamSlotExpr(0)), params)).WithBuilderTypeName("IQueryBuilder").Build()`. SQL is now an `SqlExpr` tree; `SqlExprRenderer.Render()` produces the same string. Assertions unchanged.

**Test counts:** RawSql ~4, Transition ~6, Clause ~25, Join ~10, Terminal ~15, FileEmitter ~0 (E2E). Total ~60.

### 8.11 Validation

- **All 2,929 existing tests pass.** Only fixture construction changes; no assertion modifications.
- **Byte-identical output.** Generated interceptor source text matches before and after for every test case.
- **No old type references remain.** Codebase search for `UsageSiteInfo`, `PrebuiltChainInfo`, `ChainAnalysisResult`, `ClauseInfo` (class), `CarrierClassInfo`, `PendingClauseInfo`, `ChainParameterInfo` returns zero hits outside git history and docs.
- **No conversion layer remains.** Pipeline flows `TranslatedCallSite[]` -> `AssembledPlan[]` -> `FileEmitter` directly.
- **CarrierEmitter fully migrated.** All public methods accept `CarrierPlan` and `AssembledPlan`.

### 8.12 Files Changed

| File | Action | Step |
|---|---|---|
| `CodeGen/RawSqlBodyEmitter.cs` | Modify signatures and field accesses | 6B.1 |
| `CodeGen/TransitionBodyEmitter.cs` | Modify signatures and field accesses | 6B.2 |
| `CodeGen/CarrierEmitter.cs` | Modify signatures progressively | 6B.2-6B.5 |
| `CodeGen/ClauseBodyEmitter.cs` | Modify signatures, add `RenderClauseSql`, update 10 methods | 6B.3 |
| `CodeGen/JoinBodyEmitter.cs` | Modify signatures and join-specific accesses | 6B.4 |
| `CodeGen/TerminalBodyEmitter.cs` | Modify signatures, SqlMap -> SqlVariants, 8 methods | 6B.5 |
| `CodeGen/FileEmitter.cs` | Rewrite constructor, field types, lookups, dispatch | 6B.6 |
| `Generation/InterceptorCodeGenerator.cs` | Update `GenerateDispatchTable` and utility signatures | 6B.5-6B.6 |
| `Generation/InterceptorCodeGenerator.Utilities.cs` | Update `CollectStaticFields`, `CollectMappingInstances`, `CollectEntityReaderInstances` | 6B.6 |
| `Generation/CarrierClassBuilder.cs` | Replace with `CarrierPlanBuilder` | 6B.6 |
| `IR/PipelineOrchestrator.cs` | Remove conversion layer | 6B.6 |
| `Models/UsageSiteInfo.cs` | Delete (extract enums first) | 6B.7 |
| `Models/PrebuiltChainInfo.cs` | Delete (extract enums first) | 6B.7 |
| `Models/ChainAnalysisResult.cs` | Delete (extract enums first) | 6B.7 |
| `Models/ClauseInfo.cs` | Delete (extract enums, `SetActionAssignment` first) | 6B.7 |
| `Models/CarrierClassInfo.cs` | Delete (extract `CarrierField` etc. first) | 6B.7 |
| `Models/PendingClauseInfo.cs` | Delete | 6B.7 |
| `Models/ChainParameterInfo.cs` | Delete | 6B.7 |
| `Sql/CompileTimeSqlBuilder.cs` | Remove `PrebuiltSqlResult` | 6B.7 |
| Test files (~15 files) | Migrate fixture construction | 6B.1-6B.5 |
| New: `Testing/TestCallSiteBuilder.cs` | Test fixture builder for `TranslatedCallSite` | 6B.1 |
| New: `Testing/TestPlanBuilder.cs` | Test fixture builder for `AssembledPlan` | 6B.2 |
| New: `Models/InterceptorKind.cs` | Enum extracted from `UsageSiteInfo.cs` | 6B.7 |
| New: `Models/ClauseKind.cs` | Enums extracted from `ClauseInfo.cs` | 6B.7 |
| New: `Models/CarrierField.cs` | Types extracted from `CarrierClassInfo.cs` | 6B.7 |
| New: `Models/SetActionAssignment.cs` | Type extracted from `ClauseInfo.cs` | 6B.7 |
| New: `IR/CarrierPlan.cs` | New carrier type replacing `CarrierClassInfo` | 6B.2 |
