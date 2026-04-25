# Review: branch 270-mirror-pg-coverage-to-sqlserver

## Classifications
Applied: all C → A. Final: 7A / 0B / 0C / 24D.

| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---------|---------|-----|-----|-------|--------------|
| 1 | Plan Compliance | Plan said ~217 cross-dialect mirror sites; commit message claims 259 added; actual `await ss.Execute*` count is 206 (vs 217 `await pg.Execute*`) | low | C | A | Counts reconciled in PR body. 217 pg sites = 208 ss mirrors + 9 #274 skips. (Adding the 2 missed SchemaTests sites brings ss to 208.) |
| 2 | Plan Compliance | Plan named SchemaTests in the file list footprint, but final diff has no SchemaTests change while reference PR #266 did touch it (CrossDialectSchemaTests still has Pg sites and zero Ss sites) | low | C | A | Mirrored the 2 pg-execute sites in CrossDialectSchemaTests.cs (`Select_SingleColumn` + `Delete_All_NoWhereClause`); also added missing `.SortedByAsync(s => s)` to the first site to address the same flake hazard PR #266 surfaced for PG row-order. |
| 3 | Plan Compliance | Plan called for `MigrationRunner_InsertsHistoryRow_OnSqlServer` to live in `SqlServerIntegrationTests.cs` AND in a separate `SqlServerMigrationRunnerTests.cs`; only the latter shipped (matches the PG pattern) | low | D | D |  |
| 4 | Correctness | `MsSqlTestContainer.IsDockerUnavailable` walks `cur.InnerException` with `cur is not null` test against an inner field that's already been deref'd via `!`, then `break`s on `null` after — net effect is a redundant break but the loop logic is sound | low | C | A | Cleaned up `IsDockerUnavailable`: removed null-forgiving `!` and the redundant `break`. The `for (var cur = (Exception?)ex; cur is not null; cur = cur.InnerException)` shape is now idiomatic. |
| 5 | Correctness | `EnsureBaselineAsync` releases `sp_releaseapplock` but never closes/disposes the sa connection on the success path — connection is `await using` declared at L193 so it cleans up correctly | low | D | D |  |
| 6 | Correctness | `DropOwnedSchemaAsync` calls `SqlConnection.ClearAllPools()` to allow `DROP LOGIN` — this clears pools for ALL SqlConnection users in the process, including the shared `quarry_test_user` pool, forcing concurrent harnesses to re-authenticate. Side-effect-heavy hammer (MsSqlTestContainer.cs:279) | medium | C | A | Replaced `SqlConnection.ClearAllPools()` with `SqlConnection.ClearPool(probe)` keyed by the per-harness user's connection string (probe is a non-opened SqlConnection — no auth round-trip). Other live harnesses' pools are now untouched. |
| 7 | Correctness | `SchemaHasUsersTableAsync` (MsSqlTestContainer.cs:396) treats any non-null/non-DBNull scalar as "ready" but only checks for `users` table — partial-baseline state (login created but tables not seeded) would erroneously skip the rest of provisioning | low | C | A | (1) Renamed/tightened to `SchemaHasSeededTableAsync` and pointed it at the LAST seeded table (`shipments`) with a row-count probe; partial-state baselines now correctly fail the readiness check. (2) Made `CREATE SCHEMA` idempotent via `IF NOT EXISTS (sys.schemas) EXEC('CREATE SCHEMA …')`. (3) Added `DropAllObjectsInSchemaAsync` call before re-creating tables, so partial-baseline retries clean up their own debris. |
| 8 | Correctness | `SeedTableAsync` toggles IDENTITY_INSERT OFF in `finally` but if the OFF call itself throws, subsequent table seeds will fail silently with stale ON state. Acceptable for test setup but worth noting (MsSqlTestContainer.cs:606) | low | D | D |  |
| 9 | Correctness | The raw `BEGIN TRANSACTION`/`ROLLBACK TRANSACTION` workaround returns connection to pool with potential stale session state (IDENTITY_INSERT toggles at server are session-scoped; here only sa-side seeds use them, so the test connection is clean) | low | D | D |  |
| 10 | Correctness | Generator OUTPUT-clause fix is gated on `dialect == SqlDialect.SqlServer` so PG/Lite/MySQL are unaffected. `IdentityColumnName` and `QuotedIdentityColumnName` are populated together in `InsertInfo.FromEntityInfo`, so the dropped null-check on `QuotedIdentityColumnName` (replaced by `IdentityColumnName`) is equivalent | low | D | D |  |
| 11 | Correctness | `CreateOwnedSchemaAsync` reuses the same fixed `TestUserPassword` literal across every per-harness owned-schema login — fine for test env but defeats the purpose of per-harness isolation if a malicious test wanted to authenticate as another harness's user | low | D | D |  |
| 12 | Security | Hardcoded password `Quarry-Test-2026!` (MsSqlTestContainer.cs:44) is test-only, never escapes the container, and is documented inline. Defensible | low | D | D |  |
| 13 | Security | `CHECK_POLICY = OFF` on `CREATE LOGIN` is the documented escape from container password-complexity rules in test context. Acceptable | low | D | D |  |
| 14 | Security | String-concatenated SQL throughout `MsSqlTestContainer` for schema/user/login names. All names are derived from `Guid.NewGuid().ToString("N")` or fixed constants — no user-controlled input. Defensible | low | D | D |  |
| 15 | Security | New dependency `Testcontainers.MsSql 4.*` matches the `Testcontainers.PostgreSql 4.*` line already shipped in PR #266 — same vendor, established. Acceptable | low | D | D |  |
| 16 | Test Quality | `SqlServerIntegrationTests` mirrors `PostgresIntegrationTests` four-test set 1:1, including the same `BuildWantedIds()` constant-folding-defeat pattern. Good | low | D | D |  |
| 17 | Test Quality | `SqlServerMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnSqlServer` only verifies `version`, `name`, `status` columns of `__quarry_migrations` — PG version verifies the same three. Symmetric | low | D | D |  |
| 18 | Test Quality | 9 window-function sites have `await ss.Execute*` replaced with a comment referencing #274; the SQL-string emit assertion above each block remains intact, so emit-side regression guard is preserved | low | D | D |  |
| 19 | Test Quality | The 7 `default(DateTime) → new DateTime(2024, 1, 1)` substitutions in CrossDialectInsertTests / CrossDialectEnumTests preserve the test intent (Insert succeeds + identity > 0) on Lite/Pg; new value sits comfortably inside SqlClient's DATETIME range | low | D | D |  |
| 20 | Test Quality | Phase 5b changes Lite/Pg/My/Ss insert payloads simultaneously — broadens the assertion inputs across all four dialects rather than adding an Ss-only override. Loses the per-dialect-divergence visibility but keeps the cross-dialect mirror principle intact | low | D | D |  |
| 21 | Test Quality | `Ss.Disposable` chain in `QueryTestHarness.DisposeAsync` calls `Ss.Dispose()` before the rollback runs (QueryTestHarness.cs:321) — `SsDb.Dispose` only disposes the wrapper, not the underlying `_sqlConnection`, but ordering is reversed vs Pg (which calls `Pg.Dispose()` AFTER its rollback at L365). Stylistic asymmetry, no functional impact | low | C | A | Moved `Ss.Dispose()` to AFTER the rollback in `QueryTestHarness.DisposeAsync`, mirroring the PG path's wrapper-after-rollback ordering. |
| 22 | Codebase Consistency | `MsSqlTestContainer.cs` mirrors `PostgresTestContainer.cs`'s shape: lazy singleton, Docker-unavailable detection, baseline gate, owned-schema path. Differences are dialect-driven (sp_getapplock vs pg_advisory_lock, IDENTITY_INSERT vs setval, login+user vs search_path) | low | D | D |  |
| 23 | Codebase Consistency | `SqlServerIntegrationTests.cs` follows `PostgresIntegrationTests.cs` line-for-line on assertions and structure; the `(_, _, _, Ss)` deconstruction matches the Pg pattern's `(_, Pg, _, _)` | low | D | D |  |
| 24 | Codebase Consistency | Identifier quoting consistently uses square brackets in SQL Server DDL (MsSqlTestContainer.cs L443+) and in test assertions; PG uses double quotes. Verified across the DDL port | low | D | D |  |
| 25 | Codebase Consistency | Pg path uses `SqlConnection.BeginTransactionAsync()`; Ss path uses raw `BEGIN TRANSACTION`. Asymmetry well-justified by inline comment at QueryTestHarness.cs:179-189 (SqlClient validation issue with QueryExecutor's generic DbCommand path) | low | D | D |  |
| 26 | Codebase Consistency | Phase 5a `CS = "COLLATE SQL_Latin1_General_CP1_CS_AS"` is applied to every NVARCHAR column declaration. Consistent. The override is column-level; existing generator-emitted SQL is unchanged | low | D | D |  |
| 27 | Integration / Breaking Changes | OUTPUT-clause emit shape change for SQL Server (`VALUES (...) OUTPUT INSERTED.[Id]` → `OUTPUT INSERTED.[Id] VALUES (...)`). Old shape was invalid SQL — no real SQL Server consumer could have been using it. Not a breaking change in practice | medium | C | A | Documented as a breaking-change-in-shape-only entry in the PR body (Breaking Changes section) so downstream reviewers and consumers can grep for it. The old shape was invalid SQL Server syntax (`Incorrect syntax near 'OUTPUT'`) — no functional consumer affected. |
| 28 | Integration / Breaking Changes | Manifest `quarry-manifest.sqlserver.md` regenerated: 587-line diff almost entirely OUTPUT-position rewrites plus added shapes from the Phase 3 integration tests. Expected | low | D | D |  |
| 29 | Integration / Breaking Changes | `useOwnSsSchema` parameter added to `QueryTestHarness.CreateAsync` — public test-API surface change, but harness is `internal sealed` so no downstream consumer affected | low | D | D |  |
| 30 | Integration / Breaking Changes | New `Testcontainers.MsSql` dependency adds Docker requirement for SQL Server tests; `Assert.Ignore` path on Docker-unavailable matches PG behavior so devs without Docker see clean Ignored results | low | D | D |  |
| 31 | Integration / Breaking Changes | Harness now opens a real SqlConnection per test on top of the existing Npgsql + SQLite connections; CI cold-start adds ~20-30s for SQL Server container, ~5s for PG, total ~35s integration boot. Acceptable per plan | low | D | D |  |

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan and Phase 4 commit message claim 259 ss execute sites added; actual count is 206 (vs 217 pg). 9 are skipped (#274 window functions), 1 is skipped (#267 Join_Distinct_OrderBy_Limit), and ~1 is the `WindowFunction_Lag_NullableDto_Execution` test that runs as `ss2` rather than `ss`. The "259" number in the commit message appears to count something other than `await ss.Execute*` invocations — possibly tuple-deconstruction touch sites or all `ss.` references. The plan's "~217" target is met if you count meaningful execution sites; the commit message's "259" is misleading | low | Discrepancy between commit-message claim and grep-able reality. Doesn't change correctness but reviewers tracking phase progress against the plan will trip on it |
| Plan named `CrossDialectSchemaTests` as a target file. Final diff has zero changes to it (PR #266 did add Pg execution there). Looking at the file, it has 0 `await pg.Execute*` so the absence is correct, but the plan mentioned it explicitly | low | Minor plan-vs-reality drift; not actionable |
| Plan's Phase 5 specified one commit per triage finding "with `# Triage: <short description>`". Three triage commits shipped (5a, 5b, 5c) matching the 3 categorisation buckets in workflow.md Decisions. Good adherence | low | Plan compliance verified |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `MsSqlTestContainer.DropOwnedSchemaAsync` calls `SqlConnection.ClearAllPools()` (line 279) which clears pools for ALL SqlConnection users in the process, not just for the doomed login. In a parallelised test run this forces every other live harness to re-authenticate on its next command. Recommended: `SqlConnection.ClearPool(perUserConnection)` against the login-specific connection string instead | medium | Cross-test pool churn under parallelisation. Does not produce wrong results but increases per-test cost when many owned-schema tests run concurrently |
| `SchemaHasUsersTableAsync` is a binary "users-table-exists → done" check. If a previous process crashed mid-provisioning (login created, schema created, but `users` table not seeded), the probe would see no `users` table → re-provision → `CREATE LOGIN` would fail with "already exists" since the IF NOT EXISTS guard in `ProvisionLoginAndUserAsync` covers it but `CREATE SCHEMA` does not have that guard (MsSqlTestContainer.cs:205) — would throw `There is already an object named 'quarry_test'` | low | Recovery path from a half-provisioned baseline is fragile. Practical impact is rare (would need a process to crash between line 205 and seed completion) but the asymmetry between login (idempotent) and schema (not) is a sharp edge |
| `IsDockerUnavailable` (line 149-165) iterates inner exceptions but the loop body sets `cur = ex.InnerException!` via the for-update clause, while the `if (cur.InnerException is null) break;` inside the body is redundant with the for-condition check. The non-null-forgiving `!` on `cur.InnerException!` will produce a NullReferenceException on the iteration AFTER reaching null, except the loop body already broke. Functional but confusing logic shape | low | Maintenance burden; the loop reads correctly but defies the for-loop idiom |
| `QueryTestHarness.DisposeAsync` calls `Ss.Dispose()` BEFORE the rollback runs (line 321), then runs the rollback through `_sqlConnection` (line 333). PG path calls `Pg.Dispose()` AFTER the rollback (line 365). Pg.Dispose / SsDb.Dispose only dispose the wrapper context, so functionally equivalent, but the ordering asymmetry is a footgun if anyone later changes one of the wrappers to dispose the underlying connection | low | Stylistic asymmetry; no functional impact today |
| Generator OUTPUT-fix: condition `dialect == SqlDialect.SqlServer && insertInfo?.IdentityColumnName != null` is correctly applied at both the inline OUTPUT-emit site and the suffix-skip site. PG/Lite/MySQL paths unchanged | low | Verified safe |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 5b changed `default(DateTime)` to `new DateTime(2024, 1, 1)` for ALL four dialects in 7 tests, not just Ss. This loses the explicit-default-DateTime-payload coverage on Lite/Pg/My — those dialects DID accept `default(DateTime)` before. Test intent is preserved but a generator regression that broke `default(DateTime)` binding on Pg/Lite would no longer be caught | low | Slight coverage loss; the alternative (per-dialect override) would be more verbose. Acceptable trade-off but worth noting |
| `WindowFunction_Lag_NullableDto_Execution` was previously destructured as `(Lite, Pg, _, _)`; Phase 4 updated to `(Lite, Pg, _, Ss)` and added a parallel `ss2` build. Mirror is correct but uses `ss2` rather than `ss` — easy to miss in greps that look for `ss.Execute` | low | Minor inconsistency; the file has both `ss.Execute` (counted) and `ss2.Execute` for this one test |
| `WhereInCollection_OnSqlServer_ExecutesSuccessfully` uses the same `BuildWantedIds()` method-call defeat pattern as `WhereInCollection_OnPostgreSQL_ExecutesSuccessfully`. Verified: this preserves the runtime collection-expansion code path that PR #266 review finding #11 specifically called out | low | Consistent with PG-side guard |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `MsSqlTestContainer` matches `PostgresTestContainer` shape; differences are all dialect-driven and explained in inline comments | low | Verified |
| Pg uses `BeginTransactionAsync()`, Ss uses raw `BEGIN TRANSACTION` SQL. Asymmetry is necessary (SqlClient validation issue with Quarry's generic DbCommand), and the inline comment at QueryTestHarness.cs:179-189 explains why. Justified | low | Verified |
| Identifier quoting (square brackets for SS, double quotes for PG) is consistent across DDL, seed inserts, and test assertions. Verified | low | Verified |
| `RowOrderExtensions` rename from `PgRowOrderExtensions` is correctly generalised in doc comments to mention both PG and SQL Server | low | Verified |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| OUTPUT-clause emit shape change for SQL Server. `INSERT ... VALUES (...) OUTPUT INSERTED.[Id]` → `INSERT ... OUTPUT INSERTED.[Id] VALUES (...)`. The old shape produced "Incorrect syntax near 'OUTPUT'" at runtime on SQL Server — no real SQL Server consumer could have used Quarry against an actual SqlConnection prior to this fix. The shape change is therefore not breaking in any practical sense, only in mock-asserted shape | medium | Reviewers checking the manifest diff and any external SQL-shape regression tests will see ~30 line moves; documented in workflow.md Decisions |
| `quarry-manifest.sqlserver.md` regenerated with ~587 line diff: most are OUTPUT-position rewrites (existing shapes), the rest are new shapes added by Phase 3 integration tests | low | Expected; deterministic generator output |
| `useOwnSsSchema` added to `QueryTestHarness.CreateAsync`. Harness is `internal sealed`, no public API surface impact | low | Verified |
| Docker requirement extended: SQL Server-backed tests now require Docker, mirroring PG. `Assert.Ignore` path with cached reason matches PG. Acceptable | low | Verified |
| Per-test cost: real SqlConnection open + raw BEGIN/ROLLBACK adds ~20-30ms per test. PG cold-start ~5s, SQL Server cold-start ~20-30s, total ~35s integration boot. Documented as acceptable in plan Risks section | low | Verified |

## Issues Created
- #274: Generator: SQL Server window functions return BIGINT but reader expects INT
