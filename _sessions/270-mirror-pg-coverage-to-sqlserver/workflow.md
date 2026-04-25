# Workflow: 270-mirror-pg-coverage-to-sqlserver

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #270
pr:
session: 1
phases-total: 5
phases-complete: 2

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

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-04-25  |           | INTAKE complete; baseline recorded; entering DESIGN |
