# <img src="./docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Zero runtime dependencies. Structured logging via Logsmith Abstraction mode.

---

## Table of Contents

- [Packages](#packages)
- [Why Quarry Exists](#why-quarry-exists)
- [Comparison with Other Approaches](#comparison-with-other-approaches)
- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Schema Definition](#schema-definition)
- [Context Definition](#context-definition)
- [Querying](#querying)
- [Prepared Queries](#prepared-queries)
- [Modifications](#modifications)
- [Raw SQL](#raw-sql)
- [Migrations](#migrations)
- [Scaffolding](#scaffolding)
- [Logging](#logging)

## Packages

| Name | NuGet | Description |
|------|-------|-------------|
| [`Quarry`](https://www.nuget.org/packages/Quarry) | [![Quarry](https://img.shields.io/nuget/v/Quarry.svg?maxAge=60)](https://www.nuget.org/packages/Quarry) | Runtime types: builders, schema DSL, dialects, executors. |
| [`Quarry.Generator`](https://www.nuget.org/packages/Quarry.Generator) | [![Quarry.Generator](https://img.shields.io/nuget/v/Quarry.Generator.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Generator) | Roslyn incremental source generator + interceptor emitter. |
| [`Quarry.Analyzers`](https://www.nuget.org/packages/Quarry.Analyzers) | [![Quarry.Analyzers](https://img.shields.io/nuget/v/Quarry.Analyzers.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers) | Compile-time SQL query analysis rules (QRA series) with code fixes. |
| [`Quarry.Analyzers.CodeFixes`](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | [![Quarry.Analyzers.CodeFixes](https://img.shields.io/nuget/v/Quarry.Analyzers.CodeFixes.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | Code fix providers for QRA diagnostics. |
| [`Quarry.Tool`](https://www.nuget.org/packages/Quarry.Tool) | [![Quarry.Tool](https://img.shields.io/nuget/v/Quarry.Tool.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Tool) | CLI tool for migrations and database scaffolding (`quarry` command). |

---

## Why Quarry Exists

In most .NET data access libraries, SQL is built at runtime. LINQ expressions are translated on every call, column mapping relies on reflection, and query errors only surface when the code executes. This makes runtime performance harder to predict and rules out scenarios like NativeAOT where reflection is restricted.

**Quarry is a compile-time SQL builder — not an ORM.** A Roslyn incremental source generator analyzes your C# query expressions at build time and emits pre-built SQL as string literals — no runtime translation, no reflection, no fallback. Queries that can't be statically analyzed produce a compile error, not a runtime surprise.

Unlike an ORM, Quarry does not track entity state or generate change sets. You write explicit Insert, Update, and Delete calls, and the generator compiles each one into fixed SQL. The closest analogues outside .NET are [sqlc](https://sqlc.dev/) (Go) and [SQLDelight](https://cashapp.github.io/sqldelight/) (Kotlin).

| | ORM (EF Core) | Compile-time SQL builder (Quarry) | Micro-ORM (Dapper) |
|---|---|---|---|
| Schema definition | Yes | Yes | No |
| SQL authoring | Auto (LINQ → SQL at runtime) | Auto (C# → SQL at compile time) | Manual |
| Object mapping | Reflection-based | Source-generated readers | Reflection / AOT |
| Change tracking | Yes | No | No |
| Migrations | Yes | Yes | No |

---

## Comparison with Other Approaches

| Capability | Quarry | EF Core | Dapper | SqlKata |
|---|---|---|---|---|
| SQL generated at compile time | Yes | No (runtime LINQ translation) | No (hand-written SQL) | No (runtime builder) |
| Reflection-free hot path | Yes¹ | No | Partial (AOT mode) | No |
| NativeAOT compatible | Yes | Partial | Partial | No |
| Compile-time diagnostics | Yes | Limited | No | No |
| No dependencies | Yes | No | Yes | Yes |
| Type-safe schema definition | Yes | Yes (DbContext/model) | No | No |
| Multi-dialect support | Yes (4 dialects) | Yes (providers) | Manual | Yes |
| Join support | Up to 4 tables | Unlimited | Manual | Yes |
| Navigation subqueries | Yes (Any/All/Count) | Yes (full LINQ) | No | No |
| Conditional branch analysis | Yes | No | No | No |
| Database scaffolding | Yes | Yes | No | No |
| Change tracking | No | Yes | No | No |
| Migrations | Yes (code-first) | Yes | No | No |
| Prepared multi-terminal queries | Yes | No | No | No |

¹ Captured closure variables use a cached `FieldInfo` read per parameter; all SQL dispatch, binding, and row materialization is reflection-free.

---

## Features

### Compile-Time SQL Generation

The Roslyn incremental source generator analyzes every query call site and emits SQL as string literals in interceptor methods. No SQL is built at runtime — what you see in the generated code is exactly what executes. All SELECT queries emit explicit column lists (never `SELECT *`), making generated SQL predictable and immune to schema column-order drift.

### Carrier-Only Architecture

All query chains are compiled into **carrier classes** — generated abstract base classes that hold pre-built SQL, parameter arrays, and conditional dispatch logic. There is no runtime query builder; every chain must be fully analyzable at compile time. Chains that cannot be statically analyzed produce a compile-time error (QRY032).

### Execution Interceptors

All terminal methods — `ExecuteFetchAllAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ToAsyncEnumerable`, and `ToDiagnostics` — are intercepted at compile time. The generator emits pre-built SQL, ordinal-based readers, and pre-allocated parameter arrays directly into the interceptor, bypassing any runtime translation.

### Conditional Branch Support

Queries built with `if`/`else` branching are fully supported at compile time. The generator assigns each conditional clause a bit index and enumerates all possible clause combinations as a bitmask (up to 256 variants with 8 conditional bits). Each combination maps to its own pre-built SQL variant, so conditional query construction has zero runtime SQL building cost.

```csharp
var query = db.Users().Select(u => u);

if (activeOnly)
    query = query.Where(u => u.IsActive);

if (sortByName)
    query = query.OrderBy(u => u.UserName);

// The generator emits up to 4 SQL variants (2 bits × 2 states)
// and dispatches to the correct one at runtime via bitmask
var results = await query.Limit(10).ExecuteFetchAllAsync();
```

### Prepared Queries

`.Prepare()` compiles a query chain once and returns a `PreparedQuery<T>` that supports multiple terminal operations — fetch, scalar, streaming, diagnostics — without rebuilding the chain. Available on all builder types (select, join, insert, update, delete, batch insert).

### Zero-Allocation Readers

Intercepted query paths use ordinal-based `Func<DbDataReader, T>` delegates generated at compile time — no reflection and no dictionary lookups by column name.

### Multi-Dialect Support

Four SQL dialects — `SQLite`, `PostgreSQL`, `MySQL`, and `SqlServer` — with correct quoting, parameter formatting, pagination, and identity/returning syntax. Multiple contexts with different dialects can coexist in the same project.

### Type-Safe Schema DSL

Define tables as C# classes inheriting `Schema`. Columns are expression-bodied properties with typed modifiers. The generator reads the syntax tree directly — no attributes, no conventions, no runtime model building.

### Initializer-Aware Inserts

`Insert` inspects object initializer syntax at compile time. Only explicitly set properties generate INSERT columns, producing minimal SQL without runtime reflection over property values.

### Batch Insert

`InsertBatch` separates column declaration (compile-time analyzable lambda) from data provision (runtime collection), enabling full carrier optimization for multi-row inserts.

### Navigation Subqueries

`Many<T>` properties expose `Any()`, `All()`, and `Count()` as compile-time markers. The generator translates them into correlated `EXISTS` and `COUNT` subqueries with proper FK-to-PK correlation.

### Custom Entity Readers and Type Mappings

Override generated materialization with `EntityReader<T>`, or map custom CLR types to database types with `TypeMapping<TClr, TDb>`. Both integrate with the generated interceptor pipeline.

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

Optional: add compile-time query analysis rules:

```xml
<PackageReference Include="Quarry.Analyzers" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

---

## Quick Start

```csharp
// 1. Define a schema
public class UserSchema : Schema
{
    public static string Table => "users";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
}

// 2. Define a context
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

// 3. Query
await using var db = new AppDb(connection);

var activeUsers = await db.Users()
    .Select(u => new { u.UserName, u.Email })
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Limit(10)
    .ExecuteFetchAllAsync();
```

The generator emits an interceptor that replaces the `ExecuteFetchAllAsync` call with pre-built SQL and a typed reader. No runtime translation occurs.

---

## Schema Definition

Inherit `Schema`. Declare columns as expression-bodied properties.

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<decimal> Total => Precision(18, 2);

    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);

    public Index IX_UserName => Index(UserName).Unique();
    public Index IX_CreatedAt => Index(CreatedAt.Desc());
    public Index IX_Active => Index(IsActive).Where(IsActive);
}
```

**Column types:** `Key<T>` (primary key), `Col<T>` (standard), `Ref<TSchema, TKey>` (foreign key), `Many<T>` (1:N navigation). Generated entities use `EntityRef<TEntity, TKey>` for FK properties with optional navigation access via `.Id` and `.Value`.

**Modifiers:** `Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p, s)`, `Default(v)`, `Default(() => v)`, `MapTo("name")`, `Mapped<TMapping>()`, `Sensitive()`.

**Indexes:** Fluent modifiers: `Unique()`, `Where(col)`, `Where("raw SQL")`, `Include(columns...)`, `Using(IndexType)`. Sort direction via `.Asc()` / `.Desc()`. Index types: `BTree`, `Hash`, `Gin`, `Gist`, `SpGist`, `Brin` (PostgreSQL), `Clustered`, `Nonclustered` (SQL Server).

**Naming styles:** Override `NamingStyle` property — `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase`.

**Enums:** Automatically detected, stored and read as the underlying integral type.

---

## Context Definition

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

Dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`.

Multiple contexts with different dialects can coexist. Each generates its own interceptor file with dialect-correct SQL.

---

## Querying

All query builder methods return interfaces (`IQueryBuilder<T>`, `IJoinedQueryBuilder<T1, T2>`, etc.) to keep internal builder methods hidden from the public API.

### Select

```csharp
db.Users().Select(u => u);                                         // entity
db.Users().Select(u => u.UserName);                                // single column
db.Users().Select(u => (u.UserId, u.UserName));                    // tuple
db.Users().Select(u => new UserDto { Name = u.UserName });         // DTO
```

### Where

```csharp
db.Users().Where(u => u.IsActive && u.UserId > minId);

// Operators: ==, !=, <, >, <=, >=, &&, ||, !
// Null: u.Email == null, u.Email != null
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// IN: new[] { 1, 2, 3 }.Contains(u.UserId)
// Raw: Sql.Raw<bool>("\"Age\" > @p0", 18)
```

### OrderBy, GroupBy, Aggregates

```csharp
db.Users().OrderBy(u => u.UserName);
db.Users().OrderBy(u => u.CreatedAt, Direction.Descending);

db.Orders().GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)));
```

Aggregate markers: `Sql.Count()`, `Sql.Sum()`, `Sql.Avg()`, `Sql.Min()`, `Sql.Max()`. Aggregates work in both single-table and joined projections.

### Pagination and Distinct

```csharp
db.Users().Select(u => u).Limit(10).Offset(20);
db.Users().Select(u => u.UserName).Distinct();
```

### Joins

```csharp
// 2-table join (also LeftJoin, RightJoin)
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Where((u, o) => o.Total > 100)
    .Select((u, o) => (u.UserName, o.Total));

// Navigation-based join
db.Users().Join(u => u.Orders)
    .Select((u, o) => (u.UserName, o.Total));

// 3/4-table chained joins (max 4 tables)
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
    .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName));
```

### Navigation Subqueries

On `Many<T>` properties inside `Where`:

```csharp
db.Users().Where(u => u.Orders.Any());                         // EXISTS
db.Users().Where(u => u.Orders.Any(o => o.Total > 100));       // filtered EXISTS
db.Users().Where(u => u.Orders.All(o => o.Status == "paid"));  // NOT EXISTS + negated
db.Users().Where(u => u.Orders.Count() > 5);                   // scalar COUNT
db.Users().Where(u => u.Orders.Count(o => o.Total > 50) > 2); // filtered COUNT
```

---

## Prepared Queries

`.Prepare()` compiles a query chain into a `PreparedQuery<T>` that supports multiple terminal operations on the same pre-built SQL. Available on all builder types.

```csharp
// Build once, execute multiple ways
var prepared = db.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .Prepare();

var diag = prepared.ToDiagnostics();            // inspect SQL and metadata
var all = await prepared.ExecuteFetchAllAsync(); // fetch all rows
var first = await prepared.ExecuteFetchFirstAsync(); // fetch first row
```

Prepare works with modifications too:

```csharp
var prepared = db.Users()
    .Insert(new User { UserName = "x", IsActive = true })
    .Prepare();

var diag = prepared.ToDiagnostics();
var affected = await prepared.ExecuteNonQueryAsync();
```

When a single terminal is called after `.Prepare()`, the generator produces identical SQL to a direct chain — zero overhead.

---

## Modifications

### Insert

```csharp
// Single insert — initializer-aware, only set properties generate columns
await db.Users().Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync();
var id = await db.Users().Insert(user).ExecuteScalarAsync<int>();  // returns generated key

// Batch insert — column-selector + data-provider pattern
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
```

### Update

Requires `Where()` or `All()` before execution. Two `Set` overloads:

```csharp
// Assignment syntax — single or multiple columns in one lambda
await db.Users().Update()
    .Set(u => u.UserName = "New")
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

await db.Users().Update()
    .Set(u => { u.UserName = "New"; u.IsActive = true; })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Captured variables work — extracted at runtime from the delegate closure
var newName = GetNameFromInput();
await db.Users().Update()
    .Set(u => { u.UserName = newName; u.IsActive = true; })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Entity form — sets all initialized properties
await db.Users().Update()
    .Set(new User { UserName = "New" })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

### Delete

```csharp
// Requires Where() or All() before execution
await db.Users().Delete().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

### Execution Methods

| Method | Returns |
|---|---|
| `ExecuteFetchAllAsync()` | `Task<List<T>>` |
| `ExecuteFetchFirstAsync()` | `Task<T>` (throws if empty) |
| `ExecuteFetchFirstOrDefaultAsync()` | `Task<T?>` |
| `ExecuteFetchSingleAsync()` | `Task<T>` (throws if not exactly one) |
| `ExecuteScalarAsync<T>()` | `Task<T>` |
| `ExecuteNonQueryAsync()` | `Task<int>` |
| `ToAsyncEnumerable()` | `IAsyncEnumerable<T>` |
| `ToDiagnostics()` | `QueryDiagnostics` (SQL, parameters, optimization metadata, clause breakdown) |
| `Prepare()` | `PreparedQuery<T>` (reusable multi-terminal handle) |

### Query Diagnostics

`ToDiagnostics()` returns a `QueryDiagnostics` object with the generated SQL, bound parameters, optimization metadata, and a per-clause breakdown. Available on all builder types.

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Select(u => u)
    .ToDiagnostics();

Console.WriteLine(diag.Sql);                // SELECT ... FROM "users" WHERE ...
Console.WriteLine(diag.Dialect);            // SQLite
Console.WriteLine(diag.Tier);              // PrebuiltDispatch
Console.WriteLine(diag.IsCarrierOptimized); // True
Console.WriteLine(diag.Kind);              // Select
Console.WriteLine(diag.TableName);         // users

foreach (var p in diag.Parameters)
    Console.WriteLine($"{p.Name} = {p.Value}");

foreach (var clause in diag.Clauses)
    Console.WriteLine($"{clause.ClauseType}: {clause.SqlFragment} (active={clause.IsActive})");
```

For conditional chains, each clause reports `IsConditional` and `IsActive` so you can inspect which branches were taken and verify the generated SQL for each path.

Additional metadata available on `QueryDiagnostics`:

| Property | Description |
|---|---|
| `SqlVariants` | All pre-built SQL variants keyed by conditional bitmask |
| `ActiveMask` | Current runtime bitmask for conditional clauses |
| `ConditionalBitCount` | Number of conditional bits in the chain |
| `ProjectionColumns` | SELECT column breakdown with types and ordinals |
| `ProjectionKind` | Entity, Dto, Tuple, or SingleColumn |
| `Joins` | JOIN metadata (table, kind, alias, ON condition) |
| `CarrierClassName` | Name of the generated carrier class |
| `IdentityColumnName` | Identity column for INSERT chains |
| `InsertRowCount` | Number of rows for batch inserts |

---

## Raw SQL

Source-generated typed readers — zero reflection.

```csharp
await db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

---

## Migrations

Code-first migration scaffolding via the `quarry` CLI tool. Reads C# schema definitions via Roslyn, diffs against the previous snapshot, and generates migration files — no database connection required.

### Setup

```sh
dotnet tool install --global Quarry.Tool
```

### CLI Commands

```sh
quarry migrate add InitialCreate                # scaffold from schema changes
quarry migrate add AddUserEmail -p src/MyApp    # specify project path
quarry migrate add-empty SeedData               # empty migration for custom SQL
quarry migrate list                             # list all migrations
quarry migrate validate                         # check version integrity
quarry migrate remove                           # remove latest migration files
quarry migrate diff                             # preview schema changes (no file generation)
quarry migrate script                           # generate incremental migration SQL for a version range
quarry migrate status -c <conn>                 # show applied vs pending status (requires connection)
quarry migrate squash                           # collapse all migrations into a single baseline
quarry migrate bundle                           # build a self-contained migration executable
quarry create-scripts -d postgresql -o schema.sql  # generate full DDL
```

Each `migrate add` generates a migration class with `Upgrade()`, `Downgrade()`, and `Backup()` methods plus a snapshot capturing the full schema state as compilable C#. Operations are risk-classified: `[+]` Safe, `[~]` Cautious, `[!]` Destructive.

### Applying Migrations at Runtime

The source generator emits a `MigrateAsync` method on each `QuarryContext`:

```csharp
await using var db = new AppDb(connection);
await db.MigrateAsync(connection);                                // apply all pending
await db.MigrateAsync(connection, new MigrationOptions            // with options
{
    Direction = MigrationDirection.Downgrade,
    TargetVersion = 1,
    DryRun = true,
    RunBackups = true,
    Logger = msg => Console.WriteLine(msg)
});
```

The runtime creates a `__quarry_migrations` history table to track applied versions.

For detailed CLI documentation, see [Quarry.Tool README](src/Quarry.Tool/README.md).

---

## Scaffolding

Reverse-engineer an existing database into Quarry schema classes and a context — database-first workflow.

```sh
quarry scaffold -d sqlite --database school.db -o Schemas --namespace MyApp
quarry scaffold -d postgresql --server localhost --user admin --password secret --database mydb -o Schemas
quarry scaffold -c "Server=localhost;Database=mydb" -d sqlserver -o Schemas --ni
```

### Options

| Flag | Description |
|---|---|
| `-d, --dialect` | SQL dialect (required): `sqlite`, `postgresql`, `mysql`, `sqlserver` |
| `--database` | Database file (SQLite) or name |
| `--server`, `--port`, `--user`, `--password` | Connection parameters |
| `-c, --connection` | Connection string (alternative to individual params) |
| `-o, --output` | Output directory (default: `.`) |
| `--namespace` | Namespace for generated classes |
| `--schema` | Schema filter (e.g. `public`, `dbo`) |
| `--tables` | Comma-separated table filter |
| `--naming-style` | `Exact`, `SnakeCase`, `CamelCase`, `LowerCase` |
| `--no-navigations` | Skip generating `Many<T>` navigation properties |
| `--no-singularize` | Don't singularize table names to class names |
| `--context` | Custom context class name |
| `--ni` | Non-interactive mode (auto-accept implicit FKs) |

### What It Generates

- One schema class per table with `Key<T>`, `Col<T>`, `Ref<T, TKey>`, and `Many<T>` properties
- A `QuarryContext` subclass with `IEntityAccessor<T>` methods for each table
- Automatic detection of junction tables (many-to-many), implicit foreign keys by naming convention, and naming style inference

---

## Logging

Quarry uses [Logsmith](https://www.nuget.org/packages/Logsmith) in **Abstraction mode** for structured logging. All logging types are source-generated into the `Quarry.Logging` namespace — no Logsmith DLL ships with Quarry. Consumers wire logging by implementing a single interface.

### Log Categories

| Category | Level | What it logs |
|---|---|---|
| `Quarry.Connection` | Information | Connection opened/closed |
| `Quarry.Query` | Debug | SQL generated, fetch completion (row count + elapsed time), scalar results |
| `Quarry.Modify` | Debug | SQL generated, modification completion (operation + row count + elapsed time) |
| `Quarry.RawSql` | Debug | SQL generated, fetch/non-query/scalar completion |
| `Quarry.Parameters` | Trace | Parameter values bound to queries (`@p0 = value`) |
| `Quarry.Execution` | Warning | Slow query detection (elapsed time + SQL) |
| `Quarry.Migration` | Information | Migration applying/applied/rolled back, dry run, SQL generated |

### Setup

Implement `ILogsmithLogger` and assign to `LogsmithOutput.Logger`:

```csharp
using Quarry.Logging;

LogsmithOutput.Logger = new ConsoleLogger();

sealed class ConsoleLogger : ILogsmithLogger
{
    public bool IsEnabled(LogLevel level, string category) => level >= LogLevel.Debug;

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        Console.WriteLine($"[{entry.Level}] {entry.Category}: {Encoding.UTF8.GetString(utf8Message)}");
    }
}
```

To disable logging, set `LogsmithOutput.Logger = null` (the default).

### Slow Query Detection

```csharp
db.SlowQueryThreshold = TimeSpan.FromSeconds(1); // default: 500ms
db.SlowQueryThreshold = null;                    // disable
```

### Sensitive Parameter Redaction

Mark columns with `Sensitive()` in the schema — parameters display as `***` in all log output.

### Operation Correlation

Every log entry includes an `[opId]` prefix. All entries from the same query/modification share the same opId, enabling correlation across SQL, parameter, and completion logs.
