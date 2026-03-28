# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry.Analyzers

Compile-time SQL query analysis rules for Quarry. 19 Roslyn diagnostics detect performance issues, wasteful patterns, and dialect-specific problems in Quarry query call sites. Analyzers run against the interceptor call sites discovered by `Quarry.Generator` and evaluate the rendered SQL at compile time.

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
<PackageReference Include="Quarry.Analyzers" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

Requires `Quarry` and `Quarry.Generator` to be referenced in the same project.

To also get automatic code fixes, add the CodeFixes package:

```xml
<PackageReference Include="Quarry.Analyzers.CodeFixes" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

---

## How It Works

Quarry.Analyzers hooks into the Roslyn compilation pipeline alongside `Quarry.Generator`. When the generator discovers interceptor call sites (`.Where()`, `.Join()`, `.Select()`, etc.), the analyzer evaluates the rendered SQL expression for each site and runs all 19 diagnostic rules against it.

All analysis happens at compile time — no runtime overhead.

---

## Diagnostic Rules

All rules are enabled by default. Suppress individual rules via `#pragma`, `.editorconfig`, or `[SuppressMessage]`.

### QRA1xx — Simplification

| ID | Title | Severity | What it detects |
|----|-------|----------|-----------------|
| QRA101 | Count compared to zero | Info | `Count() > 0`, `Count() == 0` — use `Any()` instead |
| QRA102 | Single-value IN clause | Info | `IN (@p0)` with one value — simplify to `==` |
| QRA103 | Tautological condition | Info | Always-true conditions (`1 = 1`, `col = col`) |
| QRA104 | Contradictory condition | Info | Always-false conditions (`x > 5 AND x < 3`) |
| QRA105 | Redundant condition | Info | Subsumed conditions (`x > 5 AND x > 3`) |
| QRA106 | Nullable without null check | Info | Nullable column in `==` comparison without null handling |

### QRA2xx — Wasted Work

| ID | Title | Severity | What it detects |
|----|-------|----------|-----------------|
| QRA201 | Unused join | Warning | Joined table not referenced in SELECT, WHERE, or ORDER BY |
| QRA202 | Wide table SELECT * | Info | `Select(u => u)` on tables exceeding column threshold |
| QRA203 | ORDER BY without LIMIT | Info | Sorting without pagination on unbounded result sets |
| QRA204 | Duplicate projection column | Info | Same column projected multiple times in SELECT |
| QRA205 | Cartesian product | Warning | JOIN with missing or trivial ON condition (`1 = 1`) |

### QRA3xx — Performance

| ID | Title | Severity | What it detects |
|----|-------|----------|-----------------|
| QRA301 | Leading wildcard LIKE | Info | `Contains()` -> `LIKE '%...%'` prevents index usage |
| QRA302 | Function on column in WHERE | Info | `LOWER()`, `UPPER()`, `TRIM()`, etc. on columns in WHERE |
| QRA303 | OR across different columns | Info | `col1 = x OR col2 = y` prevents single-index scan |
| QRA304 | WHERE on non-indexed column | Info | Filter on column not covered by any declared index |

### QRA4xx — Patterns

| ID | Title | Severity | What it detects |
|----|-------|----------|-----------------|
| QRA401 | Query inside loop | Warning | Execution method inside `for`/`foreach`/`while`/LINQ — N+1 risk |
| QRA402 | Multiple queries on same table | Info | Multiple independent queries on the same entity in one method |

### QRA5xx — Dialect

| ID | Title | Severity | What it detects |
|----|-------|----------|-----------------|
| QRA501 | Dialect optimization available | Info | PostgreSQL: suggest `ILIKE` over `LOWER() + LIKE`; SQLite: suggest `COLLATE NOCASE` |
| QRA502 | Suboptimal for dialect | Warning | SQLite: `RIGHT JOIN` unsupported; SQL Server: `OFFSET` requires `ORDER BY` |

---

## Code Fixes

Three rules include automatic code fix providers (via `Quarry.Analyzers.CodeFixes`). These appear as lightbulb suggestions in your IDE:

| ID | Fix | What it does |
|----|-----|--------------|
| QRA101 | Count to Any | Rewrites `Count() > 0` to `Any()` and `Count() == 0` to `!Any()`. Handles async variants. |
| QRA102 | Single-value IN to Equals | Converts `new[] { x }.Contains(col)` to `col == x`. |
| QRA201 | Remove unused join | Removes the `.Join(...)` call when the joined table is unreferenced. |

---

## Configuration

### EditorConfig

```ini
# .editorconfig
[*.cs]
# Column threshold for QRA202 (wide table SELECT *), default: 10
quarry_analyzers.wide_table_column_count = 12
```

### Suppressing Rules

```ini
# .editorconfig — suppress a rule project-wide
[*.cs]
dotnet_diagnostic.QRA203.severity = none
```

```csharp
// Per-site suppression
#pragma warning disable QRA301
var results = await db.Users
    .Where(u => u.UserName.Contains(search))
    .ExecuteFetchAllAsync();
#pragma warning restore QRA301
```

---

## Severity Summary

Four rules default to **Warning** — these indicate likely bugs or significant performance issues:

| ID | Rule |
|----|------|
| QRA201 | Unused join |
| QRA205 | Cartesian product |
| QRA401 | Query inside loop (N+1) |
| QRA502 | Suboptimal for dialect |

The remaining 15 rules default to **Info** — suggestions that may or may not apply depending on context.

---

## Usage Examples

Analyzers fire automatically on Quarry query chains. No configuration is needed beyond installation.

```csharp
// QRA101 fires: "Use Any() instead of Count() > 0"
var hasUsers = await db.Users()
    .Where(u => u.IsActive)
    .ExecuteCountAsync() > 0;

// Fix: use Any()
var hasUsers = await db.Users()
    .Where(u => u.IsActive)
    .ExecuteAnyAsync();
```

```csharp
// QRA201 fires: "Joined table 'Orders' is not referenced in SELECT, WHERE, or ORDER BY"
var users = await db.Users()
    .Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => u.UserName)
    .ExecuteFetchAllAsync();
```

```csharp
// QRA401 fires: "Query execution inside loop — potential N+1 query"
foreach (var userId in userIds)
{
    var orders = await db.Orders()
        .Where(o => o.UserId.Id == userId)
        .ExecuteFetchAllAsync();
}
```
