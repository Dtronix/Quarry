# <img src="../../docs/images/logo-128.png" height="48"> Quarry.Migration

Cross-ORM conversion toolkit for [Quarry](https://github.com/Dtronix/Quarry). Translate Dapper, EF Core, ADO.NET, and SqlKata call sites in existing C# source into equivalent Quarry chain API code. Backs the `quarry convert --from <tool>` CLI and ships Roslyn analyzers with IDE code fixes.

---

## Install

Add as an analyzer-only reference so the Roslyn analyzers run over your project without adding a runtime dependency:

```xml
<PackageReference Include="Quarry.Migration" Version="*"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

For CLI conversion over an entire project, install the `Quarry.Tool` global tool:

```bash
dotnet tool install --global Quarry.Tool
quarry convert --from {dapper|efcore|adonet|sqlkata} --project src/MyApp
```

---

## What It Does

Each source tool has an independent converter pipeline:

1. **`*Detector`** — finds call sites via Roslyn syntactic+semantic analysis. For Dapper, matches `QueryAsync<T>` / `ExecuteAsync` / `QueryFirstAsync<T>` / `QueryFirstOrDefaultAsync<T>` / `QuerySingleAsync<T>` / `ExecuteScalarAsync<T>` and sync variants. For EF Core, matches `Where`/`Select`/`Join`/etc. on `IQueryable<T>`. For ADO.NET, matches `ExecuteReaderAsync` / `ExecuteScalarAsync` / `ExecuteNonQueryAsync` and walks back to the last `CommandText` assignment. For SqlKata, matches `Query()` fluent calls.
2. **SQL parsing** — embedded SQL strings are parsed by the recursive-descent parser in `Quarry.Shared/Sql/Parser/` (tokenizer → AST → walker).
3. **`SchemaResolver`** — resolves table and column names against your Quarry `Schema` classes by introspecting the compilation.
4. **`ChainEmitter`** — walks the SQL AST and emits equivalent chain API code (`db.Users().Where(u => …).Select(u => …).ExecuteFetchAllAsync()`).
5. **`*Converter`** — orchestrates detection → parsing → resolution → emission and returns `IConversionEntry[]` with `IConversionDiagnostic` entries.
6. **`*MigrationAnalyzer` + `*MigrationCodeFix`** — surface each convertible call site in the IDE with a lightbulb fix that replaces the source code in place.

---

## Supported Translations

For all four source tools, the converter covers the common relational query surface:

- `SELECT` with column projections, full entity projections, and DTO construction.
- `WHERE` with operators, `IS NULL`/`IS NOT NULL`, `IN` / `BETWEEN`, `LIKE`.
- Joins: `INNER`, `LEFT`, `RIGHT`, `CROSS`, `FULL OUTER`.
- `GROUP BY` / `HAVING`, aggregates (`COUNT`, `SUM`, `AVG`, `MIN`, `MAX`).
- `ORDER BY` (ascending/descending), `LIMIT` / `OFFSET`.
- `DELETE` and `UPDATE` with matching `WHERE`. DELETE/UPDATE without a `WHERE` emits `.All()` with a warning.
- `INSERT` — emits a TODO comment, since Quarry's `Insert` requires an entity object rather than positional column values. Review manually.
- `WITH … AS (…)` common table expressions — see below.

Constructs that fall outside the converter's grammar are emitted as `Sql.Raw` fragments so the query still runs; the analyzer flags them with a QRM00x-warnings diagnostic so you can review.

### Common Table Expressions

A `WITH` query converts to `.With<…>(…)` plus `.FromCte<T>()`. Quarry names a CTE after the C# type passed to `With<T>`, so the emitted `WITH` name is the type name rather than the original SQL name — harmless, because the outer `FROM` reference changes with it.

A `SELECT *` body reuses the source entity type and needs nothing new:

```sql
WITH recent AS (SELECT * FROM orders WHERE total > 100) SELECT order_id FROM recent
```
```csharp
db.With<Order>(o => o.Where(o => o.Total > 100))
    .FromCte<Order>()
    .Select(o => o.OrderId)
```

A body that projects a subset of columns needs a DTO, which the code fix synthesizes and inserts into the file alongside the rewritten call:

```sql
WITH recent_orders AS (SELECT order_id, total FROM orders) SELECT total FROM recent_orders
```
```csharp
db.With<Order, RecentOrders>(o => o
        .Select(o => new RecentOrders { OrderId = o.OrderId, Total = o.Total }))
    .FromCte<RecentOrders>()
    .Select(r => r.Total)

public class RecentOrders
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}
```

A CTE query is reported not-convertible — rather than converted into something that would produce different SQL — when any of these hold:

- the `WITH` is recursive (the runtime has no recursive `With<>`);
- the outer query reads from a real table instead of a CTE (that shape needs an entity accessor after `With<>()`, which requires the context to derive from `QuarryContext<TSelf>`);
- a CTE is used as a join target (`Join<TCte>` currently resolves against the underlying table, not the CTE);
- a CTE body contains a join, `DISTINCT`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT` or `OFFSET`, or projects an expression rather than plain columns;
- the synthesized DTO name would collide with an existing entity type.

---

## Diagnostic Reference

Each source tool gets a three-code family. The analyzer only activates when the source tool's framework type is present in the compilation, so downstream projects without the source library see no noise.

| Source tool | Detected | With warnings | Not convertible |
|---|---|---|---|
| Dapper | QRM001 (Info) | QRM002 (Warning) | QRM003 (Info) |
| EF Core | QRM011 (Info) | QRM012 (Warning) | QRM013 (Info) |
| ADO.NET | QRM021 (Info) | QRM022 (Warning) | QRM023 (Info) |
| SqlKata | QRM031 (Info) | QRM032 (Warning) | QRM033 (Info) |

All convertible diagnostics ship with an accompanying IDE code fix that replaces the source call site with the converted chain.

---

## CLI Usage

```bash
# Dapper → Quarry
quarry convert --from dapper --project src/MyApp

# EF Core → Quarry (converts DbContext query chains, leaves DbSet definitions alone)
quarry convert --from efcore --project src/MyApp

# ADO.NET → Quarry (detector uses the last CommandText before each Execute* call)
quarry convert --from adonet --project src/MyApp

# SqlKata → Quarry
quarry convert --from sqlkata --project src/MyApp
```

The CLI applies fixes for every call site the converter can translate cleanly, leaving QRM-flagged-not-convertible sites untouched. Review those manually using the per-tool conversion tables in [`llm-migrate.md`](../../llm-migrate.md).

---

## ADO.NET Detector Caveat

The ADO.NET detector uses the **last** `CommandText` assignment before each `Execute*` call and positionally filters `DbParameter` instances assigned to the same command between executions. Reused `DbCommand` variables across multiple execution calls are handled correctly.

Code that heavily mutates a shared `DbCommand` (looped `CommandText` reassignment, cross-method parameter building) still warrants manual review — the converter flags such sites with QRM022 warnings rather than converting silently.

---

## Public API

- `DapperConverter`, `EfCoreConverter`, `AdoNetConverter`, `SqlKataConverter` — programmatic entry points.
- `IConversionDiagnostic` — severity, code, span, message. Uniform across source tools.
- `IConversionEntry` — single converted call site: original location + replacement code + `IConversionDiagnostic[]`.
- `SchemaResolver`, `ChainEmitter`, `SchemaMap` — reusable building blocks if you want to build a converter for another source tool.
- `SqlDialect` (internal, duplicated in this assembly) — kept internal so the shared name does not clash between `Quarry.dll` and `Quarry.Migration.dll` when both are referenced.

---

## License

MIT. See [LICENSE](../../LICENSE).
