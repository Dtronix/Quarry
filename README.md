# <img src="./docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Zero runtime dependencies. Structured logging via Logsmith Abstraction mode.

**[Documentation](https://dtronix.github.io/Quarry/)** | **[API Reference](https://dtronix.github.io/Quarry/api/)** | **[Benchmark Dashboard](https://dtronix.github.io/Quarry-benchmarks/)**

---

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
| Reflection-free | Yes | No | Partial (AOT mode) | No |
| Zero-allocation query dispatch | Yes (carrier architecture) | No | No | No |
| NativeAOT compatible | Yes | Partial | Partial | No |
| Compile-time diagnostics | Yes | Limited | No | No |
| Compile-time constant inlining | Yes | No | N/A | No |
| No runtime dependencies | Yes | No | Yes | Yes |
| Type-safe schema definition | Yes | Yes (DbContext/model) | No | No |
| Multi-dialect support | Yes (4 dialects) | Yes (providers) | Manual | Yes |
| Join support | Up to 6 tables | Unlimited | Manual | Yes |
| Navigation joins (One\<T\>, HasManyThrough) | Yes (compile-time) | Yes (conventions) | No | No |
| Navigation subqueries | Yes (Any/All/Count/Sum/Min/Max/Avg) | Yes (full LINQ) | No | No |
| Window functions | Yes (compile-time) | Limited | Manual | No |
| Common Table Expressions | Yes (single + multi-CTE) | Raw SQL only | Manual | Limited |
| Set operations (Union/Intersect/Except) | Yes | Yes (LINQ) | Manual | Yes |
| Raw SQL with compile-time readers | Yes (ordinal-cached) | No (runtime binding) | Manual | No |
| Conditional branch analysis | Yes | No | No | No |
| Database scaffolding | Yes | Yes | No | No |
| Change tracking | No | Yes | No | No |
| Migrations | Yes (code-first, bundles, seed data, views, stored procedures, checksums, squash) | Yes | No | No |
| Cross-ORM migration converters | Yes (EF Core, Dapper, ADO.NET, SqlKata) | No | No | No |
| Structured logging | Yes (Logsmith) | Yes (built-in) | No | No |
| SQL manifest emission | Yes (opt-in) | No | No | No |
| Prepared multi-terminal queries | Yes (all builder types) | No | No | No |


---

## Performance

Quarry approaches raw ADO.NET throughput with near-zero framework overhead — faster than Dapper and significantly faster than EF Core or SqlKata. This is possible because the compile-time architecture eliminates entire categories of runtime work:

- **Pre-built SQL string literals** — no runtime translation or expression tree walking
- **Ordinal-based readers** — no reflection or column-name lookups
- **Carrier dispatch** — zero-allocation query execution via generated carrier classes
- **No runtime dependencies** — nothing to initialize or warm up

Benchmarks are run against Raw ADO.NET, Dapper, EF Core, and SqlKata using [BenchmarkDotNet](https://benchmarkdotnet.org/) on an in-memory SQLite database. See the [live benchmark dashboard](https://dtronix.github.io/Quarry-benchmarks/dev/bench/) for run-over-run trends with per-commit reports and the [benchmark methodology](https://dtronix.github.io/Quarry/articles/benchmarks.html) for category descriptions and how to run locally.

---

## Features

- **[Compile-time SQL generation](https://dtronix.github.io/Quarry/articles/getting-started.html)** — all SQL emitted as string literals at build time; no runtime translation
- **[Carrier-only architecture](https://dtronix.github.io/Quarry/articles/diagnostics.html)** — generated carrier classes hold pre-built SQL, parameters, and conditional dispatch; non-analyzable chains are compile errors
- **[Execution interceptors](https://dtronix.github.io/Quarry/articles/querying.html)** — all terminal methods intercepted with pre-built SQL, ordinal-based readers, and pre-allocated parameter arrays
- **[Conditional branch support](https://dtronix.github.io/Quarry/articles/querying.html)** — `if`/`else` query construction emits up to 256 SQL variants dispatched by bitmask at zero runtime cost
- **[Prepared queries](https://dtronix.github.io/Quarry/articles/prepared-queries.html)** — `.Prepare()` on all builder types (select, join, insert, update, delete) for reusable multi-terminal execution
- **[Zero-allocation readers](https://dtronix.github.io/Quarry/articles/querying.html)** — ordinal-based `Func<DbDataReader, T>` delegates generated at compile time
- **[Multi-dialect support](https://dtronix.github.io/Quarry/articles/switching-dialects.html)** — SQLite, PostgreSQL, MySQL, SQL Server with correct quoting, parameters, pagination, and identity syntax
- **[Type-safe schema DSL](https://dtronix.github.io/Quarry/articles/schema-definition.html)** — columns as expression-bodied properties; no attributes, no conventions, no runtime model building
- **[Navigation subqueries](https://dtronix.github.io/Quarry/articles/querying.html)** — `Any()`, `All()`, `Count()` on `Many<T>` properties compile to correlated EXISTS/COUNT subqueries
- **[Custom type mappings](https://dtronix.github.io/Quarry/articles/schema-definition.html)** — `TypeMapping<TClr, TDb>` with optional `IDialectAwareTypeMapping` for dialect-specific SQL types
- **[Migrations](https://dtronix.github.io/Quarry/articles/migrations.html)** — code-first migrations with bundles, seed data, views/stored procedures, squash, checksums, and runtime hooks
- **[Scaffolding](https://dtronix.github.io/Quarry/articles/scaffolding.html)** — reverse-engineer existing databases into schema classes and a context
- **[Query diagnostics](https://dtronix.github.io/Quarry/articles/diagnostics.html)** — `ToDiagnostics()` surfaces SQL, parameters, variants, projection metadata, and carrier class info
- **[Structured logging](https://dtronix.github.io/Quarry/articles/logging.html)** — Logsmith Abstraction mode with categories, slow query detection, sensitive redaction, and operation correlation
- **[Analyzer rules](https://dtronix.github.io/Quarry/articles/analyzer-rules.html)** — compile-time QRA diagnostics with code fixes
- **[Benchmarks](https://dtronix.github.io/Quarry/articles/benchmarks.html)** — comprehensive BenchmarkDotNet suite comparing Quarry against Raw ADO.NET, Dapper, EF Core, and SqlKata

---

## Installation

```xml
<PackageReference Include="Quarry" Version="*" />
```

Enable interceptors by adding your context's namespace to `InterceptorsNamespaces` in your `.csproj`:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```

Replace `MyApp.Data` with the namespace containing your `QuarryContext` subclass. If your context has no namespace, use `Quarry.Generated`.

Optional: add compile-time query analysis rules:

```xml
<PackageReference Include="Quarry.Analyzers" Version="*"
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
    .Select(u => (u.UserName, u.Email))
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Limit(10)
    .ExecuteFetchAllAsync();
```

The generator emits an interceptor that replaces the `ExecuteFetchAllAsync` call with pre-built SQL and a typed reader. No runtime translation occurs.

---

## Samples

| Sample | Description |
|--------|-------------|
| [`Quarry.Sample.WebApp`](https://github.com/Dtronix/Quarry/tree/master/src/Samples/Quarry.Sample.WebApp) | ASP.NET Core Razor Pages app with SQLite — demonstrates schema definition, context setup, querying, authentication, migrations, and Logsmith logging integration |
