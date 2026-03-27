# Getting Started

This guide walks you through installing Quarry and running your first compile-time query.

## Prerequisites

Quarry targets .NET 10. Before you begin, make sure you have:

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later installed.
- A project targeting `net10.0` (set `<TargetFramework>net10.0</TargetFramework>` in your `.csproj`).
- A SQL database you want to query. Quarry supports SQLite, PostgreSQL, MySQL, and SQL Server.

## Installation

Add the Quarry package to your project. The source generator (`Quarry.Generator`) is included automatically:

```xml
<PackageReference Include="Quarry" Version="1.0.0" />
```

Enable interceptors by adding your context's namespace to `InterceptorsNamespaces` in your `.csproj`. The generator emits C# 12 interceptor methods into this namespace, which is how it replaces your terminal calls with pre-built SQL at compile time:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```

Replace `MyApp.Data` with the namespace containing your `QuarryContext` subclass. If your context has no namespace, use `Quarry.Generated`.

### Optional: Quarry.Analyzers

The `Quarry.Analyzers` package adds a set of compile-time analysis rules (the QRA series) that catch common mistakes and suggest improvements to your query code. These rules run alongside the generator during compilation and provide warnings, errors, and code fixes directly in your IDE.

Examples of what the analyzers detect:

- Simplification opportunities (e.g., redundant `.Where(u => true)` clauses).
- Wasteful patterns (e.g., selecting all columns when only one is used).
- Performance concerns (e.g., missing pagination on large result sets).
- Dialect-specific issues (e.g., unsupported features for the target database).

To install:

```xml
<PackageReference Include="Quarry.Analyzers" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

## Define a Schema

A schema class describes the shape of a database table. Quarry reads this class at compile time -- it never executes it. Each property declares a column with its type and optional modifiers.

Create a class inheriting from `Schema`:

```csharp
public class UserSchema : Schema
{
    // Maps this schema to the "users" table in the database.
    public static string Table => "users";

    // Primary key column with auto-increment (IDENTITY / AUTOINCREMENT).
    public Key<int> UserId => Identity();

    // VARCHAR(100) column. Length() sets the max length for string columns.
    public Col<string> UserName => Length(100);

    // Nullable column. The ? on string? makes it NULL in the database.
    public Col<string?> Email { get; }

    // Column with a default value. The generator emits DEFAULT in DDL
    // and omits this column from INSERT when not explicitly set.
    public Col<bool> IsActive => Default(true);
}
```

The generator uses the schema to determine column names, types, nullability, and relationships. It also generates a corresponding entity class (`User`) with matching properties that you use in queries and results.

**Column types at a glance:** `Key<T>` declares a primary key, `Col<T>` a standard column, `Ref<TSchema, TKey>` a foreign key, and `Many<T>` a one-to-many navigation for subqueries.

## Define a Context

The context is your entry point for all database operations. It declares which entities are available and which SQL dialect to target. The generator fills in the implementation at build time.

Create a partial class extending `QuarryContext`:

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
```

Each `partial` method returns an `IEntityAccessor<T>`, which is the starting point for building queries, inserts, updates, and deletes against that table. The `Dialect` property controls how SQL is quoted, parameterized, and paginated. Supported dialects are `SQLite`, `PostgreSQL`, `MySQL`, and `SqlServer`.

You can define multiple contexts with different dialects in the same project. The generator resolves which context applies at each call site and emits dialect-correct SQL for each one.

## Write Your First Query

```csharp
await using var db = new AppDb(connection);

var activeUsers = await db.Users()
    .Select(u => new { u.UserName, u.Email })
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Limit(10)
    .ExecuteFetchAllAsync();
```

Every method in the chain is a compile-time instruction, not a runtime operation. The generator analyzes the entire chain -- projection, filter, ordering, pagination -- and produces a single SQL string literal. `ExecuteFetchAllAsync` is the terminal method; the generator intercepts it and replaces the call with pre-built SQL execution and a typed reader. No SQL is built or translated at runtime.

## What Happens at Build Time

When you build your project, the Roslyn incremental source generator runs through your code and produces several artifacts:

1. **Entity classes.** For each schema (e.g., `UserSchema`), the generator emits a corresponding entity class (`User`) with typed properties matching the schema columns. Foreign keys become `EntityRef<TEntity, TKey>` properties.

2. **Context partials.** The generator fills in the partial methods on your `QuarryContext` subclass, wiring up entity accessors, insert/update/delete entry points, and the `MigrateAsync` method.

3. **Interceptor methods.** For every terminal call site (e.g., `ExecuteFetchAllAsync`, `ExecuteNonQueryAsync`, `ToDiagnostics`), the generator emits a static method annotated with `[InterceptsLocation]`. This method contains:
   - The SQL query as a **string literal** -- no runtime builder, no expression tree translation.
   - A **typed reader delegate** that maps `DbDataReader` columns to your result type by ordinal, without reflection or dictionary lookups.
   - **Pre-allocated parameter arrays** for any captured variables in your lambdas.

4. **Carrier classes.** Queries with conditional branches (e.g., `if`/`else` around `.Where()`) are compiled into carrier classes that hold all SQL variants as const strings. At runtime, a bitmask selects the correct variant -- still no SQL construction.

The result is that your application ships with all SQL pre-built. The hot path is a switch on a bitmask, a parameterized `DbCommand`, and an ordinal-based reader. This makes Quarry fully compatible with NativeAOT and eliminates the runtime cost of SQL generation.

## Verify Generated SQL

Use `ToDiagnostics()` to inspect the SQL that the generator produced for a query. This is useful during development to confirm the generated output matches your expectations:

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .ToDiagnostics();

Console.WriteLine(diag.Sql);
// SELECT "UserId", "UserName", "Email", "IsActive" FROM "users" WHERE "IsActive" = 1
```

`ToDiagnostics()` is itself an intercepted terminal, so the SQL it returns is exactly the SQL that `ExecuteFetchAllAsync` would execute.

## Next Steps

- [Schema Definition](schema-definition.md) -- column types, modifiers, indexes, foreign keys, naming styles
- [Context Definition](context-definition.md) -- dialects, multiple contexts, interceptor namespaces
- [Querying](querying.md) -- joins, aggregates, subqueries, conditional branches, pagination
- [Modifications](modifications.md) -- insert, update, delete, batch insert
- [Prepared Queries](prepared-queries.md) -- compile a chain once, execute multiple ways
- [Migrations](migrations.md) -- code-first migration scaffolding and runtime application
- [Scaffolding](scaffolding.md) -- reverse-engineer an existing database into schema classes
- [Diagnostics](diagnostics.md) -- query diagnostics, SQL inspection, clause breakdown
- [Logging](logging.md) -- structured logging, slow query detection, sensitive parameter redaction
- [Analyzer Rules](analyzer-rules.md) -- compile-time query analysis rules and code fixes
