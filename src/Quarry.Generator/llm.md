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
Terminals: `ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ExecuteScalarAsync<T>`, `ExecuteNonQueryAsync`, `ToAsyncEnumerable`, `ToDiagnostics`.

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
Stage 2.5: Enrichment      DisplayClassEnricher → enriched RawCallSite[] (display class names, captured variable types)
Stage 3: Bind + Translate   CallSiteBinder → BoundCallSite (entity refs, context resolved)
                            CallSiteTranslator → TranslatedCallSite (SQL expression bound, parameters extracted)
Stage 4: Chain Analysis     ChainAnalyzer → QueryPlan[] (groups by ChainId, classifies optimization tier)
Stage 5: Assembly + Emit    SqlAssembler → AssembledPlan[] (rendered SQL per conditional mask)
                            CarrierAnalyzer → CarrierPlan[]
                            FileEmitter → interceptor .g.cs files
```
Stages 4-5 run inside PipelineOrchestrator.AnalyzeAndGroupTranslated() after all sites are collected.

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

Clauses inside `if/else` blocks get assigned bit indices. At runtime, the carrier accumulates a mask. The terminal dispatches to the correct pre-rendered SQL variant via `mask switch { 0 => sql0, 1 => sql1, ... }`. Max 8 conditional bits (256 variants); beyond that, falls back to RuntimeBuild.

## File Map

### Entry Point
| File | Purpose |
|------|---------|
| `QuarryGenerator.cs` | IIncrementalGenerator. Registers 3 pipelines: schema/context, interceptors, migrations. Stages 2-5 orchestration. |

### Parsing (Stage 1-2.5) — `Parsing/`
| File | Purpose |
|------|---------|
| `UsageSiteDiscovery.cs` | Stage 1. Discovers Quarry call sites → RawCallSite. Symbol resolution, ChainId computation, scope detection. |
| `SchemaParser.cs` | Parses Schema classes → EntityInfo (columns, navigations, indexes, naming). |
| `ContextParser.cs` | Parses [QuarryContext] classes → ContextInfo (dialect, entities, mappings). |
| `ChainAnalyzer.cs` | Stage 4. Groups sites by ChainId → QueryPlan. Conditional classification, projection building, parameter enrichment. |
| `AnalyzabilityChecker.cs` | Determines compile-time vs runtime fallback eligibility. Receiver tracing, lambda capture checks. |
| `DisplayClassEnricher.cs` | Stage 2.5. Batch closure analysis per method. Predicts display class names, collects captured variable types. |
| `DisplayClassNameResolver.cs` | Display class name prediction utilities. Method ordinals, closure ordinals, error type resolution. |
| `ChainResultTypeResolver.cs` | Resolves captured variable types from chain terminal results via EntityRegistry. |
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

### Utilities — `Utilities/`
| File | Purpose |
|------|---------|
| `TypeClassification.cs` | Central type classification: IsValueType, GetReaderMethod, NeedsSignCast, IsUnresolvedTypeName, BuildTupleTypeName, SplitTupleElements. |
| `SymbolDisplayCache.cs` | Caches SymbolDisplayFormat results. |
| `FileHasher.cs` | Content hashing for incremental output. |

### Models — `Models/`
Key types: `InterceptorKind` (40+ enum values), `ClauseKind`, `QueryKind`, `ColumnInfo`, `EntityInfo`, `ContextInfo`, `InsertInfo`, `NavigationInfo`, `ProjectionInfo`, `ExecutionInfo`, `ClauseExtractionPlan`, `FileInterceptorGroup`.

## InterceptorKind Categories

| Category | Kinds |
|----------|-------|
| Clause | Select, Where, OrderBy, ThenBy, GroupBy, Having, Set, DeleteWhere, UpdateWhere, UpdateSet, UpdateSetAction, UpdateSetPoco |
| Terminal | ExecuteFetchAll, ExecuteFetchFirst, ExecuteFetchFirstOrDefault, ExecuteFetchSingle, ExecuteScalar, ExecuteNonQuery, ToAsyncEnumerable, ToDiagnostics, Prepare |
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
| QRY001 | Warning | Query not fully analyzable (runtime fallback) |
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
| QRY032 | Error | Chain not analyzable |
| QRY033 | Error | Forked query chain |
| QRY034 | Warning | .Trace() requires QUARRY_TRACE define |
| QRY035 | Error | PreparedQuery escapes scope |
| QRY050-055 | Mixed | Migration diagnostics |
| QRY900 | Error | Internal generator error (pipeline exception) |

## Key Design Decisions

1. **Incremental caching**: All pipeline models implement `IEquatable<T>`. Equality on TranslatedCallSite includes PipelineError to detect error state changes.
2. **[ThreadStatic] for side-channels**: PipelineErrorBag and TraceCapture use thread-static storage (not ConcurrentBag) because the incremental pipeline is single-threaded per compilation.
3. **Display class prediction**: Generator predicts compiler-generated closure class names to emit [UnsafeAccessor] methods for captured variable extraction without reflection.
4. **Error type resolution cascade**: When SemanticModel reports TypeKind.Error for a captured variable: TryResolveErrorType (generic type args) → TryQualifyErrorTypeFromUsings (namespace search) → ChainResultTypeResolver (chain terminal analysis) → fallback "object".
5. **IsUnresolvedTypeName strict/lenient split**: Strict treats "object" as unresolved (chain analysis). Lenient allows "object" (projection analysis where it is a valid placeholder via fallbackToObject).
6. **Enum constant folding**: SqlExprAnnotator folds enum member accesses to LiteralExpr before parameter extraction. CapturedValueExpr reaching the translator are always genuine runtime captures.
7. **Conditional mask limit**: Max 8 conditional bits (256 SQL variants). Beyond that, tier falls back to RuntimeBuild.

## Testing

- **2465 tests** (2404 Quarry.Tests + 61 Analyzers.Tests)
- Cross-dialect SQL output tests in `Quarry.Tests/SqlOutput/CrossDialect*.cs` — primary regression gate
- `QueryTestHarness` seeds SQLite with known data, runs queries, asserts SQL + result values across all 4 dialects
- `TypeClassificationTests` — 135 unit tests for type classification utilities
- `DisplayClassEnricherTests` — closure analysis and type resolution tests
- `DateTimeOffsetIntegrationTests` — GetFieldValue round-trip tests
