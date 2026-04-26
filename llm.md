# Quarry

Compile-time SQL builder for .NET 10. Roslyn source generators + C# 12 interceptors. All SQL pre-built. Zero reflection, AOT compatible. Logging via Logsmith Abstraction mode (zero-dependency).

**Architecture: Carrier-only.** All query chains must be statically analyzable. No runtime SQL builder fallback. Non-analyzable chains produce compile error QRY032.

## Packages

- `Quarry` (net10.0) — Runtime: carrier base classes, interfaces, schema DSL, executor, migrations. Logsmith 0.5.0 `<LogsmithMode>Abstraction</LogsmithMode>` (PrivateAssets=all)
- `Quarry.Generator` (netstandard2.0) — Roslyn incremental generator: interceptor emission, entity/context codegen, migration codegen, opt-in SQL manifest emission
- `Quarry.Analyzers` (netstandard2.0) — 21 compile-time SQL analysis rules (QRA series) + code fixes
- `Quarry.Migration` (netstandard2.0) — Cross-ORM conversion toolkit: parses SQL in source code (Dapper/EF Core/ADO.NET/SqlKata), resolves against Quarry schemas, emits equivalent chain API code. Roslyn analyzers (QRM series) + IDE code fixes per source tool. Backs the `quarry convert --from <tool>` CLI
- `Quarry.Tool` (net10.0) — CLI: `quarry migrate`, `quarry scaffold`, `quarry create-scripts`, `quarry convert --from {dapper|efcore|adonet|sqlkata}`
- `Quarry.Shared` — Shared source project (linked via MSBuild `<Import>`): SQL formatting, migration diffing/codegen, scaffold introspection, **SQL parser** (tokenizer + recursive-descent parser + AST + walker under `Sql/Parser/`, `#if QUARRY_GENERATOR`-gated so zero runtime surface). Conditional namespace: `QUARRY_GENERATOR` → `Quarry.Generators.Sql`, else `Quarry.Shared.Sql`
- `Quarry.Tests` — NUnit tests using `QueryTestHarness` (4-dialect cross-dialect testing)
- `Quarry.Migration.Tests` — NUnit tests for converters and analyzer code fixes
- `Quarry.Benchmarks` (net10.0) — BenchmarkDotNet vs raw ADO.NET, Dapper, EF Core, SqlKata. Published run-over-run to `Quarry-benchmarks` GitHub Pages
- `Quarry.Sample.WebApp` (net10.0) — Razor Pages + SQLite sample app demonstrating schema, context, queries, auth, migrations
- `Quarry.Sample.Aot` (net10.0) — PublishAot verification sample
- `Samples/4_DapperMigration` — End-to-end `quarry convert --from dapper` sample

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

Column types: `Key<T>` PK, `Col<T>` standard, `Ref<TSchema,TKey>` FK, `Many<T>` 1:N nav, `One<T>` reverse-side 1:1 nav, `Index`, `CompositeKey`. Generated entities use `EntityRef<TEntity,TKey>` for FKs.
Modifiers: `Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `Default(()=>v)`, `MapTo("name")`, `Mapped<TMapping>()`, `Sensitive()`.

**Navigation declarations:**
- `public Many<Order> Orders => HasMany<Order>(o => o.UserId);` — 1:N
- `public Many<Tag> Tags => HasManyThrough<Tag, OrderTag>();` — M:N skip navigation (junction→target JOIN is implicit in terminals)
- `public One<User> User => HasOne<User>();` — reverse One<T> navigation, produces nullable `T?` property on generated entity; lambdas need `!.` (e.g. `o.User!.IsActive`)

Navigation diagnostics: QRY060 (no FK for One<T>), QRY061 (ambiguous FK), QRY062 (HasOne references invalid column), QRY063 (target entity not found), QRY064/065 (HasManyThrough invalid junction/target navigation).
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

### EntityReader

Annotate a schema with `[EntityReader(typeof(MyReader))]` to route every `Select(p => p)` identity projection for that entity through a custom `EntityReader<T>` instead of the default ordinal-based materializer. The reader's `Read(DbDataReader)` method owns the materialization — useful for setting non-column properties (e.g. `DisplayLabel`) or applying entity-level transformations.

**Per-context resolution.** Quarry emits one entity class per `QuarryContext` (in the context's namespace), so `App.Pg.Product` and `App.My.Product` are distinct CLR types even when generated from the same schema. The `[EntityReader]` attribute resolves to a *simple-name* reference, and the generator looks the reader up at `<contextNamespace>.<readerSimpleName>` for every interceptor it emits. When schema and context share a namespace, this resolves to the same class as the schema-namespace declaration — so single-context consumers see no change. When a schema is referenced by multiple contexts in different namespaces, each context expects its own reader class at its own namespace, e.g. `App.Pg.MyReader : EntityReader<App.Pg.Product>` and `App.My.MyReader : EntityReader<App.My.Product>`. A missing or mis-declared per-context reader surfaces as an ordinary C# compile error against the generated interceptor reference — no analyzer rule, no fallback.

### Context

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

// Opt-in typed accessor chains — required to chain .With<Dto>(...).Users()....
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext<AppDb>
{
    public partial IEntityAccessor<User> Users();
}
```

Multiple contexts with different dialects can coexist. Generator resolves context from receiver chain at each call site.

**`QuarryContext<TSelf>`:** Generic base class enabling typed post-`With` accessor chains (`db.With<Dto>(…).Users().Join<…>()`). Opt-in — existing non-generic `QuarryContext` continues to work. `QuarryContext.With<TDto>()` is `virtual` so derived `With` overrides participate in dispatch.

**`ownsConnection`:** Constructor accepts optional `bool ownsConnection = false`. When `true`, context disposes the underlying `DbConnection` on `Dispose`/`DisposeAsync`. When `false` (default), context only closes connections it opened. Generator emits constructor overloads with the parameter on generated context classes. Use `ownsConnection: true` for DI registrations where consumers shouldn't manage connection lifetime:
```csharp
services.AddScoped(_ => new AppDb(new SqliteConnection(cs), ownsConnection: true));
```

**InterceptorsNamespaces:** C# 12 requires every namespace that emits interceptors to be opted into the MSBuild `InterceptorsNamespaces` property. Quarry's NuGet package auto-registers `Quarry.Generated` (used for generic helpers) via `build/Quarry.targets`. Consumers must also add the namespace of each `QuarryContext` subclass:
```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```
If missing, analyzer QRY044 surfaces the exact line to paste before the build fails with `CS9137`.

### Querying

```csharp
await using var db = new AppDb(connection);

// Select (tuple, DTO, single column, entity)
// NOTE: Entity accessors are methods — db.Users() not db.Users
// NOTE: OrderBy is on IQueryBuilder<T>, not IEntityAccessor<T> — must come after Where() or Select()
var users = await db.Users()
    .Where(u => u.IsActive && u.UserId > minId)
    .Select(u => new UserDto { Name = u.UserName })
    .OrderBy(u => u.UserName)
    .Limit(10).Offset(20)
    .ExecuteFetchAllAsync();

// Aggregates — GroupBy available on IEntityAccessor<T> and IQueryBuilder<T>
db.Orders().GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)));

// Joins (2–6 table, explicit) — supports whole-entity projection from any alias
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => (u.UserName, o.Total))
    .Where((u, o) => o.Total > 100);
// Navigation: db.Users().Join(u => u.Orders)
// Joined entity projection: .Select((u, o) => o) — projects full entity from alias
// Also: LeftJoin, RightJoin, CrossJoin<T>(), FullOuterJoin<T>(condition)
// QRA502 warns: FULL OUTER JOIN on SQLite/MySQL
// Join-aware nullable propagation: columns on the nullable side of LEFT/RIGHT/FULL OUTER are IsDBNull-guarded in generated readers.

// Subqueries on Many<T> — Any/All/Count + aggregates
db.Users().Where(u => u.Orders.Any(o => o.Total > 100));          // EXISTS
db.Users().Where(u => u.Orders.All(o => o.Status == "paid"));      // NOT EXISTS + negated
db.Users().Where(u => u.Orders.Count() > 5);                       // scalar COUNT
db.Users().Where(u => u.Orders.Sum(o => o.Total) > 100);                        // correlated SUM subquery
db.Users().Where(u => u.Orders.Max(o => o.Total) >= 300);                       // correlated MAX
db.Users().Where(u => u.Orders.Average(o => o.Total) > 100);                    // alias: Avg
// Also supported in Select projections (tuples, DTOs, joined-context). QRY074 (error) surfaces unresolvable nav aggregates.
db.Users().Select(u => (u.UserName, Orders: u.Orders.Count(), Total: u.Orders.Sum(o => o.Total)));

// One<T> navigation (requires `!.` on nullable nav property)
db.Orders().Where(o => o.User!.IsActive);

// Set operations (IQueryBuilder<T> / IQueryBuilder<TEntity,TResult>)
// Post-set WHERE/GROUPBY/HAVING auto-wrap as subquery. Cross-entity supported.
db.Users().Select(u => u.UserName).Union(db.Products().Select(p => p.Name));
// Also: UnionAll, Intersect, IntersectAll, Except, ExceptAll
// Diagnostics: QRY070 (IntersectAll dialect), QRY071 (ExceptAll dialect), QRY072 (projection mismatch).

// Window functions in projections
db.Sales().Select(s => (
    s.Region,
    s.Amount,
    Rank: Sql.Rank(over => over.PartitionBy(s.Region).OrderByDescending(s.Amount)),
    RunningTotal: Sql.Sum(s.Amount, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
    Previous: Sql.Lag(s.Amount, 1, 0m, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate))
));
// Ranking: RowNumber, Rank, DenseRank, Ntile
// Offset/value: Lag, Lead, FirstValue, LastValue
// Aggregate-OVER: Sum, Count, Avg, Min, Max
// Fluent IOverClause: PartitionBy, OrderBy, OrderByDescending. Non-column args (offsets, default values, Ntile buckets) parameterized at compile time. Frame specs (ROWS/RANGE) not yet supported.

// Common Table Expressions (requires QuarryContext<TSelf> for typed post-With accessors)
db.With<User, ActiveUser>(users => users
        .Where(u => u.IsActive)
        .Select(u => new ActiveUser(u.UserId, u.UserName)))
    .FromCte<ActiveUser>()
    .Where(a => a.UserName.StartsWith("a"))
    .ExecuteFetchAllAsync();
// Multi-CTE: db.With<A>(…).With<B>(…).FromCte<A>().Join<B>(…)
// Direct-argument With<TDto>(IQueryBuilder<TDto>) overloads REMOVED — use lambda form only.
// Diagnostics: QRY080 (CTE inner not analyzable), QRY081 (FromCte without With), QRY082 (duplicate CTE name).

// Where operators: ==, !=, <, >, <=, >=, &&, ||, !, null checks
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// Collection: IEnumerable<T>/IReadOnlyList<T>/T[] .Contains(col) → IN (empty collection emits IN (SELECT 1 WHERE 1=0))
// Raw: Sql.Raw<bool>("\"Age\" > @p0", 18)
```

### Modifications

```csharp
// NOTE: All modifications go through entity accessors — db.Users().Insert(...), NOT db.Insert(...)

// Insert — initializer-aware (only set properties generate columns)
await db.Users().Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync();
var id = await db.Users().Insert(user).ExecuteScalarAsync<int>();

// Batch insert
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();

// Update — requires Where() or All()
// Set() takes Action<T> with assignment syntax, NOT a two-argument selector
await db.Users().Update().Set(u => u.UserName = "New").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Users().Update().Set(u => { u.UserName = "New"; u.IsActive = true; }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Users().Update().Set(new User { UserName = "New" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();

// Delete — requires Where() or All()
await db.Users().Delete().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

### PreparedQuery (Multi-Terminal)

`.Prepare()` freezes a chain into `PreparedQuery<TResult>`, allowing multiple terminals on the same compiled chain:

```csharp
var q = db.Users().Where(u => u.IsActive).Select(u => u).Prepare();
var diag = q.ToDiagnostics();           // inspect SQL
var all  = await q.ExecuteFetchAllAsync(); // execute
```

Single-terminal: zero overhead (elided via `Unsafe.As`). Multi-terminal: carrier covers all observed terminals.
Scope constraint: PreparedQuery variable must not escape method scope (no return, no argument passing, no lambda capture) — QRY035 error.
No terminals on PreparedQuery → QRY036 error.

### Execution Methods

`ExecuteFetchAllAsync()` → `Task<List<T>>`, `ExecuteFetchFirstAsync()` → `Task<T>`, `ExecuteFetchFirstOrDefaultAsync()` → `Task<T?>`, `ExecuteFetchSingleAsync()` → `Task<T>`, `ExecuteFetchSingleOrDefaultAsync()` → `Task<T?>`, `ExecuteScalarAsync<T>()` → `Task<T>`, `ExecuteNonQueryAsync()` → `Task<int>`, `ToAsyncEnumerable()` → `IAsyncEnumerable<T>`, `ToDiagnostics()` → `QueryDiagnostics`.

These terminals are also available directly on `IQueryBuilder<T>` (no need to call `.Select(x => x)` first before executing an entity fetch).

**Value-type FirstOrDefault caveat:** The interface uses unconstrained `TResult?`, which for value types (tuples, primitives, enums) does NOT produce `Nullable<T>` — it returns `default(T)` when no rows match (same as LINQ's `FirstOrDefault()`). This means callers cannot distinguish "no rows" from "a row whose value is `default`" (e.g., `0` for `long`, `default` for a tuple). Workarounds: use `ExecuteFetchFirstAsync` (throws on empty result), or project to a reference type (entity or DTO) where `null` signals "no rows".

### Raw SQL

```csharp
// RawSqlAsync<T> is IAsyncEnumerable<T> — not Task<List<T>>. Use .ToListAsync() or await foreach.
IAsyncEnumerable<User> rows = db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await foreach (var u in rows) { … }
List<User> buffered = await db.RawSqlAsync<User>("SELECT * FROM users", ).ToListAsync();

await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

**Reader strategy:** When the SQL argument is a string literal the shared SQL parser can resolve, the generator emits a static lambda with hardcoded ordinals (one-time `GetOrdinal` lookup eliminated). Otherwise falls back to a `file struct IRowReader<T>` — `GetName` called once per result set, no per-row lambda or closure allocation. Column matching is case-insensitive (`ToLowerInvariant`).

**Row entity shape:** `RawSqlAsync<T>` and `RawSqlScalarAsync<T>` materialize rows by calling `new T()` and assigning each column to a public settable property. `T` must therefore be a concrete (non-abstract, non-interface) class or struct with:
- a public parameterless constructor, and
- public `get; set;` properties (not `init`-only).

Positional records, init-only properties, abstract classes, and interfaces are rejected at compile time with QRY043. For immutable result shapes, project on a chain query (`Select(x => new Dto { ... })`) — the immutability comes from the projection, not from the row type. Nested row types (declared inside an enclosing class) are supported; the generator emits their fully qualified names in the generated interceptor.

**Diagnostics:** QRY031 (error) — unresolvable generic `T`. QRY041 (warn) — unresolvable column in literal SQL. QRY042 (info + code fix) — RawSqlAsync convertible to chain API. QRY043 (error) — row entity type not materializable (no parameterless ctor, init-only property, abstract class, or interface).

**Error propagation:** On the buffered multi-row path, `ReadAsync` errors propagate as raw `DbException` (not wrapped in `QuarryQueryException`). Connection-open failures still wrap.

### Diagnostics (QueryDiagnostics)

`ToDiagnostics()` returns compile-time analysis: `Sql`, `Parameters` (active only), `AllParameters`, `Kind`, `Dialect`, `TableName`, `Clauses` (per-clause SQL + params + source location + conditional info), `SqlVariants` (`Dictionary<int, SqlVariantDiagnostic>` — mask→SQL map), `ProjectionColumns`, `ProjectionKind`, `CarrierClassName`, `Joins`, `IsDistinct`, `Limit`, `Offset`, `IdentityColumnName`, `ActiveMask` (int), `ConditionalBitCount`, `TierReason`, `DisqualifyReason`, `UnmatchedMethodNames`.

### Trace

Add `QUARRY_TRACE` to consumer `.csproj` + `.Trace()` to chain. Trace comments emitted as `// [Trace]` lines in generated interceptors. Categories: Discovery, Binding, Translation (per-site), ChainAnalysis, Assembly, Carrier (per-chain). Without `QUARRY_TRACE` symbol: QRY034 warning.

### Scaffold

`quarry scaffold --connection "..." --dialect SQLite --output ./Schemas` — reverse-engineers DB to schema classes. Per-dialect introspectors, junction table detection, implicit FK detection, singularization.

### SQL Manifest (opt-in)

Enable per-dialect markdown documentation of every generated SQL statement:

```xml
<PropertyGroup>
  <QuarrySqlManifestPath>$(MSBuildProjectDirectory)/sql-manifest</QuarrySqlManifestPath>
</PropertyGroup>
```

Generator emits `quarry-manifest.{sqlite|postgresql|mysql|sqlserver}.md`, one per dialect. Each manifest lists every chain's SQL, parameter table (including `LIMIT`/`OFFSET` rows), bitmask-labeled conditional variants, and summary stats. `WriteIfChanged` guard suppresses spurious git diffs. Zero overhead when unset. Write failures surface as QRY040 warning.

### Cross-ORM Conversion (Quarry.Migration)

`quarry convert --from {dapper|efcore|adonet|sqlkata} --project <path>` — parses existing SQL strings in source code, resolves against Quarry entity schemas, emits equivalent chain API code. Driven by `Quarry.Migration` analyzers which only activate when the target framework type is present in the compilation.

Converter structure (one set per source tool):

- `*Detector` — finds call sites in source (e.g., `DapperDetector` → `QueryAsync`/`ExecuteAsync`/… patterns)
- `*Converter` — orchestrates: detects, parses SQL via `Quarry.Shared/Sql/Parser/`, resolves columns via `SchemaResolver`, emits chain code via `ChainEmitter`
- `*MigrationAnalyzer` + `*MigrationCodeFix` — Roslyn analyzer + IDE lightbulb fix
- Supports SELECT/WHERE/joins (INNER/LEFT/RIGHT/CROSS/FULL OUTER)/GROUP BY/HAVING/ORDER BY/LIMIT/aggregates/IN/BETWEEN/IS NULL/LIKE, plus DELETE/UPDATE/INSERT (INSERT emits TODO since Quarry needs entity objects). `Sql.Raw` fallback for unsupported constructs.

Uniform result interfaces: `IConversionDiagnostic` (severity, code, span, message), `IConversionEntry` (original site + converted code + diagnostics). Four diagnostic families: QRM001–003 (Dapper), QRM011–013 (EF Core), QRM021–023 (ADO.NET), QRM031–033 (SqlKata). Each family: detection (Info), with-warnings (Warning), not-convertible (Info).

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
- `JoinedCarrierBase<T1,T2>` through `JoinedCarrierBase6<T1..T6>` (± TResult) — explicit joins (max 6 tables). Generated via T4 templates from arity 2.
- `DeleteCarrierBase<T>`, `UpdateCarrierBase<T>`, `InsertCarrierBase<T>`, `BatchInsertCarrierBase<T>` — modifications

All base class methods throw `InvalidOperationException` — generator replaces them with actual implementations via interceptors.

**Optimization:** Only `PrebuiltDispatch` exists. ≤8 conditional bits → up to 256 SQL variants. Mask type is `int` (max value 255 with 8-bit cap). Non-analyzable chains → compile error QRY032.

**Captured Variable Extraction:** All captured variables are extracted at compile time via `[UnsafeAccessor]` extern methods targeting compiler-generated display classes. No expression trees or reflection at runtime. Per-variable extraction methods emitted on the carrier class (e.g., `__ExtractVar_x_0(displayClass)`). `CaptureKind` enum: `ClosureCapture` (display class field), `FieldCapture` (class-level static/instance field).

### Builder Interfaces

**Query:** `IEntityAccessor<T>` (entry point, includes `GroupBy<TKey>()`, terminals via `.Select(x => x)` omission) → `IQueryBuilder<T>` (no projection; exposes execution terminals directly) → `IQueryBuilder<T,TResult>` (with projection). `IJoinedQueryBuilder<T1,T2>` through `IJoinedQueryBuilder6<T1..T6>` (± TResult). All lambda parameters are bare `Func<>` delegates (not `Expression<Func<>>`); expression analysis happens at compile time via source generator.

**Chain-continuation methods** (`OrderBy`, `ThenBy`, `Limit`, `Offset`, `Distinct`, `WithTimeout`) are on `IQueryBuilder<T>`, NOT on `IEntityAccessor<T>`. They only become available after the first clause (`.Where()`, `.Select()`, `.GroupBy()`) transitions the chain from the entity accessor. Writing `db.Users().OrderBy(...)` directly will not compile — use `db.Users().Where(...).OrderBy(...)` or `db.Users().Select(...).OrderBy(...)`.

**Modification entry points** are on `IEntityAccessor<T>`: `.Insert(entity)`, `.Update()`, `.Delete()`, `.InsertBatch(selector)`. Access via the entity accessor method — `db.Users().Insert(...)`, NOT `db.Insert(...)`.

**Update `.Set()` overloads:** `Set(Action<T> assignment)` uses assignment syntax (`u => u.Name = value` or `u => { u.Name = value; u.Active = true; }`), and `Set(T entity)` uses initializer-aware entity form. There is NO two-argument `Set(selector, value)` overload.

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

Entry: `QuarryGenerator : IIncrementalGenerator` — three pipelines, split between design-time (IDE/IntelliSense) and build-time (`dotnet build`).

**Pipeline 1 — Schema/Context (design-time, `RegisterSourceOutput`):** `ClassDeclarationSyntax` → `ContextParser` → `SchemaParser` per entity → emits entity classes + context partials. Per-context with no `Collect()` — zero cross-context aggregation at design-time. Reports QRY003, QRY017, QRY026, QRY027, QRY028 immediately.

**Pipeline 1 cross-context check (build-time, `RegisterImplementationSourceOutput`):** Duplicate TypeMapping detection (QRY016) across collected contexts.

**Pipeline 2 — Interceptors (build-time, `RegisterImplementationSourceOutput`, 7-stage IR):**

```
Stage 1:   Discovery        → RawCallSite        (syntax + semantic)
Stage 2:   Binding          → BoundCallSite       (+ entity/ctx from EntityRegistry)
Stage 2.5: DisplayClassEnrichment → RawCallSite   (+ DisplayClassName, CapturedVariableTypes, CaptureKind)
Stage 3:   Translation      → TranslatedCallSite  (+ SQL expr/params)
Stage 4:   Analysis         → AnalyzedChain       (+ query plan, tier, masks)
Stage 5:   Assembly         → AssembledPlan       (+ SQL strings per mask)
Stage 6:   Emission         → C# interceptor source
```

**Stage 2.5** batch-enriches all raw call sites with display class metadata via `DisplayClassEnricher.EnrichAll()`. Groups sites by containing method to cache closure analysis (one `AnalyzeDataFlow` per method, not per site).

User code resolves against builder interfaces (`IQueryBuilder<T>`, etc.) at design-time; interceptors only replace implementations at build-time via `[InterceptsLocation]`.

**Pipeline 3 — Migrations (build-time, `RegisterImplementationSourceOutput`):** Discovers `[Migration]`/`[MigrationSnapshot]` → `MigrationInfo`/`SnapshotInfo` → QRY050–055 diagnostics.

### Stage 1 — Discovery (`Parsing/`)

| Class | Role |
|---|---|
| `UsageSiteDiscovery` | Syntactic predicate (`IsQuarryMethodCandidate`) + semantic analysis → `RawCallSite` with method, kind, entity, SqlExpr, chain ID, conditional info, projection, Prepare detection. Object initializer chain differentiation: uses per-member `SpanStart` as scope key. SetAction lambda processing extracts all captured identifiers with type metadata (`SetActionAllCapturedIdentifiers`) |
| `DisplayClassEnricher` | Batch enriches `RawCallSite` with `DisplayClassName`, `CapturedVariableTypes`, `CaptureKind`. Groups by containing method for single `AnalyzeDataFlow` per method |
| `DisplayClassNameResolver` | Predicts compiler display class names: `ContainingType+<>c__DisplayClass{methodOrdinal}_{closureOrdinal}`. Methods: `ComputeMethodOrdinal()`, `AnalyzeMethodClosures()`, `LookupClosureOrdinal()`, `CollectCapturedVariableTypes()` |
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

`ChainAnalyzer.Analyze(translatedSites, registry)` → `AnalyzedChain[]`. Groups by ChainId, identifies terminal, detects forks (→ QRY033), allocates conditional bitmasks, classifies tier. Pre-join clause sites retranslated with join context for table alias qualification.

**Multi-terminal handling:** Detects `.Prepare()` site + prepared terminals. Single-terminal → standard chain (Prepare elided). Multi-terminal → carrier covers all observed terminals.

**Parameter enrichment:** `EnrichParametersFromColumns` matches Where/Having params to entity columns for IsEnum/IsSensitive metadata. `EnrichSetParametersFromColumns` does the same for Set clause assignments (different expression structure). Enum parameters without explicit underlying type default to `int`. Capture metadata (`CapturedFieldName`, `CapturedFieldType`, `IsStaticCapture`) propagated to `QueryParameter` for UnsafeAccessor generation.

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
| `CarrierAnalyzer` | Carrier eligibility, `CarrierPlan` (incl. `BuildExtractionPlans` for per-clause UnsafeAccessor generation) |
| `CarrierEmitter` | Carrier class declarations (including `_sql` field, `[UnsafeAccessor]` extern methods) + method bodies, mask-gated parameter binding and logging |
| `ClauseBodyEmitter` | Thin delegation layer: detects resolvable captured params, delegates to `CarrierEmitter.EmitCarrierClauseBody()`. Uses `"func"` param name if captured params exist, else `"_"` |
| `JoinBodyEmitter` | Join/LeftJoin/RightJoin + joined clauses |
| `TerminalBodyEmitter` | FetchAll, FetchFirst, ExecuteNonQuery, ExecuteScalar, ToDiagnostics, Prepare |
| `TransitionBodyEmitter` | Delete/Update/Insert transitions, ChainRoot, Limit/Offset/Distinct/WithTimeout |
| `RawSqlBodyEmitter` | RawSqlAsync, RawSqlScalarAsync |
| `InterceptorRouter` | Routes `InterceptorKind` → emitter category |
| `TerminalEmitHelpers` | `BuildParamConditionalMap`, `GetParameterValueExpression`, shared terminal emission utilities |

**Carrier SQL ownership:** `CarrierEmitter.EmitCarrierSqlField` emits SQL on the carrier class itself. Single-variant → `static readonly string _sql`; multi-variant → `static readonly string[] _sql` indexed by mask. Terminal emitters reference carrier's `_sql` field instead of inline SQL. Diagnostics `SqlVariantDiagnostic` entries reference the carrier's field.

**UnsafeAccessor extraction:** `CarrierEmitter` emits `[UnsafeAccessor(UnsafeAccessorKind.Field)]` or `[UnsafeAccessor(UnsafeAccessorKind.StaticField)]` extern methods per captured variable per clause on the carrier class. Method naming: `__ExtractVar_{varName}_{clauseIndex}`. Clause interceptors call extracted variable methods to bind parameters from `func.Target` (closure) or null target (static).

**Mask-aware parameter binding:** `EmitCarrierCommandBinding` groups conditional parameters by bit index, emitting `if ((Mask & (1 << bitIndex)) != 0)` blocks. Collection parameters (`IsCollection`) expanded into N individual DbParameters in a loop. Unconditional parameters and pagination parameters are always bound. No intermediate `__pVal*` locals — values read directly from carrier fields. Same mask-gating applies to inline parameter logging in `EmitInlineParameterLogging`.

**Receiver type construction:** `InterceptorCodeGenerator.BuildReceiverType` centralizes `this` parameter type building. `IEntityAccessor`/`EntityAccessor` take only entity type arg; `IQueryBuilder` takes entity + result type args. `IsEntityAccessorType` helper identifies accessor types.

### SqlExpr IR (`IR/SqlExpr*`)

Dialect-agnostic expression tree. Pipeline: `SqlExprParser.Parse` (C# syntax → unresolved) → `SqlExprAnnotator.Annotate` (type enrichment, error type guard, member-access expression type resolution, **constant inlining**) → `SqlExprBinder.Bind` (column resolution, boolean context propagation for AND/OR children → bare bool columns emit `col = 1`/`col = TRUE`) → `SqlExprClauseTranslator.ExtractParameters` (captured values → param slots) → `SqlExprRenderer.Render` (dialect-specific SQL).

**Constant inlining** (in `SqlExprAnnotator`): `InlineConstantCollections` — inlines constant/readonly array initializers in IN clauses to `LiteralExpr` values. `InlineConstantLikePatterns` — inlines static readonly/const string values in LIKE patterns. `TryResolveConstantArray` — resolves variable to array initializer with reassignment guards (`IsLocalReassigned`, `IsLocalReassignedInBlock`). Compile-time constants from member access (e.g., enum values) converted to `LiteralExpr` in `ApplyCapturedTypes`. Static field flags propagated via `CapturedValueExpr.IsStaticField`.

Nodes: `ColumnRefExpr`, `ResolvedColumnExpr`, `ParamSlotExpr`, `LiteralExpr`, `BinaryOpExpr`, `UnaryOpExpr`, `FunctionCallExpr`, `InExpr`, `IsNullCheckExpr`, `LikeExpr` (+ `NeedsEscape` flag), `CapturedValueExpr` (+ `IsStaticField` flag), `SqlRawExpr`, `RawCallExpr`, `SubqueryExpr`.

### Key Model Types

- `ContextInfo` — className, namespace, dialect, schema, entities
- `EntityInfo` — entityName, tableName, namingStyle, columns, navigations, indexes, compositeKeyColumns, customEntityReaderClass
- `ColumnInfo` — propertyName, columnName, clrType, isNullable, kind, modifiers, isEnum, customTypeMappingClass
- `RawCallSite` — 60+ fields: method, kind, builderKind, entity, SqlExpr, chainId, conditionalInfo, projectionInfo, IsPreparedTerminal, PreparedQueryEscapeReason. Mutable enrichment fields (excluded from Equals/GetHashCode): `DisplayClassName`, `CapturedVariableTypes` (var→CLR type), `CaptureKind`, `EnrichmentLambda`. `SetActionAllCapturedIdentifiers` (var→(Type, IsStaticField, ContainingClass)). `WithResultTypeName(string)` immutable copy for post-hoc type patching
- `BoundCallSite` — raw + context, dialect, entity (EntityRef), joinedEntity, insertInfo, updateInfo. `WithRaw(RawCallSite)` immutable copy
- `TranslatedCallSite` — bound + translatedClause (resolvedExpr, parameters, sqlFragment). Convenience accessors: `CapturedVariableTypes`, `SetActionAllCapturedIdentifiers`. `WithResolvedResultType(string)` chains Raw→Bound→Translated copy
- `QueryPlan` (IR) — kind, tables, joins, where/order/group/having/set terms, projection, pagination, parameters, tier, conditional masks (`IReadOnlyList<int>`). `QueryParameter`: `needsUnsafeAccessor`, `CapturedFieldName`, `CapturedFieldType`, `IsStaticCapture`
- `AssembledPlan` — plan + sqlVariants (`Dictionary<int, AssembledSqlVariant>`), PreparedTerminals, PrepareSite
- `CarrierPlan` — isEligible, className, fields, parameters, maskType, implementedInterfaces, `ExtractionPlans: IReadOnlyList<ClauseExtractionPlan>`. `GetExtractionPlan(clauseUniqueId)` for lookup during emission
- `ClauseExtractionPlan` — `ClauseUniqueId`, `DelegateParamName` ("func"/"action"), `Extractors: IReadOnlyList<CapturedVariableExtractor>`. Groups per-variable UnsafeAccessor metadata for one clause
- `CapturedVariableExtractor` — `MethodName` (e.g., `__ExtractVar_x_0`), `VariableName`, `VariableType`, `DisplayClassName`, `CaptureKind`, `IsStaticField`
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
| `CaptureKind` | None, ClosureCapture (display class field), FieldCapture (class-level static/instance field) |

### Equality Infrastructure

All model types implement `IEquatable<T>` for Roslyn incremental caching. `EquatableArray<T>`, `EquatableDictionary<TKey,TValue>`, `EqualityHelpers`.

### Navigation Subquery Pipeline

`Many<T>` exposes compile-time markers: `Any()`, `Any(pred)`, `All(pred)`, `Count()`, `Count(pred)`, `Sum(selector)`, `Min(selector)`, `Max(selector)`, `Avg(selector)`/`Average(selector)`. `SqlExprParser` detects `<param>.<nav>.<Method>()` → `SubqueryExpr`. FK-to-PK correlation via `NavigationInfo.ForeignKeyPropertyName`. Scope stack in `SqlExprBinder` enables nesting. SQL: `EXISTS (SELECT 1 ...)`, `NOT EXISTS (... AND NOT ...)`, `(SELECT COUNT(*) ...)`, `(SELECT SUM/MIN/MAX/AVG(column) ...)`.

### Insert Pipeline

Single: `UsageSiteDiscovery` extracts `InitializedPropertyNames` from object initializer → `InsertInfo.FromEntityInfo` filters columns (skip computed/identity). Batch: `InsertBatch(lambda)` → column selector analyzed at compile time → `Values(collection)` at runtime → `BatchInsertSqlBuilder.Build()` expands prefix with entity count. MaxParameterCount guard (2100).

### Variable-Walking Chain Unification

`VariableTracer.TraceToChainRoot(receiver, semanticModel, ct, maxHops=2)` traces through builder-type variable declarations. Only traces builder types to prevent context variable collapse. `TraceResult.FirstVariableName` → ChainId consistency.

### LIKE Parameterization

`SqlLikeHelpers`: escapes `\%_` in literals, dialect-aware concatenation (MySQL `CONCAT()`, SqlServer `+`, PostgreSQL/SQLite `||`), `ESCAPE '\'` only when needed. Constant string LIKE patterns inlined as literals at compile time (no parameterization). `LikeExpr.NeedsEscape` controls `ESCAPE '\'` clause emission.

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

**Chain (QRY030–036):** QRY030 (info): prebuilt dispatch applied. QRY031 (error): unresolvable RawSql `T` generic parameter. QRY032 (error): chain not analyzable. QRY033 (error): forked chain (multiple terminals). QRY034 (warn): Trace without QUARRY_TRACE. QRY035 (error): PreparedQuery escapes scope. QRY036 (error): Prepare with no terminals.

**Manifest (QRY040):** QRY040 (warn): SQL manifest write failure.

**RawSqlAsync literal resolution (QRY041–042):** QRY041 (warn): RawSqlAsync column expression without alias / unresolvable. QRY042 (info + code fix): RawSqlAsync convertible to chain API.

**Migration (QRY050–055):** QRY050: schema drift. QRY051: unknown table/column ref. QRY052: version gap/duplicate. QRY053: pending migrations. QRY054: destructive without backup. QRY055: nullable-to-non-null.

**Navigation (QRY060–065):** QRY060: no FK for One<T>. QRY061: ambiguous One<T> FK. QRY062: HasOne references invalid column. QRY063: navigation target entity not found. QRY064: HasManyThrough invalid junction navigation. QRY065: HasManyThrough invalid target navigation.

**Set operations (QRY070–072):** QRY070 (warn): INTERSECT ALL not supported on this dialect. QRY071 (warn): EXCEPT ALL not supported on this dialect. QRY072 (error): set operation projection mismatch (column count/type).

**Projection subqueries (QRY074):** QRY074 (error): navigation aggregate (`Sum`/`Min`/`Max`/`Avg`/`Average`/`Count`) in a `Select` projection could not be resolved — the nav property does not exist on the outer entity, or its target entity is not registered on the context.

**CTEs (QRY080–082):** QRY080 (error): CTE inner query not analyzable. QRY081 (error): `FromCte` without matching `With`. QRY082 (error): duplicate CTE name in chain.

**Internal:** QRY900: generator error (stack trace surfaced).

**Retired:** QRY073 (removed in v0.3.0 — cross-entity set-ops now supported). The ID is intentionally skipped; `#pragma warning disable QRY073` directives remain inert.

**Analyzer (QRA series):** QRA101–106 (simplification), QRA201–205 (wasteful), QRA301–305 (performance), QRA401–402 (patterns), QRA501–502 (dialect). QRA502 (warn): FULL OUTER JOIN on SQLite/MySQL. QRA305 (info): mutable `static readonly` array in IN clause — generator inlines initializer at compile time but elements can be mutated at runtime; suggests `ImmutableArray<T>`. Code fixes: QRA101, QRA102, QRA201.

**Migration converter (QRM series — Quarry.Migration package):** QRM001/011/021/031 (info): Dapper/EFCore/ADO.NET/SqlKata call detected, convertible. QRM002/012/022/032 (warn): converted with warnings. QRM003/013/023/033 (info): not convertible (with reason). All include IDE code fix provider to replace source site with generated chain code.

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
| Execution | `Internal/QueryExecutor.cs` (static carrier execution methods), `BatchInsertSqlBuilder.cs`, `IQueryExecutionContext.cs`, `OpId.cs` |
| Context | `Context/QuarryContext.cs` (implements `IQueryExecutionContext`), `QuarryContextAttribute.cs` |
| Dialect | `Quarry.Shared/Sql/SqlDialect.cs`, `SqlFormatting.cs` (+ per-dialect partials), `SqlClauseJoining.cs` |
| Aggregates | `Query/Sql.cs` — compile-time markers (Count, Sum, Avg, Min, Max, Raw, Exists) |
| Logging | `Logging/QueryLog.cs`, `ModifyLog.cs`, `RawSqlLog.cs`, `ConnectionLog.cs`, `ParameterLog.cs`, `ExecutionLog.cs` — Logsmith Abstraction mode |
| Migration runtime | `Migration/MigrationRunner.cs`, `MigrationBuilder.cs`, `DdlRenderer.cs`, `SqlTypeMapper.cs` |
| Migration shared | `Quarry.Shared/Migration/Diff/SchemaDiffer.cs`, `CodeGen/MigrationCodeGenerator.cs`, `SnapshotCodeGenerator.cs` |
| Scaffold | `Quarry.Shared/Scaffold/IDatabaseIntrospector.cs`, per-dialect introspectors, `ScaffoldCodeGenerator.cs` |
| Generator entry | `QuarryGenerator.cs` — 3 pipelines, EntityRegistry, trace collection |
| Parsing | `Parsing/UsageSiteDiscovery.cs`, `AnalyzabilityChecker.cs`, `ChainAnalyzer.cs`, `VariableTracer.cs`, `ContextParser.cs`, `SchemaParser.cs`, `DisplayClassEnricher.cs`, `DisplayClassNameResolver.cs` |
| IR | `IR/RawCallSite.cs`, `BoundCallSite.cs`, `TranslatedCallSite.cs`, `CallSiteBinder.cs`, `CallSiteTranslator.cs`, `SqlAssembler.cs`, `AssembledPlan.cs`, `EntityRegistry.cs`, `PipelineOrchestrator.cs`, `QueryPlan.cs` |
| SqlExpr | `IR/SqlExprParser.cs`, `SqlExprAnnotator.cs`, `SqlExprBinder.cs`, `SqlExprClauseTranslator.cs`, `SqlExprRenderer.cs`, `SqlExprNodes.cs` |
| CodeGen | `CodeGen/FileEmitter.cs`, `CarrierAnalyzer.cs`, `CarrierEmitter.cs`, `CarrierPlan.cs`, `ClauseBodyEmitter.cs`, `JoinBodyEmitter.cs`, `TerminalBodyEmitter.cs`, `TerminalEmitHelpers.cs`, `TransitionBodyEmitter.cs`, `RawSqlBodyEmitter.cs`, `InterceptorRouter.cs` |
| Generation | `Generation/ContextCodeGenerator.cs`, `EntityCodeGenerator.cs`, `InterceptorCodeGenerator.cs`, `MigrateAsyncCodeGenerator.cs` |
| Extraction | `Models/CapturedVariableExtractor.cs`, `ClauseExtractionPlan.cs` |
| Projection | `Projection/ProjectionAnalyzer.cs` (whole joined-entity projection via `AnalyzeJoinedEntityProjection`), `ReaderCodeGenerator.cs` (`GetValue()` fallback with explicit cast for `byte[]`/`DateTimeOffset`) |
| Translation | `Translation/SqlLikeHelpers.cs`, `ParameterInfo.cs` (enriched: `CapturedFieldName`, `CapturedFieldType`, `IsStaticCapture`, `CollectionElementType`, `CustomTypeMappingClass`, `IsEnum`, `EnumUnderlyingType`, `CollectionReceiverSymbol`, `CanGenerateDirectPath`) |
| Trace | `IR/TraceCapture.cs`, `Query/TraceExtensions.cs` |
| Models | `Models/` — ContextInfo, EntityInfo, ColumnInfo, InsertInfo, ProjectionInfo, InterceptorKind, OptimizationTier, QueryKind, NavigationInfo, RawSqlTypeInfo, MigrationInfo, DiagnosticInfo, FileInterceptorGroup, CapturedVariableExtractor, ClauseExtractionPlan, etc. |

### Test Infrastructure

**QueryTestHarness** (`Quarry.Tests/QueryTestHarness.cs`): Disposable harness providing 4 dialect contexts — `Lite` (SQLite, real in-memory DB), `Pg`/`My`/`Ss` (mock connections, SQL verification only). `CreateAsync()` seeds default schema (users, orders, order_items, accounts) + test data. `AssertDialects()` verifies SQL across all 4 dialects.

**Test pattern (per-dialect):**
```csharp
await using var t = await QueryTestHarness.CreateAsync();
var (Lite, Pg, My, Ss) = t;
var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Prepare();
var my = My.Users().Select(u => (u.UserId, u.UserName)).Prepare();
var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Prepare();
QueryTestHarness.AssertDialects(
    lt.ToDiagnostics(), pg.ToDiagnostics(), my.ToDiagnostics(), ss.ToDiagnostics(),
    sqlite: "...", pg: "...", mysql: "...", ss: "...");
var results = await lt.ExecuteFetchAllAsync(); // execute on SQLite only
```

**Test files:** `SqlOutput/CrossDialect*.cs` (18+ files, 4-dialect SQL verification), `SqlOutput/PrepareTests.cs` (Prepare single/multi-terminal), `SqlOutput/JoinedEntityProjectionTests.cs` (joined entity projection), `Generation/CarrierGenerationTests.cs` (carrier class emission), `Generation/ConditionalCarrierTests.cs` (mask-gated parameter binding), `Integration/PrepareIntegrationTests.cs` (Prepare execution), `Integration/JoinedCarrierIntegrationTests.cs`, `IR/SqlExprAnnotatorInliningTests.cs` (constant inlining), `Parsing/DisplayClassEnricherTests.cs`, `DialectTests.cs` (pagination formatting), `UsageSiteDiscoveryTests.cs`, `VariableTracerTests.cs`.

### Build & Test

```sh
dotnet build
dotnet test src/Quarry.Tests
dotnet test src/Quarry.Analyzers.Tests
```

### Release Notes Workflow

Per-version release notes live in `docs/articles/releases/release-notes-vX.Y.Z.md`, written when a new release is tagged via the `llm-release.md` skill. Between tags, contributors accumulate notes in **`docs/articles/releases/release-notes-next.md`** (the staging file).

**For a PR that needs a release-notes entry** (any user-visible change — fix, feature, behavior change, breaking change, perf): append the entry to the appropriate section in `release-notes-next.md`. The file uses the same Highlights / Breaking Changes / New Features / Performance / Architecture / Bug Fixes / Documentation & Tooling / Migration Guide / Stats / Full Changelog skeleton as `release-notes-vX.Y.Z.md` (Appendix in `llm-release.md`). Empty sections are omitted from the seed; PRs add sections as needed. PR descriptions remain the source of truth — `release-notes-next.md` is a curated, edited summary, not a verbatim copy.

**For a PR that doesn't need an entry** (internal refactor with no behavior change, test-only, build/CI tweak): leave `release-notes-next.md` untouched.

**At release time** the `llm-release.md` skill reads `release-notes-next.md`, merges and refines its content into the new `release-notes-vX.Y.Z.md`, then **deletes** `release-notes-next.md` and stages the deletion into the `Release vX.Y.Z` commit. The next PR that needs an entry recreates the file from the same skeleton.
