# Quarry.Generator — LLM Reference

Roslyn incremental source generator for the Quarry ORM. Generates compile-time interceptors that replace runtime query building with prebuilt SQL, zero-allocation carrier classes, and typed DbDataReader delegates.

Target: `netstandard2.0` | Deps: `Microsoft.CodeAnalysis.CSharp 5.0.0` | Ships as: analyzer NuGet (`analyzers/dotnet/cs`)

## User-Facing API

### Context Declaration
```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Post> Posts();
}
```
- Partial class + `[QuarryContext]` triggers generation
- Each partial method returning `IEntityAccessor<T>` registers an entity table
- Supported dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`

### Schema Definition
```csharp
public class UserSchema : Schema
{
    public static string Table => "users";
    public static NamingStyleKind NamingStyle => NamingStyleKind.SnakeCase;
    public Key<int> Id { get; }
    public Col<string> Name { get; }
    public Col<string?> Email { get; }
    public Ref<Post, int> AuthorId { get; }          // FK → EntityRef<Post,int>
    public Many<Post> Posts { get; }                   // Navigation
}
```
- Convention: `{EntityName}Schema` discovered automatically
- Column types: `Key<T>`, `Col<T>`, `Ref<TEntity,TKey>` (FK)
- Navigation: `Many<T>` (one-to-many)
- Modifiers: `.Identity()`, `.Computed()`, `.HasDefault()`, `.MaxLength(n)`, `.Unique()`, `.Sensitive()`, `.MapTo<TCustom>()`
- `[EntityReader]` attribute on schema → custom reader class

### Query API (Fluent Chain)
```csharp
// SELECT
var users = await db.Users().Where(u => u.Active).OrderBy(u => u.Name).ExecuteFetchAllAsync(ct);
var first = await db.Users().Where(u => u.Id == id).ExecuteFetchFirstAsync(ct);
var dto   = await db.Users().Select(u => new { u.Id, u.Name }).ExecuteFetchAllAsync(ct);

// JOIN
var joined = await db.Users()
    .Join<Post>((u, p) => u.Id == p.AuthorId.Id)
    .Select((u, p) => new { u.Name, p.Title })
    .ExecuteFetchAllAsync(ct);

// Navigation join
var nav = await db.Users().Join<Post>(u => u.Posts).ExecuteFetchAllAsync(ct);

// INSERT
await db.Users().Insert(user).ExecuteNonQueryAsync(ct);
var newId = await db.Users().Insert(user).ExecuteScalarAsync<int>(ct);

// UPDATE
await db.Users().Update().Set(u => u.Name, "New").Where(u => u.Id == 1).ExecuteNonQueryAsync(ct);
await db.Users().Update().Set(u => { u.Name = "New"; u.Active = true; }).Where(...).ExecuteNonQueryAsync(ct);

// DELETE
await db.Users().Delete().Where(u => u.Id == 1).ExecuteNonQueryAsync(ct);

// RAW SQL
var results = await db.RawSqlAsync<UserDto>("SELECT id, name FROM users WHERE id = @p0", userId);
var count   = await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");

// DIAGNOSTICS
var diag = db.Users().Where(u => u.Active).ToDiagnostics();  // SQL + params at compile time
var sql  = db.Users().Where(u => u.Active).ToSql();

// PAGINATION / MISC
db.Users().Limit(10).Offset(20)
db.Users().Distinct()
db.Users().WithTimeout(TimeSpan.FromSeconds(5))
db.Users().Trace()  // requires QUARRY_TRACE preprocessor symbol
```

### Builder Interfaces
- `IEntityAccessor<T>` — entry point from context, zero-alloc struct
- `IQueryBuilder<T>` / `IQueryBuilder<T, TResult>` — SELECT chain
- `IJoinedQueryBuilder<T1,T2>` / `<T1,T2,T3>` / `<T1,T2,T3,T4>` — joined chains
- `IDeleteBuilder<T>` / `IUpdateBuilder<T>` / `IInsertBuilder<T>` — mutation chains
- `IBatchInsertBuilder<T>` / `IExecutableBatchInsert<T>` — batch insert chains (column-selector → values → terminal)

### Execution Terminals
`ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync<T>`, `ToAsyncEnumerable`

---

## Generator Architecture

### Pipeline Stages

```
Stage 1: Discovery    Stage 2: Binding    Stage 3: Translation    Stage 4: Analysis    Stage 5: Assembly    Stage 6: Emission
RawCallSite ──────→ BoundCallSite ──────→ TranslatedCallSite ──→ AnalyzedChain ──────→ AssembledPlan ──────→ C# source
  (syntax only)      (+ entity/ctx)       (+ SQL expr/params)    (+ query plan)        (+ SQL strings)
```

**Entry point**: `QuarryGenerator : IIncrementalGenerator` — registers two phases:
1. Schema discovery (contexts + entities) → emits entity classes, context partials, migration code
2. Usage site discovery → binding → translation → chain analysis → assembly → interceptor emission

### Stage 1 — Discovery (`Parsing/`)

| Class | Role |
|---|---|
| `ContextParser` | Parses `[QuarryContext]` classes → `ContextInfo` (dialect, schema, entity list) |
| `SchemaParser` | Finds `{Entity}Schema : Schema` → `EntityInfo` (columns, navigations, indexes, keys, custom reader) |
| `UsageSiteDiscovery` | Identifies Quarry builder method invocations → `RawCallSite` |
| `VariableTracer` | Traces through variable declarations to unify chains split across locals (up to 2 hops) |
| `AnalyzabilityChecker` | Validates chain is compile-time analyzable (no cross-method, no dynamic capture, no loops) |
| `NamingConventions` | Delegates property→column naming (Exact/SnakeCase/CamelCase/LowerCase) |

`UsageSiteDiscovery.IsQuarryMethodCandidate()` — syntactic predicate for incremental generator filtering.
`UsageSiteDiscovery.DiscoverRawCallSite()` — full semantic analysis producing `RawCallSite` with: method name, interceptor kind, entity type, parsed `SqlExpr`, chain ID, conditional info, projection info, set-action assignments.

### Stage 2 — Binding (`IR/CallSiteBinder`)

`CallSiteBinder.Bind(RawCallSite, EntityRegistry)` → `BoundCallSite`
- Resolves entity from `EntityRegistry` (multi-key index: schema-qualified, context-qualified, short name)
- Builds `InsertInfo`/`UpdateInfo` from entity columns
- Resolves join entity types and navigation FK relationships
- Detects ambiguous entity resolution → `QRY015`

`EntityRegistry.Build(contexts)` — compiled entity metadata cache, changes only when schemas change.

### Stage 3 — Translation (`IR/CallSiteTranslator`)

`CallSiteTranslator.Translate(BoundCallSite)` → `TranslatedCallSite`
- Runs `SqlExprBinder` (column ref → resolved column with table qualifier)
- Runs `SqlExprClauseTranslator.ExtractParameters` (captured values → `@p{n}` param slots)
- Runs `SqlExprRenderer.Render` → SQL fragment string
- Synthesizes navigation join ON clauses from FK metadata
- Enriches collection parameter element types

### Stage 4 — Analysis (`Parsing/ChainAnalyzer`)

`ChainAnalyzer.Analyze(translatedSites, registry)` → `AnalyzedChain[]`
- Groups by `ChainId`, identifies execution terminal
- Fork detection (multiple terminals → runtime fallback)
- Conditional clause bitmask allocation (bit per conditional Where/OrderBy/etc.)
- Tier classification:
  - `PrebuiltDispatch` (≤4 conditional bits) — enumerate all SQL variants
  - `PrequotedFragments` (>4 bits) — cached SQL fragments composed at runtime
  - `RuntimeBuild` — fallback for non-analyzable chains
- Builds `QueryPlan` with: table refs, joins, where/order/group/having/set terms, pagination, projection, parameters

### Stage 5 — Assembly (`IR/SqlAssembler`)

`SqlAssembler.Assemble(chain, registry)` → `AssembledPlan`
- Renders SQL for each possible conditional bitmask value
- Dispatches to SELECT/DELETE/UPDATE/INSERT rendering
- Handles RETURNING/OUTPUT for identity inserts
- Dialect-specific pagination (OFFSET/FETCH vs LIMIT)
- Counts parameters per variant

### Stage 6 — Emission (`CodeGen/`)

`PipelineOrchestrator.AnalyzeAndGroupTranslated()` → `FileInterceptorGroup[]` (grouped by context+file)

`FileEmitter.Emit()` orchestrates per-file output:

| Emitter | Handles |
|---|---|
| `CarrierAnalyzer` | Determines carrier eligibility, builds `CarrierPlan` with fields/params |
| `CarrierEmitter` | Emits carrier class declarations and carrier-path method bodies |
| `ClauseBodyEmitter` | Where, OrderBy, Select, GroupBy, Having, Set clause interceptors |
| `JoinBodyEmitter` | Join/LeftJoin/RightJoin and joined-builder clause methods |
| `TerminalBodyEmitter` | FetchAll, FetchFirst, ExecuteNonQuery, ExecuteScalar, ToDiagnostics |
| `TransitionBodyEmitter` | Delete/Update/Insert transitions, ChainRoot, Limit/Offset/Distinct/WithTimeout |
| `RawSqlBodyEmitter` | RawSqlAsync, RawSqlScalarAsync interceptors |
| `InterceptorRouter` | Routes `InterceptorKind` → emitter category |

**Carrier optimization**: eligible chains get a `file sealed class Chain_N` that implements all builder interfaces, stores parameters as fields, uses `Unsafe.As` casts instead of allocations, and dispatches SQL via `ClauseMask` switch.

### Other Generators (`Generation/`)

| Class | Output |
|---|---|
| `ContextCodeGenerator` | Partial context class with entity accessor factory methods, dialect field, constructors |
| `EntityCodeGenerator` | Entity class properties (columns, FK as `EntityRef<T,K>`, navigation as `NavigationList<T>`) |
| `InterceptorCodeGenerator` | Dispatch tables, diagnostic arrays, cached extractors, type mapping utilities |
| `MigrateAsyncCodeGenerator` | `MigrateAsync` partial method with migration tuple array |
| `ReaderCodeGenerator` | Typed `DbDataReader` delegates per projection (entity/DTO/tuple/scalar/anonymous) |
| `ProjectionAnalyzer` | Analyzes `Select()` lambdas → `ProjectionInfo` with column metadata |

---

## SqlExpr IR (`IR/SqlExpr*`)

Dialect-agnostic expression tree for SQL fragments.

### Node Types (`SqlExprNodes.cs`)
| Node | Key Fields |
|---|---|
| `ColumnRefExpr` | ParameterName, PropertyName, NestedProperty |
| `ResolvedColumnExpr` | QuotedColumnName, TableQualifier |
| `ParamSlotExpr` | LocalIndex, ClrType, ValueExpression, IsCaptured, IsCollection, IsEnum, CustomTypeMappingClass |
| `LiteralExpr` | SqlText, ClrType, IsNull |
| `BinaryOpExpr` | Left, Operator, Right |
| `UnaryOpExpr` | Operator, Operand |
| `FunctionCallExpr` | FunctionName, Arguments[], IsAggregate |
| `InExpr` | Operand, Values[], IsNegated |
| `IsNullCheckExpr` | Operand, IsNegated |
| `LikeExpr` | Operand, Pattern, IsNegated, LikePrefix, LikeSuffix, NeedsEscape |
| `CapturedValueExpr` | VariableName, SyntaxText, ClrType, ExpressionPath |
| `SqlRawExpr` | SqlText (unresolved raw SQL) |
| `RawCallExpr` | MethodName, Arguments, Namespace |
| `SubqueryExpr` | OuterParameterName, NavigationPropertyName, SubqueryKind, Predicate |

### Processing Pipeline
1. `SqlExprParser.Parse(ExpressionSyntax, lambdaParams)` — C# syntax → unresolved SqlExpr (no SemanticModel)
2. `SqlExprAnnotator.Annotate()` — best-effort type enrichment from SemanticModel
3. `SqlExprBinder.Bind(expr, entityInfo, dialect)` — `ColumnRefExpr` → `ResolvedColumnExpr`
4. `SqlExprClauseTranslator.ExtractParameters()` — `CapturedValueExpr` → `ParamSlotExpr` with `@p{n}`
5. `SqlExprRenderer.Render(expr, dialect)` — final SQL string with dialect-specific quoting

### Enums
- `SqlExprKind`: ColumnRef, ResolvedColumn, ParamSlot, Literal, BinaryOp, UnaryOp, FunctionCall, InExpr, IsNullCheck, LikeExpr, CapturedValue, SqlRaw, RawCall, ExprList, Subquery
- `SqlBinaryOperator`: Equal, NotEqual, LessThan, GreaterThan, LTE, GTE, And, Or, Add, Subtract, Multiply, Divide, Modulo, BitwiseAnd/Or/Xor
- `SqlUnaryOperator`: Not, Negate
- `SubqueryKind`: Exists, All, Count

---

## Key Model Types (`Models/`)

### Enums
| Enum | Values |
|---|---|
| `InterceptorKind` | 54+ values: Select, Where, OrderBy, ThenBy, GroupBy, Having, Set, Join, LeftJoin, RightJoin, Execute*, Insert*, BatchInsert* (ColumnSelector, Values, ExecuteNonQuery, ExecuteScalar, ToSql, ToDiagnostics), Delete*, Update*, RawSql*, Limit, Offset, Distinct, WithTimeout, Trace, transitions, Unknown |
| `BuilderKind` | Query, Delete, ExecutableDelete, Update, ExecutableUpdate, JoinedQuery, EntityAccessor, BatchInsert, ExecutableBatchInsert |
| `QueryKind` | Select, Delete, Update, Insert |
| `OptimizationTier` | PrebuiltDispatch, PrequotedFragments, RuntimeBuild |
| `ClauseKind` | Where, OrderBy, GroupBy, Having, Set, Join |
| `JoinClauseKind` | Inner, Left, Right |
| `ProjectionKind` | Entity, Anonymous, Dto, Tuple, SingleColumn, Unknown |
| `ClauseRole` | Select, Where, OrderBy, ThenBy, GroupBy, Having, Join, Set, Limit, Offset, Distinct, DeleteWhere, UpdateWhere, UpdateSet, WithTimeout, ChainRoot, *Transition |
| `BranchKind` | Independent, MutuallyExclusive |

### Core Records/Classes (all `IEquatable<T>` for incremental caching)
- `ContextInfo` — className, namespace, dialect, schema, entities[], entityMappings[], location
- `EntityInfo` — entityName, schemaClassName, tableName, namingStyle, columns[], navigations[], indexes[], compositeKeyColumns[], customEntityReaderClass
- `ColumnInfo` — propertyName, columnName, clrType, isNullable, kind, referencedEntityName, modifiers, isEnum, customTypeMappingClass, dbClrType
- `ColumnModifiers` — isIdentity, isComputed, maxLength, precision, scale, hasDefault, isForeignKey, mappedName, customTypeMapping, isUnique, isSensitive
- `NavigationInfo` — propertyName, relatedEntityName, foreignKeyPropertyName
- `IndexInfo` — name, columns[], isUnique, indexType, filter, includeColumns
- `InsertInfo` — columns[], identityColumnName, identityPropertyName
- `ProjectionInfo` — kind, resultTypeName, columns[], isOptimalPath, customEntityReaderClass
- `ProjectedColumn` — propertyName, columnName, clrType, ordinal, alias, sqlExpression, isAggregate, tableAlias
- `RawSqlTypeInfo` — resultTypeName, typeKind (Scalar/Entity/Dto), properties[]
- `MigrationInfo` — version, name, className, namespace, hasDestructiveSteps
- `DiagnosticInfo` — diagnosticId, location, messageArgs
- `DiagnosticLocation` — (struct) filePath, line, column, span

### Pipeline Data Types
- `RawCallSite` — 60+ fields: method, location, kind, builderKind, entityType, expression (SqlExpr), clauseKind, chainId, conditionalInfo, projectionInfo, analyzability flags
- `BoundCallSite` — raw + contextClassName, dialect, entity (EntityRef), joinedEntity, insertInfo, updateInfo
- `TranslatedCallSite` — bound + translatedClause (kind, resolvedExpr, parameters[], sqlFragment), keyTypeName, valueTypeName
- `TranslatedClause` — kind, resolvedExpression (SqlExpr), parameters[], isSuccess, joinKind, joinedTableName, tableAlias, setAssignments
- `QueryPlan` — kind, primaryTable, joins[], whereTerms[], orderTerms[], groupByExprs[], havingExprs[], setTerms[], projection, pagination, insertColumns[], conditionalTerms[], possibleMasks[], parameters[], tier, notAnalyzableReason
- `AssembledPlan` — plan (QueryPlan), sqlVariants (mask→sql), maxParameterCount, executionSite, clauseSites[], entityTypeName, dialect, isTraced
- `FileOutputGroup` / `FileInterceptorGroup` — contextClassName, filePath, sites[], plans[], diagnostics[], carrierPlans[]
- `CarrierPlan` — isEligible, className, baseClassName, fields[], staticFields[], parameters[], maskType, maskBitCount, implementedInterfaces[], deadMethods[]
- `EntityRef` (IR) — entityName, tableName, schemaName, columns[], navigations[], customEntityReaderClass (lightweight EntityInfo for pipeline)
- `EntityRegistry` — multi-key entity index; `Resolve(entityTypeName, contextClassName?)` with ambiguity detection

### Equality Infrastructure
- `EquatableArray<T>` — ImmutableArray wrapper with structural equality
- `EquatableDictionary<TKey,TValue>` — ImmutableDictionary wrapper with structural equality
- `EqualityHelpers` — SequenceEqual, DictionaryEqual, SqlExprSequenceEqual helpers
- `HashCode` (polyfill) — System.HashCode for netstandard2.0

---

## Diagnostics (`DiagnosticDescriptors.cs`)

45+ diagnostics, category "Quarry":

| ID | Severity | Summary |
|---|---|---|
| QRY001 | Warning | Query not analyzable (fallback to runtime) |
| QRY002 | Error | Missing Table property on schema |
| QRY003 | Error | Invalid column type (not Col/Key/Ref) |
| QRY004 | Warning | Unmapped column type |
| QRY005 | Warning | Empty schema |
| QRY006 | Warning | Missing schema class |
| QRY007 | Error | Duplicate column name |
| QRY008 | Warning | Ref<T> without matching entity |
| QRY009 | Warning | Missing FK property for navigation |
| QRY010 | Error | TypeMapping mismatch |
| QRY011 | Error | Select required (projection missing) |
| QRY015 | Warning | Ambiguous context resolution |
| QRY019 | Warning | Clause not translatable |
| QRY026-027 | Error | Custom EntityReader validation |
| QRY028 | Error | Index constraint validation |
| QRY029 | Warning | Sql.Raw placeholder mismatch |
| QRY030 | Info | Prebuilt dispatch optimization applied |
| QRY031 | Info | Prequoted fragments optimization applied |
| QRY032 | Info | Runtime build fallback with reason |
| QRY033 | Error | Forked query chain (multiple terminals) |
| QRY034 | Warning | Trace flag validation |
| QRY050-055 | Error/Warning | Migration validation |
| QRY900 | Error | Internal generator error |

---

## Utilities

- `FileHasher.ComputeFileTag(filePath)` — stable filesystem-safe tag from path (e.g. `src_Models_User`)
- `SymbolDisplayCache` — ConditionalWeakTable cache for type display strings
- `TraceCapture` — ThreadStatic per-site trace message accumulator (keyed by UniqueId)
- `SqlLikeHelpers` — LIKE escape and dialect-aware concatenation

## Key Design Decisions

1. **All model types implement `IEquatable<T>`** — required for Roslyn incremental caching to avoid redundant regeneration
2. **Two-tier generation**: entity/context classes (Phase 1, always) + interceptors (Phase 2, per-file)
3. **Carrier optimization**: eligible chains get zero-alloc sealed classes using `Unsafe.As` casts between builder interfaces
4. **Conditional dispatch**: ≤4 conditional clauses → enumerate all 2^N SQL variants; >4 → fragment composition
5. **SqlExpr is dialect-agnostic** — dialect applied only at render time
6. **Graceful degradation**: non-analyzable chains fall back to runtime `QueryBuilder` with `QRY001` warning. `EmitFileInterceptors` catches exceptions and emits `QRY900` diagnostic instead of crashing the generator.
7. **No SemanticModel in IR** — `EntityRegistry` + metadata-only types enable pure data transforms after Stage 1
8. **Variable-walking chain unification**: `VariableTracer.TraceToChainRoot` traces through builder-type variable declarations (up to 2 hops) so chains split across locals share the same `ChainId`. Only traces builder types to avoid collapsing independent chains from the same context variable.
