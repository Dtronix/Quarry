# Quarry v0.1.0 — Type-Safe, Compile-Time SQL for .NET 10

_Released 2026-03-13_

Quarry is a SQL builder and query execution library for .NET 10 that moves SQL generation from runtime to **compile time**. Using Roslyn source generators and C# interceptors, Quarry emits pre-built SQL, typed data readers, and parameter bindings directly into your assembly — no reflection, no expression trees, no runtime surprises.

This initial release targets **SQLite, PostgreSQL, MySQL, and SQL Server** with a unified, fluent C# API.

---

## Why Quarry?

Most .NET data access falls into two camps: full ORMs (EF Core) that hide SQL behind abstractions, or micro-ORMs (Dapper) that give you raw SQL strings with no compile-time safety. Quarry occupies the middle ground:

- **SQL is generated at build time**, not at runtime — if it compiles, the SQL is valid.
- **Zero-reflection hot path** — intercepted queries use ordinal-based readers with pre-allocated parameter arrays.
- **NativeAOT compatible** — no expression trees, no dynamic codegen, no reflection emit.
- **Multi-dialect from a single codebase** — write once, target four databases with correct quoting, pagination, and identity syntax.
- **Conditional branch analysis** — `if`/`else` query building is fully enumerated into a dispatch table at compile time.

## Three Optimization Tiers

Quarry's generator analyzes every query call site and selects the best strategy:

| Tier | Strategy | When | Runtime Cost |
|------|----------|------|--------------|
| **1** | Pre-built dispatch table | ≤4 conditional branches (≤16 SQL variants) | Near-zero — const string lookup |
| **2** | Pre-quoted fragment concat | >4 conditional branches | Minimal string concatenation |
| **3** | Runtime `SqlBuilder` | Dynamically constructed queries | Traditional builder overhead |

> **Note:** Tier 3 and the runtime `SqlBuilder` fallback were removed in v0.2.0 in favor of the carrier-only architecture. In v0.2.0+, non-analyzable chains are compile errors rather than silent fallbacks.

---

## Packages

### `Quarry` — Core Runtime

The runtime library containing the schema DSL, query builders, modification builders, dialect support, migration runner, and execution infrastructure.

**Install this if:** You are building an application that talks to a relational database and want type-safe, compile-time SQL generation with a fluent C# API. This is the required foundation — every Quarry project needs it.

```
dotnet add package Quarry
```

### `Quarry.Generator` — Source Generator

A Roslyn incremental source generator that runs at build time. It discovers your `Schema` classes and `QuarryContext`, then emits entity classes, column metadata, and interceptor methods containing pre-built SQL and typed readers.

**Install this if:** You are using the `Quarry` package (you almost certainly want this). Without it, queries fall back to Tier 3 runtime building. With it, you get Tier 1/2 compile-time SQL, zero-reflection readers, and build-time diagnostics (QRY001–QRY055).

```
dotnet add package Quarry.Generator
```

### `Quarry.Analyzers` — Query Linter

18 Roslyn analyzers (QRA series) that detect performance anti-patterns, wasted work, and dialect-specific issues in your Quarry query code at compile time.

**Install this if:** You want IDE-integrated feedback on query quality — catching N+1 loops (QRA401), unused joins (QRA201), leading-wildcard LIKE patterns (QRA301), Cartesian products (QRA205), and dialect-specific pitfalls (QRA501/502). Recommended for all projects.

Highlights:
- **Simplification** (QRA1xx) — `Count() > 0` → `Any()`, tautological/contradictory conditions, redundant filters.
- **Wasted work** (QRA2xx) — unused joins, wide `SELECT *`, ORDER BY without LIMIT.
- **Performance** (QRA3xx) — function-on-column in WHERE, OR across columns, missing indexes.
- **Patterns** (QRA4xx) — query inside loop (N+1), multiple queries on same table.
- **Dialect** (QRA5xx) — suggests `ILIKE` on PostgreSQL, warns about SQLite `RIGHT JOIN`.

```
dotnet add package Quarry.Analyzers
```

### `Quarry.Analyzers.CodeFixes` — Auto-Fix Provider

Automatic code fix actions for a subset of analyzer diagnostics, surfaced as IDE lightbulb suggestions.

**Install this if:** You're using `Quarry.Analyzers` and want one-click fixes for flagged patterns — replacing `Count() > 0` with `Any()`, simplifying single-value `Contains` to `==`, and removing unused joins.

```
dotnet add package Quarry.Analyzers.CodeFixes
```

### `Quarry.Tool` — CLI for Migrations & Scaffolding

A `dotnet tool` providing code-first migration scaffolding and database-first reverse engineering.

**Install this if:** You need to evolve your database schema over time (migrations) or bootstrap a Quarry project from an existing database (scaffolding). Not required for query-only usage.

```
dotnet tool install Quarry.Tool
```

Key commands:
- `quarry migrate add <name>` — diff your schemas against the last snapshot and generate a migration.
- `quarry migrate list` / `validate` — inspect and validate migration chains.
- `quarry scaffold` — reverse-engineer an existing database into `Schema` classes and a `QuarryContext`.
- `quarry create-scripts` — emit full DDL from your current schemas.

Supports interactive rename detection (Levenshtein-based), risk classification (safe/cautious/destructive), dialect-specific warnings (e.g., SQLite ALTER limitations), and backup generation for destructive operations.

---

## Quick Look

### Define a Schema

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";
    protected override NamingStyle Naming => NamingStyle.SnakeCase;

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);

    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);

    public Index IX_Email => Index(Email).Unique();
}
```

No attributes, no conventions, no runtime model building — the generator reads the syntax tree directly.

### Query with Compile-Time SQL

```csharp
[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial QueryBuilder<User> Users { get; }
    public partial QueryBuilder<Order> Orders { get; }
}

// Simple query — Tier 1: SQL is a const string in the compiled assembly
var activeUsers = await db.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Select(u => (u.UserId, u.UserName))
    .ExecuteFetchAllAsync();

// Conditional building — generator enumerates all branch combinations
var query = db.Users.Select(u => u);
if (nameFilter != null)
    query = query.Where(u => u.UserName == nameFilter);
if (sortByName)
    query = query.OrderBy(u => u.UserName);
var results = await query.Limit(50).ExecuteFetchAllAsync();
```

> **Note:** In v0.2.0+, accessors moved from properties (`db.Users`) to methods (`db.Users()`), and `QueryBuilder<T>` was removed. See the v0.2.0 migration guide.

### Modify Data

```csharp
// Insert — only explicitly set properties generate columns
var id = await db.Insert(new User { UserName = "alice", IsActive = true })
    .ExecuteScalarAsync<int>();

// Update with safety guard (requires Where or All)
await db.Update<User>()
    .Set(u => u.IsActive, false)
    .Where(u => u.UserId == id)
    .ExecuteNonQueryAsync();

// Delete
await db.Delete<User>().Where(u => u.UserId == id).ExecuteNonQueryAsync();
```

---

## How It Compares

| Capability | Quarry | EF Core | Dapper |
|---|---|---|---|
| SQL generated at compile time | Yes | No | No |
| Reflection-free hot path | Yes | No | Partial |
| NativeAOT compatible | Yes | Partial | Partial |
| Compile-time diagnostics | 55+ rules | Limited | None |
| Type-safe schema definition | Yes | Yes | No |
| Multi-dialect (single codebase) | 4 dialects | Yes | Manual |
| Conditional branch analysis | Yes | No | No |
| Navigation subqueries | Yes | Yes | No |
| Code-first migrations | Yes | Yes | No |
| Database scaffolding | Yes | Yes | No |
| Change tracking | No | Yes | No |
| Join limit | 4 tables | Unlimited | Manual |

---

## Supported Databases

| Database | Dialect Enum | Quoting | Pagination | Identity |
|---|---|---|---|---|
| SQLite | `SqlDialect.SQLite` | `"identifier"` | LIMIT/OFFSET | `last_insert_rowid()` |
| PostgreSQL | `SqlDialect.PostgreSQL` | `"identifier"` | LIMIT/OFFSET | RETURNING |
| MySQL | `SqlDialect.MySQL` | `` `identifier` `` | LIMIT/OFFSET | `LAST_INSERT_ID()` |
| SQL Server | `SqlDialect.SqlServer` | `[identifier]` | OFFSET/FETCH | `SCOPE_IDENTITY()` |

---

## Getting Started

```bash
# Add the core package and generator
dotnet add package Quarry
dotnet add package Quarry.Generator

# (Recommended) Add analyzers
dotnet add package Quarry.Analyzers
dotnet add package Quarry.Analyzers.CodeFixes

# (Optional) Install the CLI tool for migrations/scaffolding
dotnet tool install Quarry.Tool
```
