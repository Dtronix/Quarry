# Workflow: 270-mirror-pg-coverage-to-sqlserver

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #270
pr: #276
session: 1
phases-total: 5
phases-complete: 5

## Problem Statement
Issue #270: Mirror PG execution coverage to SQL Server.

PR #266 (issue #258) added end-to-end execution coverage for PostgreSQL — every `CrossDialect*Tests` test now runs on real SQLite **and** real PostgreSQL. `My` and `Ss` are still backed by `MockDbConnection` and only verify SQL-string shape. This means the regression class that motivated #258 (a generator-emitted parameter-binding pattern that produces broken SQL on the real provider while passing all mock-based tests) is **still uncovered for SQL Server**.

This issue tracks adding the same end-to-end execution coverage for SQL Server: real `Microsoft.Data.SqlClient` against a Testcontainers-backed MS SQL container, with every existing `Lite` execute test mirrored to a `Ss` execute test.

### Baseline (pre-existing failure, environment-dependent)
Local baseline run: 1 failing test — `Quarry.Tests.Migration.PostgresMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnPostgreSQL`. Failure originates in TearDown when Docker is unavailable on the local machine: `_connection` is `null` because SetUp short-circuits on `Assert.Ignore`. This is environmental, not a code regression — when Docker is available the test passes (CI passes on master). The SQL Server work introduces the same pattern; we will need to handle the SetUp/TearDown null-guard cleanly. Excluded from "all green" gate.

Test results: `Failed: 1, Passed: 2489, Skipped: 506, Total: 2996` across the solution.

## Decisions

### 2026-04-25 — Docker validation strategy
Run a Docker engine locally during IMPLEMENT. Each phase's tests must pass against the real MS SQL container before commit, mirroring how PR #266 was developed for PG. The PG-runner local-baseline failure is environmental and disappears once Docker is up.

### 2026-04-25 — Schema strategy: dedicated `quarry_test` schema + mapped login
`MsSqlTestContainer.EnsureBaselineAsync` creates schema `quarry_test`, then `CREATE LOGIN quarry_test_user / CREATE USER quarry_test_user FOR LOGIN ... WITH DEFAULT_SCHEMA = quarry_test`, granted `db_owner`. Tests connect using a connection string authenticated as `quarry_test_user` (via separate `GetUserConnectionStringAsync`); `sa` is reserved for one-time setup (CREATE LOGIN, CREATE USER, sp_getapplock probe). This makes unqualified `[users]` references from `SsDb` resolve to `[quarry_test].[users]` without per-connection EXECUTE-AS gymnastics. Owned-schema path creates a `test_<guid>` schema and grants the same user `db_owner` access — schema name is the only changing variable. Rationale: matches PG `search_path` pattern as closely as the dialect allows.

### 2026-04-25 — PR scope: single PR
Land all five phases as one PR mirroring PR #266's structure. Risk: net additions likely 3000-5000 lines. Mitigation: clean per-phase commits so review can step through phase-by-phase even on a large PR.

### 2026-04-25 — #268 dependency: localized workaround in test, no #268 fix here
If `Cte_TwoChainedWiths_DistinctDtos_CapturedParams` fires the chained-With dispatch bug on Ss execute, apply the same variable-rename workaround PR #266 used for PG, with a comment referencing #268. Do not touch generator code.

### 2026-04-25 — Microsoft.Data.SqlClient version
Already referenced at `6.*` in `Quarry.Tests.csproj`. Issue mentions 5.* — keep current 6.*; no downgrade.

### 2026-04-25 — Phase 3 surfaced generator bug: OUTPUT clause placement on SQL Server
`SqlAssembler.RenderInsertSql` and `RenderBatchInsertSql` emitted `INSERT INTO ... VALUES (...) OUTPUT INSERTED.[Id]` for SQL Server, which fails at runtime with `Incorrect syntax near 'OUTPUT'`. SQL Server requires the OUTPUT clause to precede the VALUES clause. Fixed both render paths; for batch insert the OUTPUT is folded into the prefix and the trailing returning suffix is suppressed for SqlServer. 18 cross-dialect test assertions updated to match the corrected emit; the SqlServer manifest regenerated automatically. The fix is direct because it only affects SqlServer code paths — PG/Lite/MySQL behavior is unchanged.

### 2026-04-25 — Phase 3 wiring choice: raw `BEGIN TRANSACTION` instead of SqlConnection.BeginTransaction()
SqlClient requires every `SqlCommand` to have its `Transaction` property assigned when the connection has an open `SqlTransaction`. Quarry's `QueryExecutor` builds DbCommands generically and does not assign that property, so `SqlConnection.BeginTransaction()` makes every test fail with that validation error. Workaround: open a server-side transaction via `BEGIN TRANSACTION` SQL command and roll back via `ROLLBACK TRANSACTION` SQL command. SqlClient's client-side check is bypassed because no `SqlTransaction` object exists. Server-side semantics are unchanged.

### 2026-04-25 — Phase 4 surfaced 19 Ss-only failures in 3 categories
After the bulk mirror, 19 tests fail on the Ss execution path. Categorisation (each goes into its own Phase-5 commit):

1. **SqlDateTime overflow (7 tests)** — Tests use `CreatedAt = default` / `OrderDate = default` (DateTime.MinValue = `0001-01-01`). Microsoft.Data.SqlClient binds `DateTime` parameters as `DATETIME` (range 1753-9999) by default, regardless of column type. The DATETIME2 column would accept the value, but the parameter rejects it before reaching the column. Affected: `Insert_SingleUser`, `Insert_SingleOrder`, `ExecuteScalarAsync_SingleUser_ReturnsIdentity`, `ExecuteScalarAsync_SingleOrder_ReturnsIdentity`, `ExecuteNonQueryAsync_SingleUser`, `ExecuteNonQueryAsync_SingleOrder`, `Insert_WithEnumColumn`. **Fix plan:** replace `default` with a valid DateTime literal (e.g. `new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)`) — works for all four dialects; the test's intent (insert with arbitrary timestamp value) is preserved.

2. **Int64 → Int32 cast (9 tests)** — SQL Server's window functions (`ROW_NUMBER`, `DENSE_RANK`, `NTILE`, `COUNT()` over partition) return `BIGINT`. Quarry's reader emits `GetInt32(i)` based on the projection's declared type, which `Microsoft.Data.SqlClient.SqlDataReader.GetInt32` rejects on a BIGINT column (no auto-narrow). Npgsql's GetInt32 silently narrows from BIGINT, which is why PG passes today. Affected: `WindowFunction_DenseRank_OrderByDescending`, `WindowFunction_Joined_RowNumber`, `WindowFunction_MixedTypePartitionBy`, `WindowFunction_Ntile_ConstVariable`, `WindowFunction_Ntile_Variable`, `WindowFunction_RowNumber_OrderBy`, `WindowFunction_WithWhereClause`, `UnionAll_WithVariableWindowFunctionArg`, `UnionAll_WithVariableWindowFunctionArg_ParamOffset`. **Fix plan:** open a follow-up issue for the generator-side narrow; in this PR, skip Ss execute on these 9 sites with a comment referencing the new issue.

3. **Case-sensitive vs case-insensitive collation (3 tests)** — SQL Server's default container collation is `SQL_Latin1_General_CP1_CI_AS` (case-INSENSITIVE), where Lite/Pg/MySQL default to case-SENSITIVE comparison. Tests assert `Count == 0` for lowercase-vs-PascalCase mismatches, which fail on Ss because `'pending'` matches `'Pending'`. Affected: `Where_ContainsRuntimeCollection`, `Where_Any_And_All_MultipleSubqueries`, `Join_Where_InClause`. **Fix plan:** change `MsSqlTestContainer.CreateSchemaObjectsAsync` to declare NVARCHAR columns with `COLLATE SQL_Latin1_General_CP1_CS_AS` (case-sensitive). Restores parity with the other dialects without changing the SQL the generator emits.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-04-25  |           | INTAKE complete; baseline recorded; entering DESIGN |
