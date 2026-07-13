# Quarry

Compile-time SQL builder for .NET 10. Roslyn source generators + C# 12 interceptors. All SQL pre-built. Zero reflection, AOT compatible. Logging via Logsmith Abstraction mode (zero-dependency).

**Architecture: Carrier-only.** All query chains must be statically analyzable. No runtime SQL builder fallback. Non-analyzable chains produce compile error QRY032.

> This document covers **using** Quarry. For generator/compiler internals (pipeline stages, IR, carrier emission, model types), see [`src/Quarry.Generator/llm.md`](src/Quarry.Generator/llm.md). Long-form usage guides live in [`docs/articles/`](docs/articles/).

## Packages

- `Quarry` (net10.0) — Runtime: carrier base classes, interfaces, schema DSL, executor, migrations. Logsmith 0.5.0 Abstraction mode (zero runtime dependency).
- `Quarry.Generator` (netstandard2.0) — Roslyn incremental generator: interceptor emission, entity/context codegen, migration codegen, opt-in SQL manifest emission.
- `Quarry.Analyzers` (netstandard2.0) — Compile-time SQL analysis rules (QRA series) + code fixes.
- `Quarry.Migration` (netstandard2.0) — Cross-ORM conversion toolkit (Dapper/EF Core/ADO.NET/SqlKata → Quarry). QRM analyzers + IDE code fixes. Backs `quarry convert`.
- `Quarry.Tool` (net10.0) — CLI: `quarry migrate`, `scaffold`, `create-scripts`, `convert --from {dapper|efcore|adonet|sqlkata}`.
- Samples: `Quarry.Sample.WebApp` (Razor Pages + SQLite), `Quarry.Sample.Aot` (PublishAot verification).

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

**Navigation declarations** (type parameters are always Schema classes, not generated entity types):
- `public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);` — 1:N
- `public Many<TagSchema> Tags => HasManyThrough<TagSchema, OrderTagSchema, OrderSchema>(o => o.OrderTags, ot => ot.Tag);` — M:N skip navigation; requires a `Many<JunctionSchema>` and a `One<TargetSchema>` on the junction (junction→target JOIN is implicit in terminals)
- `public One<UserSchema> User => HasOne<UserSchema>();` — reverse One<T> navigation, produces nullable `T?` property on generated entity; lambdas need `!.` (e.g. `o.User!.IsActive`)

Navigation diagnostics: QRY060 (no FK for One<T>), QRY061 (ambiguous FK), QRY062 (HasOne references invalid column), QRY063 (target entity not found), QRY064/065 (HasManyThrough invalid junction/target navigation).
NamingStyle: `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase`.
Index modifiers: `Unique()`, `Where(col)`, `Where("sql")`, `Include(cols...)`, `Using(IndexType)`, `.Asc()`/`.Desc()`. Index types: `BTree`, `Hash`, `Gin`, `Gist`, `SpGist`, `Brin` (PostgreSQL), `Clustered`, `Nonclustered` (SQL Server).

### Foreign Keys & EntityRef

A schema declares an FK with `Ref<TSchema, TKey>`; the generated entity exposes it as `EntityRef<TEntity, TKey>` — a readonly struct with two members:

- **`.Id`** — the raw FK value (the value stored in the column). Use it for reads and writes. Assigning a `TKey` converts implicitly: `new Order { UserId = 42 }`.
- **`.Value`** — navigation to the referenced entity. `null` unless the related entity was fetched via a join.

```csharp
int userId = order.UserId.Id;                       // raw FK value

// Join: compare .Id against the referenced PK column (never the EntityRef directly)
var results = await db.Orders()
    .Join<User>((o, u) => o.UserId.Id == u.UserId)
    .Select((o, u) => o)
    .ExecuteFetchAllAsync();

string name = results[0].UserId.Value!.UserName;     // .Value populated after the join
```

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

**Per-context resolution.** Quarry emits one entity class per `QuarryContext` (in the context's namespace), so `App.Pg.Product` and `App.My.Product` are distinct CLR types even when generated from the same schema. The `[EntityReader]` attribute resolves to a *simple-name* reference, looked up at `<contextNamespace>.<readerSimpleName>`. When schema and context share a namespace this is the same class. When a schema is referenced by multiple contexts in different namespaces, each context expects its own reader class at its own namespace (e.g. `App.Pg.MyReader : EntityReader<App.Pg.Product>`). A missing/mis-declared per-context reader surfaces as an ordinary C# compile error against the generated interceptor reference — no analyzer rule, no fallback.

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

**`Schema` attribute property:** qualifies all table references with a database schema (`Schema = "public"` → `"public"."users"` on PostgreSQL, `[dbo].[users]` on SQL Server, `` `db`.`users` `` on MySQL; ignored on SQLite). Lets the same schema classes target different DB schemas (multi-tenant).

**`ownsConnection`:** Constructor accepts optional `bool ownsConnection = false`. When `true`, context disposes the underlying `DbConnection` on `Dispose`/`DisposeAsync`. When `false` (default), context only closes connections it opened. Use `ownsConnection: true` for DI registrations:
```csharp
services.AddScoped(_ => new AppDb(new SqliteConnection(cs), ownsConnection: true));
```

**Constructor params:** full form is `new AppDb(connection, ownsConnection: false, defaultTimeout: TimeSpan?, defaultIsolation: IsolationLevel?)`. The connection must be a `DbConnection` (required for async; non-`DbConnection` throws `ArgumentException`). Default timeout 30s. If passed **open**, the context leaves it open on dispose; if **closed**, it opens on first query and closes on dispose. Don't share one context across concurrent operations — create one per unit of work.

**`MySqlBackslashEscapes`** (MySQL only, default `true`): controls how `LIKE` patterns are escaped. Set `false` only when the MySQL session/server runs `NO_BACKSLASH_ESCAPES` in `sql_mode`. No effect on other dialects. Mismatching the flag against the actual `sql_mode` causes a `1064` syntax error or doubled backslashes in matched data.

**InterceptorsNamespaces:** C# 12 requires every namespace that emits interceptors to be opted into the MSBuild `InterceptorsNamespaces` property. Quarry's NuGet package auto-registers `Quarry.Generated` via `build/Quarry.targets`. Consumers must also add the namespace of each `QuarryContext` subclass:
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
    .OrderBy(u => u.UserName)             // also: .OrderBy(u => u.CreatedAt, Direction.Descending)
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

// Conditional clauses — reassign inside if/else; compiled to bitmask-dispatched SQL variants
var q = db.Users().Select(u => u);
if (filterActive) q = q.Where(u => u.IsActive);          // plain if
if (byName)       q = q.OrderBy(u => u.UserName);        // independent conditionals compose
else if (byDate)  q = q.OrderBy(u => u.CreatedAt);       // else-if cascades supported (any arm count)
if (page)         q = q.Limit(25).Offset(50);            // Limit/Offset/Distinct honor the branch
q = urgent ? q.WithTimeout(TimeSpan.FromSeconds(5)) : q; // ternary form supported
await q.ExecuteFetchAllAsync();
// Participating (consume a mask bit when branched): Where, OrderBy/ThenBy, GroupBy, Having,
// Select, Set, joins, Limit, Offset, Distinct. WithTimeout is branch-safe WITHOUT a bit
// (falls back to DefaultTimeout). Multiple clauses per branch are fine. Limits: 8 bits
// (256 variants), nesting ≤ 2 cascade levels (a whole if/else-if/else chain = 1 level) → QRY032.

// Where operators: ==, !=, <, >, <=, >=, &&, ||, !, null checks
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// Collection: IEnumerable<T>/IReadOnlyList<T>/T[] .Contains(col) → IN (empty collection emits IN (SELECT 1 WHERE 1=0))
// Raw: Sql.Raw<bool>("\"Age\" > @p0", 18) — also valid in Select projections: .Select(u => Sql.Raw<string>("UPPER({0})", u.UserName))
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

// Runtime-conditional column set — pass a generated User.Patch (value form or by-ref lambda)
var patch = new User.Patch { UserName = "New" };
await db.Users().Update().Set(patch).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Users().Update()
    .Set((ref User.Patch p) => { if (newName is not null) p.UserName = newName; })
    .Where(u => u.UserId == 1).ExecuteNonQueryAsync();

// Delete — requires Where() or All()
await db.Users().Delete().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

Batch insert enforces a conservative 2100-parameter ceiling (`entityCount * columnsPerRow`) across all dialects; `Values()` throws `ArgumentException` on overflow — chunk with `.Chunk(n)`. The `Patch` struct supports up to 64 updatable columns (QRY045 beyond that); identity/computed columns are excluded; FK columns bind `.Id`. An all-empty patch (mask 0) throws `InvalidOperationException` at execute time.

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

**Value-type FirstOrDefault caveat:** The interface uses unconstrained `TResult?`, which for value types (tuples, primitives, enums) does NOT produce `Nullable<T>` — it returns `default(T)` when no rows match (same as LINQ's `FirstOrDefault()`). Callers cannot distinguish "no rows" from "a row whose value is `default`". Workarounds: use `ExecuteFetchFirstAsync` (throws on empty), or project to a reference type (entity or DTO) where `null` signals "no rows".

### Raw SQL

```csharp
// RawSqlAsync<T> is IAsyncEnumerable<T> — not Task<List<T>>. Use .ToListAsync() or await foreach.
IAsyncEnumerable<User> rows = db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await foreach (var u in rows) { … }
List<User> buffered = await db.RawSqlAsync<User>("SELECT * FROM users").ToListAsync();

await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

**Reader strategy:** When the SQL argument is a string literal the shared SQL parser can resolve, the generator emits a static lambda with hardcoded ordinals. Otherwise falls back to a `file struct IRowReader<T>` — `GetName` called once per result set, no per-row lambda or closure allocation. Column matching is case-insensitive.

**Row entity shape:** `RawSqlAsync<T>` / `RawSqlScalarAsync<T>` materialize rows via `new T()` + public settable properties. `T` must be a concrete (non-abstract, non-interface) class or struct with a public parameterless constructor and public `get; set;` properties (not `init`-only). Positional records, init-only properties, abstract classes, and interfaces are rejected with QRY043. For immutable shapes, project on a chain query (`Select(x => new Dto { ... })`).

**Diagnostics:** QRY031 (error) — unresolvable generic `T`. QRY041 (warn) — unresolvable column in literal SQL. QRY042 (info + code fix) — RawSqlAsync convertible to chain API. QRY043 (error) — row entity type not materializable.

### Diagnostics (QueryDiagnostics)

`ToDiagnostics()` returns compile-time analysis: `Sql`, `Parameters` (active only), `AllParameters`, `Kind`, `Dialect`, `TableName`, `Clauses` (per-clause SQL + params + source location + conditional info), `SqlVariants` (`Dictionary<int, SqlVariantDiagnostic>` — mask→SQL map), `ProjectionColumns`, `ProjectionKind`, `CarrierClassName`, `Joins`, `IsDistinct`, `Limit`, `Offset`, `IdentityColumnName`, `ActiveMask` (int), `ConditionalBitCount`, `TierReason`, `DisqualifyReason`, `UnmatchedMethodNames`. Available on every builder type and on `PreparedQuery<T>`; does not hit the database, so it's the primary tool for asserting generated SQL in tests.

### Trace

Add `QUARRY_TRACE` to consumer `.csproj` `DefineConstants` + `.Trace()` to the chain. Trace comments emitted as `// [Trace]` lines in generated interceptors. Categories: Discovery, Binding, Translation (per-site), ChainAnalysis, Assembly, Carrier (per-chain). Without the `QUARRY_TRACE` symbol: QRY034 warning. `.Trace()` is a compile-time-only no-op at runtime.

### Migrations & Scaffolding (CLI)

Code-first migrations and database-first scaffolding are driven by the `quarry` CLI (`Quarry.Tool`); apply migrations at runtime via the generated `await db.MigrateAsync(connection[, MigrationOptions])`.

- **Migrations** — `quarry migrate add <name>` (scaffold from schema diff), `list`, `validate`, `diff`, `script`, `bundle`, `squash`, `status`. Migrations are compilable C# (`MigrationBuilder` ops + snapshots); diagnostics QRY050–055. See [docs/articles/migrations.md](docs/articles/migrations.md).
- **Scaffolding** (DB → schema + context classes) — `quarry scaffold -d <dialect> --connection "…" -o ./Schemas --namespace MyApp`. Junction-table and implicit-FK detection, singularization. See [docs/articles/scaffolding.md](docs/articles/scaffolding.md).

### SQL Manifest (opt-in)

Enable per-dialect markdown documentation of every generated SQL statement:

```xml
<PropertyGroup>
  <QuarrySqlManifestPath>$(MSBuildProjectDirectory)/sql-manifest</QuarrySqlManifestPath>
</PropertyGroup>
```

Generator emits `quarry-manifest.{sqlite|postgresql|mysql|sqlserver}.md`, one per dialect. Each lists every chain's SQL, parameter table (including `LIMIT`/`OFFSET` rows), bitmask-labeled conditional variants, and summary stats. `WriteIfChanged` guard suppresses spurious git diffs. Zero overhead when unset. Write failures surface as QRY040 warning. See [docs/articles/sql-manifest.md](docs/articles/sql-manifest.md).

### Cross-ORM Conversion

`quarry convert --from {dapper|efcore|adonet|sqlkata} --project <path>` parses existing SQL strings in source code, resolves them against Quarry entity schemas, and emits equivalent chain API code (`Sql.Raw` fallback for unsupported constructs). Driven by `Quarry.Migration` Roslyn analyzers (QRM001–033, with IDE code fixes) that only activate when the source framework type is present. See [docs/articles/migrating-to-quarry.md](docs/articles/migrating-to-quarry.md).

### Logging

Logsmith Abstraction mode — zero runtime dependency. Logsmith 0.5.0 with `<LogsmithMode>Abstraction</LogsmithMode>` + `PrivateAssets="all"` generates logging types directly into the Quarry assembly. No `using Logsmith;`. Log checks use `LogsmithOutput.Logger?.IsEnabled(level, category) == true` (null-safe for no-logger scenarios).

Categories: `Quarry.Connection` (Info), `Quarry.Query`/`Quarry.Modify`/`Quarry.RawSql` (Debug), `Quarry.Parameters` (Trace, sensitive columns redacted), `Quarry.Execution` (Warning: slow queries), `Quarry.Migration` (Info). `Sensitive()` modifier → parameter values displayed as `[SENSITIVE]` in logs. Per-operation `opId` via `OpId.Next()` correlates all log entries (`[N]` prefix). Slow-query threshold: `db.SlowQueryThreshold` (default 500ms; `null` disables). See [docs/articles/logging.md](docs/articles/logging.md) for the `ILogsmithLogger` wiring and MS.Extensions/Serilog bridges.

## Constraints & Limitations

What an LLM should avoid suggesting — Quarry is a compile-time SQL builder, not an ORM:

- **Single analyzable scope.** A full chain (entry → terminal) must live in one method body. No storing builders in fields/collections, passing across methods, returning them, or building inside a loop → **QRY032**. Use `if`/`else if`/`else` or ternary reassignment for conditional clauses (bitmask-dispatched, up to 8 bits / 256 variants; nesting ≤ 2 cascade levels), not dynamic composition — see the Querying section for participating methods.
- **Entity accessors are methods:** `db.Users()`, not `db.Users`.
- **Chain-continuation methods** (`OrderBy`, `ThenBy`, `Limit`, `Offset`, `Distinct`, `WithTimeout`) live on `IQueryBuilder<T>` and only appear after a first clause (`Where`/`Select`/`GroupBy`). `db.Users().OrderBy(...)` won't compile.
- **Modification entry points** are on `IEntityAccessor<T>`: `db.Users().Insert(...)`/`.Update()`/`.Delete()`/`.InsertBatch(...)`. Update/Delete require `Where()` or `All()` before a terminal (QRY012).
- **`Select` required** before a fetch terminal when not fetching the whole entity (QRY011).
- **No anonymous-type projections** — use tuples or DTOs (QRY014).
- **Max 6-table explicit joins** — beyond that use CTEs (`.With<…>()`) or `RawSqlAsync`.
- **No change tracking, no lazy loading** — every insert/update/delete is explicit; related data via explicit joins or `Many<T>` subqueries.
- **No `IQueryable` composition across methods.** For "build once, run many ways," use `.Prepare()`.

## Coming from EF Core

| EF Core | Quarry |
|---|---|
| `DbContext` | `QuarryContext` |
| `DbSet<T>` | `IEntityAccessor<T>` (partial method) |
| Entity + attributes/Fluent API | `Schema` class with typed column properties |
| `context.Users.Where(...)` | `db.Users().Where(...)` |
| `.ToListAsync()` | `.ExecuteFetchAllAsync()` |
| `.FirstOrDefaultAsync()` | `.ExecuteFetchFirstOrDefaultAsync()` |
| `SaveChangesAsync()` | explicit `.Insert()` / `.Update()` / `.Delete()` chains |
| `Add-Migration` | `quarry migrate add` |
| `Update-Database` | `await db.MigrateAsync(connection)` |
| `Scaffold-DbContext` | `quarry scaffold` |

`db.Users().Where(...)` etc. take bare `Func<>` lambdas (not `Expression<Func<>>`) — analysis happens at compile time. See [docs/articles/why-quarry.md](docs/articles/why-quarry.md) and [docs/articles/migrating-to-quarry.md](docs/articles/migrating-to-quarry.md).

## Reference

### Dialect Differences

| | SQLite | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|---|
| Quote | `"` | `"` | `` ` `` | `[`/`]` |
| Params | `@p0` | `$1` (1-based) | `?` | `@p0` |
| Bool | `1`/`0` | `TRUE`/`FALSE` | `1`/`0` | `1`/`0` |
| Pagination | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `OFFSET/FETCH` |
| Returning | `RETURNING` | `RETURNING` | `LAST_INSERT_ID()` | `OUTPUT INSERTED` |
| Schema qualify | (ignored) | `"public"."t"` | `` `db`.`t` `` | `[dbo].[t]` |
| Concat | `\|\|` | `\|\|` | `CONCAT()` | `+` |

`SqlDialect` enum: `SQLite=0`, `PostgreSQL=1`, `MySQL=2`, `SqlServer=3`. Switch targets by changing the `[QuarryContext(Dialect = …)]` value and rebuilding — query, schema, and modification code are unchanged. See [docs/articles/switching-dialects.md](docs/articles/switching-dialects.md).

### Exceptions

`QuarryException` → `QuarryConnectionException`, `QuarryQueryException` (has `Sql`), `QuarryMappingException` (has `SourceType`/`TargetType`).

### Diagnostics (common codes)

Full QRY/QRA/QRM reference: [docs/articles/analyzer-rules.md](docs/articles/analyzer-rules.md). The codes most likely to come up:

| Code | Meaning |
|---|---|
| QRY011 | `Select` required before execution |
| QRY012 | `Where`/`All` required for Update/Delete |
| QRY014 | Anonymous-type projection unsupported (use tuple/DTO) |
| QRY032 | Chain not analyzable (escapes scope / loop / >8 conditional bits) |
| QRY033 | Forked chain — multiple terminals (use `.Prepare()`) |
| QRY035 / QRY036 | PreparedQuery escapes scope / has no terminal |
| QRY043 | Raw-SQL row entity not materializable |
| QRY044 | `[QuarryContext]` namespace missing from `<InterceptorsNamespaces>` |
| QRY045 | Entity has >64 updatable columns — no `Patch` struct |
| QRY060–065 | Navigation (`One<T>` / `HasManyThrough`) misconfiguration |
| QRY070–072 | Set-operation dialect/projection issues |
| QRY074 | Navigation aggregate in `Select` projection unresolved |
| QRY080–082 | CTE misuse |
| QRY900 | Internal generator error (file an issue) |

`QRA` (advisory analyzer rules) and `QRM` (cross-ORM conversion) diagnostics are documented in the articles above.
