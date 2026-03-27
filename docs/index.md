---
_layout: landing
---

<div style="text-align: center; padding: 2rem 0;">
  <img src="images/logo-128.png" alt="Quarry" style="height: 96px; margin-bottom: 1rem;" />
  <h1 style="margin-bottom: 0.25rem;">Quarry</h1>
  <p style="font-size: 1.25rem; color: #666; max-width: 640px; margin: 0 auto;">
    Type-safe SQL builder for .NET 10. Write C# — the compiler emits SQL. Zero reflection. AOT compatible.
  </p>
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

---

## Explore the Documentation

<div class="row" style="margin-top: 1rem;">
<div class="col-md-6">

### [Getting Started](articles/getting-started.md)
Install Quarry and write your first compile-time query in minutes.

### [Schema Definition](articles/schema-definition.md)
Define tables as C# classes with typed column properties.

### [Switching Dialects](articles/switching-dialects.md)
Change one enum value to retarget your entire project to a different database.

### [Querying](articles/querying.md)
Select, filter, join, aggregate — all compiled to SQL at build time.

### [Prepared Queries](articles/prepared-queries.md)
Compile once, execute multiple ways with zero overhead.

</div>
<div class="col-md-6">

### [Modifications](articles/modifications.md)
Insert, update, and delete with initializer-aware compile-time analysis.

### [Migrations](articles/migrations.md)
Code-first migration scaffolding via the CLI tool.

### [Diagnostics](articles/diagnostics.md)
Inspect generated SQL, parameters, and optimization metadata.

### [API Reference](api/index.md)
Auto-generated reference for all public types and methods.

</div>
</div>
