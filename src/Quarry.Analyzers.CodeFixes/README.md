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
<PackageReference Include="Quarry.Analyzers.CodeFixes" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

Requires `Quarry.Analyzers` to be referenced in the same project. The code fixes act on diagnostics reported by the analyzer package.

---

## Available Code Fixes

| Diagnostic | Fix Title | Transformation |
|------------|-----------|----------------|
| QRA101 | Replace Count() comparison with Any() | Rewrites `Count() > 0` to `Any()`, `Count() == 0` to `!Any()`. Handles async variants. |
| QRA102 | Replace single-value Contains with == | Rewrites `new[] { x }.Contains(col)` to `col == x`. |
| QRA201 | Remove unused join | Removes the `.Join(...)` call from the query chain, preserving the receiver. |

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
db.Users.Where(u => new[] { 42 }.Contains(u.UserId));

// After fix applied
db.Users.Where(u => u.UserId == 42);
```

### QRA201 — Remove Unused Join

```csharp
// Before (flagged by QRA201)
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Select((u, o) => u.UserName);  // 'o' never used

// After fix applied
db.Users
    .Select(u => u.UserName);
```
