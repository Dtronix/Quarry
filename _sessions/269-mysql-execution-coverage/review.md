# Review: #269 (PR pending)

Branch `269-mysql-execution-coverage` mirrors PR #266's PostgreSQL execution-coverage pattern to MySQL. Test-only diff: 4 commits, ~1,800 net lines added across 28 files. Source code under `src/Quarry/**` is untouched; new code lives entirely in `src/Quarry.Tests/`.

## Classifications

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|---------------------|-----|-----|-------|--------------|
Applied: 1→A, 2→A, 3→A, 4→A, 5→A (user-overridden from Rec C). Then a 10th finding surfaced in the architectural discussion of the `NO_BACKSLASH_ESCAPES` test-container pin — re-evaluated as a latent generator bug, filed as issue #273, classified C. Final: 5A / 0B / 1C / 4D.

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|---------------------|-----|-----|-------|--------------|
| 1 | Test Quality | `CrossDialectSubqueryTests.cs` line 1630-1639: PG NULL-into-non-nullable-decimal exception assertion has no MySQL mirror | low | C | A | Added my2 mirror in `Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead`; deconstruction `(Lite, Pg, My, _)`. Test passes (MySQL surfaces same exception class on NULL-into-decimal read) |
| 2 | Test Quality | `CrossDialectSelectTests.cs` `NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsNullForNoMatch` has no My mirror (deconstruction discards `My`) | low | C | A | Changed deconstruction to `(Lite, Pg, My, _)`, added parallel my chain + assertion |
| 3 | Test Quality | `CrossDialectSelectTests.cs` `NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsNullForNoMatch` has no My mirror (deconstruction discards `My`) | low | C | A | Same fix as #2 |
| 4 | Test Quality | `CrossDialectSelectTests.cs` `NoSelect_ExecuteFetchSingleOrDefaultAsync_ThrowsOnMultipleRows` has no My mirror (deconstruction discards `My`) | low | C | A | Same fix as #2 + parallel try/catch InvalidOperationException assertion |
| 5 | Test Quality | `CrossDialectWindowFunctionTests.cs` LAG-function test has Pg2 execute block but no my2 mirror | low | C | A | `WindowFunction_Lag_NullableDto_Execution`: deconstruction `(Lite, Pg, My, _)`; added my2 LAG chain + 3-row assertions matching pgByTotal |
| 6 | Codebase Consistency | `MySqlIntegrationTests.ContainerBootstraps_OnMySQL` uses `Does.StartWith("8.4")` but never invokes `EnsureBaselineAsync` — fine for the probe but skips the GET_LOCK gate exercised on every harness path | info | D | D |  |
| 7 | Security | Init script grants `ALL PRIVILEGES ON *.* WITH GRANT OPTION` to `mysql@%` — test-only ephemeral container | info | D | D |  |
| 8 | Plan Compliance | Plan said the rename targeted PG callers in CteTests + NavigationJoinTests + SelectTests; rename + new `SortedByAsync` callers verified across all sites | info | D | D |  |
| 9 | Correctness | `MySqlMigrationRunnerTests.TearDown` correctly null-checks `_connection` and `_database` before access; pattern is more defensive than the PG twin which still NREs locally | info | D | D |  |
| 10 | Plan Compliance | Test-container `--sql-mode=...,NO_BACKSLASH_ESCAPES` is masking a generator-level defect: Quarry-emitted `LIKE '...\_...' ESCAPE '\'` is not portable to default-mode MySQL; consumer servers without `NO_BACKSLASH_ESCAPES` would hit 1064 | medium | B | C | Filed as **#273** (SqlDialectConfig refactor + LIKE-emit fix). PR #271 ships the test-container mitigation as a stop-gap; the proper generator fix lands in the follow-up. Reclassified from B (mitigation in this PR) to C (deferred to dedicated issue with full design discussion) |

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All five plan phases implemented as specified. Phase 1 skeleton, Phase 2 DDL/seed + harness wiring, Phase 3 four focused tests, Phase 4 254-site mirror, Phase 5 triage handled inline (collation + sql_mode + manual Money round-trip insert) — see commit c2d3857 message. | info | Confirms decisions executed faithfully |
| `RowOrderExtensions` rename applied uniformly: file renamed `PgRowOrderExtensions.cs` → `RowOrderExtensions.cs`, class + XML doc generalised, no `PgRowOrderExtensions` reference remains; PG callers and new My callers both use `SortedByAsync`. | info | Decision compliance verified |
| `Col<DateTimeOffset>` → `DATETIME` (no offset) per `SqlTypeMapper.MapMySql` at SqlTypeMapper.cs:94. Implemented at `MySqlTestContainer.CreateSchemaObjectsAsync` lines 322-326 and seed strips offset suffixes (`'2024-06-15 10:30:00'`). XML doc explicitly flags the offset-loss caveat for Phase 5 triage. | info | Decision compliance verified |
| FK constraints intentionally omitted from MySQL DDL (mirrors SQLite + PG harness). | info | Decision compliance verified |
| Two server-config departures (`utf8mb4_bin`, `NO_BACKSLASH_ESCAPES`) added in commit c2d3857 with extensive in-code comments naming the surfaced tests. Matches the workflow.md decision verbatim. | info | Decision compliance verified |
| `IgnoreCommandTransaction=True` set via `MySqlConnectionStringBuilder` in `QueryTestHarness.CreateAsync` lines 156-160. Decision compliance verified. | info | — |
| GRANT ALL init script is supplied via `WithResourceMapping` to `/docker-entrypoint-initdb.d/01-grant-all.sql` (UTF-8 byte payload). Init-script path is correct (mysql:8.4 entrypoint runs `*.sql` from that directory before readiness probe). | info | Decision compliance verified |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `QueryTestHarness.CreateAsync` exception-unwind path (lines 183-226) correctly handles MySQL state in the right order: rollback transaction → drop owned database (only if connection still Open) → dispose connection, all wrapped in inner try/catch so a downstream failure preserves the original exception. PG block is identical-shape. | info | Resource leak avoided on partial-setup failure |
| `MySqlMigrationRunnerTests.TearDown` (lines 49-66) null-checks `_connection` AND `_database` before usage. This is more defensive than `PostgresMigrationRunnerTests.TearDown` (PG twin still NREs locally when Docker is absent — noted as the pre-existing baseline failure in workflow.md). | info | Phase 1 plan called this out; correctly fixed |
| Init-script SQL bytes terminate with `\n` and use single-quoted SQL identifiers — correct for the mysql:8.4 entrypoint runner | info | — |
| `TableExistsAsync` (line 229) uses `cmd.CreateParameter` + `@db`/`@tb`. MySqlConnector accepts `@`-prefixed names; bound by name. Correct. | info | — |
| `EnsureBaselineAsync` `GET_LOCK` block in finally always releases via `RELEASE_LOCK`. If the connection has already failed mid-DDL, `RELEASE_LOCK` will throw too — the catch is implicit (the outer `try/finally` of `_baselineLock.WaitAsync` will still release the semaphore). Acceptable: a torn baseline state is better surfaced as a hard failure than masked. | info | — |
| `MySqlMigrationRunnerTests.SetUp` runs `CREATE DATABASE \`{x}\`; USE \`{x}\`;` as a single multi-statement command. MySqlConnector defaults `AllowUserVariables=False` and `AllowLoadLocalInfile=False` but multi-statement is enabled by default. Acceptable. | info | — |
| `QueryTestHarness.DisposeAsync` ordering: My (rollback → drop → dispose) → Pg (rollback → drop → dispose) → Lite → Mock → Sqlite. Matches the create order such that an exception during one provider's teardown doesn't leak the rest. | info | — |

No correctness defects found.

## Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Init script grants `ALL PRIVILEGES ON *.* WITH GRANT OPTION` to `mysql@%`. Test-only ephemeral container, never reachable from outside the test process; required for per-test `CREATE DATABASE` and the migration runner test. Comment in `MySqlTestContainer.GetContainerAsync` lines 79-88 explains the rationale. | info | — |
| `EnsureBaselineAsync` interpolates `BaselineDatabaseName` (compile-time constant `"quarry_test"`) into SQL; not user-controlled. | info | No injection surface |
| `CreateOwnedDatabaseAsync` interpolates `Guid.NewGuid().ToString("N").Substring(0, 12)` into the `CREATE DATABASE` statement (line 249). Hex-only output, safe. | info | — |
| `MySqlMigrationRunnerTests` interpolates `migtest_<guid10>` into `CREATE DATABASE` / `DROP DATABASE` (line 39, 54). Hex-only, safe. | info | — |

No security concerns beyond the noted info-level items.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **Subquery NULL-decimal asymmetry**: `CrossDialectSubqueryTests.cs` lines 1630-1639 has a Pg2 block asserting that a SELECT projecting an aggregate that resolves to SQL NULL into a non-nullable decimal throws `QuarryQueryException` / `InvalidOperationException` / `InvalidCastException`. There is no parallel My2 block. Same materialisation rule applies on MySqlConnector. Adding the mirror would harden against an InvalidCastException divergence. | low | Asymmetric coverage — the only such asymmetry where PG asserts an exception |
| **CrossDialectSelectTests three-site asymmetry**: tests `NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsNullForNoMatch` (line 835), `NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsNullForNoMatch` (line 919), and `NoSelect_ExecuteFetchSingleOrDefaultAsync_ThrowsOnMultipleRows` (line 934) deconstruct the harness as `var (Lite, Pg, _, _) = t;`, discarding `My`. The PG side asserts and there is no My mirror. Three matched siblings (`NoSelect_ExecuteFetchSingleAsync`, `NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsEntity`, `NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsEntity`) DO mirror. Inconsistent shape. | low | Coverage gap; add `var (Lite, Pg, My, _) = t;` and one `await my.ExecuteFetchXxxAsync()` block |
| **WindowFunction LAG asymmetry**: `CrossDialectWindowFunctionTests.cs` `WindowFunction_LagFunction` test (lines 580-617) has a `pg2 = Pg.Orders()...Prepare()` execute-and-assert block but no `my2`. PG block asserts `pgByTotal[0..2]` row values from `LAG`. MySQL 8 supports LAG; mirror should pass. | low | Coverage gap |
| Counts: `await pg.Execute*` / `await pg2.Execute*` total 259 vs `await my.Execute*` / `await my2.Execute*` total 254 across `Quarry.Tests/SqlOutput/CrossDialect*Tests.cs`. The 5-site delta matches the four asymmetries above (the subquery asymmetry counts as one missed; the three SelectTests asymmetries plus the WindowFunction one make four more — total five). Plan target was "~254 mirror sites" which matches the My-side count exactly. | info | Mirror count matches plan; the gaps are the only known asymmetries |
| `MySqlIntegrationTests` correctly mirrors `PostgresIntegrationTests` shape: same three test names (entity insert, batch insert, where-in-collection), same `BuildWantedIds()` constant-inlining-defeat helper, same explicit-projection workaround comments. The bootstrap probe is the only my-only test. Comments correctly attribute the GH-258 motivation. | info | — |
| `MySqlMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnMySQL` mirrors `PostgresMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnPostgreSQL` semantically — same demo migration, same history-table assertions on `(version, name, status)`. Uses `SqlDialect.MySQL` which is a real enum value with full DDL support per `DdlRenderer.cs` and the `__quarry_migrations` create-table case at `MigrationRunner.cs:457`. | info | — |
| `MySqlIntegrationTests` test `EntityInsert_OnMySQL_ExecutesSuccessfully` performs assertions on `City` and `Street` separately rather than as a tuple — symmetric with `PostgresIntegrationTests` but slightly verbose. Acceptable, matches reference. | info | — |
| Test isolation pattern (transactional rollback default, owned-DB opt-out for migration) is sound. Cross-process baseline gating (`GET_LOCK`) is correct. Per-test database names use 12-hex-char Guid suffix (collision risk negligible). | info | — |

The four asymmetric-coverage findings above are class-C (separate issue / could land in a follow-up). None block the PR; the mirror plan said "~254 sites" and the mirror produced exactly 254 my-side execute calls.

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `MySqlTestContainer.cs` mirrors `PostgresTestContainer.cs` very closely: same `_containerLock` / `_baselineLock` / `_dockerUnavailableReason` shape, same `IsDockerUnavailable` heuristic, same `EnsureBaselineAsync` block-and-probe pattern, same XML-doc structure. Substantive divergences are the substantive ones (database-vs-schema, `GET_LOCK` vs `pg_advisory_lock`, init-script + `WithCommand` for sql_mode/collation). | info | High consistency; reviewer can read either file with the other's mental model |
| `MySqlIntegrationTests.cs` mirrors `PostgresIntegrationTests.cs` byte-for-byte where possible: same XML doc shape, same comments referencing GH-258, same `BuildWantedIds()` helper, same `var (_, _, My, _) = t;` deconstruction (PG uses `var (_, Pg, _, _)`), same explicit-projection workaround note. | info | — |
| `MySqlMigrationRunnerTests.cs` mirrors `PostgresMigrationRunnerTests.cs`. Two minor improvements: (a) `_connection`/`_database` are nullable `?`-typed instead of `null!` (more defensive) and (b) TearDown null-checks both. The PG twin's NRE-on-Docker-absent is a pre-existing issue unrelated to this PR. | info | — |
| `QueryTestHarness.cs` parameter order: My fields appear after Pg fields, matching the alphabetical Pg/My/Lite/Ss style at the call sites. `DisposeAsync` uses My-then-Pg-then-Lite ordering — matches `CreateAsync`. XML doc on `My` property updated to "real `MySqlConnection`". | info | — |
| `RowOrderExtensions` (post-rename) class signature unchanged so PG callers still resolve. XML doc generalised. PG `pgResults`/`myResults` symmetry sites use the same `SortedByAsync` extension. | info | — |
| `MySqlTestContainer.GetConnectionStringAsync` returns `container.GetConnectionString()` — note that this is the Testcontainers default (`...Database=quarry_test...` because `WithDatabase` was passed). `IgnoreCommandTransaction` is appended downstream in `QueryTestHarness.CreateAsync`, not here. Same shape as PG side. | info | — |
| All MySQL DDL emits identifiers with backticks; mirrors `SqlTypeMapper.MapMySql` per the decision. `CREATE VIEW \`Order\` AS SELECT * FROM \`orders\`` matches the PG `"Order"` view shape, preserving mixed case. | info | — |

No consistency defects found.

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test-only change. No source code under `src/Quarry/**` modified. Production consumers are not affected. | info | — |
| New dev-only NuGet dependency: `Testcontainers.MySql` Version `4.*` added to `Quarry.Tests.csproj` next to existing `Testcontainers.PostgreSql` `4.*`. `MySqlConnector` `2.*` was already present. No version bumps. | info | — |
| API surface: `QueryTestHarness.CreateAsync` adds optional parameter `useOwnMyDatabase = false`. Existing callers compile without change (default value preserved); `useOwnPgSchema` is unchanged. | info | — |
| `PgRowOrderExtensions` renamed to `RowOrderExtensions`. Internal class. No external consumers (verified — `Quarry.Tests` is `internal`). | info | — |
| `quarry-manifest.mysql.md` regenerated by source generator: 366-line growth reflecting the new `Execute*` call sites in the manifest emitter. Auto-generated; reviewing each entry is unnecessary. | info | — |
| No migration of in-memory state, no schema versioning concerns, no published-API contract changes. | info | — |

No breaking changes.
