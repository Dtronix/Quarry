# Workflow: 269-mysql-execution-coverage

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #269
pr: #271
session: 1
phases-total: 5
phases-complete: 5

## Problem Statement
Mirror the PostgreSQL execution coverage that PR #266 (issue #258) added to the MySQL provider so that `My`-targeted CrossDialect tests run end-to-end against a real MySqlConnector + Testcontainers MySQL container instead of `MockDbConnection`. The current mock-only coverage hides the same class of generator-emitted parameter-binding regressions on MySQL that #258 surfaced on PostgreSQL.

Reference implementation: PR #266. Substitutions: `Testcontainers.MySql`, `MySqlConnector`, `mysql:8.4`, `GET_LOCK` for the cross-process baseline gate, `Quarry.Migration.SqlTypeMapper.MapMySql` as the DDL source of truth, backtick identifier quoting, dedicated `quarry_test` database (no schema-as-namespace).

### Baseline Test Status (recorded 2026-04-25)
- `dotnet test`: **Failed: 1, Passed: 2489, Skipped: 506, Total: 2996**
- Pre-existing failure: `Quarry.Tests.Migration.PostgresMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnPostgreSQL` — `NullReferenceException` in `TearDown` because Docker Desktop is not running on the local Windows host. The PG `Assert.Ignore` path triggers in `SetUp` before `_connection` is assigned, so `TearDown`'s `_connection.State` access throws. Environment-specific; CI has Docker and this test passes upstream. Excluded from the IMPLEMENT-phase pass/fail gate.
- Note: a parallel-shape failure in the new MySQL migration runner test is expected on the same local box. Both pass once Docker is available.

## Decisions

### 2026-04-25 — PR strategy: single PR
Land harness + 4 focused integration tests + full cross-dialect mirror as one PR. PR #266's spec ("split only if >3000 net adds") expected to hold for the symmetric MySQL diff.

### 2026-04-25 — Helper rename: PgRowOrderExtensions → RowOrderExtensions
Both PG and MySQL InnoDB lack insertion-order guarantees without ORDER BY. The single shared `Task<List<T>>.SortedByAsync(keySelector)` extension stays signature-stable; only the class name + XML doc generalise to cover both providers. PG mirror sites continue to call the same method; My mirror sites call the same method.

### 2026-04-25 — No MySQL parameter-binding probe test fixture
PR #266's `NpgsqlParameterBindingTests` was an empirical A/B/C/D probe for a known bug (Npgsql 10's mode-switch rule). MySQL has no equivalent known bug. The 4 focused integration tests (entity insert, batch insert, where-in-collection, migration runner) plus the cross-dialect mirror cover the surface. Skip the probe test.

### 2026-04-25 — `Col<DateTimeOffset>` → `DATETIME` on MySQL harness DDL
Mirror `Quarry.Migration.SqlTypeMapper.MapMySql` (src/Quarry/Migration/SqlTypeMapper.cs:94 maps both `DateTime` and `DateTimeOffset` to `DATETIME`). Harness DDL is supposed to mirror what the migration emits in production. If `events.ScheduledAt`/`CancelledAt` round-trip loses offset, triage that inline during Phase 5 (don't pre-emptively diverge from the source-of-truth mapping).

### 2026-04-25 — Database isolation: shared `quarry_test` DB + transactional default, `useOwnMyDatabase` opt-out
MySQL has no schema-as-namespace concept. Mirror the PG pattern with database substitution: shared `quarry_test` database holds the baseline DDL + seed; each harness opens its own `MySqlConnection`, sets the active database (`USE quarry_test`), and wraps the test in `BEGIN`/`ROLLBACK`. `useOwnMyDatabase: true` opt-out creates a uniquely-named database per harness for migration runner / commit-visible-state tests, dropped on dispose.

### 2026-04-25 — Cross-process baseline gate via `GET_LOCK`
Direct analogue of PG's `pg_advisory_lock` for the once-per-process DDL+seed step. `SELECT GET_LOCK('quarry_test_baseline', 60)` blocks concurrent NUnit processes sharing one container; `RELEASE_LOCK` releases it after the baseline-ready check + DDL.

### 2026-04-25 — Container image and pinning
`mysql:8.4` (LTS). Default Linux image has `lower_case_table_names=0` (case-sensitive identifiers), which matches the case-sensitive identifiers Quarry emits.

**Two server-config departures from defaults pinned in `WithCommand`:**

- `--collation-server=utf8mb4_bin` (default is `utf8mb4_0900_ai_ci`). MySQL's default collation is case-insensitive, which makes `WHERE col = 'shipped'` match a row with `Status = 'Shipped'`. PG / SQLite / SqlServer are case-sensitive. Pinning `utf8mb4_bin` gives cross-dialect parity. Surfaced by `Join_Where_InClause`, `Where_Any_And_All_MultipleSubqueries`, `Where_ContainsRuntimeCollection` failing on My with the seed data's mixed case.
- `--sql-mode=...,NO_BACKSLASH_ESCAPES`. MySQL's default sql_mode treats backslash as a string-escape character, so `'\'` is a parse error. PG (with default `standard_conforming_strings`) / SQLite / SqlServer all treat `'\'` as a literal backslash. Quarry's generator emits the same SQL `LIKE '%foo\_bar%' ESCAPE '\'` for all dialects; without `NO_BACKSLASH_ESCAPES`, MySQL throws 1064. The other sql_mode flags (`ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,...`) are the MySQL 8.4 defaults preserved verbatim. Surfaced by `Where_Contains_LiteralWithMetaChars_InlinesWithEscape` and `Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern`.

### 2026-04-25 — DDL dialect specifics
- Identifier quoting: backticks `` ` `` (MySQL native).
- Auto-increment PKs: `INT NOT NULL AUTO_INCREMENT PRIMARY KEY` (replaces PG `GENERATED BY DEFAULT AS IDENTITY`).
- `Col<bool>` → `TINYINT(1)`. `Col<decimal>`/`Col<Money>` → `DECIMAL(18,2)`. `Col<DateTime>`/`Col<DateTimeOffset>` → `DATETIME`. `Col<Guid>` → `CHAR(36)`. (Per `SqlTypeMapper.MapMySql`.)
- FK constraints intentionally omitted — InnoDB enforces FKs; mirror SQLite's effective non-enforcement to keep DELETE-by-where-clause tests passing. Same rationale as the PG harness.
- `events.ScheduledAt`/`CancelledAt`: `DATETIME` per `SqlTypeMapper.MapMySql`. Seed timezone-stripped (issue notes DST/offset triage may be needed; defer to Phase 5).
- Generated/computed `products.DiscountedPrice`: MySQL 8 supports `GENERATED ALWAYS AS (...) STORED` natively. Use it.

### 2026-04-25 — `View "Order"`
MySQL views with mixed-case names: identifier case is preserved with `lower_case_table_names=0`. Emit ``CREATE VIEW \`Order\` AS SELECT * FROM \`orders\`;`` (mirrors PG's `"Order"` view).

### 2026-04-25 — `IgnoreCommandTransaction=True` on MySQL connection string
MySqlConnector enforces that `DbCommand.Transaction` match the connection's active transaction. Quarry-emitted DbCommands don't set `Transaction`, so the harness's `BEGIN`/`ROLLBACK` envelope would otherwise reject every command. Npgsql / Microsoft.Data.Sqlite are more permissive on this point. Set `IgnoreCommandTransaction=True` via `MySqlConnectionStringBuilder` when opening the harness connection.

### 2026-04-25 — Init-script GRANT ALL for the application user
Testcontainers.MySql provisions the `mysql` application user with privileges scoped to MYSQL_DATABASE only — no global CREATE DATABASE. Per-test database creation (useOwnMyDatabase opt-out, `MySqlMigrationRunnerTests`) needs broader privileges. Add a `/docker-entrypoint-initdb.d/01-grant-all.sql` init script via `WithResourceMapping` that grants the user `ALL PRIVILEGES ON *.* WITH GRANT OPTION`. Test-only container; no security concern.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-25 INTAKE | — | Worktree created, baseline captured, transitioning to DESIGN |
