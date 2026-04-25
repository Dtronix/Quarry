## Summary

- Closes #269
- Mirrors PR #266's PostgreSQL execution-coverage pattern to MySQL: every existing `CrossDialect*Tests` test that already executed on `Lite` and on real PG now ALSO executes against a real MySqlConnector + Testcontainers MySQL 8.4 container.
- Closes the symmetric gap that #258 / #266 was the cautionary tale for: previously `My` and `Ss` were on `MockDbConnection`, so a generator regression that produces broken SQL on the real MySQL provider could ship while every mock-based assertion stayed green. After this PR, MySQL is on parity with PG; SqlServer is still mock-only.

## Reason for Change

The risk that motivated PR #266 — that `MockDbConnection` will pass any SQL string the generator produces, even when the real provider would reject it — applies symmetrically to MySQL. PR #266 demonstrated the pattern is feasible on PG; this PR finishes the work for `My`. Until this PR, every `My`-targeted assertion in the suite was an SQL-string-shape check, not an execution check.

## Impact

- `QueryTestHarness.My` moves off `MockDbConnection` onto a real `MySqlConnection` backed by a shared Testcontainers MySQL 8.4 container — "free" MySQL execution coverage for any existing test that wants to flip from `Lite.ExecuteXxxAsync` to `My.ExecuteXxxAsync`.
- 259 mirror sites added across 22 `CrossDialect*Tests.cs` files. Wherever an existing test ran `await pg.ExecuteXxxAsync(...)` and asserted, the same assertions now also run against `my`. `Diagnostics` had no `Execute*` calls so it was left alone.
- Four focused MySQL integration tests added (entity insert, batch insert, where-in-collection, migration runner) — same shape as PR #266's PG fixture.
- Developers and CI runners need Docker to run the MySQL-backed tests. Developers without Docker get a single clear "Install Docker" Ignored result instead of cascading exceptions (`MySqlTestContainer.GetContainerAsync` catches Docker-unavailable errors and calls `Assert.Ignore` — same heuristic as the PG side).

## Plan items implemented as specified

- **Phase 1** — `Testcontainers.MySql 4.*` added to `Quarry.Tests.csproj` next to the existing `Testcontainers.PostgreSql 4.*`. `MySqlConnector 2.*` was already referenced. Lazy, process-wide `MySqlTestContainer` helper. Single regression-probe test (`ContainerBootstraps_OnMySQL`) confirming Docker bootstrap works on CI.
- **Phase 2** — DDL port aligned with `Quarry.Migration.SqlTypeMapper.MapMySql`: `INT NOT NULL AUTO_INCREMENT PRIMARY KEY` for `Col<int>` PKs, `TINYINT(1)` for `Col<bool>`, `DECIMAL(18, 2)` for `Col<decimal>`/`Col<Money>`, `DATETIME` for `Col<DateTime>` and `Col<DateTimeOffset>`, `CHAR(36)` for `Col<Guid>`, backtick-quoted identifiers, `GENERATED ALWAYS AS (...) STORED` for `products.DiscountedPrice`. FK constraints intentionally omitted (mirrors SQLite + PG harness — InnoDB enforces FKs, omitting them keeps DELETE-by-where-clause tests passing).
- **Phase 2** — `QueryTestHarness.My` upgraded from `MockDbConnection` to a real `MySqlConnection` against the shared `quarry_test` baseline database. Default to transactional (`BEGIN`/`ROLLBACK`); `useOwnMyDatabase: true` opt-out creates and drops a per-test database for migration-runner / commit-visible-state tests.
- **Phase 3** — Four focused MySQL integration tests in `[Category("MySqlIntegration")]`: `EntityInsert_OnMySQL_ExecutesSuccessfully`, `InsertBatch_OnMySQL_ExecutesSuccessfully`, `WhereInCollection_OnMySQL_ExecutesSuccessfully`, plus `MySqlMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnMySQL` (the exact GH-258-class regression gate but for MySQL).
- **Phase 4** — Full cross-dialect mirror across all 22 `CrossDialect*Tests.cs` files. 259 mirror sites added (the 5 asymmetric sites surfaced by review were also mirrored — see "Gaps in original plan implemented" below).
- **Phase 5** — Inline triage of MySQL-only behavior surfaced by Phase 4 execution. See "Deviations from plan implemented" — three categories of fix landed in this PR.

## Deviations from plan implemented

- **Database isolation strategy: shared database + transactional default, per-test owned-database opt-out.** Plan called for this; implementation matches. The PG `useOwnPgSchema` pattern adapts to MySQL as `useOwnMyDatabase` because MySQL has no schema-as-namespace concept.
- **Cross-process baseline gate via `GET_LOCK`** instead of PG's `pg_advisory_lock`. Direct analogue with the same block-and-probe pattern.
- **`Col<DateTimeOffset>` → `DATETIME`** per `SqlTypeMapper.MapMySql`. Decision documented in workflow.md; XML doc in `MySqlTestContainer.CreateSchemaObjectsAsync` flags the offset-loss caveat. No tests in the cross-dialect mirror regressed on this point (the seed strips offset suffixes from `events.ScheduledAt`/`CancelledAt`).
- **Server-config pinning at the container level.** Two departures from `mysql:8.4` defaults applied via `WithCommand`:
  - `--collation-server=utf8mb4_bin` (default is `utf8mb4_0900_ai_ci`). Default MySQL collation is case-insensitive; PG / SQLite / SqlServer are case-sensitive. Surfaced by `Join_Where_InClause`, `Where_Any_And_All_MultipleSubqueries`, `Where_ContainsRuntimeCollection` failing on My with the seed's mixed-case `Status` values. Pinning `utf8mb4_bin` gives byte-level cross-dialect parity.
  - `--sql-mode=...,NO_BACKSLASH_ESCAPES`. MySQL's default sql_mode treats backslash as a string-escape, so `'\'` is a parse error. PG / SQLite / SqlServer all treat `'\'` as a literal backslash. Quarry's generator emits the ANSI form `LIKE '%foo\_bar%' ESCAPE '\'`. Surfaced by `Where_Contains_LiteralWithMetaChars_InlinesWithEscape` and `Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern`. Other sql_mode flags preserved verbatim (STRICT_TRANS_TABLES, ONLY_FULL_GROUP_BY, etc.).
- **`IgnoreCommandTransaction=True` on the MySQL connection string.** MySqlConnector enforces that `DbCommand.Transaction` match the connection's active transaction. Quarry-emitted DbCommands don't set `Transaction`, so the harness's `BEGIN`/`ROLLBACK` envelope would otherwise reject every command (Npgsql / Sqlite are more permissive).
- **Init script granting `ALL PRIVILEGES` to the application user.** Testcontainers.MySql provisions the `mysql` user with privileges scoped to MYSQL_DATABASE only — no global `CREATE DATABASE`. Per-test database creation needs broader privileges. Added a `/docker-entrypoint-initdb.d/01-grant-all.sql` init script via `WithResourceMapping`. Test-only ephemeral container; no security concern.
- **`PgRowOrderExtensions` renamed to `RowOrderExtensions`.** Both PG and MySQL InnoDB lack insertion-order guarantees without `ORDER BY`. The `SortedByAsync` extension is now used symmetrically by PG and My mirror sites. PG callers remain method-name compatible.

## Gaps in original plan implemented

- **5 asymmetric mirror sites surfaced by review.** Five sites deconstructed the harness as `var (Lite, Pg, _, _) = t;` (discarding `My`), so the agent-driven mechanical mirror correctly skipped them — adding the My side required widening the deconstruction tuple, which is a manual decision rather than a mechanical edit. These were upgraded from review-recommended Class C to Class A and addressed in this PR:
  - `CrossDialectSelectTests::NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsNullForNoMatch`
  - `CrossDialectSelectTests::NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsNullForNoMatch`
  - `CrossDialectSelectTests::NoSelect_ExecuteFetchSingleOrDefaultAsync_ThrowsOnMultipleRows`
  - `CrossDialectWindowFunctionTests::WindowFunction_Lag_NullableDto_Execution`
  - `CrossDialectSubqueryTests::Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead` (NULL-into-non-nullable-decimal exception assertion — MySqlConnector surfaces the same exception class as Npgsql)

  Mirror count after this fix: 259 sites total. Symmetric Pg/My execution surface across the entire CrossDialect suite.

- **Money round-trip test missing My-side INSERT** (surfaced by Phase 4 execution). `RoundTrip_InsertThenSelect_PreservesMoneyValue` had Lite + Pg insert blocks but the agent didn't recognise the duplicate-INSERT-setup pattern and so didn't add a parallel My insert. Manually added — straightforward.

## Migration Steps

None. Test-only change. No production source code under `src/Quarry/**` is modified. The `MigrationRunner.SqlDialect.MySQL` plumbing was already complete in production before this PR; the new `MySqlMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnMySQL` verifies it executes end-to-end.

## Performance Considerations

- Test suite: `Quarry.Tests` adds ~20s of wall-clock time for the MySQL container startup + ~3000 baseline-database test setups. Total time on a Docker-equipped machine: ~1m for `Quarry.Tests.dll` (was ~40s pre-PR). Well within CI budgets.
- Per-test overhead: pool-backed connection acquire + `USE quarry_test` + `BEGIN` + `ROLLBACK`. Symmetric with the PG-side overhead introduced by PR #266.

## Security Considerations

- New dev-only NuGet dependency: `Testcontainers.MySql 4.*` in `Quarry.Tests.csproj`. Not shipped to consumers.
- Init script grants `ALL PRIVILEGES ON *.* WITH GRANT OPTION` to the test user `mysql@%`. Test-only ephemeral container, never reachable from outside the test process; required for per-test `CREATE DATABASE` and the migration runner test. Comment in the source explains the rationale.
- No new attack surface in production code. All changes are test-side.

## Breaking Changes

**Consumer-facing:** None. Public API is unchanged.

**Internal:**
- `QueryTestHarness.CreateAsync` adds an optional parameter `useOwnMyDatabase = false`. Existing callers compile without change.
- `PgRowOrderExtensions` renamed to `RowOrderExtensions` (internal class in `Quarry.Tests`). No external consumers.

## Known Limitation / Follow-up: #273

The `--sql-mode=...,NO_BACKSLASH_ESCAPES` flag pinned on the test container in `MySqlTestContainer.GetContainerAsync` is a **stop-gap**, not the proper fix. It papers over a genuine generator-level defect: Quarry's `LIKE` emit produces `'%foo\_bar%' ESCAPE '\'` for all four dialects, but that SQL is **not portable to default-mode MySQL** (where backslash is a string-escape character). A real consumer running stock MySQL 8 without `NO_BACKSLASH_ESCAPES` set in their `sql_mode` would hit a 1064 syntax error on any `Contains` / `StartsWith` / `EndsWith` query against text data with LIKE metacharacters.

The proper fix is filed as **#273** — replace the flat `SqlDialect` enum threaded through the generator with a `SqlDialectConfig` carrier mirroring per-context flags from `QuarryContextAttribute`, and use that structure to emit MySQL-portable `LIKE` SQL regardless of `sql_mode`. Issue #273 includes the full design space (three options for the LIKE-emit fix, four phases for landing the structural refactor) and identifies the broader extensibility opportunity (`MySqlAnsiQuotes`, `PgStandardConformingStrings`, etc.) that the same carrier addresses.

This PR ships test-only coverage so MySQL execution gaps are visible going forward; the generator fix in #273 is what removes the 1064 hazard for production consumers and lets the test-container `NO_BACKSLASH_ESCAPES` pin be removed.

## Review findings

One REVIEW pass on this branch produced 9 initial findings, plus a 10th finding surfaced during the architectural discussion of the test-container `sql_mode` pin: 5 low (asymmetric mirror gaps, all upgraded to Class A and addressed in commit `4ba1622`), 1 medium (the latent generator bug, deferred to issue #273 as Class C), 4 info (compliance confirmations / non-issues, all Class D). No critical or high-severity findings. Final classification: 5A / 0B / 1C / 4D.

Tests are green at 3319/3319 across all three test projects (`Quarry.Tests` 3001, `Quarry.Analyzers.Tests` 117, `Quarry.Migration.Tests` 201).
