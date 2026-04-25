## Summary

- Closes #270.
- Mirrors the PostgreSQL execution-coverage shape PR #266 introduced (and PR #271 brought to MySQL) onto SQL Server. `QueryTestHarness.Ss` now attaches to a real `Microsoft.Data.SqlClient.SqlConnection` against a Testcontainers SQL Server 2022 container; every `await pg.Execute*Async(...)` site has a parallel `await ss.Execute*Async(...)` mirror with identical assertions.
- Surfaced and fixed one real generator bug (OUTPUT-clause placement on SQL Server).

## Reason for Change

PR #266 (issue #258) made PostgreSQL the second dialect with end-to-end execution coverage. PR #271 (issue #269) added MySQL. Issue #270 closes the same loop for SQL Server — every `CrossDialect*Tests` test now executes on real SQLite, real PostgreSQL, real MySQL, and real SQL Server. The regression class that motivated the original GH-258 fix (a generator-emitted parameter-binding pattern that produces broken SQL on the real provider while passing all mock-based tests) is now uncovered for *no* dialect Quarry supports.

## Impact

- **Test surface:** 3012 tests pass (was 3001 on master). Net new tests: 5 focused integration tests (4 in `SqlServerIntegrationTests.cs` + 1 in `SqlServerMigrationRunnerTests.cs`). Existing 22 cross-dialect test files gained Ss-execute mirrors at every existing `pg.Execute*` site (208 sites mirrored, 9 explicitly skipped pending #274, 1 skipped per #267).
- **CI cold-start cost:** SQL Server container adds ~20–30s to integration test boot. PG was ~5s; MySQL was ~10s. Total integration boot ~45s.
- **Runtime correctness:** the OUTPUT-clause fix in `SqlAssembler.RenderInsertSql` / `RenderBatchInsertSql` makes Quarry's SQL Server emit valid for the first time. Any consumer running Quarry against a real `SqlConnection` was previously failing with `Incorrect syntax near 'OUTPUT'` on every entity-insert returning an identity.

## Plan items implemented as specified

All five plan phases shipped per `_sessions/270-mirror-pg-coverage-to-sqlserver/plan.md`:

1. **Phase 1** — `Testcontainers.MsSql 4.*` package + `MsSqlTestContainer.cs` skeleton + Docker-unavailable smoke probe (`MsSqlContainerSmokeTests.SqlServerContainer_BootsAndAcceptsConnection`).
2. **Phase 2** — Schema DDL port (per `Quarry.Migration.SqlTypeMapper.MapSqlServer`), `quarry_test_user` mapped login provisioning, `QueryTestHarness.Ss` upgrade from `MockDbConnection` to a real `SqlConnection`. Transactional-rollback isolation by default, owned-schema opt-out via `useOwnSsSchema: true`.
3. **Phase 3** — Four focused integration tests + `SqlServerMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnSqlServer`.
4. **Phase 4** — Bulk Ss-execute mirror across 22 cross-dialect test files (208 sites + 1 #267 skip + 9 #274 skips = 218 ≈ 217 pg sites).
5. **Phase 5** — Three triage commits: case-sensitive collation, `default(DateTime)` substitution, BIGINT-window-function skips with #274 follow-up issue.

## Deviations from plan implemented

- **OUTPUT-clause generator fix landed in this PR (Phase 3, not Phase 5).** Phase 3's first integration test surfaced that `SqlAssembler.RenderInsertSql` emitted `INSERT INTO ... VALUES (...) OUTPUT INSERTED.[Id]` for SQL Server, which is invalid syntax (must be `... OUTPUT INSERTED.[Id] VALUES (...)`). Plan classified this as Phase 5 triage; it was foundational, so fixed inline in Phase 3 along with 18 cross-dialect SQL-string-assertion updates and a manifest regeneration. Same rationale extends to `RenderBatchInsertSql` so batch-insert with `ExecuteScalarAsync` is also valid.
- **Raw `BEGIN TRANSACTION` instead of `SqlConnection.BeginTransaction()`** in the harness's transactional-rollback path. SqlClient requires every `SqlCommand` to have its `Transaction` property assigned when the connection has an open `SqlTransaction`; Quarry's `QueryExecutor` builds DbCommands generically and does not. The raw-SQL workaround sidesteps SqlClient's client-side check while preserving server-side transactional semantics.
- **Cross-dialect site count** is 208 mirrored / 9 #274-skipped / 1 #267-skipped = 218 ss sites versus 217 pg sites (one extra captured by including a `ss2` second-chain mirror in `CrossDialectSubqueryTests.WindowFunction_Lag_NullableDto_Execution`, plus the 2 `CrossDialectSchemaTests` sites that the plan did not call out explicitly).

## Gaps in original plan implemented

- **Case-sensitive collation on Ss schema** — SQL Server containers default to `SQL_Latin1_General_CP1_CI_AS` (case-INsensitive); Lite/Pg/MySQL all compare strings case-sensitively. Three tests (`Where_ContainsRuntimeCollection`, `Where_Any_And_All_MultipleSubqueries`, `Join_Where_InClause`) failed on Ss without the override. Phase 5a applies `COLLATE SQL_Latin1_General_CP1_CS_AS` at column declaration; generator-emitted SQL is unchanged.
- **`default(DateTime)` parameter binding** — SqlClient binds `DateTime` parameters as `DATETIME` (range 1753–9999) by default, regardless of column type. `default(DateTime)` is `0001-01-01`, which produces `SqlDateTime overflow` at parameter-binding time. Phase 5b replaces `default` with `new DateTime(2024, 1, 1)` in 7 cross-dialect insert tests (CrossDialectInsertTests + CrossDialectEnumTests). Test intent is preserved on all four dialects.
- **REMEDIATE pass added 2 missed `CrossDialectSchemaTests` mirror sites** (`Select_SingleColumn`, `Delete_All_NoWhereClause`) that the bulk-mirror agent skipped because the file was not in the explicit phase-4 file list. Also added `.SortedByAsync(s => s)` to the first to address a row-order flake that affected both pg and ss assertions. REMEDIATE also tightened `MsSqlTestContainer.IsDockerUnavailable` (removed redundant `break`), narrowed `SqlConnection.ClearPool` from `ClearAllPools` to a per-connection-string clear, and made the baseline-readiness probe check `shipments` (last seeded table) plus made `CREATE SCHEMA` idempotent for partial-baseline recovery.

## Migration Steps

None required for downstream Quarry consumers.

For test maintainers: tests that currently use `QueryTestHarness.CreateAsync()` get the `Ss`-on-real-`SqlConnection` upgrade transparently. New `useOwnSsSchema: true` parameter is available for tests that need owned-schema isolation; mirrors the existing `useOwnPgSchema` / `useOwnMyDatabase` parameters.

## Performance Considerations

- One additional real connection (SqlConnection) per harness creation. Per-test cost: SqlConnection.OpenAsync (~10–20 ms warm, more on cold pool) + raw `BEGIN TRANSACTION` (~1ms) + `ROLLBACK TRANSACTION` on dispose.
- Container cold-start: ~20–30s on CI runs. The Testcontainers helper amortises this across the full test run via lazy singleton + sp_getapplock-gated baseline.
- No production performance impact — all changes are test-side except the `SqlAssembler` OUTPUT-clause fix, which is a generator-time SQL string rearrangement (no runtime overhead change).

## Security Considerations

- New dependency: `Testcontainers.MsSql 4.*`. Same vendor and major version as the already-shipped `Testcontainers.PostgreSql 4.*` and `Testcontainers.MySql 4.*` references.
- Hardcoded test password `Quarry-Test-2026!` for the per-process `quarry_test_user` login. Test-only, never escapes the container or process. `CHECK_POLICY = OFF` on `CREATE LOGIN` is the documented escape from container password-complexity rules in test context.
- All raw-SQL string concatenation in `MsSqlTestContainer` uses fixed constants or `Guid.NewGuid().ToString("N")`-derived names — no user-controlled input.

## Breaking Changes

### Consumer-facing

**Generator emits a different SQL Server INSERT shape.** Before this PR:

```sql
INSERT INTO [tbl] (cols) VALUES (params) OUTPUT INSERTED.[Id]
```

After this PR:

```sql
INSERT INTO [tbl] (cols) OUTPUT INSERTED.[Id] VALUES (params)
```

The old shape produced `Incorrect syntax near 'OUTPUT'` at runtime on SQL Server. No real `Microsoft.Data.SqlClient` consumer could have been using the old shape. Any consumer who pinned to the old SQL string for some reason (mock-based assertions, schema audits, etc.) will see a string change. The MySQL/SQLite/PostgreSQL emit shapes are unchanged.

The same restructure applies to batch-insert prefixes: SQL Server batch-insert now emits `OUTPUT INSERTED.[Id]` in the prefix (before `VALUES`), and the trailing returning-suffix is suppressed for SQL Server. Other dialects unchanged.

### Internal

- `QueryTestHarness.CreateAsync` gained a `useOwnSsSchema` parameter. Harness is `internal sealed`; no public consumer affected.
- `PgRowOrderExtensions` was renamed to `RowOrderExtensions` (master already did this rename for the MySQL mirror). Doc-comment generalised to mention all three real-execution providers.
- `MsSqlTestContainer` adds new public surface: `EnsureBaselineAsync`, `CreateOwnedSchemaAsync`, `CreateEmptySchemaAsync`, `DropOwnedSchemaAsync`, `GetSaConnectionStringAsync`, `GetUserConnectionStringAsync`, `GetOwnedSchemaConnectionStringAsync`, `OwnedSchemaInfo`. All `internal`.

## Follow-ups

- **#274** filed: "Generator: SQL Server window functions return BIGINT but reader expects INT". Nine sites (ROW_NUMBER, DENSE_RANK, NTILE, etc.) skip Ss execution with comments referencing #274 until the generator fix lands.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
