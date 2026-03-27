# Quarry

Compile-time SQL builder for .NET 10. Roslyn source generators + C# 12 interceptors. All SQL pre-built. Zero reflection, AOT compatible. Logging via Logsmith Abstraction mode (zero-dependency).

**Architecture: Carrier-only.** All query chains must be statically analyzable. No runtime SQL builder fallback. Non-analyzable chains produce compile error QRY032.

## Packages

- `Quarry` (net10.0) — Runtime: carrier base classes, interfaces, schema DSL, executor, migrations. Logsmith 0.5.0 `<LogsmithMode>Abstraction</LogsmithMode>` (PrivateAssets=all)
- `Quarry.Generator` (netstandard2.0) — Roslyn incremental generator: interceptor emission, entity/context codegen, migration codegen
- `Quarry.Analyzers` (netstandard2.0) — 20 compile-time SQL analysis rules (QRA series) + code fixes
- `Quarry.Tool` (net10.0) — CLI: `quarry migrate`, `quarry scaffold`, `quarry create-scripts`
- `Quarry.Shared` — Shared source project (linked via MSBuild `<Import>`): SQL formatting, migration diffing/codegen, scaffold introspection. Conditional namespace: `QUARRY_GENERATOR` → `Quarry.Generators.Sql`, else `Quarry.Shared.Sql`
- `Quarry.Tests` — NUnit tests using `QueryTestHarness` (4-dialect cross-dialect testing)
- `Quarry.Benchmarks` (net10.0) — BenchmarkDotNet vs raw ADO.NET, Dapper, EF Core
- `Quarry.Sample.WebApp` (net10.0) — Razor Pages + SQLite sample app demonstrating schema, context, queries, auth, migrations

## Usage

### Schema

```csharp
[EntityReader(typeof(MyReader))]  // optional custom materialization
public class UserSchema : Schema
{
    public static string Table => "users";
    // protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<decimal> Total => Precision(18, 2);
    public Col<MyEnum> Priority { get; }           // enum → underlying type
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
    public Index IX_Name => Index(UserName).Unique();
}
```

Column types: `Key<T>` PK, `Col<T>` standard, `Ref<TSchema,TKey>` FK, `Many<T>` 1:N nav, `Index`, `CompositeKey`. Generated entities use `EntityRef<TEntity,TKey>` for FKs.
Modifiers: `Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `Default(()=>v)`, `MapTo("name")`, `Mapped<TMapping>()`, `Sensitive()`.
NamingStyle: `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase`.
Index modifiers: `Unique()`, `Where(col)`, `Where("sql")`, `Include(cols...)`, `Using(IndexType)`, `.Asc()`/`.Desc()`.

### Custom Type Mapping

```csharp
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}
// Schema: public Col<Money> Balance => Mapped<Money, MoneyMapping>();
```

Dialect-aware: implement `IDialectAwareTypeMapping` for `GetSqlTypeName(dialect)` and `ConfigureParameter(dialect, param)`.

### Context

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
}
```

Multiple contexts with different dialects can coexist. Generator resolves context from receiver chain at each call site.

**InterceptorsNamespaces:** Consumer `.csproj` must add the context's namespace to `InterceptorsNamespaces`. The generator emits interceptors into the context's namespace (or `Quarry.Generated` if the context has no namespace):
```xml
<InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
```

### Querying

```csharp
await using var db = new AppDb(connection);

// Select (tuple, DTO, single column, entity)
var users = await db.Users
    .Where(u => u.IsActive && u.UserId > minId)
    .OrderBy(u => u.UserName)
    .Select(u => new UserDto { Name = u.UserName })
    .Limit(10).Offset(20)
    .ExecuteFetchAllAsync();

// Aggregates — GroupBy available on IEntityAccessor<T> and IQueryBuilder<T>
db.Orders.GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)));

// Joins (2/3/4-table, max 4) — supports whole-entity projection from any alias
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => (u.UserName, o.Total))
    .Where((u, o) => o.Total > 100);
// Navigation: db.Users.Join(u => u.Orders)
// Joined entity projection: .Select((u, o) => o) — projects full entity from alias
// Also: LeftJoin, RightJoin

// Subqueries on Many<T>
db.Users.Where(u => u.Orders.Any(o => o.Total > 100));  // EXISTS
db.Users.Where(u => u.Orders.All(o => o.Status == "paid")); // NOT EXISTS + negated
db.Users.Where(u => u.Orders.Count() > 5);              // scalar COUNT

// Where operators: ==, !=, <, >, <=, >=, &&, ||, !, null checks
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// Collection: new[]{1,2,3}.Contains(u.Id) → IN
// Raw: Sql.Raw<bool>("\"Age\" > @p0", 18)
```

### Modifications

```csharp
// Insert — initializer-aware (only set properties generate columns)
await db.Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync();
var id = await db.Insert(user).ExecuteScalarAsync<int>();

// Batch insert
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();

// Update — requires Where() or All()
await db.Update<User>().Set(u => u.UserName, "New").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Update<User>().Set(new User { UserName = "New" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();

// Delete — requires Where() or All()
await db.Delete<User>().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

### PreparedQuery (Multi-Terminal)

`.Prepare()` freezes a chain into `PreparedQuery<TResult>`, allowing multiple terminals on the same compiled chain:

```csharp
var q = db.Users.Where(u => u.IsActive).Select(u => u).Prepare();
var diag = q.ToDiagnostics();           // inspect SQL
var all  = await q.ExecuteFetchAllAsync(); // execute
```

Single-terminal: zero overhead (elided via `Unsafe.As`). Multi-terminal: carrier covers all observed terminals.
Scope constraint: PreparedQuery variable must not escape method scope (no return, no argument passing, no lambda capture) — QRY035 error.
No terminals on PreparedQuery → QRY036 error.

### Execution Methods

`ExecuteFetchAllAsync()` → `Task<List<T>>`, `ExecuteFetchFirstAsync()` → `Task<T>`, `ExecuteFetchFirstOrDefaultAsync()` → `Task<T?>`, `ExecuteFetchSingleAsync()` → `Task<T>`, `ExecuteScalarAsync<T>()` → `Task<T>`, `ExecuteNonQueryAsync()` → `Task<int>`, `ToAsyncEnumerable()` → `IAsyncEnumerable<T>`, `ToDiagnostics()` → `QueryDiagnostics`.

### Raw SQL

```csharp
await db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

Source-generated typed readers — zero reflection.

### Diagnostics (QueryDiagnostics)

`ToDiagnostics()` returns compile-time analysis: `Sql`, `Parameters` (active only), `AllParameters`, `Kind`, `Dialect`, `TableName`, `Tier`, `IsCarrierOptimized`, `Clauses` (per-clause SQL + params + source location + conditional info), `SqlVariants` (`Dictionary<int, SqlVariantDiagnostic>` — mask→SQL map), `ProjectionColumns`, `ProjectionKind`, `CarrierClassName`, `Joins`, `IsDistinct`, `Limit`, `Offset`, `IdentityColumnName`, `ActiveMask` (int), `ConditionalBitCount`, `TierReason`, `DisqualifyReason`, `CarrierIneligibleReason`, `UnmatchedMethodNames`.

### Trace

Add `QUARRY_TRACE` to consumer `.csproj` + `.Trace()` to chain. Trace comments emitted as `// [Trace]` lines in generated interceptors. Categories: Discovery, Binding, Translation (per-site), ChainAnalysis, Assembly, Carrier (per-chain). Without `QUARRY_TRACE` symbol: QRY034 warning.

### Scaffold

`quarry scaffold --connection "..." --dialect SQLite --output ./Schemas` — reverse-engineers DB to schema classes. Per-dialect introspectors, junction table detection, implicit FK detection, singularization.

### Logging

Logsmith Abstraction mode — zero runtime dependency. Logsmith 0.5.0 with `<LogsmithMode>Abstraction</LogsmithMode>` + `PrivateAssets="all"` generates logging types directly into the Quarry assembly. No `using Logsmith;` — types are emitted into the assembly. Log checks use `LogsmithOutput.Logger?.IsEnabled(level, category) == true` pattern (null-safe for no-logger scenarios).

Categories: `Quarry.Connection` (Info), `Quarry.Query`/`Quarry.Modify`/`Quarry.RawSql` (Debug), `Quarry.Parameters` (Trace, sensitive columns redacted), `Quarry.Execution` (Warning: slow queries). `Sensitive()` modifier → parameter values displayed as `***` in logs. Per-operation `opId` via `OpId.Next()` correlates all log entries.

## Architecture (Internals)

### Carrier-Only Execution Model

All runtime builder classes (QueryBuilder, JoinedQueryBuilder, DeleteBuilder, UpdateBuilder, InsertBuilder, SqlBuilder, QueryState, ModificationExecutor, EntityAccessor) have been removed. The architecture is 100% carrier-based:

1. Generator analyzes each query chain at compile time
2. Emits a `file sealed class Chain_N` carrier extending a base carrier class
3. Carrier owns SQL: single-variant → `static readonly string _sql`; multi-variant → `static readonly string[] _sql` (array indexed by mask, gaps filled with `null!` to surface routing bugs as NRE)
4. Each clause interceptor stores parameters on carrier fields, sets `ClauseMask` bits (int)
5. Terminal interceptor reads `_sql` (or `_sql[Mask]`) and binds parameters with mask-gated conditional support
6. `QueryExecutor` static methods execute the pre-built `DbCommand`

**Carrier base classes** (`Internal/`):
- `CarrierBase<T>` / `CarrierBase<T,TResult>` — SELECT queries
- `JoinedCarrierBase<T1,T2>` through `JoinedCarrierBase4<T1,T2,T3,T4>` (± TResult) — joins (max 4 tables)
- `DeleteCarrierBase<T>`, `UpdateCarrierBase<T>`, `InsertCarrierBase<T>`, `BatchInsertCarrierBase<T>` — modifications

All base class methods throw `InvalidOperationException` — generator replaces them with actual implementations via interceptors.

**Optimization tier:** Only `PrebuiltDispatch` exists. ≤4 conditional bits → up to 16 SQL variants. Mask type is `int` (narrowed from `ulong`; max value 255 with 8-bit cap). Non-analyzable chains → compile error QRY032.

**ExpressionHelper** (`Internal/ExpressionHelper.cs`): Runtime helper for extracting collection values and member chain values from expression trees. Used by generated interceptors when the collection receiver cannot be accessed directly (non-public, instance, local, or complex expression). Methods: `ExtractContainsCollection<T>(MethodCallExpression)`, `ExtractMemberChainValue(MemberExpression)`.

### Builder Interfaces

**Query:** `IEntityAccessor<T>` (entry point, includes `GroupBy<TKey>()`) → `IQueryBuilder<T>` (no projection) → `IQueryBuilder<T,TResult>` (with projection, adds execution terminals). `IJoinedQueryBuilder<T1,T2>` through `IJoinedQueryBuilder4<T1,T2,T3,T4>` (± TResult).
**Modification:** `IDeleteBuilder<T>` → `IExecutableDeleteBuilder<T>` (via Where/All). `IUpdateBuilder<T>` → `IExecutableUpdateBuilder<T>`. `IInsertBuilder<T>`. `IBatchInsertBuilder<T>` → `IExecutableBatchInsert<T>` (via Values).
**All** support `.Prepare()` → `PreparedQuery<TResult>`.

### Dialect System

`SqlDialect` enum: `SQLite=0`, `PostgreSQL=1`, `MySQL=2`, `SqlServer=3`. `SqlFormatting` static class with `[AggressiveInlining]` switch expressions: `QuoteIdentifier`, `FormatTableName`, `FormatParameter`, `FormatBoolean`, `FormatReturningClause`, `FormatMixedPagination`, etc. `SqlClauseJoining` assembles WHERE/HAVING with auto-parenthesization.

`FormatMixedPagination(dialect, literalLimit, limitParamIndex, literalOffset, offsetParamIndex)` — handles any combination of literal and parameterized limit/offset values. Replaces former `FormatParameterizedPagination`.

| | SQLite | PostgreSQL | MySQL | SqlServer |
|---|---|---|---|---|
| Quote | `"` | `"` | `` ` `` | `[`/`]` |
| Params | `@p0` | `$1` (1-based) | `?` | `@p0` |
| Bool | `1`/`0` | `TRUE`/`FALSE` | `1`/`0` | `1`/`0` |
| Pagination | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `OFFSET/FETCH` |
| Returning | `RETURNING` | `RETURNING` | `LAST_INSERT_ID()` | `OUTPUT INSERTED` |

### Generator Pipeline

Entry: `QuarryGenerator : IIncrementalGenerator` — three pipelines.

**Pipeline 1 — Schema/Context:** `ClassDeclarationSyntax` → `ContextParser` → `SchemaParser` per entity → emits entity classes + context partials + migration code.

**Pipeline 2 — Interceptors (6-stage IR):**

```
Stage 1: Discovery     → RawCallSite        (syntax + semantic)
Stage 2: Binding       → BoundCallSite      (+ entity/ctx from EntityRegistry)
Stage 3: Translation   → TranslatedCallSite (+ SQL expr/params)
Stage 4: Analysis      → AnalyzedChain      (+ query plan, tier, masks)
Stage 5: Assembly      → AssembledPlan      (+ SQL strings per mask)
Stage 6: Emission      → C# interceptor source
```

**Pipeline 3 — Migrations:** Discovers `[Migration]`/`[MigrationSnapshot]` → `MigrationInfo`/`SnapshotInfo` → QRY050–055 diagnostics.

### Stage 1 — Discovery (`Parsing/`)

| Class | Role |
|---|---|
| `UsageSiteDiscovery` | Syntactic predicate (`IsQuarryMethodCandidate`) + semantic analysis → `RawCallSite` with method, kind, entity, SqlExpr, chain ID, conditional info, projection, Prepare detection. Object initializer chain differentiation: uses per-member `SpanStart` as scope key to prevent independent chains in `new Dto { A = db.X()..., B = db.Y()... }` from merging |
| `VariableTracer` | Traces builder-type variable declarations (up to 2 hops) for chain unification |
| `AnalyzabilityChecker` | Validates chain is compile-time analyzable (no cross-method, no dynamic, no loops) |
| `ContextParser` | `[QuarryContext]` → `ContextInfo` |
| `SchemaParser` | `{Entity}Schema : Schema` → `EntityInfo` |
| `NamingConventions` | Property→column naming (Exact/SnakeCase/CamelCase/LowerCase) |

**Prepare discovery:** Detects `.Prepare()` calls, classifies terminals on `PreparedQuery` variables (`IsPreparedTerminal`), detects scope escape (`PreparedQueryEscapeReason`), traces through variable to find originating builder type.

### Stage 2 — Binding (`IR/CallSiteBinder`)

`CallSiteBinder.Bind(RawCallSite, EntityRegistry)` → `BoundCallSite`. Resolves entity from `EntityRegistry` (multi-key index), builds `InsertInfo`/`UpdateInfo`, resolves join entities and FK relationships.

### Stage 3 — Translation (`IR/CallSiteTranslator`)

`CallSiteTranslator.Translate(BoundCallSite)` → `TranslatedCallSite`. Runs `SqlExprBinder` (column resolution), `SqlExprClauseTranslator` (parameter extraction → `@p{n}` slots), `SqlExprRenderer` (SQL fragment).

### Stage 4 — Analysis (`Parsing/ChainAnalyzer`)

`ChainAnalyzer.Analyze(translatedSites, registry)` → `AnalyzedChain[]`. Groups by ChainId, identifies terminal, detects forks (→ QRY033), allocates conditional bitmasks, classifies tier.

**Multi-terminal handling:** Detects `.Prepare()` site + prepared terminals. Single-terminal → standard chain (Prepare elided). Multi-terminal → carrier covers all observed terminals.

**Parameter enrichment:** `EnrichParametersFromColumns` matches Where/Having params to entity columns for IsEnum/IsSensitive metadata. `EnrichSetParametersFromColumns` does the same for Set clause assignments (different expression structure). Enum parameters without explicit underlying type default to `int`.

**Projection failure handling:** Failed projections (e.g., anonymous types) → chain disqualified to RuntimeBuild with appropriate reason.

**Joined entity projection:** `ProjectionInfo.JoinedEntityAlias` signals that `BuildProjection` should populate all columns from the entity at the given alias using the registry, since discovery-time column lookup is empty for joined entity projections (e.g., `.Select((u, o) => o)`).

**Type resolution:** `IsUnresolvedTypeName(typeName)` detects error types (`"?"`, `"object"`, empty/null) from semantic model to trigger enrichment from entity metadata.

### Stage 5 — Assembly (`IR/SqlAssembler`)

`SqlAssembler.Assemble(chain, registry)` → `AssembledPlan`. Renders SQL per mask variant using `FormatMixedPagination` (supports literal + parameterized limit/offset), handles RETURNING/OUTPUT for identity inserts, dialect-specific pagination. WHERE parameter indexing uses global offsets across all terms (not just active ones) to prevent parameter slot mismatches in conditional variants. `RenderWhereCondition` uses base `paramIndex` for both AND/OR children since `ParamSlotExpr.LocalIndex` is clause-global.

### Stage 6 — Emission (`CodeGen/`)

`PipelineOrchestrator` groups by context+file → `FileEmitter.Emit()`. Also performs **result type patching**: `BuildResultTypePatches` scans assembled plans for unresolved result types (including tuple types with `object` elements like `(object, object)`) and patches them via `TranslatedCallSite.WithResolvedResultType()`. `IsUnresolvedResultType` detects `"?"`, `"object"`, tuples with unresolved elements, and empty type parts from Roslyn error rendering.

Emitters:

| Emitter | Handles |
|---|---|
| `CarrierAnalyzer` | Carrier eligibility, `CarrierPlan` |
| `CarrierEmitter` | Carrier class declarations (including `_sql` field) + method bodies, mask-gated parameter binding and logging |
| `ClauseBodyEmitter` | Where, OrderBy, Select, GroupBy, Having, Set |
| `JoinBodyEmitter` | Join/LeftJoin/RightJoin + joined clauses |
| `TerminalBodyEmitter` | FetchAll, FetchFirst, ExecuteNonQuery, ExecuteScalar, ToDiagnostics, Prepare |
| `TransitionBodyEmitter` | Delete/Update/Insert transitions, ChainRoot, Limit/Offset/Distinct/WithTimeout |
| `RawSqlBodyEmitter` | RawSqlAsync, RawSqlScalarAsync |
| `InterceptorRouter` | Routes `InterceptorKind` → emitter category |
| `TerminalEmitHelpers` | `BuildParamConditionalMap`, `GetParameterValueExpression`, shared terminal emission utilities |

**Carrier SQL ownership:** `CarrierEmitter.EmitCarrierSqlField` emits SQL on the carrier class itself. Single-variant → `static readonly string _sql`; multi-variant → `static readonly string[] _sql` indexed by mask. Terminal emitters reference carrier's `_sql` field instead of inline SQL. Diagnostics `SqlVariantDiagnostic` entries reference the carrier's field.

**Mask-aware parameter binding:** `EmitCarrierCommandBinding` groups conditional parameters by bit index, emitting `if ((Mask & (1 << bitIndex)) != 0)` blocks. Collection parameters (`IsCollection`) expanded into N individual DbParameters in a loop. Unconditional parameters and pagination parameters are always bound. No intermediate `__pVal*` locals — values read directly from carrier fields. Same mask-gating applies to inline parameter logging in `EmitInlineParameterLogging`. FieldInfo cache fields for captured closure variables live on the carrier class (not the interceptor class).

**Receiver type construction:** `InterceptorCodeGenerator.BuildReceiverType` centralizes `this` parameter type building. `IEntityAccessor`/`EntityAccessor` take only entity type arg; `IQueryBuilder` takes entity + result type args. `IsEntityAccessorType` helper identifies accessor types.

### SqlExpr IR (`IR/SqlExpr*`)

Dialect-agnostic expression tree. Pipeline: `SqlExprParser.Parse` (C# syntax → unresolved) → `SqlExprAnnotator.Annotate` (type enrichment, error type guard, member-access expression type resolution) → `SqlExprBinder.Bind` (column resolution, boolean context propagation for AND/OR children → bare bool columns emit `col = 1`/`col = TRUE`) → `SqlExprClauseTranslator.ExtractParameters` (captured values → param slots) → `SqlExprRenderer.Render` (dialect-specific SQL).

Nodes: `ColumnRefExpr`, `ResolvedColumnExpr`, `ParamSlotExpr`, `LiteralExpr`, `BinaryOpExpr`, `UnaryOpExpr`, `FunctionCallExpr`, `InExpr`, `IsNullCheckExpr`, `LikeExpr`, `CapturedValueExpr`, `SqlRawExpr`, `RawCallExpr`, `SubqueryExpr`.

### Key Model Types

- `ContextInfo` — className, namespace, dialect, schema, entities
- `EntityInfo` — entityName, tableName, namingStyle, columns, navigations, indexes, compositeKeyColumns, customEntityReaderClass
- `ColumnInfo` — propertyName, columnName, clrType, isNullable, kind, modifiers, isEnum, customTypeMappingClass
- `RawCallSite` — 60+ fields: method, kind, builderKind, entity, SqlExpr, chainId, conditionalInfo, projectionInfo, IsPreparedTerminal, PreparedQueryEscapeReason. `WithResultTypeName(string)` immutable copy for post-hoc type patching
- `BoundCallSite` — raw + context, dialect, entity (EntityRef), joinedEntity, insertInfo, updateInfo. `WithRaw(RawCallSite)` immutable copy
- `TranslatedCallSite` — bound + translatedClause (resolvedExpr, parameters, sqlFragment). `WithResolvedResultType(string)` chains Raw→Bound→Translated copy
- `QueryPlan` (IR) — kind, tables, joins, where/order/group/having/set terms, projection, pagination, parameters, tier, conditional masks (`IReadOnlyList<int>`)
- `AssembledPlan` — plan + sqlVariants (`Dictionary<int, AssembledSqlVariant>`), PreparedTerminals, PrepareSite
- `CarrierPlan` — isEligible, className, fields, parameters, maskType, implementedInterfaces
- `EntityRegistry` — multi-key entity index with ambiguity detection
- `InsertInfo` — columns, identityColumnName, identityPropertyName
- `ProjectionInfo` — kind (Entity/Anonymous/Dto/Tuple/SingleColumn), resultTypeName, columns, joinedEntityAlias, failureReason

### Enums

| Enum | Key Values |
|---|---|
| `InterceptorKind` | 55+ values: Select, Where, OrderBy, ThenBy, GroupBy, Having, Set, Join, LeftJoin, RightJoin, Execute*, Insert*, BatchInsert*, Delete*, Update*, RawSql*, Limit, Offset, Distinct, WithTimeout, Trace, Prepare, transitions |
| `BuilderKind` | Query, Delete, ExecutableDelete, Update, ExecutableUpdate, JoinedQuery, EntityAccessor, BatchInsert, ExecutableBatchInsert |
| `QueryKind` | Select, Delete, Update, Insert |
| `OptimizationTier` | PrebuiltDispatch, RuntimeBuild (compile error) |
| `ClauseRole` | Select, Where, OrderBy, ThenBy, GroupBy, Having, Join, Set, Limit, Offset, Distinct, ChainRoot, *Transition, BatchInsertValues |
| `ProjectionKind` | Entity, Anonymous, Dto, Tuple, SingleColumn, Unknown |

### Equality Infrastructure

All model types implement `IEquatable<T>` for Roslyn incremental caching. `EquatableArray<T>`, `EquatableDictionary<TKey,TValue>`, `EqualityHelpers`.

### Navigation Subquery Pipeline

`Many<T>` exposes compile-time markers: `Any()`, `Any(pred)`, `All(pred)`, `Count()`, `Count(pred)`. `SqlExprParser` detects `<param>.<nav>.<Method>()` → `SubqueryExpr`. FK-to-PK correlation via `NavigationInfo.ForeignKeyPropertyName`. Scope stack in `SqlExprBinder` enables nesting. SQL: `EXISTS (SELECT 1 ...)`, `NOT EXISTS (... AND NOT ...)`, `(SELECT COUNT(*) ...)`.

### Insert Pipeline

Single: `UsageSiteDiscovery` extracts `InitializedPropertyNames` from object initializer → `InsertInfo.FromEntityInfo` filters columns (skip computed/identity). Batch: `InsertBatch(lambda)` → column selector analyzed at compile time → `Values(collection)` at runtime → `BatchInsertSqlBuilder.Build()` expands prefix with entity count. MaxParameterCount guard (2100).

### Variable-Walking Chain Unification

`VariableTracer.TraceToChainRoot(receiver, semanticModel, ct, maxHops=2)` traces through builder-type variable declarations. Only traces builder types to prevent context variable collapse. `TraceResult.FirstVariableName` → ChainId consistency.

### LIKE Parameterization

`SqlLikeHelpers`: escapes `\%_` in literals, dialect-aware concatenation (MySQL `CONCAT()`, SqlServer `+`, PostgreSQL/SQLite `||`), `ESCAPE '\'` only when needed.

### Enum Handling

`SchemaParser` sets `isEnum` flag → `EnrichParametersFromColumns`/`EnrichSetParametersFromColumns` propagate IsEnum + default `EnumUnderlyingType` to `"int"` → carrier parameter binding converts to underlying integral type → reader casts result back to enum.

### RawSql Pipeline

`UsageSiteDiscovery` resolves result type → `RawSqlTypeKind` (Scalar/Entity/Dto) → `ReaderCodeGenerator` emits typed reader delegate → interceptor calls `RawSqlAsyncWithReader<T>()`.

### Multi-Context Resolution

Entity lookup: `Dictionary<string, List<(EntityInfo, ContextInfo)>>`. `TryResolveEntityContext()` walks receiver chain to find `QuarryContext` subclass. Ambiguous → QRY015 warning.

### Generated Files

- `{Namespace}.{Entity}.g.cs` — Entity class (FK as `EntityRef<T,K>`, nav as `NavigationList<T>`)
- `{Context}.g.cs` — Context partial: constructors, properties, Insert/Update/Delete methods, MigrateAsync (self-contained: uses `SqlDialect.{Dialect}` enum directly, no instance field dependency)
- `{Context}.Interceptors.g.cs` — `file static` class with `[InterceptsLocation]` methods + carrier classes (each with `_sql` field)

### Migration System

**Three layers:**
1. **Quarry.Tool** — CLI commands, opens `.csproj` via MSBuild/Roslyn, diffs via `SchemaDiffer`, generates code
2. **Quarry.Shared** — `SchemaDiffer`, `RenameMatcher` (Levenshtein), `MigrationCodeGenerator`, `SnapshotCodeGenerator`, `BackupGenerator`, `MigrationNotificationAnalyzer`
3. **Quarry runtime** — `MigrationRunner.RunAsync()`, `DdlRenderer` (dialect DDL), `MigrationBuilder` (fluent API)

SQLite handling: table rebuild pattern for AlterColumn/DropColumn/DropForeignKey.
Risk classification: Safe, Cautious, Destructive.
Snapshot lifecycle: compile previous snapshot via Roslyn in collectible `AssemblyLoadContext` → diff against current.

### Diagnostics

**Query (QRY001–QRY019):** QRY001 (warn): not analyzable. QRY002: missing Table. QRY003: invalid column type. QRY004: unknown nav entity. QRY005: unmapped projection prop. QRY006: unsupported where op. QRY007: undefined join rel. QRY008: Sql.Raw risk. QRY009: aggregate without GroupBy. QRY010: composite key unsupported. QRY011: Select required. QRY012: Where/All required. QRY013: GUID needs ClientGenerated. QRY014: anon type unsupported. QRY015 (warn): ambiguous context. QRY016: unbound param. QRY019 (warn): clause not translatable.

**Subquery (QRY020–025):** QRY020: All needs predicate. QRY021: entity not found. QRY022: FK not found. QRY023: correlation ambiguous. QRY024: non-Many property. QRY025: composite PK.

**EntityReader (QRY026–027):** QRY026 (info): custom reader active. QRY027 (error): invalid reader type.

**Index:** QRY028 (warn): redundant unique constraint.

**Sql.Raw:** QRY029 (warn): placeholder mismatch.

**Chain (QRY030–036):** QRY030 (info): prebuilt dispatch applied. QRY032 (error): chain not analyzable. QRY033 (error): forked chain (multiple terminals). QRY034 (warn): Trace without QUARRY_TRACE. QRY035 (error): PreparedQuery escapes scope. QRY036 (error): Prepare with no terminals.

**Migration (QRY050–055):** QRY050: schema drift. QRY051: unknown table/column ref. QRY052: version gap/duplicate. QRY053: pending migrations. QRY054: destructive without backup. QRY055: nullable-to-non-null.

**Internal:** QRY900: generator error.

**Analyzer (QRA series):** QRA101–106 (simplification), QRA201–205 (wasteful), QRA301–304 (performance), QRA401–402 (patterns), QRA501–502 (dialect). Code fixes: QRA101, QRA102, QRA201.

### Exceptions

`QuarryException` → `QuarryConnectionException`, `QuarryQueryException` (has `Sql`), `QuarryMappingException` (has `SourceType`/`TargetType`).

### Key Source Files

| Area | Files |
|---|---|
| Schema DSL | `Schema/Schema.cs`, `Col.cs`, `Key.cs`, `Ref.cs`, `Many.cs`, `CompositeKey.cs`, `EntityRef.cs`, `Index.cs`, `IndexBuilder.cs` |
| Mapping | `Mapping/TypeMapping.cs`, `ITypeMappingConverter.cs`, `TypeMappingRegistry.cs`, `EntityReader.cs`, `IDialectAwareTypeMapping.cs` |
| Carrier bases | `Internal/CarrierBase.cs`, `JoinedCarrierBase.cs`/`3`/`4`, `ModificationCarrierBase.cs` |
| Query interfaces | `Query/IQueryBuilder.cs`, `IJoinedQueryBuilder.cs`, `IEntityAccessor.cs`, `Modification/IModificationBuilder.cs` |
| PreparedQuery | `Query/PreparedQuery.cs` — sealed class, all methods throw (generator replaces) |
| Diagnostics | `Query/QueryDiagnostics.cs` (rich metadata, int masks), `Query/QueryPlan.cs` (lightweight) |
| Execution | `Internal/QueryExecutor.cs` (static carrier execution methods), `BatchInsertSqlBuilder.cs`, `IQueryExecutionContext.cs`, `OpId.cs`, `ExpressionHelper.cs` |
| Context | `Context/QuarryContext.cs` (implements `IQueryExecutionContext`), `QuarryContextAttribute.cs` |
| Dialect | `Quarry.Shared/Sql/SqlDialect.cs`, `SqlFormatting.cs` (+ per-dialect partials), `SqlClauseJoining.cs` |
| Aggregates | `Query/Sql.cs` — compile-time markers (Count, Sum, Avg, Min, Max, Raw, Exists) |
| Logging | `Logging/QueryLog.cs`, `ModifyLog.cs`, `RawSqlLog.cs`, `ConnectionLog.cs`, `ParameterLog.cs`, `ExecutionLog.cs` — Logsmith Abstraction mode |
| Migration runtime | `Migration/MigrationRunner.cs`, `MigrationBuilder.cs`, `DdlRenderer.cs`, `SqlTypeMapper.cs` |
| Migration shared | `Quarry.Shared/Migration/Diff/SchemaDiffer.cs`, `CodeGen/MigrationCodeGenerator.cs`, `SnapshotCodeGenerator.cs` |
| Scaffold | `Quarry.Shared/Scaffold/IDatabaseIntrospector.cs`, per-dialect introspectors, `ScaffoldCodeGenerator.cs` |
| Generator entry | `QuarryGenerator.cs` — 3 pipelines, EntityRegistry, trace collection |
| Parsing | `Parsing/UsageSiteDiscovery.cs`, `AnalyzabilityChecker.cs`, `ChainAnalyzer.cs`, `VariableTracer.cs`, `ContextParser.cs`, `SchemaParser.cs` |
| IR | `IR/RawCallSite.cs`, `BoundCallSite.cs`, `TranslatedCallSite.cs`, `CallSiteBinder.cs`, `CallSiteTranslator.cs`, `SqlAssembler.cs`, `AssembledPlan.cs`, `EntityRegistry.cs`, `PipelineOrchestrator.cs`, `QueryPlan.cs` |
| SqlExpr | `IR/SqlExprParser.cs`, `SqlExprAnnotator.cs`, `SqlExprBinder.cs`, `SqlExprClauseTranslator.cs`, `SqlExprRenderer.cs`, `SqlExprNodes.cs` |
| CodeGen | `CodeGen/FileEmitter.cs`, `CarrierAnalyzer.cs`, `CarrierEmitter.cs`, `ClauseBodyEmitter.cs`, `JoinBodyEmitter.cs`, `TerminalBodyEmitter.cs`, `TerminalEmitHelpers.cs`, `TransitionBodyEmitter.cs`, `RawSqlBodyEmitter.cs`, `InterceptorRouter.cs` |
| Generation | `Generation/ContextCodeGenerator.cs`, `EntityCodeGenerator.cs`, `InterceptorCodeGenerator.cs`, `MigrateAsyncCodeGenerator.cs` |
| Projection | `Projection/ProjectionAnalyzer.cs` (whole joined-entity projection via `AnalyzeJoinedEntityProjection`), `ReaderCodeGenerator.cs` (`GetValue()` fallback with explicit cast for `byte[]`/`DateTimeOffset`) |
| Translation | `Translation/SqlLikeHelpers.cs`, `ParameterInfo.cs` |
| Trace | `IR/TraceCapture.cs`, `Query/TraceExtensions.cs` |
| Models | `Models/` — ContextInfo, EntityInfo, ColumnInfo, InsertInfo, ProjectionInfo, InterceptorKind, OptimizationTier, QueryKind, NavigationInfo, RawSqlTypeInfo, MigrationInfo, DiagnosticInfo, CarrierField, FileInterceptorGroup, etc. |

### Test Infrastructure

**QueryTestHarness** (`Quarry.Tests/QueryTestHarness.cs`): Disposable harness providing 4 dialect contexts — `Lite` (SQLite, real in-memory DB), `Pg`/`My`/`Ss` (mock connections, SQL verification only). `CreateAsync()` seeds default schema (users, orders, order_items, accounts) + test data. `AssertDialects()` verifies SQL across all 4 dialects.

**Test pattern:**
```csharp
await using var t = await QueryTestHarness.CreateAsync();
var (Lite, Pg, My, Ss) = t;
var q = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
QueryTestHarness.AssertDialects(
    q.ToDiagnostics(), Pg..., My..., Ss...,
    sqlite: "...", pg: "...", mysql: "...", ss: "...");
var results = await q.ExecuteFetchAllAsync(); // execute on SQLite only
```

**Test files:** `SqlOutput/CrossDialect*.cs` (18+ files, 4-dialect SQL verification), `SqlOutput/PrepareTests.cs` (Prepare single/multi-terminal), `SqlOutput/JoinedEntityProjectionTests.cs` (joined entity projection), `Generation/CarrierGenerationTests.cs` (carrier class emission), `Generation/MaskAwareTerminalBindingTests.cs` (mask-gated parameter binding), `Generation/InterceptorCodeGeneratorUtilityTests.cs`, `Integration/PrepareIntegrationTests.cs` (Prepare execution), `DialectTests.cs` (pagination formatting), `UsageSiteDiscoveryTests.cs`, `VariableTracerTests.cs`.

### Build & Test

```sh
dotnet build
dotnet test src/Quarry.Tests
dotnet test src/Quarry.Analyzers.Tests
```
