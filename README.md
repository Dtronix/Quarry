# <img src="./docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Zero runtime dependencies. Structured logging via Logsmith Abstraction mode.

**[Documentation](https://dtronix.github.io/Quarry/)** | **[API Reference](https://dtronix.github.io/Quarry/api/)**

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
| Reflection-free hot path | Yes¹ | No | Partial (AOT mode) | No |
| Zero-allocation query dispatch | Yes (carrier architecture) | No | No | No |
| NativeAOT compatible | Yes | Partial | Partial | No |
| Compile-time diagnostics | Yes | Limited | No | No |
| Compile-time constant inlining | Yes | No | N/A | No |
| No runtime dependencies | Yes | No | Yes | Yes |
| Type-safe schema definition | Yes | Yes (DbContext/model) | No | No |
| Multi-dialect support | Yes (4 dialects) | Yes (providers) | Manual | Yes |
| Join support | Up to 4 tables | Unlimited | Manual | Yes |
| Navigation subqueries | Yes (Any/All/Count) | Yes (full LINQ) | No | No |
| Conditional branch analysis | Yes | No | No | No |
| Database scaffolding | Yes | Yes | No | No |
| Change tracking | No | Yes | No | No |
| Migrations | Yes (code-first, bundles, seed data, views) | Yes | No | No |
| Prepared multi-terminal queries | Yes (all builder types) | No | No | No |

¹ Captured closure variables use a cached `FieldInfo` read per parameter; all SQL dispatch, binding, and row materialization is reflection-free.

---

## Performance

Quarry is benchmarked against Raw ADO.NET, Dapper, EF Core, and SqlKata using [BenchmarkDotNet](https://benchmarkdotnet.org/) on an in-memory SQLite database. All libraries execute the same logical operation so that numbers reflect framework overhead, not network or engine variance.

| | Dapper | **Quarry** | SqlKata | EF Core |
|---|---:|---:|---:|---:|
| **Median speed ratio** | 1.23x | <u>1.01x</u> | 1.69x | 2.47x |
| **Median alloc ratio** | 1.41x | <u>1.08x</u> | 6.45x | 5.23x |

Quarry's median overhead is **1.01x Raw ADO.NET** across 23 benchmarks — faster than Dapper with fewer allocations. See the [full benchmark results](https://dtronix.github.io/Quarry/articles/benchmarks.html) for per-category breakdowns and the [performance tracking issue](https://github.com/Dtronix/Quarry/issues/105) for run-over-run history.

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
    .Select(u => new { u.UserName, u.Email })
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
