# Quarry

Type-safe SQL builder for .NET 10 using source generators + C# 12 interceptors. All SQL generated at compile time. AOT compatible. Structured logging via Logsmith.

## Packages

- `Quarry` (net10.0) — Runtime types: builders, schema DSL, executors, migration runtime
- `Quarry.Generator` (netstandard2.0) — Roslyn incremental source generator + interceptor emitter + MigrateAsync generation
- `Quarry.Analyzers` (netstandard2.0) — 20 compile-time SQL query analysis rules (QRA series)
- `Quarry.Analyzers.CodeFixes` (netstandard2.0) — Code fixes for QRA101, QRA102, QRA201 (bundled with Analyzers)
- `Quarry.Tool` (net10.0) — CLI tool for migration + scaffold (`quarry` command, PackAsTool)
- `Quarry.Shared` — Shared project (source-level, not binary): SQL formatting, migration diffing/codegen, scaffold introspection. Linked into Quarry, Generator, and Tool via MSBuild `<Import>`. Conditional compilation: `QUARRY_GENERATOR` → `Quarry.Generators.Sql` namespace; otherwise `Quarry.Shared.Sql`.
- `Quarry.Tests` — NUnit tests (references both Quarry + Quarry.Generator)
- `Quarry.Benchmarks` (net10.0) — BenchmarkDotNet comparisons vs raw ADO.NET, Dapper, EF Core

## Usage

### Schema Definition

Inherit `Schema`. Declare columns as expression-bodied properties. Generator reads syntax tree at compile time.

```csharp
[EntityReader(typeof(MyUserReader))]  // optional: custom materialization
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
    public Col<MyEnum> Priority { get; }           // enum → stored as underlying type
    // public Col<T> Foo => Computed<T>();        // read-only
    // public Col<T> Foo => MapTo<T>("col_name"); // explicit column name
    // public Col<Guid> Id => ClientGenerated();  // client-side GUID

    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);

    // Composite primary key
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}
```

Column types: `Key<T>` (PK), `Col<T>` (standard), `Ref<TSchema,TKey>` (FK, TSchema must derive from Schema), `Many<T>` (1:N nav, T must derive from Schema), `Index` (database index), `CompositeKey` (multi-column PK marker). Generated entities use `EntityRef<TEntity,TKey>` for FK properties (holds `.Id` key + optional `.Value` navigation).
Modifiers: `Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `Default(()=>v)`, `MapTo("name")`, `Mapped<TMapping>()`, `Sensitive()`.
NamingStyle: `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase`.
Enum columns: detected automatically, stored/read as underlying integral type, cast on read.

**Indexes:** Declare as `Index` properties on schema: `public Index IX_Name => Index(UserName).Unique();`. Fluent modifiers: `Unique()`, `Where(boolColumn)`, `Where("raw SQL")`, `Include(columns...)`, `Using(IndexType)`. Column sort: `.Asc()` / `.Desc()`. Index types: `BTree`, `Hash`, `Gin`, `Gist`, `SpGist`, `Brin` (PostgreSQL), `Clustered`, `Nonclustered` (SQL Server). Key files: `Schema/Index.cs`, `IndexBuilder.cs`, `IndexType.cs`, `IndexedColumn.cs`.

### Custom Type Mappings

Map custom C# types to database primitives via `TypeMapping<TCustom, TDb>` + `Mapped<TCustom, TMapping>()` modifier:

```csharp
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

// In schema:
public Col<Money> Balance => Mapped<Money, MoneyMapping>();
```

**Pipeline:** Write path wraps values with `mapper.ToDb(value)`. Read path wraps with `mapper.FromDb(reader.GetXxx(ordinal))`. Where expressions detect mapped columns and propagate mapping to comparison parameters. Auto-registration via constructor side-effect into `TypeMappingRegistry`.

**Two execution paths:**
- **Compile-time interceptor:** Static `readonly` mapper instances, direct `.ToDb()`/`.FromDb()` calls in generated code.
- **Runtime fallback (QRY001):** `QueryExecutor.NormalizeParameterValue` → `TypeMappingRegistry.TryConvert` (ToDb only; FromDb always generated).

**Diagnostics:** QRY003 (column type not primitive/enum and has no mapping), QRY017 (TCustom mismatch), QRY018 (duplicate mapping for same TCustom across contexts).

**Dialect-aware mappings:** Implement `IDialectAwareTypeMapping` on a `TypeMapping` subclass to provide:
- `GetSqlTypeName(SqlDialect)` — dialect-specific SQL type name for DDL/CAST (e.g., "jsonb" on PostgreSQL)
- `ConfigureParameter(SqlDialect, DbParameter)` — provider-specific parameter properties (e.g., `NpgsqlDbType.Jsonb`)

**CLR-to-SQL type mapping:** `SqlFormatting.GetColumnTypeName(dialect, clrType, maxLength?, precision?, scale?)` maps CLR types to dialect-specific SQL type names for DDL generation. Each dialect provides correct mappings (e.g., `bool` → `BIT` on SQL Server, `BOOLEAN` on PostgreSQL, `INTEGER` on SQLite).

Key files: `Mapping/TypeMapping.cs`, `ITypeMappingConverter.cs`, `TypeMappingRegistry.cs`, `IDialectAwareTypeMapping.cs`.

### Custom Entity Reader

Override auto-generated ordinal-based reader with `EntityReader<T>` + `[EntityReader]` attribute on schema:

```csharp
public class MyUserReader : EntityReader<User>
{
    public override User Read(DbDataReader reader) => new User { /* custom logic */ };
}
```

Only applies to entity projections (`Select(u => u)`); tuple/DTO projections still use generated readers. Diagnostics: QRY026 (info, active reader), QRY027 (error, invalid reader type).

### Context Definition

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
}
```

Context properties use interface return types (`IQueryBuilder<T>`). Generator implements with concrete builders. Interceptors cast back via `Unsafe.As<>`.

Generates: entity classes, context impl with `Create()` factory method, interceptors.
Dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`.

**Multi-context:** Multiple contexts with different dialects can coexist. Each generates its own interceptor file with dialect-correct SQL. Generator resolves context from receiver chain at each call site.

### Querying

```csharp
await using var db = new AppDb(connection);

// Select — tuple, DTO, single column, or entity
var users = await db.Users
    .Select(u => new UserDto { Name = u.UserName, Email = u.Email })
    .Where(u => u.IsActive && u.UserId > minId)
    .OrderBy(u => u.UserName)
    .Limit(10).Offset(20)
    .ExecuteFetchAllAsync();

// Aggregates (Sql.* methods — compile-time only, throw at runtime)
db.Users.Select(u => Sql.Count());
db.Orders.GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)));

// 2-table joins
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => new UserOrderDto { UserName = u.UserName, Total = o.Total })
    .Where((u, o) => o.Total > 100)
    .ExecuteFetchAllAsync();
// Also: LeftJoin, RightJoin; navigation-based: Join(u => u.Orders)

// 3/4-table chained joins (max 4 tables)
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
    .Select((u, o, oi) => new { u.UserName, o.Total, oi.ProductName })
    .ExecuteFetchAllAsync();

// Navigation subqueries on Many<T> properties
db.Users.Where(u => u.Orders.Any()).ExecuteFetchAllAsync();                    // EXISTS
db.Users.Where(u => u.Orders.Any(o => o.Total > 100)).ExecuteFetchAllAsync();  // filtered EXISTS
db.Users.Where(u => u.Orders.All(o => o.Status == "paid")).ExecuteFetchAllAsync(); // NOT EXISTS + negated
db.Users.Where(u => u.Orders.Count() > 5).ExecuteFetchAllAsync();              // scalar COUNT subquery
db.Users.Where(u => u.Orders.Count(o => o.Total > 50) > 2).ExecuteFetchAllAsync(); // filtered COUNT subquery

// Where expressions: ==, !=, <, >, <=, >=, &&, ||, !, is null, is not null
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// Collection: new[] { 1,2,3 }.Contains(u.Id) => IN (1,2,3)
// Sql.Raw<T>("expr", params), Sql.Exists<T>(subquery)
```

### Modifications

```csharp
// Insert — initializer-aware: only explicitly set properties generate columns
await db.Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync();
var id = await db.Insert(user).ExecuteScalarAsync<int>(); // returns generated key
var sql = db.Insert(user).ToSql(); // preview SQL

// Batch insert — column-selector lambda + data-provider collection
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
// Variable-stored
var batch = db.Users().InsertBatch(u => (u.UserName, u.IsActive));
await batch.Values(users).ExecuteNonQueryAsync();

// Update — must call Where() or All() before execution
await db.Update<User>().Set(u => u.UserName, "New").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
// Update — POCO Set (initializer-aware, only initialized properties become SET clauses)
await db.Update<User>().Set(new User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();

// Delete — must call Where() or All() before execution
await db.Delete<User>().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

### Execution Methods

`ExecuteFetchAllAsync()` → `Task<List<T>>`, `ExecuteFetchFirstAsync()` → `Task<T>`, `ExecuteFetchFirstOrDefaultAsync()` → `Task<T?>`, `ExecuteFetchSingleAsync()` → `Task<T>`, `ExecuteScalarAsync<T>()` → `Task<T>`, `ExecuteNonQueryAsync()` → `Task<int>`, `ToAsyncEnumerable()` → `IAsyncEnumerable<T>`, `ToSql()` → `string`.

### Raw SQL

Source-generated interceptors eliminate reflection for `RawSqlAsync<T>` and `RawSqlScalarAsync<T>` — typed reader delegates generated at compile time based on result type (entity, DTO, or scalar).

```csharp
await db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
```

### Set Operations

```csharp
db.Union(query1, query2);      // also UnionAll, Except, Intersect
```

### Scaffold (Reverse Engineering)

```sh
quarry scaffold --connection "..." --dialect SQLite --output ./Schemas
```

Reverse-engineers existing database to Quarry schema classes. Per-dialect introspectors (`SqliteIntrospector`, `PostgreSqlIntrospector`, `MySqlIntrospector`, `SqlServerIntrospector`) implement `IDatabaseIntrospector`. `ReverseTypeMapper` converts SQL types to CLR types. `JunctionTableDetector` identifies M:N tables. `ImplicitForeignKeyDetector` infers relationships. `Singularizer` converts plural table names. `ScaffoldCodeGenerator` emits schema + context files.

Key files: `Quarry.Shared/Scaffold/`, `Quarry.Tool/Commands/ScaffoldCommand.cs`.

### Logging

Quarry uses Logsmith source-generated log classes. All Debug/Trace-level methods use `AlwaysEmit = true` to prevent `[Conditional("DEBUG")]` stripping in Release builds.

**Log categories and levels:**
- `Quarry.Connection` (Information): connection opened/closed
- `Quarry.Query` (Debug): SQL generated, fetch completion (row count + elapsed ms), scalar results; Error on failure
- `Quarry.Modify` (Debug): SQL generated, modification completion (operation + row count + elapsed ms); Error on failure
- `Quarry.RawSql` (Debug): SQL generated, fetch/non-query/scalar completion; Error on failure
- `Quarry.Parameters` (Trace): parameter binding (`@p0 = value`), sensitive columns redacted as `***`
- `Quarry.Execution` (Warning): slow query detection when elapsed > `SlowQueryThreshold`
- `Quarry.Migration` (Information): migration applying/applied/rolled back, dry run, SQL generated, errors

**Configuration:**
```csharp
LogManager.Initialize(c =>
{
    c.MinimumLevel = LogLevel.Debug;
    c.SetMinimumLevel("Quarry.Parameters", LogLevel.None); // per-category override
    c.AddSink(new ConsoleSink());
});
```

**Runtime control on context:**
- `db.SlowQueryThreshold = TimeSpan.FromSeconds(1)` — default 500ms, `null` disables
- All log calls guarded by `LogManager.IsEnabled(level, CategoryName)` — zero allocation when disabled

**Operation correlation:** Each operation gets a unique `opId` via `OpId.Next()` (`Interlocked.Increment`). All log entries for the same query/modification share the opId: `[42] SQL: ...`, `[42] Fetched 3 rows in 1.2ms`, `[42] @p0 = value`.

**Sensitive columns:** `Sensitive()` modifier on schema columns → `ColumnInfo.IsSensitive` → generator propagates to `ModificationParameter.IsSensitive` → parameter values displayed as `***` in logs. Only affects log output, not actual parameter binding.

**Key files:** `Logging/QueryLog.cs`, `ModifyLog.cs`, `RawSqlLog.cs`, `ConnectionLog.cs`, `ParameterLog.cs`, `ExecutionLog.cs`, `Internal/OpId.cs`.

## Architecture (Internals)

### Dialect System

`SqlDialect` enum (`SQLite=0`, `PostgreSQL=1`, `MySQL=2`, `SqlServer=3`) defined in `Quarry.Shared/Sql/SqlDialect.cs` with conditional namespace (`Quarry.Generators.Sql` when `QUARRY_GENERATOR` defined, else `Quarry`).

`SqlFormatting` static class (`Quarry.Shared/Sql/SqlFormatting.cs` + per-dialect partials) replaces the old `ISqlDialect` interface hierarchy. All methods are `[AggressiveInlining]` switch expressions. Key methods: `QuoteIdentifier`, `FormatTableName`, `FormatParameter`, `GetParameterName`, `FormatBoolean`, `FormatReturningClause`, `GetLastInsertIdQuery`, `GetIdentitySyntax`, `FormatStringConcat`, `FormatPagination`, `FormatParameterizedPagination`, `GetColumnTypeName`.

`SqlClauseJoining` static class (`Quarry.Shared/Sql/SqlClauseJoining.cs`) assembles WHERE/HAVING clauses: single condition → no parens; multiple → `(cond1) AND (cond2)`. Has callback overload for compile-time rendering.

`SqlDialectFactory` (`Quarry/Dialect/SqlDialectFactory.cs`) reduced to minimal pass-through convenience wrapper returning enum values.

### Builder Interface Abstraction

Public API uses interfaces; concrete builders are internal. Interceptors cast via `Unsafe.As<>`.

**Query interfaces:** `IQueryBuilder<T>` (no projection: Where, Select, OrderBy, ThenBy, Offset, Limit, Distinct, GroupBy, Having, WithTimeout, Join/LeftJoin/RightJoin, ToSql). `IQueryBuilder<TEntity, TResult>` (with projection: adds execution methods). `IJoinedQueryBuilder<T1,T2>` through `IJoinedQueryBuilder4<T1,T2,T3,T4>` (+ projected variants with `TResult`). Max 4 tables.

**Modification interfaces:** `IDeleteBuilder<T>` → `IExecutableDeleteBuilder<T>` (via Where/All). `IUpdateBuilder<T>` → `IExecutableUpdateBuilder<T>` (via Where/All). `IInsertBuilder<T>` (single-entity insert). `IBatchInsertBuilder<T>` (after column-selector `InsertBatch(lambda)`) → `IExecutableBatchInsert<T>` (after `Values(collection)`, supports ExecuteNonQueryAsync, ExecuteScalarAsync, ToSql, ToDiagnostics).

Key files: `Query/IQueryBuilder.cs`, `Query/IJoinedQueryBuilder.cs`, `Query/Modification/IModificationBuilder.cs`.

### Generator Pipeline

Three incremental pipelines in `QuarryGenerator.cs`:

**Pipeline 1 — Schema/Context discovery:**
`ClassDeclarationSyntax` → `ContextParser.HasQuarryContextAttribute` (syntactic) → `ContextParser.ParseContext` (semantic) → `SchemaParser.FindAndParseSchema` per entity (+ `[EntityReader]` validation) → generates entity/context files. Entity files namespaced as `{Context.Namespace}.{Entity}.g.cs`.

**Pipeline 2 — Usage site interception:**
`InvocationExpressionSyntax` → `UsageSiteDiscovery.IsQuarryMethodCandidate` (syntactic) → `UsageSiteDiscovery.DiscoverUsageSite` (semantic, includes RawSql type resolution) → `AnalyzabilityChecker` → clause translation or `PendingClauseInfo` → combine with Pipeline 1 → `EnrichUsageSiteWithEntityInfo` (fixes aggregate CLR types from entity column metadata) → group by `(ContextClassName, Namespace)` → `InterceptorCodeGenerator` (per-context file).

**Pipeline 2b — Chain analysis + pre-built SQL (NEW):**
After enrichment, `AnalyzeExecutionChains` finds terminal execution sites → `ChainAnalyzer.AnalyzeChain()` determines optimization tier → for Tier 1: `CompileTimeSqlBuilder.BuildSelectSqlMap/UpdateSqlMap/DeleteSqlMap` generates SQL for all mask variants → `ReaderCodeGenerator` generates reader delegate → bundles into `PrebuiltChainInfo` → `InterceptorCodeGenerator` emits execution interceptors with dispatch tables.

**Pipeline 3 — Migration analysis:**
Discovers `[Migration]` and `[MigrationSnapshot]` attributed classes → extracts `MigrationInfo` and `SnapshotInfo` → emits QRY050–QRY055 diagnostics.

**Enrichment phase** performs: context resolution from call site receiver chain (`ResolveContextFromCallSite`), multi-value entity lookup (same entity name in multiple contexts), dialect propagation, deferred clause translation (syntactic→semantic when EntityInfo available), joined projection analysis, join condition translation, insert initializer property extraction, aggregate type resolution from entity metadata (for generated entities where semantic model returns error types), and RawSql type enrichment with entity column metadata.

### Chain Analysis & Optimization Tiers

`ChainAnalyzer` (`Parsing/ChainAnalyzer.cs`) performs intra-method dataflow analysis on query builder chains to classify optimization tier.

**Analysis flow:**
1. Detect chain type: direct fluent chain vs variable-based chain
2. For direct chains: walk backward collecting invocations → Tier 1 with mask=0
3. For variable chains: find containing method body, cache typed descendants
4. Build flow graph: find all assignments to tracked variable, match RHS against usage sites
5. Check disqualifiers: loop assignment, try/catch, opaque method return, passed as argument, lambda capture → Tier 3
6. Identify branch points: classify as Independent (if-only) or MutuallyExclusive (if/else)
7. Assign bit indices to conditional clauses, enumerate possible masks

**Optimization tiers (`OptimizationTier` enum):**
- **Tier 1 (PrebuiltDispatch):** ≤4 conditional bits → up to 16 SQL variants as const string literals. Zero runtime string work. `ClauseMask` switch expression dispatches to correct variant.
- **Tier 2 (PrequotedFragments):** >4 conditional bits → pre-quoted SQL fragments concatenated at runtime. Lightweight assembly vs full runtime quoting.
- **Tier 3 (RuntimeBuild):** Non-analyzable chains → existing `SqlBuilder` path unchanged. Emits QRY032.

**Constants:** `MaxTier1Bits=4` (16 variants max), `MaxIfNestingDepth=2`.

Key types: `ChainAnalysisResult` (tier, clauses, conditional clauses, possible masks), `ChainedClauseSite` (site, isConditional, bitIndex, ClauseRole), `ConditionalClause` (bitIndex, BranchKind), `PrebuiltChainInfo` (analysis + SQL map + reader code + entity metadata + MaxParameterCount).

### Pre-built SQL Infrastructure

**CompileTimeSqlBuilder** (`Sql/CompileTimeSqlBuilder.cs`): Mirrors runtime `SqlBuilder`/`SqlModificationBuilder` at compile time. Produces byte-identical SQL for all mask variants. Key methods: `BuildSelectSqlMap`, `BuildUpdateSqlMap`, `BuildDeleteSqlMap` (batch build all mask variants), `BuildInsertSql` (no conditionals). Internally: `GetActiveClauses` filters by mask, `ComputeParameterBaseOffsets` renumbers parameters sequentially across active clauses.

**SqlFragmentTemplate** (`Sql/SqlFragmentTemplate.cs`): Separates static SQL text from parameter slot positions. `TextSegments[]` interleaved around `ParameterSlots[]`. `Render(dialect, parameterBaseIndex)` produces final SQL with correct global parameter placeholders. `FromClauseInfo` factory splits SQL on exact `@pN` matches (not regex).

**Execution interceptors** (vs clause interceptors):
- **Clause interceptors** (existing): target individual `.Where()`, `.Select()` calls, translate lambdas to SQL fragments
- **Execution interceptors** (new): target terminal methods (`ExecuteFetchAllAsync`, `ExecuteNonQuery`, `ToSql`), dispatch `ClauseMask` → pre-built SQL string literal, call `ExecuteWithPrebuiltSqlAsync`/`ExecuteWithPrebuiltParamsAsync`

**Pre-allocated parameter binding:** `QueryBuilder.AllocatePrebuiltParams(MaxParameterCount)` called by first clause interceptor. `BindParam(value)` writes sequentially. `SetClauseBit(bit)` accumulates mask. At terminal: `WithPrebuiltParams` hydrates `QueryState`. Same pattern on `JoinedQueryBuilder`, `UpdateBuilder`, `DeleteBuilder`.

### Multi-Context Resolution

Entity lookup is `Dictionary<string, List<(EntityInfo, ContextInfo)>>` — same entity name can exist in multiple contexts. Resolution: `TryResolveEntityContext()` walks receiver chain to find concrete `QuarryContext` subclass → determines dialect. Ambiguous cases (unresolvable context) emit `QRY015` warning and use first match.

### Join Pipeline

Join chain: `QueryBuilder<T>` → `JoinedQueryBuilder<T1,T2>` → `JoinedQueryBuilder3<T1,T2,T3>` → `JoinedQueryBuilder4<T1,T2,T3,T4>` (max 4 tables). Table aliasing: positional aliases `t0`, `t1`, `t2`, `t3` assigned via `QueryState.FromTableAlias`. Joined clauses (Where/OrderBy/Select) use multi-parameter lambdas translated with `ExpressionTranslationContext.TableAliases` for qualified column references (`t0."column"`).

Translation methods: `ClauseTranslator.TranslateJoin` (2-param lambda → ON SQL), `TranslateChainedJoinFromEntityInfo` (N-param for chained joins), `TranslateJoinedWhere`/`TranslateJoinedOrderBy` (multi-entity clauses). `ProjectionAnalyzer.AnalyzeJoined` handles multi-entity Select with per-parameter column lookups including joined aggregate projections.

### Navigation Subquery Pipeline

`Many<T>` exposes compile-time markers: `Any()`, `Any(predicate)`, `All(predicate)`, `Count()`, `Count(predicate)` — all throw at runtime, replaced by interceptors.

**Translation flow:** `ExpressionSyntaxTranslator` detects `<param>.<navProperty>.<Method>()` → resolves `NavigationInfo` from `EntityInfo.Navigations` → `TranslateNavigationAny/All/Count` → `TranslateSubqueryInner` builds correlated subquery.

**FK-to-PK correlation:** `ResolveForeignKeyCorrelation` finds FK column on inner entity matching `NavigationInfo.ForeignKeyPropertyName`, resolves outer PK (same-name match or single PK fallback). Composite PKs rejected (QRY025).

**Scope management:** `ExpressionTranslationContext` maintains `List<SubqueryScope>` stack. Each scope has `ParameterName`, `EntityInfo`, `TableAlias` (sq0, sq1, ...), `ColumnLookup`. Scopes checked innermost-first, enabling arbitrary nesting.

**SQL patterns:**
- `Any()` → `EXISTS (SELECT 1 FROM inner AS "sq0" WHERE "sq0"."FK" = "outer"."PK")`
- `Any(pred)` → same + `AND (pred)`
- `All(pred)` → `NOT EXISTS (... WHERE correlation AND NOT (pred))`
- `Count()` → `(SELECT COUNT(*) FROM inner AS "sq0" WHERE correlation)`
- `Count(pred)` → `(SELECT COUNT(*) FROM inner AS "sq0" WHERE correlation AND (pred))`

Key types: `SubqueryScope` (`Translation/SubqueryScope.cs`), `NavigationInfo` (`Models/NavigationInfo.cs`).

### RawSql Interceptor Pipeline

**Discovery:** `UsageSiteDiscovery` identifies `RawSqlAsync<T>` and `RawSqlScalarAsync<T>` calls → resolves result type T via semantic model → classifies as `RawSqlTypeKind.Scalar`, `Entity`, or `Dto`.

**Enrichment:** Entity result types enriched with column metadata from Pipeline 1. DTO types resolved from semantic model properties. Scalar types mapped to `DbDataReader.GetXxx()` methods.

**Code generation:** `InterceptorCodeGenerator` emits typed reader delegates: entity/DTO types get ordinal-based property readers, scalar types get direct `reader.GetXxx(0)` calls. Generated interceptors call `RawSqlAsyncWithReader<T>()` or `RawSqlScalarAsyncWithConverter<T>()` on the context.

Key type: `RawSqlTypeInfo` (`Models/RawSqlTypeInfo.cs`) — `RawSqlTypeKind`, `RawSqlPropertyInfo` (per-property metadata including FK, enum, TypeMapping support).

### Insert Interceptor Pipeline

**Single insert discovery:** `UsageSiteDiscovery` identifies `Insert` call chains → extracts `InitializedPropertyNames` from object initializer syntax by walking fluent chain backward. Returns `null` if any argument is non-analyzable (variables, factory methods).

**Column selection:** `InsertInfo.FromEntityInfo()` filters columns: skip computed, skip identity (moved to RETURNING/OUTPUT), then if `InitializedPropertyNames` provided → include only those properties. Fallback: all non-identity/non-computed columns.

**Interceptor kinds:** `InsertExecuteNonQuery`, `InsertExecuteScalar`, `InsertToSql`. Generated code calls `builder.SetColumns()`, iterates entities to `AddParameter`/`AddRow`, delegates to `ModificationExecutor`.

### Batch Insert Pipeline

**API:** `IEntityAccessor<T>.InsertBatch<TColumns>(Func<T, TColumns> columnSelector)` → `IBatchInsertBuilder<T>` → `.Values(IEnumerable<T>)` → `IExecutableBatchInsert<T>`. Column selector lambda is analyzed at compile time; data provision happens at runtime.

**Discovery:** `UsageSiteDiscovery` identifies `InsertBatch`, `Values`, and terminal (`ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ToSql`, `ToDiagnostics`) call sites. `ExtractBatchInsertColumnNamesFromChain` walks the receiver chain (and traces through variables via `VariableTracer`) to find the `InsertBatch(lambda)` call and extract column names from the lambda.

**Variable-stored chains:** `VariableTracer.TraceToChainRoot` traces through up to 2 variable assignments (builder-type locals only) to unify fragmented chains. `ComputeChainId` and `ResolveContextFromCallSite` both use this tracing to ensure all sites in a variable-split chain share the same ChainId and context.

**Code generation:** `BatchInsertCarrierBase<T>` carrier class stores `BatchEntities` field. Terminal interceptors call `BatchInsertSqlBuilder.Build()` which expands the compile-time SQL prefix with runtime entity count and parameter placeholders. `MaxParameterCount` (2100) guard prevents oversized batches.

**Interceptor kinds:** `BatchInsertColumnSelector`, `BatchInsertValues`, `BatchInsertExecuteNonQuery`, `BatchInsertExecuteScalar`, `BatchInsertToSql`, `BatchInsertToDiagnostics`.

### Variable-Walking Chain Unification

**Problem:** When a fluent chain is split across local variables, each variable assignment gets a different `ChainId`, fragmenting the chain for analysis.

**Solution:** `VariableTracer` (`Parsing/VariableTracer.cs`) provides reusable primitives:
- `WalkFluentChainRoot(expr)` — walks nested invocations to the deepest non-invocation receiver
- `TraceToChainRoot(receiver, semanticModel, ct, maxHops=2)` — traces through builder-type variable declarations to find the original chain origin. Only traces through variables whose type is a known Quarry builder (prevents context variable collapse).
- `IsBuilderType(ITypeSymbol)` / `IsBuilderTypeName(shortName)` — consolidated builder type checks

**Consumers:** `ComputeChainId` (chain grouping), `ExtractBatchInsertColumnNamesFromChain` (column name extraction), `ResolveContextFromCallSite` (context resolution), `AnalyzabilityChecker.HasAnalyzableInitializer` (QRY001 suppression).

**Key invariant:** `TraceResult.FirstVariableName` records the deepest variable (closest to chain origin), matching `GetAssignedVariableName` on the root statement for ChainId consistency.

### LIKE Parameterization

String methods (`Contains`, `StartsWith`, `EndsWith`) generate parameterized LIKE with proper escaping via `SqlLikeHelpers`:
- Escapes `\` → `\\`, `%` → `\%`, `_` → `\_` in literal values
- Dialect-aware concatenation: MySQL `CONCAT()`, SqlServer `+`, PostgreSQL/SQLite `||`
- `ESCAPE '\'` clause emitted only when escaping occurs

### Enum Handling

Enum columns detected via `TypeKind.Enum` in `ColumnInfo.GetTypeMetadata()` (unwraps `Nullable<T>`). Pipeline: `SchemaParser` sets `isEnum` flag → `InterceptorCodeGenerator` handles captured enum variables (unwraps C# compiler `UnaryExpression(Convert)`) → reader casts result to enum type → `QueryExecutor.NormalizeParameterValue()` converts enums to underlying integral type at runtime for ADO.NET binding.

### Execution Model

**Three-tier optimization:**

1. **Tier 1 (PrebuiltDispatch):** Fully analyzable chain with ≤4 conditional bits → execution interceptor carries const string SQL for every possible code path (up to 16 variants). `ClauseMask` switch expression selects correct SQL. Pre-allocated parameter array filled by clause interceptors. Zero runtime string composition.

2. **Tier 2 (PrequotedFragments):** Analyzable chain with >4 conditional bits → pre-quoted SQL fragments stored on `QueryState`. `SqlBuilder.BuildFromPrequotedFragments` concatenates at runtime. Skips quoting step.

3. **Tier 3 (RuntimeBuild):** Non-analyzable chain (loop, try/catch, opaque assignment) → existing `SqlBuilder` path. `QRY001` warning for clause-level; `QRY032` for chain-level.

**Pre-built execution path:** `QueryExecutor.ExecuteWithPrebuiltSqlAsync<T>` / `ExecuteWithPrebuiltParamsAsync<T>` take pre-built SQL + reader delegate. `ModificationExecutor` has parallel `ExecuteInsertNonQueryWithPrebuiltSqlAsync`, `ExecuteUpdateWithPrebuiltSqlAsync`, `ExecuteDeleteWithPrebuiltSqlAsync`.

**QueryState extensions for pre-built:** `ClauseMask: ulong` (tracks active conditional clauses), `OffsetParameterIndex`/`LimitParameterIndex` (parameterized pagination), `PrebuiltSelectFragment`/`PrebuiltFromFragment`/etc. (Tier 2 fragments). `SetClauseBitMutable(bit)` for in-place mutation during chain building.

**SqlBuilder caching:** Thread-static `StringBuilder` via `AcquireStringBuilder()`/`ToStringAndRelease()` (recycles if <1024 bytes).

### Key Source Files

| Area | Files |
|---|---|
| Schema DSL | `Schema/Schema.cs`, `Col.cs`, `Key.cs`, `Ref.cs`, `Many.cs`, `CompositeKey.cs`, `EntityRef.cs`, `ColumnBuilder.cs`, `RefBuilder.cs` |
| Mapping | `Mapping/EntityReader.cs` (custom reader base), `EntityReaderAttribute.cs`, `TypeMapping.cs`, `ITypeMappingConverter.cs`, `TypeMappingRegistry.cs`, `IDialectAwareTypeMapping.cs` |
| Query building | `Query/IQueryBuilder.cs`, `IJoinedQueryBuilder.cs`, `QueryBuilder.cs` (pre/post-select + pre-allocated params), `JoinedQueryBuilder.cs` (2/3/4-table variants), `SetOperationBuilder.cs` |
| Query state | `Query/QueryState.cs` (immutable with-methods, `FromTableAlias` for joins, `ClauseMask`, pagination params, pre-built fragments), `Query/SqlBuilder.cs` (SQL assembly + `BuildFromPrequotedFragments`) |
| Modifications | `Query/Modification/IModificationBuilder.cs`, `InsertBuilder.cs`, `UpdateBuilder.cs` (→`ExecutableUpdateBuilder`), `DeleteBuilder.cs` (→`ExecutableDeleteBuilder`), `SqlModificationBuilder.cs`, `InsertState.cs`, `UpdateState.cs`, `DeleteState.cs`, `ModificationState.cs` |
| Context | `Context/QuarryContext.cs` (implements `IQueryExecutionContext`, `Insert<T>`/`InsertMany<T>` entry points, `RawSqlAsyncWithReader<T>`/`RawSqlScalarAsyncWithConverter<T>` internal helpers), `QuarryContextAttribute.cs` |
| Dialect/SQL formatting | `Quarry.Shared/Sql/SqlDialect.cs` (enum), `SqlFormatting.cs` (+ per-dialect partials), `SqlClauseJoining.cs`, `Quarry/Dialect/SqlDialectFactory.cs` (convenience wrapper) |
| Execution | `Internal/QueryExecutor.cs` (+ `NormalizeParameterValue`, `ExecuteWithPrebuiltSqlAsync`, `PromotePaginationParameters`), `ModificationExecutor.cs` (+ pre-built SQL methods), `IQueryExecutionContext.cs`, `OpId.cs` |
| Logging | `Logging/QueryLog.cs`, `ModifyLog.cs`, `RawSqlLog.cs`, `ConnectionLog.cs`, `ParameterLog.cs`, `ExecutionLog.cs`, `MigrationLog.cs` — Logsmith source-generated log classes |
| Schema indexes | `Schema/Index.cs`, `IndexBuilder.cs`, `IndexType.cs`, `IndexedColumn.cs` |
| Migration runtime | `Migration/MigrationRunner.cs`, `MigrationBuilder.cs`, `MigrationOptions.cs`, `MigrationDirection.cs`, `MigrationOperations.cs`, `DdlRenderer.cs`, `TableBuilder.cs`, `ColumnBuilder.cs`, `SqlTypeMapper.cs` |
| Migration attributes | `Migration/MigrationAttribute.cs`, `MigrationSnapshotAttribute.cs` |
| Migration models | `Migration/SchemaSnapshot.cs`, `TableDef.cs`, `ColumnDef.cs`, `ForeignKeyDef.cs`, `IndexDef.cs`, `ColumnKind.cs`, `NamingStyleKind.cs`, `ForeignKeyAction.cs` |
| Migration builders | `Migration/SchemaSnapshotBuilder.cs`, `TableDefBuilder.cs`, `ColumnDefBuilder.cs` |
| Migration shared | `Quarry.Shared/Migration/Diff/SchemaDiffer.cs`, `RenameMatcher.cs`, `LevenshteinDistance.cs`, `CodeGen/MigrationCodeGenerator.cs`, `SnapshotCodeGenerator.cs`, `MigrationNotificationAnalyzer.cs`, `SchemaHasher.cs`, `BackupGenerator.cs` |
| Scaffold | `Quarry.Shared/Scaffold/IDatabaseIntrospector.cs`, `SqliteIntrospector.cs`, `PostgreSqlIntrospector.cs`, `MySqlIntrospector.cs`, `SqlServerIntrospector.cs`, `ReverseTypeMapper.cs`, `JunctionTableDetector.cs`, `ImplicitForeignKeyDetector.cs`, `Singularizer.cs`, `ScaffoldCodeGenerator.cs` |
| Scaffold tool | `Quarry.Tool/Commands/ScaffoldCommand.cs` |
| Migration tool | `Quarry.Tool/Commands/MigrateCommands.cs`, `Schema/ProjectSchemaReader.cs`, `Schema/SnapshotCompiler.cs`, `Schema/DialectResolver.cs` |
| Migration generator | `Generation/MigrateAsyncCodeGenerator.cs`, `Models/MigrationInfo.cs`, `Models/SnapshotInfo.cs` |
| Aggregates | `Query/Sql.cs` — compile-time-only markers (Count, Sum, Avg, Min, Max, Raw, Exists), all throw at runtime |
| Generator entry | `QuarryGenerator.cs` — `IIncrementalGenerator`, three pipelines (schema/context, usage site + chain analysis, migration), multi-context enrichment, `BuildEntityRegistry()`, aggregate type enrichment, RawSql enrichment |
| Parsing | `Parsing/ContextParser.cs`, `SchemaParser.cs` (+ enum detection, `[EntityReader]` validation), `UsageSiteDiscovery.cs` (context resolution + joined entity extraction + insert initializer analysis + RawSql type resolution), `AnalyzabilityChecker.cs` (clause-level + full-chain analyzability), `ChainAnalyzer.cs` (intra-method dataflow, tier classification, mask enumeration), `NamingConventions.cs` |
| Translation | `Translation/ClauseTranslator.cs` (single + joined clause/join-condition translation), `ExpressionSyntaxTranslator.cs` (C# AST→SQL with qualified column names + subquery translation + Count(predicate) support), `ExpressionTranslationContext.cs` (entity metadata + table aliases + parameter tracking + subquery scope stack), `ExpressionTranslationResult.cs`, `SyntacticClauseTranslator.cs` + `SyntacticExpressionParser.cs` (syntax-only fallback), `SqlLikeHelpers.cs` (LIKE parameterization + cross-dialect escaping), `SubqueryScope.cs` (nested subquery state) |
| Projection | `Projection/ProjectionAnalyzer.cs` (single + joined projection analysis, aggregate type resolution with column lookup fallback, joined aggregate projection), `ReaderCodeGenerator.cs` (column list + reader delegate with table alias support) |
| Compile-time SQL | `Sql/CompileTimeSqlBuilder.cs` (mirror of runtime SqlBuilder for compile-time SQL generation, batch mask variant building), `Sql/SqlFragmentTemplate.cs` (text/parameter slot separation, dialect-aware rendering) |
| Code gen | `Generation/ContextCodeGenerator.cs`, `EntityCodeGenerator.cs`, `InterceptorCodeGenerator.cs` (clause interceptors + execution interceptors + dispatch tables + pre-allocated params + enum handling + tuple type sanitization) |
| Models | `ContextInfo.cs`, `EntityInfo.cs` (+ `CustomEntityReaderClass`), `EntityMapping.cs`, `ColumnInfo.cs` (+ `isEnum`, `GetTypeMetadata()`), `UsageSiteInfo.cs` (dialect + context + joined entities + pending clauses + `InitializedPropertyNames`), `ProjectionInfo.cs` (+ `CustomEntityReaderClass`, `TableAlias`), `ClauseInfo.cs` (+ `JoinClauseInfo`, `OrderByClauseInfo`, `SetClauseInfo`), `PendingClauseInfo.cs`, `InsertInfo.cs`, `ExecutionInfo.cs`, `RawSqlTypeInfo.cs` (+ `RawSqlPropertyInfo`, `RawSqlTypeKind`), `InterceptorMethodInfo.cs`, `SyntacticExpression.cs`, `NavigationInfo.cs`, `NamingStyleKind.cs`, `ChainAnalysisResult.cs` (tier, clauses, masks, conditional clauses), `PrebuiltChainInfo.cs` (SQL map, reader code, MaxParameterCount) |

### Generated Files (per context)

- `{Namespace}.{Entity}.g.cs` — Entity class with typed properties from schema (FK properties as `EntityRef<TEntity,TKey>`)
- `{Context}.g.cs` — Context partial: constructors, `Create()` factory, `IQueryBuilder<T>` properties, `Insert`/`Update`/`Delete` methods, `MigrateAsync()` (when migrations exist)
- `{Context}.Interceptors.g.cs` — `file static` class with `[InterceptsLocation]` methods per call site: clause interceptors (Where/Select/OrderBy/etc.) + execution interceptors (dispatch tables) + cached static fields (one file per context)

### Migration System Architecture

**Three-layer design:**

1. **Quarry.Tool (CLI)** — `quarry migrate add|add-empty|list|validate|remove`, `quarry create-scripts`, `quarry scaffold`. Opens `.csproj` via MSBuild/Roslyn, extracts schemas via `ProjectSchemaReader`, compiles previous snapshot via `SnapshotCompiler` (collectible `AssemblyLoadContext`), diffs via `SchemaDiffer`, generates code via `MigrationCodeGenerator`/`SnapshotCodeGenerator`.

2. **Quarry.Shared (diffing/codegen)** — `SchemaDiffer` compares `SchemaSnapshot` objects (tables, columns, FKs, indexes). `RenameMatcher` uses Levenshtein distance for table/column rename detection with configurable thresholds. `MigrationCodeGenerator` emits `Upgrade()`/`Downgrade()`/`Backup()` methods with risk-classified comments. `SnapshotCodeGenerator` emits compilable C# snapshots. `BackupGenerator` produces backup/restore SQL for destructive steps. `MigrationNotificationAnalyzer` emits dialect-specific warnings (e.g., SQLite table rebuild requirements).

3. **Quarry (runtime)** — `MigrationRunner.RunAsync()` manages `__quarry_migrations` history table, runs migrations in transactions, supports upgrade/downgrade/dry-run/backup. `DdlRenderer` converts `MigrationOperations` to dialect-specific DDL (handles SQLite table rebuild for `AlterColumn`/`DropColumn`). `MigrationBuilder` is the user-facing fluent API within migration methods. Source generator emits `MigrateAsync()` on each `QuarryContext` via `MigrateAsyncCodeGenerator`.

**Generator Pipeline 3 — Migration analysis (in `QuarryGenerator.cs`):**
Discovers `[Migration]` and `[MigrationSnapshot]` attributed classes → extracts `MigrationInfo` (version, destructive steps, backup presence, SQL steps, referenced tables/columns) and `SnapshotInfo` (version, schema hash) → emits QRY050–QRY055 diagnostics: schema drift detection (hash comparison), version gaps/duplicates, pending migrations, destructive-without-backup, nullable-to-non-null-without-data-migration.

**Snapshot lifecycle:** Each snapshot is standalone compilable C# using `SchemaSnapshotBuilder` fluent API. Tool compiles previous snapshot in-memory via Roslyn → loads in collectible `AssemblyLoadContext` → invokes `Build()` → gets `SchemaSnapshot` → diffs against current schema extracted from project.

**Risk classification:** `StepClassification.Classify()` — Safe (`CreateTable`, `AddIndex`, `AddForeignKey`, nullable `AddColumn`), Cautious (`AlterColumn`, `RenameTable`, `RenameColumn`), Destructive (`DropTable`, `DropColumn`, `DropIndex`, `DropForeignKey`, non-nullable `AddColumn` without default).

**SQLite handling:** `DdlRenderer` implements table rebuild pattern for `AlterColumn`, `DropColumn`, `DropForeignKey` — renames original table, creates new table with modifications, copies data, drops old table. `MigrationNotificationAnalyzer` emits warnings for these operations.

### Design Patterns

- **Immutable builder:** `QueryBuilder`/`JoinedQueryBuilder` — every method returns new instance via new `QueryState` (except mutable pre-built param binding: `BindParam`, `SetClauseBit`)
- **Mutable builder:** `InsertBuilder`/`UpdateBuilder`/`DeleteBuilder` — interceptors call `AddRow`/`SetColumns` incrementally
- **Interface abstraction:** Public API exposes `IQueryBuilder<T>`, `IJoinedQueryBuilder<T1,T2>`, `IModificationBuilder` interfaces. Interceptors cast to concrete types via `Unsafe.As<>`.
- **Two-phase safety:** Update/Delete require `.Where()` or `.All()` to get `ExecutableXxxBuilder<T>` before execution
- **Two-phase translation:** Syntactic parse during discovery → semantic translation during enrichment when EntityInfo available (via `PendingClauseInfo`)
- **Static dialect formatting:** `SqlFormatting` static class with switch expressions — zero virtual dispatch, fully inlineable
- **Compile-time markers:** `Sql.*` methods, `Many<T>.Any/All/Count`, set operations on context — all `throw` at runtime; replaced by interceptors
- **Per-context grouping:** Interceptors grouped by `(ContextClassName, Namespace)` for separate file generation with correct dialect
- **Initializer-aware inserts:** Generator extracts property names from object initializer syntax to minimize INSERT column lists
- **Three-tier execution:** Chain analysis → PrebuiltDispatch / PrequotedFragments / RuntimeBuild based on analyzability and conditional complexity
- **Pre-allocated params:** Fixed-size `object?[]` allocated once at chain start via `AllocatePrebuiltParams(MaxParameterCount)`, filled sequentially by clause interceptors

### Dialect Differences

| | SQLite | PostgreSQL | MySQL | SqlServer |
|---|---|---|---|---|
| Quote | `"` | `"` | `` ` `` | `[`/`]` |
| Params | `@p0` | `$1` (1-based) | `?` | `@p0` |
| Bool | `1`/`0` | `TRUE`/`FALSE` | `1`/`0` | `1`/`0` |
| Pagination | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `OFFSET/FETCH` |
| Identity | `AUTOINCREMENT` | `GENERATED ALWAYS AS IDENTITY` | `AUTO_INCREMENT` | `IDENTITY(1,1)` |
| Returning | `RETURNING` | `RETURNING` | `SELECT LAST_INSERT_ID()` | `OUTPUT INSERTED` |
| Concat | `\|\|` | `\|\|` | `CONCAT()` | `+` |
| LIKE escape | `\|\|` concat | `\|\|` concat | `CONCAT()` | `+` concat |

### Diagnostics

**Query analysis (QRY001–QRY019):**
QRY001 (warn): query not fully analyzable. QRY002: missing Table. QRY003: invalid column type. QRY004: unknown nav entity. QRY005 (warn): unmapped projection prop. QRY006: unsupported where op. QRY007: undefined join rel. QRY008 (warn): Sql.Raw injection risk. QRY009: aggregate without GroupBy. QRY010: composite keys unsupported. QRY011: Select required before exec. QRY012: Where/All required. QRY013: GUID key needs ClientGenerated. QRY014: anonymous type unsupported. QRY015 (warn): ambiguous context resolution. QRY016 (warn): unbound parameter placeholder. QRY019 (warn): clause not translatable at compile time.

**Subquery (QRY020–QRY025):**
QRY020: All() requires predicate. QRY021: subquery entity not found. QRY022: subquery FK column not found. QRY023 (warn): subquery correlation ambiguous. QRY024: method called on non-Many property. QRY025: composite-PK entity unsupported for subqueries.

**EntityReader (QRY026–QRY027):**
QRY026 (info): custom entity reader active. QRY027 (error): `[EntityReader]` type doesn't inherit `EntityReader<T>` or T doesn't match entity.

**Index (QRY028):**
QRY028 (warn): redundant unique constraint — column has both `.Unique()` modifier and a single-column unique index.

**Chain optimization (QRY030–QRY032):**
QRY030 (info): chain optimized tier 1 (pre-built dispatch). QRY031 (info): chain optimized tier 2 (pre-quoted fragments). QRY032 (info): chain not analyzable for pre-built SQL.

**Migration (QRY050–QRY055):**
QRY050 (warn): schema changed since last snapshot. QRY051 (warn): migration references unknown table/column. QRY052: migration version gap or duplicate. QRY053 (info): pending migrations detected. QRY054 (warn): destructive step without backup. QRY055 (warn): nullable-to-non-null without data migration.

**Analyzer rules (QRA series — Quarry.Analyzers):**
QRA101–QRA106 (simplification): count-to-zero, single-value IN, tautology, contradiction, redundant, nullable-without-null-check. QRA201–QRA205 (wasteful): unused join, wide SELECT, orderby-without-limit, duplicate projection, cartesian product. QRA301–QRA304 (performance): leading-wildcard LIKE, function-on-column, OR-different-columns, non-indexed WHERE. QRA401–QRA402 (patterns): query-inside-loop, multiple-same-table. QRA501–QRA502 (dialect): dialect optimization, suboptimal-for-dialect. Code fixes for QRA101, QRA102, QRA201.

**Internal:** QRY900: internal generator error.

### Exceptions

`QuarryException` → `QuarryConnectionException`, `QuarryQueryException` (has `Sql` property), `QuarryMappingException` (has `SourceType`/`TargetType`).

### Test Structure

Tests in `Quarry.Tests/`:
- `Samples/` — 4 context classes (TestDbContext/SQLite, PgDb/PostgreSQL, MyDb/MySQL, SsDb/SqlServer) with interface return types (`IQueryBuilder<T>`), 6 schemas (User/Order/OrderItem/Account/Product/Widget), DTOs, MockDbConnection, SchemaQualifiedContexts
- `Samples/InterceptorIntegrationTests.cs` — End-to-end tests via generated interceptors: select/where/join/pagination/distinct/insert/update/aggregate/conditional branching/captured parameters/execution interceptors/EntityRef/NavigationList
- `Integration/` — SQLite in-memory execution tests (`SqliteIntegrationTestBase` creates schema + seeds data): Select, Where, Join, Complex, Aggregate, EntityReader, RawSql, TypeMapping, Logging, SetOperation
- `SqlOutput/` — SQL string assertion tests:
  - `CrossDialect*.cs` — 4-dialect comparison tests using `AssertDialects()` helper (select, where, join, complex, insert, enum, string ops, subquery, aggregate, orderby, schema, misc, composition, update, delete, type mapping)
  - `CompileTimeRuntimeEquivalenceTests.cs` — Verifies compile-time `CompileTimeSqlBuilder` produces byte-identical SQL to runtime `SqlBuilder` across all 4 dialects for SELECT/DELETE/UPDATE/INSERT/pagination/conditional masks/GROUP BY/HAVING
  - `CompileTimeConverter.cs` — Bridge converting runtime state to compile-time clause inputs for equivalence testing
  - `SqlTestCase.cs` + `TestCaseExtensions.cs` — `ToTestCase()` captures both SQL and internal state from builders for testing
  - Per-feature: Aggregate, CombinedClause, Delete, Insert, Join (2/3/4-table), NullHandling, OrderBy, Pagination, ParameterFormatting, SchemaQualification, Select, SetOperation, StringOperation, Subquery, Update, Where
- `ChainAnalyzerTests.cs` — Dataflow analysis: direct/variable chains, independent/mutual-exclusive conditionals, tier classification, mask correctness, disqualifiers (loops, try/catch, lambda capture)
- `CompileTimeSqlBuilderTests.cs` — Compile-time SQL generation: per-dialect formatting, fragment templating, parameter remapping, all statement types
- `Migration/` — Migration system tests: SchemaDifferTests (+ EdgeCase, ForeignKey, Rename variants), MigrationBuilderTests (+ Strategy, SQLiteRebuild), MigrationCodeGeneratorTests, SnapshotCodeGeneratorTests, MigrationRunnerIntegrationTests (SQLite), MigrationSampleIntegrationTests, BackupGeneratorTests, RenameMatcherTests, SchemaHasherTests, SqlTypeMapperTests, ColumnBuilderTests, TableBuilderTests, CrossDialectDdlTests, DdlRendererDialectTests, MigrateAsyncCodeGeneratorTests, MigrationDiagnosticLogicTests, MigrationNotificationAnalyzerTests, ProjectSchemaReaderIndexTests, MigrationStepClassifyTests
- `Scaffold/` — ScaffoldCodeGeneratorTests
- Unit tests: EnumParameterTests, SubqueryTests, SyntacticClauseTranslatorTests, InlineExtractionGeneratorTests, UsageSiteDiscoveryTests, SelectProjectionTests, EntityReaderTests, RawSqlGeneratorPipelineTests, RawSqlInterceptorTests, TypeMapping*Tests, ExecutionInterceptorTests

Tests in `Quarry.Analyzers.Tests/`: PatternRuleTests, PerformanceRuleTests, SimplificationRuleTests, WastedWorkRuleTests, DialectRuleTests, MultipleQueriesRuleTests, SyntaxDrivenRuleTests, CodeFixTests.

Samples in `Samples/`: `1_SimpleMigration` (3-step migration demo with SQLite), `2_MigrationScripting` (script generation demo).

### Build & Test

```sh
dotnet build
dotnet test src/Quarry.Tests                                      # all tests
dotnet test src/Quarry.Analyzers.Tests                             # analyzer tests
dotnet run --project src/Quarry.Benchmarks -- --filter "*Select*"  # benchmarks
```
