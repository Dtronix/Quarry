---
_layout: landing
---

<div style="text-align: center; padding: 0.75rem 0 0.5rem;">
  <img src="images/logo-128.png" alt="Quarry" style="height: 64px; margin-bottom: 0.5rem;" />
  <h1 style="margin-bottom: 0.25rem; font-size: 2rem;">Quarry</h1>
  <p style="font-size: 1.1rem; color: #666; max-width: 640px; margin: 0 auto;">
    Type-safe SQL builder for .NET 10. Write C# — the compiler emits SQL. Zero reflection. AOT compatible.
  </p>
</div>

---

## Explore the Documentation

<div class="row" style="margin-top: 0.5rem;">
<div class="col-md-6">

### [Getting Started](articles/getting-started.md)
Install Quarry and write your first compile-time query in minutes.

### [Schema Definition](articles/schema-definition.md)
Define tables as C# classes with typed column properties.

### [Context Definition](articles/context-definition.md)
Configure your QuarryContext with dialect, schema, and connection settings.

### [Switching Dialects](articles/switching-dialects.md)
Change one enum value to retarget your entire project to a different database.

### [Querying](articles/querying.md)
Select, filter, join, aggregate — all compiled to SQL at build time.

### [Prepared Queries](articles/prepared-queries.md)
Compile once, execute multiple ways with zero overhead.

### [Modifications](articles/modifications.md)
Insert, update, and delete with initializer-aware compile-time analysis.

</div>
<div class="col-md-6">

### [Migrations](articles/migrations.md)
Code-first migration diffing via the CLI tool.

### [Scaffolding](articles/scaffolding.md)
Reverse-engineer an existing database into schema classes.

### [Diagnostics](articles/diagnostics.md)
Inspect generated SQL, parameters, and optimization metadata.

### [Logging](articles/logging.md)
Structured logging with sensitive parameter redaction and slow query detection.

### [Analyzer Rules](articles/analyzer-rules.md)
Compile-time SQL analysis rules and code fixes.

### [Migrating to Quarry](articles/migrating-to-quarry.md)
Migrate from Dapper, EF Core, SqlKata, or raw ADO.NET.

### [SQL Manifest](articles/sql-manifest.md)
Opt-in per-dialect markdown documentation of every generated SQL statement.

### [Benchmarks](articles/benchmarks.md)
Performance comparison against Dapper, EF Core, and SqlKata. See the [live benchmark dashboard](https://dtronix.github.io/Quarry-benchmarks/) for trends across commits.

### [Release Notes](articles/releases/index.md)
Per-version changelogs and migration guides.

</div>
</div>

---

## See It in Action

You write a query in C#:

```csharp
var activeUsers = await db.Users()
    .Select(u => new { u.UserName, u.Email })
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Limit(10)
    .ExecuteFetchAllAsync();
```

At compile time, the source generator emits this SQL as a string literal — no runtime translation:

```sql
SELECT "UserName", "Email" FROM "users" WHERE "IsActive" = 1 ORDER BY "UserName" LIMIT 10
```

---

## Quick Install

```xml
<PackageReference Include="Quarry" Version="1.0.0" />
```

The source generator is included automatically. Enable interceptors in your `.csproj`:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```

---

## Why Quarry?

<div class="row" style="margin-top: 1rem;">
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Compile-Time SQL</h3>
<p>All SQL is generated at build time by a Roslyn source generator. No runtime query building, no surprises.</p>
</div>
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Zero Reflection</h3>
<p>Ordinal-based readers and pre-allocated parameter arrays. Fully NativeAOT compatible.</p>
</div>
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Switch Dialects Instantly</h3>
<p>Change one enum value and rebuild. SQLite, PostgreSQL, MySQL, SQL Server — all SQL re-emits automatically. Run multiple dialects side by side.</p>
</div>
</div>

<div class="row">
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Conditional Branches</h3>
<p>Build queries with <code>if</code>/<code>else</code> — the generator emits all SQL variants and dispatches via bitmask.</p>
</div>
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Type-Safe Schema</h3>
<p>Define tables as C# classes. Columns, foreign keys, indexes, and navigations — all checked at compile time.</p>
</div>
<div class="col-md-4" style="margin-bottom: 1.5rem;">
<h3>Migrations & Scaffolding</h3>
<p>Code-first migration diffing via CLI. Reverse-engineer existing databases into schema classes.</p>
</div>
</div>
