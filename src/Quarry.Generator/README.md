# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry.Generator

Roslyn incremental source generator that analyzes fluent query chains at compile time and emits interceptor methods containing pre-built SQL, ordinal-based readers, and zero-allocation carrier classes. No SQL is built at runtime — what you see in the generated code is exactly what executes.

## Packages

| Name | NuGet | Description |
|------|-------|-------------|
| [`Quarry`](https://www.nuget.org/packages/Quarry) | [![Quarry](https://img.shields.io/nuget/v/Quarry.svg?maxAge=60)](https://www.nuget.org/packages/Quarry) | Runtime types: builders, schema DSL, dialects, executors. |
| [`Quarry.Generator`](https://www.nuget.org/packages/Quarry.Generator) | [![Quarry.Generator](https://img.shields.io/nuget/v/Quarry.Generator.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Generator) | Roslyn incremental source generator + interceptor emitter. |
| [`Quarry.Analyzers`](https://www.nuget.org/packages/Quarry.Analyzers) | [![Quarry.Analyzers](https://img.shields.io/nuget/v/Quarry.Analyzers.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers) | Compile-time SQL query analysis rules (QRA series) with code fixes. |
| [`Quarry.Analyzers.CodeFixes`](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | [![Quarry.Analyzers.CodeFixes](https://img.shields.io/nuget/v/Quarry.Analyzers.CodeFixes.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | Code fix providers for QRA diagnostics. |
| [`Quarry.Tool`](https://www.nuget.org/packages/Quarry.Tool) | [![Quarry.Tool](https://img.shields.io/nuget/v/Quarry.Tool.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Tool) | CLI tool for migrations and database scaffolding (`quarry` command). |

---

## Installation

```xml
<PackageReference Include="Quarry" Version="1.0.0" />
<PackageReference Include="Quarry.Generator" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

Enable interceptors by adding your `QuarryContext` namespace to `InterceptorsNamespaces` in your `.csproj`. The generator emits interceptors into the same namespace as your context class:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```

Replace `MyApp.Data` with the namespace containing your `QuarryContext` subclass. If your context has no namespace, use `Quarry.Generated`.

To inspect generated code, add:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

---

## Quick Start

### 1. Define a schema

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
}
```

### 2. Define a context

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
```

Dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`.

### 3. Query

```csharp
await using var db = new AppDb(connection);

var activeUsers = await db.Users()
    .Where(u => u.IsActive)
    .Select(u => new UserDto { Name = u.UserName, Email = u.Email })
    .OrderBy(u => u.UserName)
    .Limit(10)
    .ExecuteFetchAllAsync();
```

The generator emits an interceptor that replaces `ExecuteFetchAllAsync` with pre-built SQL and a typed reader. No runtime translation occurs.

---

## How It Works

The generator runs a multi-stage pipeline during compilation:

1. **Discovery** — Scans syntax trees for method calls on Quarry builder types (`Where`, `Select`, `Join`, `Insert`, etc.)
2. **Binding** — Enriches each call site with semantic information: entity metadata, dialect, parameter types
3. **Translation** — Resolves column references, parameters, and expression trees into SQL expression IR
4. **Chain Analysis** — Groups call sites into fluent chains, identifies terminals, analyzes conditional branches
5. **SQL Assembly** — Renders each chain into dialect-specific SQL string literals for every possible clause combination
6. **Carrier Analysis** — Determines if the chain qualifies for zero-allocation carrier optimization
7. **Code Emission** — Generates `[InterceptsLocation]` methods that replace the original calls at compile time

The result: fluent C# queries become pre-compiled SQL execution with full type safety.

---

## Optimization Tiers

The generator classifies every query chain into an optimization tier:

| Mode | Name | Description |
|------|------|-------------|
| **PrebuiltDispatch** | Pre-built dispatch | All clauses analyzed. SQL dispatch table emitted as constants. Zero runtime string work. |
| **RuntimeBuild** | Compile error | Chain not statically analyzable. Produces QRY032 compile error directing the user to restructure. |

PrebuiltDispatch is the only output mode for well-formed chains. The generator emits `QRY001` or `QRY032` diagnostics when a chain cannot be analyzed.

---

## Carrier Architecture

For all analyzed chains, the generator emits a **carrier class** — a lightweight sealed class that holds all parameters, conditions, and state for the query. Carriers eliminate all intermediate builder allocations on the execution path.

Each carrier:
- Implements the builder interfaces (`IQueryBuilder<T>`, `IDeleteBuilder<T>`, etc.)
- Stores parameters as typed fields (`P0`, `P1`, ...)
- Tracks conditional clause activation via a bitmask field (`Mask`)
- Contains the pre-built SQL dispatch table as `const string` fields
- Uses ordinal-based `Func<DbDataReader, T>` delegates for materialization — no reflection

Use `ToDiagnostics()` to verify carrier optimization:

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .ToDiagnostics();

Console.WriteLine(diag.Kind);              // Select
Console.WriteLine(diag.CarrierClassName);  // Chain_...
```

---

## Conditional Branch Support

Queries built with `if`/`else` branching are fully supported. The generator assigns each conditional clause a bit index and enumerates all possible combinations as a bitmask dispatch table.

```csharp
var query = db.Users().Select(u => u);

if (activeOnly)
    query = query.Where(u => u.IsActive);

if (sortByName)
    query = query.OrderBy(u => u.UserName);

// Generator emits up to 4 SQL variants (2 bits x 2 states)
// and dispatches to the correct one at runtime via bitmask
var results = await query.Limit(10).ExecuteFetchAllAsync();
```

Each clause reports its conditional state via `ToDiagnostics()`:

```csharp
foreach (var clause in diag.Clauses)
    Console.WriteLine($"{clause.ClauseType}: active={clause.IsActive}, conditional={clause.IsConditional}");
```

---

## Prepared Queries

`.Prepare()` freezes a query chain and allows multiple terminal operations without rebuilding:

```csharp
var prepared = db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .Prepare();

var all = await prepared.ExecuteFetchAllAsync();
var first = await prepared.ExecuteFetchFirstAsync();
var diag = prepared.ToDiagnostics();
```

`.Prepare()` is zero-cost — it performs an `Unsafe.As` cast with no allocation. The generator intercepts each terminal on the `PreparedQuery<T>` variable independently.

Constraints:
- The `PreparedQuery` variable must not escape the declaring method (no returns, field assignments, or lambda captures — `QRY035`)
- At least one terminal must be invoked on the prepared variable (`QRY036`)

---

## Query Diagnostics

`ToDiagnostics()` returns compile-time analysis metadata without executing the query:

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Select(u => u)
    .ToDiagnostics();

Console.WriteLine(diag.Sql);               // SELECT ... FROM "users" WHERE ...
Console.WriteLine(diag.Dialect);           // SQLite
Console.WriteLine(diag.Kind);             // Select

foreach (var p in diag.Parameters)
    Console.WriteLine($"{p.Name} = {p.Value} ({p.TypeName})");

foreach (var clause in diag.Clauses)
    Console.WriteLine($"{clause.ClauseType}: {clause.SqlFragment}");
```

Available on all builder types — SELECT, INSERT, UPDATE, DELETE, and batch insert chains.

---

## Supported Query Patterns

### Select

```csharp
db.Users().Select(u => u);                                    // full entity
db.Users().Select(u => u.UserName);                           // single column
db.Users().Select(u => (u.UserId, u.UserName));               // tuple
db.Users().Select(u => new UserDto { Name = u.UserName });    // named DTO
```

Anonymous type projections are not supported (`QRY014`). Use named records, classes, or tuples.

### Where

```csharp
db.Users().Where(u => u.IsActive && u.UserId > minId);
db.Users().Where(u => u.Email != null);
db.Users().Where(u => u.UserName.Contains("smith"));        // LIKE '%smith%'
db.Users().Where(u => new[] { 1, 2, 3 }.Contains(u.UserId)); // IN clause
db.Users().Where(u => Sql.Raw<bool>("\"Age\" > @p0", 18));  // raw SQL
```

Supported operators: `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`. String methods: `Contains`, `StartsWith`, `EndsWith`, `ToLower`, `ToUpper`, `Trim`, `Substring`.

### Joins

Up to 6 tables. Supports `Join`, `LeftJoin`, `RightJoin`, `CrossJoin`, `FullOuterJoin`, and navigation-based joins:

```csharp
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => (u.UserName, o.Total));

db.Users().Join(u => u.Orders)              // navigation-based
    .Select((u, o) => (u.UserName, o.Total));

db.Users().CrossJoin<Region>();               // no condition
db.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id);
```

Columns projected from the nullable side of a LEFT/RIGHT/FULL OUTER join are `IsDBNull`-guarded in the generated reader automatically.

### Navigation Joins

`One<T>` with `HasOne<T>()` emits a reverse-side nullable nav property. `HasManyThrough<TTarget, TJunction>()` emits many-to-many skip navigation with the junction→target JOIN implicit in `Count()`, `Any()`, and aggregates.

```csharp
public class OrderSchema : Schema
{
    public One<User> User => HasOne<User>();
    public Many<Tag> Tags => HasManyThrough<Tag, OrderTag>();
}

db.Orders().Where(o => o.User!.IsActive);
db.Orders().Where(o => o.Tags.Any(t => t.Name == "urgent"));
```

### Navigation Subqueries

`Many<T>` properties support `Any()`, `All()`, `Count()`, `Sum`, `Min`, `Max`, `Avg`/`Average` in WHERE and SELECT clauses, translated to correlated `EXISTS`, `COUNT`, or aggregate subqueries:

```csharp
db.Users().Where(u => u.Orders.Any(o => o.Total > 100));
db.Users().Where(u => u.Orders.Count() > 5);
db.Users().Select(u => new {
    u.UserName,
    OrderTotal = u.Orders.Sum(o => o.Total),
    BiggestOrder = u.Orders.Max(o => o.Total),
});
```

### Aggregates

```csharp
db.Orders().GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)));
```

Markers: `Sql.Count()`, `Sql.Sum()`, `Sql.Avg()`, `Sql.Min()`, `Sql.Max()`.

### Window Functions

Aggregate-OVER and ranking/offset functions with a fluent `IOverClause`:

```csharp
db.Sales().Select(s => new {
    s.Region,
    s.Amount,
    Rank = Sql.Rank(over => over.PartitionBy(s.Region).OrderByDescending(s.Amount)),
    RunningTotal = Sql.Sum(s.Amount, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
    Previous = Sql.Lag(s.Amount, 1, 0m, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
});
```

Supported: `RowNumber`, `Rank`, `DenseRank`, `Ntile`, `Lag`, `Lead`, `FirstValue`, `LastValue`, and `Sum`/`Count`/`Avg`/`Min`/`Max(col, over => …)`. Non-column arguments are parameterized at compile time. ROWS/RANGE frame specifications are deferred to a later release.

### Set Operations

```csharp
db.Users().Select(u => u.UserName)
    .Union(db.Admins().Select(a => a.DisplayName));
```

Available: `Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll`. Cross-entity set operations are supported. Post-set `Where`/`GroupBy`/`Having` auto-wrap as a subquery.

### Common Table Expressions

```csharp
db.With<User, ActiveUser>(users => users
        .Where(u => u.IsActive)
        .Select(u => new ActiveUser(u.UserId, u.UserName)))
    .FromCte<ActiveUser>()
    .Where(a => a.UserName.StartsWith("a"))
    .ExecuteFetchAllAsync();
```

Multi-CTE chains (`.With<A>(…).With<B>(…)`) and typed post-`With` accessors (`QuarryContext<TSelf>`) supported. Diagnostics: QRY080, QRY081, QRY082.

### Insert

```csharp
// Single — initializer-aware, only set properties generate columns
var id = await db.Users()
    .Insert(new User { UserName = "x", IsActive = true })
    .ExecuteScalarAsync<int>();

// Batch — column-selector + data-provider pattern
await db.Users()
    .InsertBatch(u => (u.UserName, u.IsActive))
    .Values(users)
    .ExecuteNonQueryAsync();
```

### Update

```csharp
await db.Users().Update()
    .Set(u => { u.UserName = "New"; u.IsActive = true; })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

### Delete

```csharp
await db.Users().Delete()
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

Update and Delete require `Where()` or `All()` before execution (`QRY012`).

### Execution Terminals

| Method | Returns |
|---|---|
| `ExecuteFetchAllAsync()` | `Task<List<T>>` |
| `ExecuteFetchFirstAsync()` | `Task<T>` (throws if empty) |
| `ExecuteFetchFirstOrDefaultAsync()` | `Task<T?>` |
| `ExecuteFetchSingleAsync()` | `Task<T>` (throws if not exactly one) |
| `ExecuteFetchSingleOrDefaultAsync()` | `Task<T?>` (throws if more than one) |
| `ExecuteScalarAsync<T>()` | `Task<T>` |
| `ExecuteNonQueryAsync()` | `Task<int>` |
| `ToAsyncEnumerable()` | `IAsyncEnumerable<T>` |
| `ToDiagnostics()` | `QueryDiagnostics` |
| `Prepare()` | `PreparedQuery<T>` |

These terminals are also available directly on `IQueryBuilder<T>` (no need for an identity `.Select(u => u)` when fetching full entities).

---

## Inline Constants and Collection Parameters

The generator detects constant values and emits them as SQL literals (no parameter overhead):

```csharp
db.Users().Where(u => u.Status == "Active")
// Generated: WHERE "Status" = 'Active'  — literal, no parameter
```

Collection parameters expand to IN clauses with per-element binding:

```csharp
var ids = new[] { 1, 2, 3 };
db.Users().Where(u => ids.Contains(u.UserId))
// Generated: WHERE "UserId" IN (@p0, @p1, @p2)
```

---

## Schema Features

### Column Types

| Type | Purpose |
|---|---|
| `Key<T>` | Primary key |
| `Col<T>` | Standard column |
| `Ref<TSchema, TKey>` | Foreign key with navigation |
| `Many<T>` | One-to-many navigation (compile-time marker) |

### Column Modifiers

`Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p, s)`, `Default(v)`, `Default(() => v)`, `MapTo("name")`, `Mapped<TMapping>()`, `Sensitive()`, `Unique()`, `Collation("...")`.

### Indexes

```csharp
public Index IX_Email => Index(Email).Unique();
public Index IX_Created => Index(CreatedAt.Desc());
public Index IX_Active => Index(Email).Where(IsActive);
public Index IX_Covering => Index(Email).Include(UserName, CreatedAt);
```

Fluent modifiers: `Unique()`, `Where(col)`, `Where("raw SQL")`, `Include(columns...)`, `Using(IndexType)`.

### Custom Type Mappings

```csharp
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}

// In schema:
public Col<Money> Price => Mapped<MoneyMapping>();
```

---

## Generator Diagnostics (QRY Series)

### Errors

| ID | Title |
|----|-------|
| QRY002 | Missing `Table` property on schema class |
| QRY003 | Invalid column type with no TypeMapping |
| QRY004 | Navigation references unknown entity |
| QRY006 | Unsupported operation in Where expression |
| QRY007 | Join references undefined relationship |
| QRY009 | Aggregate without GroupBy |
| QRY010 | Composite primary keys not supported |
| QRY011 | Select() required before execution terminal |
| QRY012 | Update/Delete requires Where() or All() |
| QRY013 | GUID key requires ClientGenerated() |
| QRY014 | Anonymous type projection not supported |
| QRY017 | TypeMapping type mismatch |
| QRY018 | Duplicate TypeMapping for same type |
| QRY020 | All() requires a predicate |
| QRY021 | Subquery entity not found in context |
| QRY022 | Subquery FK column not found |
| QRY024 | Subquery on non-navigation property |
| QRY025 | Subquery on composite-PK entity |
| QRY027 | Invalid EntityReader type |
| QRY029 | Sql.Raw placeholder mismatch |
| QRY032 | Query chain not analyzable |
| QRY031 | Unresolvable `RawSqlAsync<T>` generic type parameter |
| QRY033 | Forked query chain (multiple terminals on same builder variable) |
| QRY035 | PreparedQuery escapes method scope |
| QRY036 | PreparedQuery has no terminals |
| QRY052 | Migration version gap or duplicate |
| QRY060–065 | Navigation misconfiguration (`One<T>`, `HasManyThrough`) |
| QRY072 | Set operation projection mismatch |
| QRY080 | CTE inner query not analyzable |
| QRY081 | `FromCte` without matching `With` |
| QRY082 | Duplicate CTE name in chain |

### Warnings

| ID | Title |
|----|-------|
| QRY001 | Query not fully analyzable (runtime fallback) |
| QRY005 | Unmapped property in Select projection |
| QRY008 | Potential SQL injection in Sql.Raw |
| QRY015 | Ambiguous context resolution for entity |
| QRY016 | Unbound parameter placeholder in generated SQL |
| QRY019 | Clause not translatable at compile time |
| QRY023 | Subquery FK-to-PK correlation ambiguous |
| QRY028 | Redundant unique constraint (column + index) |
| QRY034 | .Trace() requires QUARRY_TRACE define |
| QRY040 | SQL manifest write failure |
| QRY041 | RawSqlAsync column expression without alias |
| QRY050 | Schema changed since last migration snapshot |
| QRY070 | `IntersectAll` not supported on this dialect |
| QRY071 | `ExceptAll` not supported on this dialect |
| QRY051 | Migration references unknown table/column |
| QRY054 | Destructive migration without backup |
| QRY055 | Nullable to non-null without data migration |

### Info

| ID | Title |
|----|-------|
| QRY026 | Custom EntityReader active |
| QRY030 | Query chain optimized |
| QRY042 | RawSqlAsync convertible to chain (code fix available) |
| QRY053 | Pending migrations detected |

---

## Multi-Dialect Support

Four SQL dialects with correct quoting, parameter formatting, pagination, and identity/returning syntax. Multiple contexts with different dialects can coexist in the same project — each generates its own interceptor file.

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class LiteDb : QuarryContext { ... }

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class PgDb : QuarryContext { ... }
```

---

## Raw SQL

`RawSqlAsync<T>` returns `IAsyncEnumerable<T>` — buffer with `.ToListAsync()` or stream with `await foreach`. When the SQL argument is a string literal the shared parser can resolve, the generator emits a static reader lambda with hardcoded ordinals; otherwise it falls back to a struct-based ordinal cache. Column matching is case-insensitive.

```csharp
// Stream
await foreach (var u in db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId))
    Process(u);

// Buffer
List<User> users = await db.RawSqlAsync<User>("SELECT * FROM users", ).ToListAsync();

await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

Diagnostics: `QRY031` (unresolvable generic `T`), `QRY041` (unresolvable column in literal SQL), `QRY042` (Raw SQL convertible to chain — info + code fix).

---

## SQL Manifest

Opt-in per-dialect markdown documentation of every generated SQL statement. Enable via MSBuild:

```xml
<PropertyGroup>
  <QuarrySqlManifestPath>$(MSBuildProjectDirectory)/sql-manifest</QuarrySqlManifestPath>
</PropertyGroup>
```

Zero overhead when disabled. See the [SQL Manifest article](https://dtronix.github.io/Quarry/articles/sql-manifest.html) for details.

## Migrations

The generator emits a `MigrateAsync` method on each context for runtime migration execution:

```csharp
await db.MigrateAsync(connection);
await db.MigrateAsync(connection, new MigrationOptions
{
    TargetVersion = 5,
    DryRun = true,
    RunBackups = true
});
```

Migration scaffolding is handled by the `quarry` CLI tool — see [Quarry.Tool](https://www.nuget.org/packages/Quarry.Tool) for CLI documentation.

---

## Logging

Quarry uses [Logsmith](https://www.nuget.org/packages/Logsmith) for structured logging with categories: `Quarry.Query`, `Quarry.Modify`, `Quarry.Execution`, `Quarry.Parameters`, `Quarry.Connection`, `Quarry.Migration`, `Quarry.RawSql`.

Slow query detection is configurable per context:

```csharp
db.SlowQueryThreshold = TimeSpan.FromSeconds(1);
```

Mark columns with `Sensitive()` in the schema to redact parameter values in all log output.
