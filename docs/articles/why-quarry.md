# Why Quarry

Quarry is a compile-time SQL builder for .NET. A Roslyn source generator analyzes your query
chains at build time and emits all SQL as string literals -- no runtime translation, no
reflection on the hot path, no fallback. This article explains what that design gives you
and where the trade-offs are.

## The Core Idea

Most data-access libraries generate SQL at runtime. Quarry moves that work to compile time.

The source generator reads your C# query chains during the build, produces the SQL for every
reachable code path, and emits interceptors that replace your terminal calls with pre-built
execution. By the time your application starts, every query is a string constant in the
assembly.

| | Quarry |
|---|---|
| SQL generation | Compile time -- string literals in the assembly |
| Object mapping | Source-generated ordinal-based readers |
| NativeAOT | Production-ready, verified end-to-end |
| Conditional queries | Bitmask dispatch over pre-built SQL variants |
| Non-analyzable queries | Compile error (QRY032) -- no silent degradation |

---

## Designed for NativeAOT

Quarry was built for AOT from the start. There is no separate publish step, no experimental
opt-in, and no feature gap between AOT and non-AOT builds. The same source generator that
runs on every build produces the same interceptors regardless of how you publish.

This means:

- **No reflection on the hot path.** All column mapping uses ordinal-based `DbDataReader`
  access. Reader delegates are generated at compile time, not emitted via `Reflection.Emit`.
- **No runtime SQL construction.** SQL is a string literal. Parameters are pre-allocated
  arrays. There is nothing to build at runtime.
- **No AOT-specific setup.** The same `.csproj` and the same NuGet package work for regular
  and AOT builds. Add `<PublishAot>true</PublishAot>` when you are ready to publish -- nothing
  else changes.
- **All four dialects work under AOT.** SQLite, PostgreSQL, MySQL, and SQL Server are
  supported out of the box with no additional configuration.

The [AOT sample project](https://github.com/Dtronix/Quarry/tree/master/src/Samples/Quarry.Sample.Aot)
verifies 15 scenarios end-to-end under `<PublishAot>true</PublishAot>`, including joins,
navigation subqueries, custom type mappings, and projections.

### Inner-Loop Developer Experience

Because Quarry uses an incremental Roslyn source generator, interceptors are regenerated on
every keystroke in the IDE. You see updated SQL, diagnostics, and compile errors in real time
-- the same experience whether you plan to publish with AOT or not. There is no separate
generation step to run before publishing.

---

## Conditional Queries as a First-Class Feature

Real applications build queries conditionally. A search endpoint might filter by status, date
range, keyword, or any combination. Quarry's generator understands `if`/`else` blocks around
query clauses and pre-builds every SQL variant at compile time.

```csharp
var q = db.Orders().OrderBy(o => o.OrderId);

if (filterByStatus)
    q = q.Where(o => o.Status == status);

if (filterByDate)
    q = q.Where(o => o.CreatedAt >= startDate);

var results = await q.Select(o => o).ExecuteFetchAllAsync();
```

Under the hood, the generator produces a carrier class with a `string[]` containing all four
SQL variants (no filter, status only, date only, both). At runtime, the active conditions set
bits in a mask, and `_sql[mask]` selects the correct pre-built SQL. Zero string operations.
Zero runtime SQL construction.

This scales to 8 conditional clauses (256 variants) with the same mechanism. The runtime cost
is a single integer index into an array -- the same cost regardless of how many conditions
are active.

For context, conditionally composed queries are a common challenge for AOT-oriented
frameworks. Approaches that rely on static analysis at publish time typically require each
combination to be written as a separate, fully-composed expression -- two conditions become
four copies, three become eight. Quarry's bitmask approach avoids that duplication entirely.

---

## Compile-Time Safety

If the generator cannot fully analyze a query chain, it emits **compile error QRY032** rather
than falling back to a runtime path. Your build fails with a clear diagnostic instead of
producing an application that silently degrades.

```csharp
// QRY032: the builder escapes its scope, so the generator
// cannot determine the final SQL at compile time.
IQueryBuilder<User> BuildQuery(AppDb db)
{
    return db.Users().Where(u => u.IsActive);  // error QRY032
}
```

The fix is always to keep the query chain in a single analyzable scope -- the same constraint
that makes the SQL deterministic at compile time.

Beyond QRY032, the generator enforces 40+ compile-time diagnostics covering missing table
definitions, invalid column types, unmapped projections, unsupported operators, undefined
joins, aggregates without `GroupBy`, navigation misconfiguration (`QRY060`–`065`), set-op
projection mismatches (`QRY070`–`072`), and CTE misuse (`QRY080`–`082`), among others. The
optional `Quarry.Analyzers` package adds another 19 rules covering simplification
opportunities, wasteful patterns, performance concerns, and dialect-specific issues. The
optional `Quarry.Migration` package adds 12 `QRM` diagnostics that power automated conversion
from Dapper, EF Core, ADO.NET, and SqlKata. See [Analyzer Rules](analyzer-rules.md) for the
full list.

---

## Performance

The overhead is low because there is very little for Quarry to do at runtime. The hot path is:

1. Read a pre-built SQL string literal.
2. Bind pre-allocated parameters.
3. Execute the command.
4. Read results by ordinal using a generated reader delegate.

This is the same work hand-written ADO.NET does. There are no expression trees to walk, no
SQL to compile, no reflection to perform, and no model to initialize on first use. Cold start
is near-zero because the SQL and reader delegates are embedded in the assembly at build time
-- no model compilation, no IL emission, no cache priming.

For benchmark results across 23 categories comparing Quarry against Raw ADO.NET, Dapper,
EF Core, and SqlKata, see the [Benchmarks](benchmarks.md) article.

---

## Trade-Offs

Quarry is a compile-time SQL builder, not an ORM. That distinction comes with a different set
of trade-offs:

| Capability | Available in Quarry |
|---|---|
| Compile-time SQL generation | Yes |
| NativeAOT support | Yes |
| Code-first migrations | Yes |
| Database scaffolding | Yes |
| Cross-ORM conversion (Dapper / EF Core / ADO.NET / SqlKata → Quarry) | Yes (`quarry convert`) |
| Multi-dialect support | Yes (4 dialects) |
| Conditional branch analysis | Yes (up to 256 variants) |
| Common Table Expressions | Yes (single + multi-CTE) |
| Window functions | Yes (ranking, offset, aggregate-OVER) |
| Set operations | Yes (Union / Intersect / Except, cross-entity) |
| Navigation joins | Yes (`One<T>`, `HasManyThrough`) |
| Navigation subqueries | Yes (`Any`, `All`, `Count`, `Sum`, `Min`, `Max`, `Avg`) |
| SQL manifest emission | Yes (opt-in, per-dialect) |
| Change tracking | No -- explicit insert/update/delete |
| Lazy loading | No -- explicit joins and subqueries |
| Arbitrary `IQueryable` composition | No -- query chain must be in a single analyzable scope |
| Unit of Work pattern | Manual |
| Join limit | Up to 6 tables (explicit); CTEs for more |

Quarry is a good fit when you want predictable performance, AOT compatibility, compile-time
query validation, and minimal runtime overhead. If your application relies on change tracking,
lazy loading, or deeply dynamic query composition that cannot be expressed as conditional
branches, an ORM may be more appropriate.

---

## Coming from EF Core

If you are migrating from EF Core, the core concepts map directly:

| EF Core | Quarry |
|---|---|
| `DbContext` | `QuarryContext` |
| `DbSet<T>` | `IEntityAccessor<T>` (partial method) |
| Entity class with attributes/Fluent API | `Schema` class with typed column properties |
| `context.Users.Where(...)` | `db.Users().Where(...)` |
| `.ToListAsync()` | `.ExecuteFetchAllAsync()` |
| `.FirstOrDefaultAsync()` | `.ExecuteFetchFirstOrDefaultAsync()` |
| `context.SaveChangesAsync()` | Explicit `.Insert()` / `.Update()` / `.Delete()` chains |
| `Add-Migration` | `quarry migrate add` |
| `Update-Database` | `await db.MigrateAsync(connection)` |
| `Scaffold-DbContext` | `quarry scaffold` |

### Key Differences in Practice

**Explicit data operations.** Quarry has no change tracker. You write insert, update, and
delete chains directly, each with its own terminal method. This gives you full visibility into
what SQL executes and when.

**Schema is the source of truth.** The `Schema` class is the single definition for your table
structure. The generator produces the entity class from it -- there is no separate entity
class to keep in sync with a Fluent API configuration.

**Queries are scoped.** The full query chain must be visible in a single method body so the
generator can analyze it at compile time. Conditional branches within that scope are fully
supported. If you have reusable query fragments, the
[Prepared Queries](prepared-queries.md) feature supports compiling a chain once and executing
it multiple ways.

**Explicit relationship loading.** Related data is loaded via explicit joins or navigation
subqueries (`Any`, `All`, `Count` on `Many<T>` properties). There is no transparent proxy
loading -- you always know when a database call will happen.

### Automated Conversion

If your existing codebase is substantial, run `quarry convert --from efcore` over the project
before hand-translating call sites. The [`Quarry.Migration`](https://www.nuget.org/packages/Quarry.Migration)
package ships Roslyn analyzers and IDE code fixes for EF Core, Dapper, ADO.NET, and SqlKata —
it converts the common relational query surface automatically and flags sites it cannot
translate with a `QRM` diagnostic for manual review. See
[Migrating to Quarry](migrating-to-quarry.md) for the full workflow.

### Getting Started

See the [Getting Started](getting-started.md) guide for installation and your first query.
