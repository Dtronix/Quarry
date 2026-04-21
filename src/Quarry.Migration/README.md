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

Constructs that fall outside the converter's grammar are emitted as `Sql.Raw` fragments so the query still runs; the analyzer flags them with a QRM00x-warnings diagnostic so you can review.

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
