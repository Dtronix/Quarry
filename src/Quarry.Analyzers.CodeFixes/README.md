# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry.Analyzers.CodeFixes

Code fix providers for Quarry analyzer diagnostics. Adds IDE lightbulb actions that automatically rewrite flagged query patterns.

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

<PackageReference Include="Quarry.Analyzers.CodeFixes" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

Both packages are required. `Quarry.Analyzers` reports diagnostics; `Quarry.Analyzers.CodeFixes` provides the IDE lightbulb fixes that act on them.

---

## Available Code Fixes

Three diagnostics currently have automatic code fixes:

| Diagnostic | Fix Title | Transformation |
|------------|-----------|----------------|
| QRA101 | Replace Count() comparison with Any() | Rewrites `Count() > 0` to `Any()`, `Count() == 0` to `!Any()`. Handles async variants. |
| QRA102 | Replace single-value Contains with == | Rewrites `new[] { x }.Contains(col)` to `col == x`. |
| QRA201 | Remove unused join | Removes the `.Join(...)` call from the query chain, preserving the receiver. |

All three support Fix All in Document / Project / Solution via the IDE lightbulb menu.

---

## All Analyzer Rules (QRA Series)

The full `Quarry.Analyzers` rule set is listed below. Rules marked with a code fix have automatic IDE rewrites provided by this package.

### QRA1xx — Simplification

| ID | Title | Severity | Code Fix | Description |
|----|-------|----------|----------|-------------|
| QRA101 | Count compared to zero | Info | Yes | `Count() > 0` or `Count() == 0` can be replaced with `Any()`. |
| QRA102 | Single-value IN clause | Info | Yes | `IN (@p0)` with one value simplifies to `=`. |
| QRA103 | Tautological condition | Info | | Always-true conditions like `1 = 1` or `col = col`. |
| QRA104 | Contradictory condition | Info | | Always-false conditions like `x > 5 AND x < 3`. |
| QRA105 | Redundant condition | Info | | A condition subsumed by a stronger one (e.g., `x > 5 AND x > 3`). |
| QRA106 | Nullable without null check | Info | | Nullable column compared with `==` without `IS NULL` / `IS NOT NULL` handling. |

### QRA2xx — Wasted Work

| ID | Title | Severity | Code Fix | Description |
|----|-------|----------|----------|-------------|
| QRA201 | Unused join | Warning | Yes | Joined table not referenced in SELECT, WHERE, or ORDER BY. |
| QRA202 | Wide table SELECT * | Info | | Full-entity projection on a table exceeding the column threshold. |
| QRA203 | ORDER BY without LIMIT | Info | | Sorting without pagination on an unbounded result set. |
| QRA204 | Duplicate projection column | Info | | Same column projected multiple times in SELECT. |
| QRA205 | Cartesian product | Warning | | JOIN with missing or trivial (`1 = 1`) ON condition. |

### QRA3xx — Performance

| ID | Title | Severity | Code Fix | Description |
|----|-------|----------|----------|-------------|
| QRA301 | Leading wildcard LIKE | Info | | `Contains()` becomes `LIKE '%...%'`, preventing index usage. |
| QRA302 | Function on column in WHERE | Info | | `LOWER()`, `UPPER()`, `SUBSTRING()`, etc. on a column prevents index usage. |
| QRA303 | OR across different columns | Info | | `col1 = x OR col2 = y` prevents single-index scan. |
| QRA304 | WHERE on non-indexed column | Info | | Filtering on a column without a declared index. |

### QRA4xx — Patterns

| ID | Title | Severity | Code Fix | Description |
|----|-------|----------|----------|-------------|
| QRA401 | Query inside loop | Warning | | Query execution inside `for`/`foreach`/`while` or LINQ `.Select()` (N+1 risk). |
| QRA402 | Multiple queries on same table | Info | | Multiple independent queries on the same entity in one method. |

### QRA5xx — Dialect

| ID | Title | Severity | Code Fix | Description |
|----|-------|----------|----------|-------------|
| QRA501 | Dialect-specific optimization | Info | | Dialect has a better alternative (e.g., PostgreSQL `ILIKE` instead of `LOWER() + LIKE`). |
| QRA502 | Suboptimal for dialect | Warning | | Feature unsupported or invalid for the target dialect (e.g., SQLite `RIGHT JOIN`, SQL Server `OFFSET` without `ORDER BY`). |

---

## Examples

### QRA101 — Count to Any

```csharp
// Before (flagged by QRA101)
var hasOrders = await db.Users
    .Where(u => u.Orders.Count() > 0)
    .ExecuteFetchAllAsync();

// After fix applied
var hasOrders = await db.Users
    .Where(u => u.Orders.Any())
    .ExecuteFetchAllAsync();
```

### QRA102 — Single-value IN to Equals

```csharp
// Before (flagged by QRA102)
db.Users().Where(u => new[] { 42 }.Contains(u.UserId));

// After fix applied
db.Users().Where(u => u.UserId == 42);
```

### QRA201 — Remove Unused Join

```csharp
// Before (flagged by QRA201)
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => u.UserName);  // 'o' never used

// After fix applied
db.Users()
    .Select(u => u.UserName);
```

### QRA401 — Query Inside Loop (N+1)

```csharp
// Flagged by QRA401
foreach (var id in userIds)
{
    var user = await db.Users
        .Where(u => u.Id == id)
        .ExecuteFetchFirstAsync();
}

// Preferred: batch query
var users = await db.Users
    .Where(u => userIds.Contains(u.Id))
    .ExecuteFetchAllAsync();
```

### QRA205 — Cartesian Product

```csharp
// Flagged by QRA205 — missing ON condition
db.Users().Join<Order>()
    .Select((u, o) => (u.Name, o.Total));

// Fixed: add join condition
db.Users().Join<Order>((u, o) => u.Id == o.UserId)
    .Select((u, o) => (u.Name, o.Total));
```

---

## Configuration

### Severity overrides via `.editorconfig`

```ini
# Disable a specific rule
dotnet_diagnostic.QRA301.severity = none

# Escalate to warning
dotnet_diagnostic.QRA103.severity = warning
```

### Rule-specific settings

```ini
# QRA202: column threshold for wide-table detection (default: 10)
quarry_analyzers.wide_table_column_count = 15
```

### Inline suppression

```csharp
#pragma warning disable QRA301
var results = db.Users().Where(u => u.Name.Contains(term));
#pragma warning restore QRA301
```
