# Migrating to Quarry

This article describes how to migrate an existing codebase from Dapper, EF Core, SqlKata, or
raw ADO.NET to Quarry. The migration is a structured, multi-phase process that produces a
conversion plan before any code is changed.

> **Manual review required.** Every migration plan must be reviewed by a developer before
> execution. Automated analysis identifies query sites and infers intent, but implicit
> patterns -- especially EF Core change tracking and navigation cascades -- need human
> verification to confirm the effective SQL operations.

## Overview

The migration follows seven phases:

1. **Configuration** -- choose source libraries, system scope, DI strategy, and migration
   preferences.
2. **Discovery** -- scan the codebase for every database interaction: NuGet packages, using
   statements, API call patterns, implicit modification flows, and DI registrations.
3. **Planning** -- produce a per-project migration plan that lists every query and modification
   site, rates its conversion complexity, and maps it to a Quarry equivalent.
4. **Schema conversion** -- create Quarry schema classes from existing entities, preferably via
   `quarry scaffold` against the live database.
5. **Context and DI setup** -- define the `QuarryContext`, update `.csproj` configuration, and
   rewire dependency injection registrations.
6. **Query migration** -- convert all query and modification call sites from the source library
   to Quarry chains.
7. **Verification** -- build, inspect generated SQL with `ToDiagnostics()`, run tests, and
   clean up old code.

Each phase completes before the next begins. The project builds after phases 4, 5, and 6
individually to catch errors incrementally.

## Supported Source Libraries

Quarry migrations support any combination of:

- **Dapper** -- parameterized SQL strings, `QueryAsync<T>`, `ExecuteAsync`
- **EF Core** -- `DbContext`, LINQ queries, change tracking, navigation properties
- **SqlKata** -- fluent query builder, `QueryFactory`
- **Raw ADO.NET** -- `SqlCommand`, `DbDataReader`, manual parameter binding

A single project may use more than one library. The discovery phase detects all of them.

## Automated Conversion with `quarry convert`

The `Quarry.Migration` package (shipped alongside `Quarry.Tool`) provides Roslyn-based converters that translate a large share of source-library call sites to equivalent Quarry chain code. Run it early in the query-migration phase to establish a baseline, then hand-translate the remaining sites flagged as "not convertible."

```sh
dotnet tool install --global Quarry.Tool
quarry convert --from dapper   --project ./src/MyApp
quarry convert --from efcore   --project ./src/MyApp
quarry convert --from adonet   --project ./src/MyApp
quarry convert --from sqlkata  --project ./src/MyApp
```

For each source tool, the converter parses embedded SQL, resolves identifiers against your Quarry entities, and emits equivalent chain code. `Sql.Raw` is used as a fallback when the construct cannot be translated. Supported shapes include SELECT/WHERE/INNER/LEFT/RIGHT/CROSS/FULL OUTER joins/GROUP BY/HAVING/ORDER BY/LIMIT/aggregates/IN/BETWEEN/IS NULL/LIKE plus DELETE and UPDATE. INSERT sites emit a TODO comment since the chain API expects entity objects.

Each source tool has its own Roslyn analyzer and IDE code-fix. Adding `Quarry.Migration` as an analyzer reference surfaces per-call-site diagnostics:

| Source tool | Detected | With warnings | Not convertible |
|---|---|---|---|
| Dapper | QRM001 | QRM002 | QRM003 |
| EF Core | QRM011 | QRM012 | QRM013 |
| ADO.NET | QRM021 | QRM022 | QRM023 |
| SqlKata | QRM031 | QRM032 | QRM033 |

Analyzers only activate when the source tool's framework type is present in the compilation, so downstream projects without the source library see no noise.

For the ADO.NET detector specifically: the converter uses the **last** `CommandText` assignment before each `Execute*` call and positionally filters parameters, so reused `DbCommand` variables across multiple executions are handled correctly. Heavy command mutation patterns still warrant manual review.

## Implicit Modification Analysis

EF Core's change tracker and navigation property manipulation create database modifications
without explicit SQL or method calls that reveal the operation. The discovery phase traces
these patterns to determine what they actually do:

- **Load-mutate-save** -- an entity is loaded, properties are changed in memory, and
  `SaveChangesAsync()` generates an UPDATE for the mutated columns. The discovery phase traces
  which properties change between load and save to produce the correct `Update().Set()` call.
- **Navigation cascades** -- adding to a collection triggers an INSERT, removing triggers a
  DELETE or FK null (depending on cascade behavior), and reassigning a reference triggers an
  FK UPDATE. The discovery phase checks `OnModelCreating` for `DeleteBehavior` configuration
  to determine the effective operation.
- **Multi-entity SaveChanges** -- a single `SaveChangesAsync()` may flush inserts, updates,
  and deletes across multiple entity types. Each becomes a separate explicit Quarry call.
- **Bulk operation libraries** -- third-party packages like `EFCore.BulkExtensions` and
  `Zack.EFCore.Batch` bypass EF's change tracker entirely. The discovery phase detects these
  and maps them to Quarry's `InsertBatch` or `RawSqlNonQueryAsync`.

These implicit flows are the highest-risk part of any migration. The plan marks each with its
traced effective SQL, but **a developer must verify** that the trace is complete and correct
before conversion.

## Conversion Complexity Ratings

Every query and modification site in the plan receives a complexity rating:

| Rating | Meaning |
|---|---|
| **Direct** | 1:1 mapping exists. Mechanical, low-risk conversion. |
| **Adapted** | Quarry equivalent exists but the pattern differs (e.g., change tracking becomes explicit Update, Include becomes Join). |
| **RawSql** | Complex SQL that should stay as `db.RawSqlAsync<T>(...)` initially. Can be converted to typed chains later. |
| **Redesign** | Pattern requires architectural changes (e.g., cross-method IQueryable composition, Unit of Work, >4-table joins). |

## Key Differences from Source Libraries

Quarry's compile-time architecture means some patterns work differently:

- **No change tracking.** Every insert, update, and delete is an explicit call.
- **No lazy loading.** Fetch the data you need upfront via queries or joins.
- **No IQueryable composition across methods.** Query chains must be statically analyzable
  within a single method. The source generator reads them at compile time.
- **Entity accessors are methods.** Write `db.Users()`, not `db.Users`.
- **No anonymous type projections.** Use DTOs or tuples in `.Select()`.
- **Max 6-table explicit joins.** Queries beyond six tables need to be split, use CTEs (`.With<…>()`), or fall back to `RawSqlAsync`.
- **OrderBy requires a prior clause.** Chain after `.Where()` or `.Select()`, not directly on
  the entity accessor.

## Dependency Injection

Quarry's `QuarryContext` takes a `DbConnection` in its constructor and implements
`IAsyncDisposable`. It fits naturally into scoped DI lifetimes:

```csharp
// Replaces AddDbContext<T> or AddScoped<IDbConnection>
var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddScoped(_ => new AppDb(new NpgsqlConnection(connectionString)));
```

For ASP.NET projects, the `Quarry.Sample.WebApp` project demonstrates the complete pattern
including startup registration, controller injection, and connection management.

## Migration System

New Quarry projects start from the existing database using `quarry scaffold`:

```sh
dotnet tool install --global Quarry.Tool
quarry scaffold --dialect PostgreSQL --connection "..." --output ./Schemas
```

This produces schema classes that match the current database state. After review, create the
initial Quarry migration and set up the ongoing workflow:

```sh
quarry migrate add InitialCreate --project ./src/MyApp.Data/MyApp.Data.csproj --context AppDb
```

See [Migrations](migrations.md) for the full migration workflow and
[Scaffolding](scaffolding.md) for scaffold options.

## LLM-Assisted Migration

The full migration process is documented as a structured LLM skill in
[`llm-migrate.md`](../../llm-migrate.md) at the repository root. This skill walks an LLM
through all seven phases with specific detection patterns, mapping tables, and conversion
recipes for each source library.

The skill includes:

- Auto-discovery patterns for NuGet packages, using statements, and API call signatures
- Full EF Core change tracking flow tracing (load-mutate-save, navigation cascades)
- Bulk operation library detection and mapping
- Per-library conversion tables (Dapper, EF Core, SqlKata, raw ADO.NET to Quarry)
- DI integration patterns for standard and custom containers
- Migration system setup from scaffold through ongoing workflow

The LLM skill produces a plan for human review before making any changes. All implicit
modification traces and complexity ratings should be verified by a developer before the
conversion phase begins.
