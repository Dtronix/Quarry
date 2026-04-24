# Release Notes

Per-version release notes for Quarry. Each page mirrors the matching [GitHub release](https://github.com/Dtronix/Quarry/releases).

| Version | Released | Highlights |
|---|---|---|
| [v0.3.2](./release-notes-v0.3.2.md) | 2026-04-24 | QRY044 false-positive fix for multi-entry `<InterceptorsNamespaces>` lists |
| [v0.3.1](./release-notes-v0.3.1.md) | 2026-04-23 | `Many<T>` aggregates + `Sql.Raw<T>` now work in Select projections, Npgsql 10 bind-parameter fix, QRY043 / QRY044 authoring-time diagnostics, `Quarry.Generated` auto-registered |
| [v0.3.0](./release-notes-v0.3.0.md) | 2026-04-21 | CTEs, window functions, UNION/INTERSECT/EXCEPT, navigation joins (`One<T>`, `HasManyThrough`), 6-table explicit joins, `Many<T>` aggregates, `RawSqlAsync` streaming, SQL manifest emission, `Quarry.Migration` converters (EF Core/Dapper/ADO.NET/SqlKata) |
| [v0.2.1](./release-notes-v0.2.1.md) | 2026-03-29 | `ownsConnection` support for `QuarryContext`, documentation improvements |
| [v0.2.0](./release-notes-v0.2.0.md) | 2026-03-29 | Carrier-only architecture, layered IR compiler pipeline, zero-alloc captured variable extraction, full migration framework, zero runtime dependencies, AOT support, DocFX site |
| [v0.1.0](./release-notes-v0.1.0.md) | 2026-03-13 | Initial release — compile-time SQL for SQLite/PostgreSQL/MySQL/SQL Server |

See the [GitHub releases page](https://github.com/Dtronix/Quarry/releases) for source downloads and git tags.
