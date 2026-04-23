# Quarry Source Generator — LLM Reference

Compile-time SQL query generator for .NET. Analyzes C# lambda expressions, generates SQL + typed interceptors via Roslyn incremental source generation. Supports SQLite, PostgreSQL, MySQL, SQL Server.

## Usage (for helping users build with Quarry)

### Schema Definition
```csharp
public class UserSchema : Schema
{
    public static string Table => "users";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}
```
Column types: `Col<T>`, `Key<T>` (PK), `Ref<TSchema, TKey>` (FK), `Many<T>` (1:N navigation).
Modifiers: `Identity()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `ClientGenerated()`, `Computed<T>()`, `IsSensitive()`.

### Context
```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

### Query API
```csharp
var results = await db.Users()
    .Where(u => u.IsActive && u.CreatedAt > cutoff)
    .Select(u => (u.UserId, u.UserName))
    .OrderBy(u => u.UserName)
    .Limit(10)
    .Prepare()
    .ExecuteFetchAllAsync();
```
Terminals: `ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ExecuteFetchSingleOrDefaultAsync`, `ExecuteScalarAsync<T>`, `ExecuteNonQueryAsync`, `ToAsyncEnumerable`, `ToDiagnostics`.

Subqueries via navigation: `.Where(u => u.Orders.Any(o => o.Total > 100))` generates `EXISTS(SELECT 1 ...)`.

Joins: `.Join<Order>((u, o) => u.UserId == o.UserId).Select(...)`.

DML: `.Delete().Where(...)`, `.Update().Set(u => u.Name, "val").Where(...)`, `.Insert(entity)`, `.BatchInsert(list, o => (o.Col1, o.Col2))`.

## Architecture Overview

### Pipeline Stages

Three parallel Roslyn incremental pipelines registered in `QuarryGenerator.Initialize()`:

**Pipeline A: Schema/Context** (design-time + build-time via RegisterSourceOutput)
```
Stage 1: Schema/Context    ContextParser + SchemaParser → ContextInfo[] + EntityInfo[]
                           EntityCodeGenerator → entity .g.cs files
                           ContextCodeGenerator → context partial .g.cs files
```

**Pipeline B: Interceptors** (build-time only via RegisterImplementationSourceOutput)
```
Stage 2: Discovery         UsageSiteDiscovery → RawCallSite[]
                           ── .Collect() barrier: all sites gathered ──
Stage 2.5: Enrichment      DisplayClassEnricher → enriched RawCallSite[] (display class names, captured variable types)
Stage 3a: Bind             CallSiteBinder → BoundCallSite[] (entity refs, context resolved)
                           Returns ImmutableArray (1:N for navigation joins). Errors → PipelineErrorBag side-channel.
Stage 3b: Translate        CallSiteTranslator → TranslatedCallSite (SQL expression bound, parameters extracted)
                           Returns single site. Errors → TranslatedCallSite.PipelineError field.
                           ── .Collect() barrier: all translated sites gathered ──
Stage 4: Chain Analysis     ChainAnalyzer → AnalyzedChain[] (groups by ChainId, classifies optimization tier)
Stage 5a: SQL Assembly      SqlAssembler → AssembledPlan[] (rendered SQL per conditional mask)
Stage 5b: Carrier Analysis  CarrierAnalyzer → CarrierPlan[] (eligibility gates, field layout, extraction plans)
Stage 5c: Post-analysis     BuildResultTypePatches: resolves unresolved tuple types from chain projections
                            PropagateChainUpdatedSites: replaces original sites with chain-enriched versions
                              (e.g., JoinedEntityTypeNames on post-join sites, patched ResultTypeName)
Stage 5d: File Grouping     GroupTranslatedIntoFiles → FileInterceptorGroup[] (keyed by context + source file)
Stage 5e: Emission          FileEmitter → interceptor .g.cs files
```
Stages 4-5d run inside PipelineOrchestrator.AnalyzeAndGroupTranslated() after all sites are collected.
Stage 5e runs per FileInterceptorGroup in RegisterImplementationSourceOutput.

**Pipeline C: Migrations** (build-time only)
```
Stage 1: Migration discovery → MigrationInfo[]
         MigrateAsyncCodeGenerator → MigrateAsync .g.cs
```

### Call Site Lifecycle

`RawCallSite` (syntax-only: location, interceptor kind, lambda expression, scope context)
→ `BoundCallSite` (adds: entity metadata, context class, SQL dialect, table names)
→ `TranslatedCallSite` (adds: translated SQL clause, parameter list, result types)

### SqlExpr Pipeline

```
SqlExprParser     C# lambda syntax → SqlExpr tree (unresolved)
SqlExprAnnotator  + SemanticModel → type annotations, enum constant folding to LiteralExpr
SqlExprBinder     + EntityInfo → ColumnRef resolved to quoted column names + table qualifiers
SqlExprClauseTranslator  CapturedValueExpr/literals → ParamSlotExpr + ParameterInfo list
SqlExprRenderer   SqlExpr tree → SQL string
```

Node types: ColumnRefExpr, ResolvedColumnExpr, ParamSlotExpr, LiteralExpr, CapturedValueExpr, BinaryOpExpr, UnaryOpExpr, FunctionCallExpr, InExpr, IsNullCheckExpr, LikeExpr, SubqueryExpr, RawCallExpr, SqlRawExpr.

### Carrier Pattern

Carrier classes (`Chain_0`, `Chain_1`, ...) are generated per-chain to avoid intermediate `QueryBuilder<T>` allocations. They hold query state (context, parameters, conditional mask) and implement all builder interfaces.

Flow: `CarrierAnalyzer.AnalyzeNew(AssembledPlan)` → `CarrierPlan` → `CarrierEmitter.EmitCarrierClass()`.

Interceptors cast the builder to the carrier via `Unsafe.As<Chain_N>()`, extract parameters, set mask bits, and dispatch SQL at the terminal.

### Conditional Clause Masking

Clauses inside `if/else` blocks deeper than the execution terminal's nesting depth get assigned bit indices. Constants: `MaxConditionalBits = 8`, `MaxIfNestingDepth = 2`. Beyond either → QRY032.

**Bit assignment** (ChainAnalyzer): For each clause site, compute `relativeDepth = clause.NestingDepth - terminal.NestingDepth`. If `relativeDepth <= 0`, the clause is unconditional (same scope as terminal). If `relativeDepth > MaxIfNestingDepth`, the chain is RuntimeBuild. Otherwise, assign a `BitIndex` (0-7). Clauses sharing the same `ConditionText` form mutually exclusive branch groups.

**Mask enumeration** (ChainAnalyzer.EnumerateMaskCombinations): Independent bits double the mask count (on/off). Mutually exclusive groups multiply by group size (exactly one bit set). Only reachable combinations are enumerated.

**SQL rendering** (SqlAssembler): For each mask, evaluate which terms are active (`BitIndex == null` or bit set in mask), then render the full SQL statement. Parameter indices are globally stable — skipped conditional terms still occupy their parameter slots to keep `@p0, @p1, ...` aligned.

**Code generation** (CarrierEmitter): Single variant → `static readonly string _sql`. Multiple variants → `static readonly string[] _sql` indexed by mask value (gaps filled with `null!`). Carrier accumulates a `byte` mask field via `Mask |= (1 << bitIndex)` as conditional clause interceptors execute. Terminal dispatches via direct array index: `_sql[__c.Mask]`.

### Error Propagation & QRY900

Errors propagate through two channels due to the Bind/Translate return type asymmetry:

| Stage | Return Type | Error Channel | Rationale |
|-------|-------------|---------------|-----------|
| 3a Bind | `ImmutableArray<BoundCallSite>` | `PipelineErrorBag.Report()` (ThreadStatic side-channel) | Returns empty array on failure — no site to attach error to. 1:N expansion for navigation joins prevents a single error-bearing return. |
| 3b Translate | `TranslatedCallSite` | `TranslatedCallSite.PipelineError` field | Scalar return allows natural error field. Equality includes PipelineError for incremental cache invalidation on error state changes. |

**QRY900 has three source paths**, all drained in `EmitFileInterceptors()`:
1. `site.PipelineError != null` on TranslatedCallSite → Translate-stage exceptions
2. `PipelineErrorBag.DrainErrors()` → Bind-stage exceptions (side-channel)
3. Exception catch in `EmitFileInterceptorsNewPipeline()` → Emission-stage exceptions

**ThreadStatic lifecycle**: `PipelineOrchestrator.AnalyzeAndGroupTranslated()` calls `PipelineErrorBag.DrainErrors()` at entry to discard stale errors from prior compilations on the same thread. Safe because the incremental pipeline is single-threaded per compilation.

### Caching Boundaries

| Stage | Granularity | Invalidation Blast Radius |
|-------|-------------|---------------------------|
| 2-2.5 | Per-site (individual transforms) | One changed call site re-enriches only that site |
| 3a-3b | Per-site (Select/SelectMany) | One changed site re-binds/re-translates only that site |
| 4-5d | **All sites** (`.Collect()` barrier) | One new/changed TranslatedCallSite triggers re-analysis of ALL chains for ALL contexts |
| 5e | Per FileInterceptorGroup | FileInterceptorGroup equality gates per-file code generation |

**EntityRegistry as cross-pipeline bridge**: Built from all `ContextInfo` objects (Pipeline A output). Passed via `.Combine(entityRegistry)` into Pipeline B stages 2.5, 3a, 3b, and 4. Consequence: changing a Schema class invalidates all call site binding for entities in that schema.

### Chain Disqualification

Chains that cannot be statically analyzed receive `OptimizationTier.RuntimeBuild` → QRY032 compile error. Disqualifiers (from `ChainAnalyzer.CheckDisqualifiers`):

| Disqualifier | Example |
|-------------|---------|
| Forked query chain | `var q = db.T().Where(...); q.Select(A).Execute(); q.Select(B).Execute();` |
| Chain variable captured in lambda | `var q = db.T(); items.Select(x => q.Where(...))` |
| Chain variable passed to non-Quarry method | `var q = db.T(); SomeMethod(q);` |
| Chain variable assigned from non-Quarry method | `var q = GetQuery();` |
| Chain crosses loop boundary | Some clauses inside loop, terminal outside (or vice versa) |
| Conditional nesting depth > 2 | Triple-nested `if/else` with conditional clauses |
| Conditional bits > 8 | More than 8 independent conditional clause groups |

### Display Class Prediction

The generator predicts compiler-generated closure class names to emit `[UnsafeAccessor]` methods for captured variable extraction without reflection.

**Algorithm** (DisplayClassEnricher + DisplayClassNameResolver):
1. Group all RawCallSites by enclosing method (walked up past local functions)
2. Compute `methodOrdinal` = index of method in `containingType.GetMembers()` (linear scan)
3. Analyze closures: pre-order traversal of lambda/local-function descendants, assign ordinals to scopes with captures
4. Final name: `"{FullyQualifiedType}+<>c__DisplayClass{methodOrdinal}_{closureOrdinal}"`
5. Classify capture kind (ClosureCapture vs FieldCapture) via `dataFlow.CapturedInside`

**Compiler assumptions** (undocumented implementation details, not guaranteed contracts):
- `GetMembers()` returns members in declaration order (all members count: backing fields, properties, accessors, methods)
- Display class naming follows `<>c__DisplayClass{M}_{C}` pattern
- Closure ordinals assigned in pre-order source traversal order
- Partial classes contribute members in compilation unit order

**Supplemental compilation**: `DisplayClassEnricher.BuildSupplementalCompilation` adds generated entity classes and context partial classes to the compilation before creating semantic models. This lets Roslyn resolve all generated types natively — no manual error-type fallbacks needed. Variables flowing from generated methods (e.g., `db.Equipments().ExecuteFetchAllAsync()`) resolve to their correct types automatically. When `TypeKind.Error` persists (e.g., types from other generators), the fallback is `"object"`.

### Subquery & Aggregate Support

**Navigation subquery methods** (recognized by `SqlExprParser.IsSubqueryMethod`):

| Pattern | SQL | Notes |
|---------|-----|-------|
| `nav.Any()` | `EXISTS (SELECT 1 FROM t WHERE correlation)` | Parameterless |
| `nav.Any(x => pred)` | `EXISTS (SELECT 1 FROM t WHERE correlation AND pred)` | With predicate |
| `!nav.Any(...)` | `NOT EXISTS (...)` | Negation supported |
| `nav.All(x => pred)` | `NOT EXISTS (SELECT 1 FROM t WHERE correlation AND NOT pred)` | Predicate required |
| `nav.Count()` | `(SELECT COUNT(*) FROM t WHERE correlation)` | Scalar subquery |
| `nav.Count(x => pred)` | `(SELECT COUNT(*) FROM t WHERE correlation AND pred)` | With predicate |

Navigation aggregates (v0.3.0): `.Sum(selector)`, `.Min(selector)`, `.Max(selector)`, `.Avg(selector)` / `.Average(selector)` follow the same correlated-subquery pattern. Still not supported on navigation: `.FirstOrDefault()`, `.Exists()`.

**Sql.* aggregate functions** (work in any expression context — Select, Where, Having):

| Function | SQL |
|----------|-----|
| `Sql.Count()` | `COUNT(*)` |
| `Sql.Count(expr)` | `COUNT(expr)` |
| `Sql.Sum(expr)` | `SUM(expr)` |
| `Sql.Avg(expr)` | `AVG(expr)` |
| `Sql.Min(expr)` | `MIN(expr)` |
| `Sql.Max(expr)` | `MAX(expr)` |

Subquery aliases are generated as `sq0`, `sq1`, etc. Correlation is always `inner.FK = outer.PK` (automatic from navigation metadata). Nested subqueries are supported (e.g., `u.Orders.Any(o => o.Items.Any(i => ...))`).

### Window Functions (Select projections)

`Sql.*` window variants use a fluent `IOverClause` lambda:

| Function | SQL |
|----------|-----|
| `Sql.RowNumber(over => …)` | `ROW_NUMBER() OVER (…)` |
| `Sql.Rank(over => …)` | `RANK() OVER (…)` |
| `Sql.DenseRank(over => …)` | `DENSE_RANK() OVER (…)` |
| `Sql.Ntile(n, over => …)` | `NTILE(n) OVER (…)` |
| `Sql.Lag(col, offset, default, over => …)` | `LAG(col, offset, default) OVER (…)` |
| `Sql.Lead(col, offset, default, over => …)` | `LEAD(col, offset, default) OVER (…)` |
| `Sql.FirstValue(col, over => …)` | `FIRST_VALUE(col) OVER (…)` |
| `Sql.LastValue(col, over => …)` | `LAST_VALUE(col) OVER (…)` |
| `Sql.{Sum,Count,Avg,Min,Max}(col, over => …)` | aggregate + OVER |

`IOverClause` fluent methods: `PartitionBy`, `OrderBy`, `OrderByDescending`. Frame specs (ROWS/RANGE) not yet supported. Non-column args (offsets, defaults, Ntile buckets) are parameterized at compile time (C# suffixes stripped: `0m` → `0`). Aggregate/window column identifiers emit backticks on MySQL and brackets on SQL Server (not double quotes).

### CTEs and Set Operations

**`.With<TDto>(lambda)` / `.With<TEntity,TDto>(lambda)` + `.FromCte<TDto>()`:** compile to standard `WITH name AS (SELECT …)` across all four dialects. Multi-CTE chains supported (`.With<A>(…).With<B>(…)`). Per-CTE parameter-space isolation prevents `@p{n}` collisions. `QRY080` / `QRY081` / `QRY082` diagnostics cover unanalyzable inner, missing `With`, and duplicate names. Typed post-`With` accessor chains require `QuarryContext<TSelf>`.

**`Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll`** on `IQueryBuilder<T>` / `IQueryBuilder<TEntity,TResult>`. Post-set-op `Where`/`GroupBy`/`Having` auto-wraps the set expression as a subquery. Cross-entity set operations are supported (`Users.Select(…).Union(Products.Select(…))`). `QRY070`/`QRY071` for dialect-unsupported variants (SQLite has no INTERSECT ALL/EXCEPT ALL); `QRY072` for projection column-count/type mismatch. Parameter indexing through set-op operands goes through `AnalyzeOperandChain`, which merges projection parameters to avoid cross-operand collisions.

### Navigation Joins and 6-Table Explicit Joins

`One<T>` with `HasOne<T>()` emits a reverse-side nullable nav; `HasManyThrough<TTarget, TJunction>()` emits many-to-many skip nav with an implicit junction→target JOIN. Schema-level diagnostics: QRY060–065. The `NavigationAccessExpr` node threads through parse → bind → translate → assemble → emit; `KnownDotNetMembers` excludes `.ToString()` / `.Equals()` etc. from being parsed as nav access. Implicit joins from nav lambdas are deduplicated against explicit joins.

Explicit joins support 2–6 tables via T4-generated `IJoinedQueryBuilder5/6` and `JoinedCarrierBase5/6`. New join kinds: `CrossJoin<T>()` (no condition), `FullOuterJoin<T>(condition)`. **Join-aware nullable readers:** the projection analyzer inspects join-side nullability and wraps reader column reads on LEFT/RIGHT/FULL OUTER nullable sides with `IsDBNull` guards. Declared tuple types unchanged; only generated reader code is affected.

### SQL Manifest Emission

Gated by MSBuild property `QuarrySqlManifestPath`. `ManifestEmitter` runs after Stage 6 and writes per-dialect markdown files (one per dialect present in the compilation). `WriteIfChanged` compares against on-disk content to suppress no-op writes. Output includes every chain's SQL, parameter table (including LIMIT/OFFSET parameters), bitmask-labeled conditional variants (`Variant[0b0001]`), and per-file summary. Write failures surface as `QRY040` warnings.

### Supplemental Compilation (v0.3.0)

The discovery stage builds a supplemental compilation containing Pipeline-A outputs (entity classes, context accessors) before creating semantic models for Pipeline-B. This replaces ~700 lines of prior error-type fallback heuristics (`TryResolveErrorType`, `TryQualifyErrorTypeFromUsings`). Remaining unresolvable types still fall back to `"object"` under the strict/lenient `IsUnresolvedTypeName` split. `EntityRegistry.Equals`/`GetHashCode` include `_allContexts` — this was a latent incremental-caching bug that could leave stale cross-context views.

### Shared SQL Parser

`Quarry.Shared/Sql/Parser/` (tokenizer, recursive-descent parser, AST, walker) is `#if QUARRY_GENERATOR`-gated — consumed by the generator, excluded from the runtime assembly. Powers: RawSqlAsync compile-time column resolution, QRY042 convertibility detection, and the `Quarry.Migration` converters.

### Carrier Dedup

Structurally-identical carrier classes are merged at emission time. Carrier class numbering (`Chain_N`) may have gaps and is not a stable contract. Dedup checks `CarrierPlan` equality (fields, parameters, extraction plans, SQL variants). Diagnostics still reference the canonical carrier name.

### Incremental SQL Mask Rendering

For chains with N conditional terms (up to 8 bits = 256 variants), shared prefix/suffix is rendered once and variant-specific middle segments are assembled via `StringBuilder.Append` rather than re-rendering from scratch per mask. Applies to SELECT and DELETE multi-mask chains.

## File Map

### Entry Point
| File | Purpose |
|------|---------|
| `QuarryGenerator.cs` | IIncrementalGenerator. Registers 3 pipelines: schema/context, interceptors, migrations. Stages 2-5 orchestration. |
| `DiagnosticDescriptors.cs` | Central registry of all QRY diagnostic descriptors (QRY001–QRY055, QRY900) with severity, title, and message format. |

### Parsing (Stage 1-2.5) — `Parsing/`
| File | Purpose |
|------|---------|
| `UsageSiteDiscovery.cs` | Stage 1. Discovers Quarry call sites → RawCallSite. Symbol resolution, ChainId computation, scope detection. |
| `SchemaParser.cs` | Parses Schema classes → EntityInfo (columns, navigations, indexes, naming). |
| `ContextParser.cs` | Parses [QuarryContext] classes → ContextInfo (dialect, entities, mappings). |
| `ChainAnalyzer.cs` | Stage 4. Groups sites by ChainId → QueryPlan. Conditional classification, projection building, parameter enrichment. |
| `AnalyzabilityChecker.cs` | Per-site analyzability gate. Checks receiver is a fluent chain (not parameter/variable), lambda is present, traces up to 2 hops in variable chains. Sets IsAnalyzable + NonAnalyzableReason on RawCallSite. |
| `DisplayClassEnricher.cs` | Stage 2.5. Batch closure analysis per method. Predicts display class names, collects captured variable types. |
| `DisplayClassNameResolver.cs` | Display class name prediction utilities. Method ordinals, closure ordinals, captured variable types. |
| `VariableTracer.cs` | Variable declaration tracing. Builder type checks, fluent chain root walking. |
| `NamingConventions.cs` | Property → column name conversion (snake_case, camelCase, etc). |

### IR (Intermediate Representation) — `IR/`
| File | Purpose |
|------|---------|
| `RawCallSite.cs` | Discovery-time model. ~50 properties: location, kind, expression, scope flags. |
| `BoundCallSite.cs` | Wraps RawCallSite + resolved entity metadata, context, dialect. |
| `TranslatedCallSite.cs` | Wraps BoundCallSite + translated clause, parameters, result types. PipelineError field. |
| `CallSiteBinder.cs` | Stage 3. Resolves entity refs from EntityRegistry → BoundCallSite. |
| `CallSiteTranslator.cs` | Stage 3. Runs SqlExpr pipeline (parse→annotate→bind→extract→render) → TranslatedCallSite. |
| `SqlExpr.cs` | Base SqlExpr class. |
| `SqlExprNodes.cs` | All SqlExpr node types (ColumnRef, Literal, BinaryOp, CapturedValue, Subquery, etc). |
| `SqlExprParser.cs` | C# expression syntax → SqlExpr tree. No SemanticModel. |
| `SqlExprAnnotator.cs` | Type annotation + constant folding (enums → LiteralExpr). |
| `SqlExprBinder.cs` | Column resolution (ColumnRef → ResolvedColumn with quoted names). |
| `SqlExprClauseTranslator.cs` | Parameter extraction. Unified for standard + subquery modes. |
| `SqlExprRenderer.cs` | SqlExpr → SQL string. Dialect-specific quoting. |
| `SqlAssembler.cs` | QueryPlan → AssembledPlan. Renders SQL per conditional mask. INSERT RETURNING/OUTPUT. |
| `QueryPlan.cs` | Dialect-agnostic query structure: terms, joins, projection, pagination, parameters. |
| `AssembledPlan.cs` | QueryPlan + rendered SQL variants + reader delegate code + execution metadata. |
| `EntityRegistry.cs` | Multi-key entity index (by type, name, accessor name). Built from all contexts. |
| `EntityRef.cs` | Lightweight entity reference (avoids Location/indices). |
| `PipelineOrchestrator.cs` | Stage 5. Chains: diagnostics → ChainAnalyzer → SqlAssembler → CarrierAnalyzer → file grouping. |
| `PipelineErrorBag.cs` | [ThreadStatic] error side-channel for Stage 3 binding failures → drained as QRY900. |
| `FileOutputGroup.cs` | Legacy output container (superseded by FileInterceptorGroup). |
| `TraceCapture.cs` | [ThreadStatic] debug trace collection for .Trace() chains. |

### Code Generation — `CodeGen/`
| File | Purpose |
|------|---------|
| `CarrierAnalyzer.cs` | Analyzes AssembledPlan → CarrierPlan. Eligibility gates, field/parameter computation, extraction plans. |
| `CarrierPlan.cs` | Carrier plan model: fields, parameters, mask, extraction plans, interfaces. |
| `CarrierParameter.cs` | Extended carrier parameter with global index, field name/type, extraction/binding code, type mapping, collection/sensitivity flags. |
| `CarrierEmitter.cs` | Emits carrier class + carrier-path method bodies (clause binding, terminal execution). |
| `InterceptorRouter.cs` | Routes InterceptorKind → EmitterCategory (Clause, Terminal, Join, Transition, RawSql). |
| `FileEmitter.cs` | Per-file orchestrator. Pass 1: carrier classes. Pass 2: interceptor methods via dispatcher. |
| `ClauseBodyEmitter.cs` | Emits Where/OrderBy/GroupBy/Having/Set/Select/UpdateSet clause bodies. |
| `JoinBodyEmitter.cs` | Emits Join/LeftJoin/RightJoin + joined clause bodies. |
| `TerminalBodyEmitter.cs` | Emits execution terminals (FetchAll, FetchFirst, Insert, BatchInsert, NonQuery, Diagnostics, Prepare). |
| `TerminalEmitHelpers.cs` | Shared: ResolveSiteParams, parameter locals, collection expansion, diagnostic arrays, return type/executor resolution. |
| `TransitionBodyEmitter.cs` | Emits Delete/Update/Insert transitions, ChainRoot, Pagination, Distinct, WithTimeout. |
| `RawSqlBodyEmitter.cs` | Emits RawSqlAsync/RawSqlScalarAsync (bypasses query builder). |

### Entity/Context Generation — `Generation/`
| File | Purpose |
|------|---------|
| `EntityCodeGenerator.cs` | Generates entity classes from EntityInfo (properties, types, defaults). |
| `ContextCodeGenerator.cs` | Generates context partial (constructors, query builder properties). |
| `InterceptorCodeGenerator.cs` | Delegates to FileEmitter. Collects cached extractor fields. |
| `InterceptorCodeGenerator.Utilities.cs` | Helpers: GetColumnValueExpression, IsBrokenTupleType, SanitizeTupleResultType. |
| `MigrateAsyncCodeGenerator.cs` | Generates MigrateAsync method from migration metadata. |

### Projection — `Projection/`
| File | Purpose |
|------|---------|
| `ProjectionAnalyzer.cs` | Analyzes Select() lambdas → ProjectionInfo (kind, columns, reader method). |
| `ReaderCodeGenerator.cs` | Generates column list SQL + typed reader delegates (entity, DTO, tuple, scalar). |

### Translation — `Translation/`
| File | Purpose |
|------|---------|
| `ParameterInfo.cs` | Parameter extracted from SQL expressions: index, name, CLR type, value expression, collection flag, capture metadata. |
| `SqlLikeHelpers.cs` | LIKE expression helpers: `EscapeLikeMetaChars()`, `FormatLikeWithParameter()`. Dialect-aware concatenation. |

### Utilities — `Utilities/`
| File | Purpose |
|------|---------|
| `TypeClassification.cs` | Central type classification: IsValueType, GetReaderMethod, NeedsSignCast, IsUnresolvedTypeName/IsUnresolvedResultType, BuildTupleTypeName, SplitTupleElements. |
| `SymbolDisplayCache.cs` | Caches ITypeSymbol.ToDisplayString() results via ConditionalWeakTable. |
| `FileHasher.cs` | Converts file paths into sanitized tags for generated file names and C# identifiers. |

### Models — `Models/`
All pipeline models implement `IEquatable<T>` for incremental caching.

| File | Type(s) | Purpose |
|------|---------|---------|
| `InterceptorKind.cs` | `enum InterceptorKind` | 40+ enum values for all interceptor categories. |
| `ColumnInfo.cs` | `class ColumnInfo` | Column from schema: property name, column name, CLR type, modifiers. |
| `ContextInfo.cs` | `class ContextInfo` | Discovered QuarryContext: configuration, dialect, entity mappings. |
| `EntityInfo.cs` | `class EntityInfo` | Discovered entity: name, table name, columns, navigations, indexes. |
| `EntityMapping.cs` | `class EntityMapping` | Maps context property name → EntityInfo. |
| `NavigationInfo.cs` | `class NavigationInfo` | One-to-many navigation (Many<T>): property name, related entity, FK. |
| `IndexInfo.cs` | `class IndexInfo` | Index: columns with sort directions, uniqueness, type (BTree/Hash), filter, includes. |
| `ProjectionInfo.cs` | `class ProjectionInfo` | Analyzed Select() lambda: kind, result type, columns, reader method. |
| `ExecutionInfo.cs` | `class ExecutionInfo` | Execution context for terminals: SQL, parameters, reader. |
| `InsertInfo.cs` | `class InsertInfo` | Insert operation metadata: columns, identity column, RETURNING clause. |
| `ClauseExtractionPlan.cs` | `class ClauseExtractionPlan` | Groups per-variable extractors for a single clause. |
| `CapturedVariableExtractor.cs` | `class CapturedVariableExtractor` | Per-variable [UnsafeAccessor] extractor: method name, variable name/type, display class, capture kind. |
| `CarrierField.cs` | `enum FieldRole`, `class CarrierField` | FieldRole (ExecutionContext, Parameter, Collection, ClauseMask, Limit, Offset, Timeout, Entity). CarrierField describes a field on the generated carrier class. |
| `SetActionAssignment.cs` | `class SetActionAssignment` | Single assignment from `Set(Action<T>)` lambda: column SQL, value type, inlined value. |
| `FileInterceptorGroup.cs` | `class FileInterceptorGroup` | Groups all interceptor data for a (context, source file) pair. Output of PipelineOrchestrator. |
| `OptimizationTier.cs` | `enum OptimizationTier`, `enum ClauseRole` | PrebuiltDispatch vs RuntimeBuild. ClauseRole tracks clause position. |
| `QueryKind.cs` | `enum QueryKind` | Query routing: Select, Delete, Update, Insert, BatchInsert. |
| `ClauseKind.cs` | `enum ClauseKind` | Clause types: Where, OrderBy, GroupBy, Having, Set. |
| `RawSqlTypeInfo.cs` | `class RawSqlTypeInfo` | Resolved result type T for RawSqlAsync<T>/RawSqlScalarAsync<T>. |
| `DiagnosticInfo.cs` | `class DiagnosticInfo` | Deferred diagnostic: ID, location, message args. Carried through pipeline for reporting in emission. |
| `DiagnosticLocation.cs` | `struct DiagnosticLocation` | Structural source location (file, line, column, span). Replaces Roslyn Location for IEquatable. |
| `MigrationInfo.cs` | `class MigrationInfo` | Migration class metadata: version, name, flags (HasDestructiveSteps, HasBackup, etc). |
| `SnapshotInfo.cs` | `class SnapshotInfo` | [MigrationSnapshot] metadata: version, name, schema hash. |
| `EquatableArray.cs` | `struct EquatableArray<T>` | ImmutableArray wrapper with element-wise equality for incremental caching. |
| `EquatableDictionary.cs` | `struct EquatableDictionary<K,V>` | ImmutableDictionary wrapper with key-value equality for incremental caching. |
| `EqualityHelpers.cs` | `static class EqualityHelpers` | SequenceEqual, HashSequence, NullableSequenceEqual, DictionaryEqual utilities. |
| `HashCodePolyfill.cs` | `struct HashCode` | System.HashCode polyfill for netstandard2.0 compatibility. |

## InterceptorKind Categories

| Category | Kinds |
|----------|-------|
| Clause | Select, Where, OrderBy, ThenBy, GroupBy, Having, Set, DeleteWhere, UpdateWhere, UpdateSet, UpdateSetAction, UpdateSetPoco |
| Terminal | ExecuteFetchAll, ExecuteFetchFirst, ExecuteFetchFirstOrDefault, ExecuteFetchSingle, ExecuteFetchSingleOrDefault, ExecuteScalar, ExecuteNonQuery, ToAsyncEnumerable, ToDiagnostics, Prepare |
| Insert Terminal | InsertExecuteNonQuery, InsertExecuteScalar, InsertToDiagnostics |
| Batch Insert | BatchInsertExecuteNonQuery, BatchInsertExecuteScalar, BatchInsertToDiagnostics, BatchInsertColumnSelector, BatchInsertValues |
| Join | Join, LeftJoin, RightJoin |
| Transition | ChainRoot, DeleteTransition, UpdateTransition, InsertTransition, AllTransition |
| Modifier | Limit, Offset, Distinct, WithTimeout |
| Raw SQL | RawSqlAsync, RawSqlScalarAsync |
| Debug | Trace |

## Diagnostics (QRY Codes)

| Code | Severity | Meaning |
|------|----------|---------|
| QRY001 | Warning | Query not fully analyzable (non-analyzable receiver/lambda) |
| QRY002 | Error | Missing Table property on schema |
| QRY003 | Error | Invalid column type / no TypeMapping |
| QRY006 | Error | Unsupported Where operation |
| QRY008 | Warning | Potential SQL injection |
| QRY009 | Error | GroupBy required for aggregate |
| QRY011 | Error | Select required before execution |
| QRY014 | Error | Anonymous type projection not supported |
| QRY015 | Warning | Ambiguous context resolution |
| QRY019 | Warning | Clause not translatable |
| QRY020 | Error | All() requires predicate |
| QRY029 | Error | Sql.Raw placeholder mismatch |
| QRY031 | Error | Unresolvable RawSqlAsync\<T\> generic type parameter |
| QRY032 | Error | Chain not analyzable |
| QRY033 | Error | Forked query chain |
| QRY034 | Warning | .Trace() requires QUARRY_TRACE define |
| QRY035 | Error | PreparedQuery escapes scope |
| QRY036 | Error | Prepared query with no terminals |
| QRY040 | Warning | SQL manifest write failure |
| QRY041 | Warning | RawSqlAsync column expression without alias (falls back to runtime ordinal discovery) |
| QRY042 | Info | RawSqlAsync convertible to chain query (code fix available) |
| QRY050-055 | Mixed | Migration diagnostics |
| QRY060 | Error | No FK column for `One<T>` navigation |
| QRY061 | Error | Ambiguous FK for `One<T>` navigation |
| QRY062 | Error | `HasOne` references invalid column |
| QRY063 | Error | Navigation target entity not found |
| QRY064 | Error | `HasManyThrough` invalid junction navigation |
| QRY065 | Error | `HasManyThrough` invalid target navigation |
| QRY070 | Warning | `IntersectAll` not supported on this dialect |
| QRY071 | Warning | `ExceptAll` not supported on this dialect |
| QRY072 | Error | Set operation projection mismatch |
| QRY074 | Error | Navigation aggregate in `Select` projection unresolved |
| QRY080 | Error | CTE inner query not analyzable |
| QRY081 | Error | `FromCte` without matching `With` |
| QRY082 | Error | Duplicate CTE name in chain |
| QRY900 | Error | Internal generator error (pipeline exception) |

QRY073 was introduced then retired in v0.3.0 when cross-entity set operations became supported; `#pragma warning disable QRY073` directives should be removed. The ID is intentionally skipped so those pragmas remain inert.

## Key Design Decisions

1. **Incremental caching**: All pipeline models implement `IEquatable<T>`. Equality on TranslatedCallSite includes PipelineError to detect error state changes.
2. **[ThreadStatic] for side-channels**: PipelineErrorBag and TraceCapture use thread-static storage (not ConcurrentBag) because the incremental pipeline is single-threaded per compilation.
3. **Display class prediction**: Generator predicts compiler-generated closure class names to emit [UnsafeAccessor] methods for captured variable extraction without reflection.
4. **Supplemental compilation**: DisplayClassEnricher builds a supplemental compilation containing generated entity/context source before creating semantic models. This eliminates TypeKind.Error for generated types; remaining error types fall back to "object".
5. **IsUnresolvedTypeName strict/lenient split**: Strict treats "object" as unresolved (chain analysis). Lenient allows "object" (projection analysis where it is a valid placeholder via fallbackToObject).
6. **Enum constant folding**: SqlExprAnnotator folds enum member accesses to LiteralExpr before parameter extraction. CapturedValueExpr reaching the translator are always genuine runtime captures.
7. **Conditional mask limit**: Max 8 conditional bits (256 SQL variants) and max nesting depth 2. Beyond either limit → QRY032 compile error.
8. **RuntimeBuild is a compile-error path, not a runtime fallback**: There is no runtime query builder. When ChainAnalyzer classifies a chain as `OptimizationTier.RuntimeBuild` (forked chain, excessive conditional depth, unanalyzable projection, disqualified chain), no SQL is rendered, no carrier is generated, and QRY032 is reported as a compile error directing the user to restructure. `CarrierAnalyzer` immediately marks RuntimeBuild chains as `Ineligible`; `SqlAssembler` produces empty SQL variants.

## Project Boundaries

| Project | Target | Role |
|---------|--------|------|
| `Quarry.Generator` | netstandard2.0 | Roslyn source generator. Compile-time analysis and code generation. |
| `Quarry` | net10.0 | Runtime library. QuarryContext, IEntityAccessor<T>, QueryBuilder<T>, execution, type mappings. |
| `Quarry.Shared` | shared projitems | Shared code compiled into both Generator and Runtime. Contains Migration/ (schema diffing, builders, DDL), Scaffold/ (database introspection for 4 dialects), and Sql/ (dialect enum, formatting). Generator excludes Migration/ and Scaffold/ directories. |

## Testing

- Cross-dialect SQL output tests in `Quarry.Tests/SqlOutput/CrossDialect*.cs` — primary regression gate
- `QueryTestHarness` seeds SQLite with known data, runs queries, asserts SQL + result values across all 4 dialects
- `TypeClassificationTests` — unit tests for type classification utilities
- `DisplayClassEnricherTests` — closure analysis and type resolution tests
- `DateTimeOffsetIntegrationTests` — GetFieldValue round-trip tests
