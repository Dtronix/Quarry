# Plan: Mirror PG execution coverage to SQL Server (#270)

## Context

PR #266 made `QueryTestHarness.Pg` execute against a real Npgsql 10 + PostgreSQL 17 Testcontainer; 217 cross-dialect test sites now exercise both `Lite` and `Pg` against real providers. `My` and `Ss` are still on `MockDbConnection`. This plan brings `Ss` to parity with `Pg`: real `Microsoft.Data.SqlClient` connection against a Testcontainers MS SQL 2022 container, with every existing `lt.Execute*` site mirrored by a parallel `ss.Execute*` site applying the same assertions.

Reference shape: PR #266 (`920e862`). Mechanical pattern is identical; the dialect-specific differences are the schema DDL, the schema-isolation strategy, and the parameter-binding particulars of `Microsoft.Data.SqlClient`.

## Key concepts

### Schema isolation via mapped login
SQL Server has no `SET search_path` equivalent. The chosen approach (DESIGN decision 2026-04-25) is to create a SQL login `quarry_test_user` whose default database-user maps to schema `quarry_test`. Connections authenticated as that user resolve unqualified `[users]` to `[quarry_test].[users]` automatically — no per-connection EXECUTE-AS, no schema rewriting in tests.

`MsSqlTestContainer.EnsureBaselineAsync` runs once per process, gated by `sp_getapplock` (SQL Server's named-lock primitive, the analogue of PG's `pg_advisory_lock`). It connects as `sa`, creates the schema, the login + user, the tables, the seed data, then releases the lock. Subsequent harnesses connect via `GetUserConnectionStringAsync` which returns a connection string with `User Id=quarry_test_user`.

Owned-schema tests still use `sa` to create `test_<guid>`, run DDL + seed, and DROP at end. Their schema isolation is per-test, so the mapped-login default-schema mechanic doesn't apply — they run unqualified queries through fully-qualified `[<schema>].[<table>]` rewrites? No — that would diverge from PG's pattern. Instead, the owned-schema path grants `quarry_test_user` access to the new schema and **alters that user's default schema** for the lifetime of the harness, then restores it on dispose. That's awkward; an alternative is to give each harness its own dedicated user with default schema set to its owned schema. The plan settles on the latter for cleanliness — see Phase 2 below.

### Transactional-rollback isolation
Default path: harness opens its `quarry_test_user` connection, calls `BEGIN TRANSACTION`, runs the test, then `ROLLBACK`. Microsoft.Data.SqlClient supports nested-style commands inside an open transaction the same way Npgsql does. `OUTPUT INSERTED.*` (the SQL Server analogue of PG's `RETURNING`) survives rollback fine.

Caveat: `IDENTITY_INSERT` toggles are session-scoped but persist for the duration of the connection unless explicitly toggled off. The seed step (Phase 2) will toggle ON before each table seed and OFF after, exactly mirroring PG's identity-reseed pattern.

### IDENTITY_INSERT around seed
The seed inserts in PG use explicit IDs followed by `setval(pg_get_serial_sequence(...), MAX(id))` to advance the sequence. SQL Server uses `IDENTITY(1,1)` with `SET IDENTITY_INSERT [tbl] ON ... INSERT ... SET IDENTITY_INSERT [tbl] OFF`. After explicit-ID inserts, the next auto-generated ID continues from `(MAX(id) + 1)` automatically — no `setval` analogue needed. SQL Server tracks the IDENTITY high-water mark internally.

### Parameter binding particulars
PR #266 changed the PG generator to emit `ParameterName = ""` and `$N` SQL placeholders, because Npgsql 10 rejected names that didn't match the placeholder text. SQL Server's `Microsoft.Data.SqlClient` is more forgiving — `@pN` SQL with `@pN` parameter names has worked since SqlClient 1.0 and is the existing Quarry emit. **No generator changes are required for SQL Server.** The mirror should pass on the existing emit.

### Row-order without ORDER BY
SQL Server, like PG, makes no row-order guarantee in the absence of `ORDER BY`. The 11 sites that PR #266 made `pgResults`-sort-aware will need parallel `ssResults`-sort-aware logic. Promote `PgRowOrderExtensions.SortedByAsync` to a dialect-neutral name (e.g. `RowOrderExtensions.SortedByAsync`) — a single helper used by both Pg and Ss assertions. (See Phase 4.)

### #268 chained-With dispatch
`Cte_TwoChainedWiths_DistinctDtos_CapturedParams` may fire the same closure-extractor mismatch on Ss as PR #266 hit on Pg. The localized variable-rename workaround already in the test for Pg should cover Ss too if the root cause is generator-level (i.e. dialect-independent). If Ss fails for a different reason, triage in Phase 5.

## Phases

Each phase is independently committable. Tests gate the commit. Phases are the natural unit of suspend/resume.

### Phase 1 — Testcontainers.MsSql + container helper + Docker probe

**Files:**
- `src/Quarry.Tests/Quarry.Tests.csproj` — add `Testcontainers.MsSql Version="4.*"`. Verify `Microsoft.Data.SqlClient` already present (it is, at `6.*`).
- `src/Quarry.Tests/Integration/MsSqlTestContainer.cs` (new) — mirror `PostgresTestContainer.cs` shape: lazy singleton container, Docker-unavailable detection, `EnsureBaselineAsync` (sp_getapplock-gated), `CreateOwnedSchemaAsync`, `GetSaConnectionStringAsync`, `GetUserConnectionStringAsync`. Schema DDL + seed deferred to Phase 2 — this phase only stubs them with `throw new NotImplementedException` so the helper compiles.
- `src/Quarry.Tests/Integration/MsSqlContainerSmokeTests.cs` (new, single test) — boots the container, opens a `SqlConnection` as sa, runs `SELECT @@VERSION`, asserts non-null. No reference to harness yet. Tagged `[Category("SqlServerIntegration")]`. This is the Phase 1 commit gate.

**Tests added/modified:**
- `MsSqlContainerSmokeTests.SqlServerContainer_BootsAndAcceptsConnection` (1 new test).

**Commit:** `Add Testcontainers.MsSql + MsSqlTestContainer skeleton + Docker probe`.

**Acceptance:** smoke test passes locally on Docker; existing 2489 tests still pass.

### Phase 2 — Schema DDL port + login/user setup + harness `Ss` upgrade

This is the largest phase. Splittable in principle but the harness change requires the DDL to exist, and the DDL is meaningless without the harness wiring the connection. Keep as one phase.

**`MsSqlTestContainer.cs` (extending Phase 1 stub):**
- `CreateSchemaObjectsAsync(SqlConnection conn, string schema)`: port the 11 tables + view per `SqlTypeMapper.MapSqlServer`:
  - `INTEGER PK` → `[ColName] INT IDENTITY(1,1) PRIMARY KEY`.
  - `TEXT NOT NULL` → `NVARCHAR(MAX) NOT NULL` (PG used `TEXT` because it's first-class; SQL Server's `NVARCHAR(MAX)` is the equivalent unbounded type).
  - `NUMERIC(18,2)` → `DECIMAL(18,2)`.
  - `BOOLEAN` → `BIT`. Default values switch from `TRUE`/`FALSE` to `1`/`0`.
  - `TIMESTAMP` → `DATETIME2`.
  - `TIMESTAMPTZ` → `DATETIMEOFFSET`.
  - `GENERATED ALWAYS AS (... ) STORED` → `AS (...) PERSISTED`.
  - All identifiers switch from `"users"` quoting to `[users]` quoting.
  - FK constraints stay omitted (same SQLite-parity reasoning as PG).
- `SeedDataAsync(SqlConnection conn, string schema)`: port the 10 seed inserts:
  - `BIT` literals are `1`/`0`, not `TRUE`/`FALSE`.
  - Datetime literals use ISO-8601 strings; SQL Server parses them into `DATETIME2`/`DATETIMEOFFSET` directly.
  - Wrap each table's seed insert in `SET IDENTITY_INSERT [<schema>].[<table>] ON ... SET IDENTITY_INSERT ... OFF`.
  - Drop the `setval`-equivalent block — SQL Server tracks IDENTITY high-water automatically.
- `EnsureBaselineAsync`: connect as `sa`, take `sp_getapplock` (named lock `quarry_test_baseline`), check whether `quarry_test.users` exists (skip if so), otherwise:
  - `CREATE SCHEMA [quarry_test]`.
  - `CREATE LOGIN [quarry_test_user] WITH PASSWORD = '<strong-password>', CHECK_POLICY = OFF` (CHECK_POLICY off so password complexity rules don't bite a fixed test password).
  - `CREATE USER [quarry_test_user] FOR LOGIN [quarry_test_user] WITH DEFAULT_SCHEMA = [quarry_test]`.
  - `ALTER ROLE [db_owner] ADD MEMBER [quarry_test_user]` (test user needs DDL for harness CreateSchema/SeedData).
  - Run `CreateSchemaObjectsAsync` + `SeedDataAsync`.
  - Release `sp_releaseapplock`.
- `CreateOwnedSchemaAsync(SqlConnection saConn)`: from an `sa` connection (not the user connection), generates `test_<guid>` schema, runs DDL + seed there, returns the schema name. Owned-schema path uses an sa-authenticated connection at the harness level (the user connection's default-schema would not redirect unqualified queries to `test_<guid>`). For full SsDb-context tests against owned schema, the harness flips to a dedicated short-lived per-harness user with default_schema set to the owned schema (`CREATE LOGIN test_<guid>_user`). On dispose, `DROP USER`/`DROP LOGIN`/`DROP SCHEMA CASCADE`-equivalent.
  - Pragmatic alternative considered: have the owned-schema path always run as `sa` and rewrite test queries to `[<schema>].[<table>]`. Rejected because it diverges from how transactional-baseline tests run, and only one current test (`PostgresMigrationRunnerTests` analogue) needs the owned-schema path.
- `GetSaConnectionStringAsync()` / `GetUserConnectionStringAsync()`: build connection strings off `_container.GetConnectionString()` swapping `User Id=`.

**`QueryTestHarness.cs`:**
- Field `_sqlConnection` (SqlConnection) + `_sqlTransaction` (SqlTransaction?) + `_ownedSsSchema` (string?) — fields parallel to the existing PG ones.
- Constructor + `CreateAsync` accept `useOwnSsSchema = false` parameter.
- Default path: `EnsureBaselineAsync()`, open `SqlConnection` via `GetUserConnectionStringAsync()`, `BEGIN TRANSACTION`. Default schema is `quarry_test` automatically.
- Owned path: `EnsureBaselineAsync()` (still needed — owned path piggybacks on the lock for the user setup), then create owned schema + dedicated user via `sa`, open user connection, no transaction (schema dropped on dispose).
- `Ss` field reassignment: `Ss = new Ss.SsDb(_sqlConnection)` in the new path. `My` stays on `MockConnection`.
- `DisposeAsync` runs Ss teardown symmetrically with Pg teardown: rollback transaction, drop owned schema + user + login, dispose connection.
- Catch-block resource unwind extends to handle `SqlConnection` and `SqlTransaction`.
- `MockConnection` property stays for the schema-qualified contexts (`SchemaSsDb`, `SchemaMyDb`, `SchemaPgDb`) and `VariableStoredChainTests`.

**Tests added/modified:**
- No new tests this phase; the Phase 1 smoke test stays. Acceptance is the existing 2489 still pass — Ss now executes on a real SqlConnection, but no test currently calls `await ss.Execute*Async()` so the harness change is invisible at test level. (Phase 3 introduces the first execution sites.)

**Commit:** `Port schema DDL to SQL Server + upgrade QueryTestHarness.Ss to real SqlConnection`.

**Acceptance:** all 2489 existing tests still pass; smoke test still passes; no Ss execute-path tests yet.

### Phase 3 — Focused integration tests on Ss

Mirror `PostgresIntegrationTests.cs` four-test set.

**Files:**
- `src/Quarry.Tests/Integration/SqlServerIntegrationTests.cs` (new). Four tests:
  - `EntityInsert_OnSqlServer_ExecutesSuccessfully` — `Ss.Addresses().Insert(...)` then read-back via `Ss.Addresses().Where(...).Select(...).ExecuteFetchFirstOrDefaultAsync()`.
  - `InsertBatch_OnSqlServer_ExecutesSuccessfully` — `Ss.Warehouses().InsertBatch(...).Values(arr).ExecuteNonQueryAsync()` then assert count.
  - `WhereInCollection_OnSqlServer_ExecutesSuccessfully` — `Ss.Users().Where(u => wantedIds.Contains(u.UserId))` with `wantedIds` from a method call so the generator emits the runtime-expansion code path.
  - `MigrationRunner_InsertsHistoryRow_OnSqlServer` — port `PostgresMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnPostgreSQL` to a SQL Server own-schema setup. Closes the symmetric concern for `MigrationRunner` against `SqlClient`.
- `src/Quarry.Tests/Migration/SqlServerMigrationRunnerTests.cs` (new) — own fixture for the migration test, parallel to `PostgresMigrationRunnerTests.cs`.

**Tests added/modified:**
- 4 new Ss-execute integration tests, 1 new migration runner test.

**Commit:** `Add Ss-execute integration tests + SqlServerMigrationRunnerTests`.

**Acceptance:** all new tests pass on the container.

### Phase 4 — Full cross-dialect mirror across 22 files

The mechanical bulk. For each `await pg.Execute*Async()` site (217 sites across 22 files), append a parallel `await ss.Execute*Async()` block applying the equivalent assertions.

**Approach (delegated to an Explore-class agent):**
- Pattern is identical for every site: copy the Pg block, rename `pg` → `ss`, rename `pgResults` → `ssResults`. Same expected values (since assertions compare against the same seeded data, the result shape doesn't change between PG and Ss).
- Sites that use `.SortedByAsync(r => r.X)` get the same call on the Ss side (using the renamed extension below).

**Helper rename:**
- `src/Quarry.Tests/PgRowOrderExtensions.cs` → `src/Quarry.Tests/RowOrderExtensions.cs`. Class `PgRowOrderExtensions` → `RowOrderExtensions`. Extension method signature unchanged. Update `using` references in the 11 PG sites.
- Doc-comment rationale generalized from "PG-only" to "PG and SQL Server real-execution sites".

**Files modified (22):** `CrossDialectAggregateTests`, `BatchInsertTests`, `ComplexTests`, `CompositionTests`, `CteTests`, `DeleteTests`, `EnumTests`, `HasManyThroughTests`, `InsertTests`, `JoinTests`, `MiscTests`, `NavigationJoinTests`, `NullableValueTests`, `OrderByTests`, `SchemaTests`, `SelectTests`, `SetOperationTests`, `StringOpTests`, `SubqueryTests`, `TypeMappingTests`, `UpdateTests`, `WhereTests`, `WindowFunctionTests`. (Note: `DiagnosticsTests` has 0 PG-execute sites per the survey — no change needed.)

**Commit:** `Mirror Pg-execute coverage to Ss across 22 cross-dialect files (217 sites)`.

**Acceptance:** all green except for triage candidates surfaced in Phase 5.

### Phase 5 — Triage Ss-only failures

Whatever Phase 4 surfaces, classify each:
- **(a) Real Ss-specific bug to fix in this PR** (e.g. parameter type-inference mismatch on `DECIMAL(18,2)` columns) — fix and commit.
- **(b) Ss-specific behavior to work around with inline comment** (e.g. `SELECT TOP` subquery shape, row-order quirk) — apply the workaround pattern (sort, skip, comment) and commit.
- **(c) Latent generator bug worth filing as a follow-up issue** — open a separate issue, link from the Ss test's skip-comment, and skip the Ss execute on that one site.

Each finding gets its own commit with `# Triage: <short description>` so REVIEW can step through the dialect concerns separately from the bulk mirror.

**Commit(s):** one per triage finding. May be zero if everything passes.

**Acceptance:** all tests green or skipped-with-tracked-issue.

## Phase dependencies

```
1 → 2 → 3 → 4 → 5
```

Strict linear. Phase 4's mirror cannot run before Phase 2's harness upgrade. Phase 3 acts as a sanity gate — if four focused tests pass cleanly, the 217-site mirror has a solid baseline.

## Test plan summary

| Phase | New tests | Mod tests | Gate |
|-------|-----------|-----------|------|
| 1     | 1 smoke   | 0         | smoke + 2489 baseline |
| 2     | 0         | 0         | 2489 baseline + smoke |
| 3     | 4 + 1 = 5 | 0         | 2494 + smoke |
| 4     | 0         | ~150–217  | 2494 + smoke (each Pg site now has an Ss assertion sibling) |
| 5     | 0         | ≤5        | green |

## Risks / unknowns

- **Container cold-start cost:** ~30s. CI runs already pay PG's ~5s; total integration boot becomes ~35s. Acceptable.
- **Mapped-login scheme failure modes:** if `CHECK_POLICY=OFF` is rejected by container config (highly unlikely for `mcr.microsoft.com/mssql/server:2022-latest`), fallback is to seed the password from `_dockerUnavailableReason`-style cached message and require an env var override.
- **Owned-schema dedicated-user complexity:** if dropping a login fails because of a still-open connection (sa's lock vs the user's connection), add explicit `SqlConnection.ClearPool()` before the drop. Risk is contained — only the migration-runner test takes this path.
- **`OFFSET 0 ROWS FETCH NEXT N ROWS ONLY` requires `ORDER BY`:** SQL Server's OFFSET/FETCH syntax is more strict than PG's `LIMIT`. Existing cross-dialect tests already assert SQL Server emits this shape; the new question is whether all such tests have an `ORDER BY` in their chain. Triage in Phase 5.
- **`SELECT DISTINCT` ordering on SQL Server:** Test `Select_Distinct` had to skip Pg execution (see #267) because PG rejects an `ORDER BY` clause that doesn't reference selected columns. SQL Server has the same rule — same skip logic applies symmetrically.
