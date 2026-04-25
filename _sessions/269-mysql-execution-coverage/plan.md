# Plan: 269-mysql-execution-coverage

This plan mirrors the phasing PR #266 used for PostgreSQL, applied to MySQL with the substitutions captured in `workflow.md` Decisions. The reference implementation already lives on `master` — most of this work is mechanically symmetrical. Each phase is independently committable and runnable.

## Phase 1 — Add Testcontainers.MySql + MySqlTestContainer skeleton

**Goal:** Get a Docker-backed MySQL 8.4 container reachable from the test process with a single sanity-check test that proves the bootstrap works on CI. No harness wiring yet, no DDL, no mirror.

### Changes

1. `src/Quarry.Tests/Quarry.Tests.csproj` — add `<PackageReference Include="Testcontainers.MySql" Version="4.*" />` next to `Testcontainers.PostgreSql`. `MySqlConnector 2.*` is already referenced and stays.

2. `src/Quarry.Tests/Integration/MySqlTestContainer.cs` (new) — directly parallel to `PostgresTestContainer.cs`, but with skeleton-only state for this phase:
   - `internal const string BaselineDatabaseName = "quarry_test";`
   - `private static readonly SemaphoreSlim _containerLock = new(1, 1);`
   - `private static MySqlContainer? _container;`
   - `private static string? _dockerUnavailableReason;`
   - `public static async Task<string> GetConnectionStringAsync()` — returns the container's connection string augmented with `Database=quarry_test`.
   - `public static async Task<MySqlContainer> GetContainerAsync()` — lazy startup with `MySqlBuilder("mysql:8.4").WithDatabase(BaselineDatabaseName).Build()`.
   - Docker-unavailable detection identical in shape to PG — copy `IsDockerUnavailable(Exception)` heuristic verbatim. On unavailable, cache the reason and `Assert.Ignore(_dockerUnavailableReason)`.
   - `EnsureBaselineAsync` / `CreateOwnedDatabaseAsync` / `CreateSchemaObjectsAsync` / `SeedDataAsync` are stubbed in this phase — they land in Phase 2. Keep the file scoped to "container starts and a connection is reachable" for the Phase 1 commit.

3. `src/Quarry.Tests/Integration/MySqlIntegrationTests.cs` (new) — placeholder fixture with one test, `[Category("MySqlIntegration")]`:

   ```csharp
   [Test]
   public async Task ContainerBootstraps_OnMySQL()
   {
       var cs = await MySqlTestContainer.GetConnectionStringAsync();
       await using var conn = new MySqlConnection(cs);
       await conn.OpenAsync();
       await using var cmd = conn.CreateCommand();
       cmd.CommandText = "SELECT VERSION()";
       var v = (string?)await cmd.ExecuteScalarAsync();
       Assert.That(v, Does.StartWith("8.4"));
   }
   ```

   This is the single regression probe. Its purpose is to fail loudly if the Docker daemon, the image pull, or the connection string format is broken — symmetric to the smoke test in PR #266's earliest commit.

### Tests

- New: `MySqlIntegrationTests.ContainerBootstraps_OnMySQL` — passes when Docker is up; `Assert.Ignore`d otherwise.
- All existing tests continue to pass (no harness change yet).

### Commit

`Add Testcontainers.MySql + MySqlTestContainer skeleton (#269)`

---

## Phase 2 — Port DDL + seed; upgrade QueryTestHarness.My

**Goal:** The MySQL container holds the same baseline schema and seed data as PG/SQLite, and `QueryTestHarness.My` is wired to a real `MySqlConnection` against that baseline. After this phase the existing CrossDialect tests still only assert SQL-string shape on My (no execute mirrors yet), but the harness can run a real query.

### Changes

1. `src/Quarry.Tests/Integration/MySqlTestContainer.cs` — fill in the rest of the body:

   - **`EnsureBaselineAsync`** — once-per-process gate that uses MySQL `GET_LOCK('quarry_test_baseline', 60)` for cross-process safety. Inside the lock: probe `information_schema.tables` for `users` in the `quarry_test` DB; if missing, run `CreateSchemaObjectsAsync` + `SeedDataAsync`; release lock. Mirrors PG's `pg_advisory_lock` block-and-probe pattern.

   - **`CreateOwnedDatabaseAsync(MySqlConnection)`** — for the migration-runner / own-database opt-out. Generates `test_<guid12>`, executes `CREATE DATABASE \`test_xxx\`;`, then runs the standard DDL+seed inside that DB. Returns the database name so the caller can `DROP DATABASE` on dispose.

   - **`CreateSchemaObjectsAsync`** — DDL port aligned with `Quarry.Migration.SqlTypeMapper.MapMySql`. Backtick-quote every identifier. Use `INT NOT NULL AUTO_INCREMENT PRIMARY KEY` for `Col<int>` PKs. Drop FK declarations (mirror SQLite/PG harness decision). Use `TINYINT(1)`, `DECIMAL(18,2)`, `DATETIME`, `CHAR(36)` per the mapper. Use ``CREATE VIEW `Order` AS SELECT * FROM `orders`;``. Same 11 tables + 1 view as the PG port, in the same column order.

   - **`SeedDataAsync`** — same row sets as the PG seed, but: (a) string literals quoted with single-quotes, (b) timestamps as `'2024-01-15 00:00:00'` (DATETIME-friendly), (c) `IsActive` values as `1`/`0` rather than `TRUE`/`FALSE` for `TINYINT(1)`, (d) no `setval(...)` aftercare — `AUTO_INCREMENT` advances on explicit-PK insert.

2. `src/Quarry.Tests/QueryTestHarness.cs`:

   - Add `using MySqlConnector;`.
   - Add fields: `private readonly MySqlConnection _mysqlConnection;`, `private readonly MySqlTransaction? _mysqlTransaction;`, `private readonly string? _ownedMyDatabase;`.
   - Constructor accepts the new MySQL state and constructs `My = new My.MyDb(mysqlConnection);` instead of using `MockConnection`.
   - `CreateAsync(bool useOwnPgSchema = false, bool useOwnMyDatabase = false)`:
     - Ensure MySQL baseline first (after PG ensure).
     - Open a `MySqlConnection`, `USE \`quarry_test\`;` (or owned DB on opt-out), wrap in `BEGIN`/`ROLLBACK` for the default path.
     - Try/catch unwind path extends to the new connection + owned DB drop.
   - `DisposeAsync` — rollback My transaction or drop owned MySQL DB, then dispose the MySQL connection. Order: My before SQLite (mirror existing Pg-then-Lite ordering).
   - `Ss` continues to use `MockConnection` for now. The XML-doc summary updates to reflect the My move.

3. `src/Quarry.Tests/PgRowOrderExtensions.cs` — rename file + class to `RowOrderExtensions.cs` / `RowOrderExtensions`. XML doc now references "PG and MySQL InnoDB". Method signature unchanged. Update existing PG callers in the same commit (mechanical sed):
   - `src/Quarry.Tests/SqlOutput/CrossDialectSelectTests.cs::Select_Distinct`
   - `src/Quarry.Tests/SqlOutput/CrossDialectNavigationJoinTests.cs` (3 sites)
   - `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` (7 sites)
   - All call `SortedByAsync` — no method-name change, just the class moves.

### Tests

- All existing CrossDialect tests continue to pass (My still asserts SQL-string shape only; no execute mirror yet).
- `Quarry.Tests.Migration.PostgresMigrationRunnerTests` continues to pass (no PG change in this phase).
- New test count: 0.

### Commit

`Port harness DDL + seed to MySQL; upgrade QueryTestHarness.My to real MySqlConnection (#269)`

---

## Phase 3 — Focused MySQL integration tests

**Goal:** Add the same four targeted execution tests PR #266 used to lock in the surface that motivated the harness upgrade. These also serve as the "did the Phase 2 harness wiring work end-to-end" gate.

### Changes

1. `src/Quarry.Tests/Integration/MySqlIntegrationTests.cs` — extend the placeholder fixture from Phase 1 with three new tests parallel to `PostgresIntegrationTests`:

   - **`EntityInsert_OnMySQL_ExecutesSuccessfully`** — `Pg.Addresses().Insert(...).ExecuteScalarAsync<int>()` mirror, but on `My`. MySQL returns `LAST_INSERT_ID()` — verify the Quarry-emitted SQL `INSERT ... ; SELECT LAST_INSERT_ID()` works through `MySqlConnector`.
   - **`InsertBatch_OnMySQL_ExecutesSuccessfully`** — three-warehouse batch insert + read-back. Verifies the runtime-expanded `?` placeholder path for batch insert.
   - **`WhereInCollection_OnMySQL_ExecutesSuccessfully`** — `wantedIds.Contains(...)` with the helper-method `BuildWantedIds()` to defeat constant inlining. Verifies the runtime collection-expansion path on MySQL.

2. `src/Quarry.Tests/Migration/MySqlMigrationRunnerTests.cs` (new) — parallel to `PostgresMigrationRunnerTests.cs`:

   - `[Category("MySqlIntegration")]`.
   - `SetUp` opens a `MySqlConnection`, creates a uniquely-named database `migtest_<guid10>`, `USE` it.
   - `TearDown` drops the database (best-effort; warnings to `TestContext.Out` mirror PG).
   - `RunAsync_InsertsHistoryRow_OnMySQL` — runs `MigrationRunner.RunAsync(_connection, migrations, dialect: "mysql", ...)` over the same demo migration shape and asserts the `__quarry_migrations` row was inserted. This is the GH-258 regression-class gate: if `MigrationRunner.InsertHistoryRowAsync` ever drifts on MySQL, this catches it.

   **Caveat to verify in this phase:** does `MigrationRunner` actually accept a `mysql` dialect string in its current shape? Check `src/Quarry/Migration/MigrationRunner.cs` and `SqlTypeMapper` — if dialect plumbing is hard-coded to PG/SQLite for `InsertHistoryRowAsync` SQL generation, fix the dialect dispatch before adding the test. (Production already supports MySQL DDL via `SqlTypeMapper.MapMySql`; the question is whether `InsertHistoryRowAsync` parameter-binding generation honors the dialect on MySQL the same way it does on PG.)

### Tests

- New: 3 in `MySqlIntegrationTests`, 1 in `MySqlMigrationRunnerTests`.
- All existing tests continue to pass.

### Commit

`Add focused MySQL integration tests (insert / batch / collection / migration runner) (#269)`

---

## Phase 4 — Full cross-dialect mirror

**Goal:** Wherever an existing `CrossDialect*Tests` test runs `await pg.ExecuteXxxAsync(...)` and asserts, the same assertions also run against `my`. Mechanical, agent-delegated work — same shape PR #266 used.

### Changes

For each of the 22 affected `CrossDialect*Tests.cs` files (every one except `CrossDialectDiagnosticsTests.cs`, which has no `Execute*` calls), add a `my` block adjacent to every existing `pg` block. The mechanical pattern:

```csharp
// existing
var pgResults = await pg.ExecuteFetchAllAsync();
Assert.That(pgResults, Has.Count.EqualTo(3));
Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
// ...

// added
var myResults = await my.ExecuteFetchAllAsync();
Assert.That(myResults, Has.Count.EqualTo(3));
Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
// ...
```

For sites that already use `RowOrderExtensions.SortedByAsync` on the PG side (the 11 sites cleaned up in PR #266's REMEDIATE), the My side uses the same helper. For sites that assert positionally without ORDER BY where the PG side is ALSO positional (the underlying chain has explicit `OrderBy(...)` so both providers return the same shape), the My side is also positional.

### Mechanics

Delegate to a coding agent in batches of ~5 files at a time:

- File list (counts approximate, derived from grep):
  - `CrossDialectAggregateTests.cs` — 7 sites
  - `CrossDialectBatchInsertTests.cs` — 3 sites
  - `CrossDialectComplexTests.cs` — 10 sites
  - `CrossDialectCompositionTests.cs` — 17 sites
  - `CrossDialectCteTests.cs` — multi-site (sort-helper-using)
  - `CrossDialectDeleteTests.cs`
  - `CrossDialectEnumTests.cs`
  - `CrossDialectHasManyThroughTests.cs`
  - `CrossDialectInsertTests.cs` — 5 sites
  - `CrossDialectJoinTests.cs`
  - `CrossDialectMiscTests.cs`
  - `CrossDialectNavigationJoinTests.cs` — multi-site (sort-helper-using)
  - `CrossDialectNullableValueTests.cs`
  - `CrossDialectOrderByTests.cs`
  - `CrossDialectSchemaTests.cs`
  - `CrossDialectSelectTests.cs` — many sites
  - `CrossDialectSetOperationTests.cs`
  - `CrossDialectStringOpTests.cs`
  - `CrossDialectSubqueryTests.cs`
  - `CrossDialectTypeMappingTests.cs`
  - `CrossDialectUpdateTests.cs` — many sites
  - `CrossDialectWhereTests.cs`
  - `CrossDialectWindowFunctionTests.cs`
- Tally: ~254 mirror sites total across 22-23 files.

The agent's instructions for each batch:
- For every `pg.ExecuteXxxAsync(...)` block followed by `pgResults`/`pgScalar`/`pgFirst`/`pgAffected`/`pgNewId` assertions, emit a parallel block using `my`/`myResults`/`myScalar`/etc. with identical assertions.
- Where the PG block uses `.SortedByAsync(...)`, the My block uses the same helper.
- Where the PG block has an inline pre-sort (legacy from before the helper extraction), the My block uses the same inline pattern.
- Do **not** edit `pg.ToDiagnostics()` blocks — they stay as-is.
- Do **not** touch SQL-string-only tests that have no `pg.Execute*` call — they remain SQL-shape-only on every dialect including My.
- Run `dotnet test --filter "FullyQualifiedName~<file>"` after each batch and report failures.

### Tests

- All ~254 new My-execute mirror sites must pass against MySQL 8.4. Failures classified in Phase 5.

### Commit

One commit per agent batch is acceptable; or a single squash if the agent runs all batches cleanly. Final commit message: `Mirror full cross-dialect execution coverage to MySQL across CrossDialect tests (#269)`.

---

## Phase 5 — Triage MySQL-only failures

**Goal:** Each failure in Phase 4 is classified and resolved. PR #266's PG run produced three categories: real generator bugs to fix, provider-specific behaviour to work around inline with a comment, and latent generator bugs to file as separate issues. Expect the same breakdown for MySQL.

### Anticipated triage categories (from issue description)

- **Decimal materialisation** — `MySqlConnector` may surface `decimal` as `decimal` directly from `DECIMAL(18,2)`; if any reader call expects `double`, fix.
- **Boolean materialisation** — `TINYINT(1)` to `bool`. `MySqlConnector` exposes `MySqlConnectionStringBuilder.TreatTinyAsBoolean = true` by default. Confirm `reader.GetBoolean(i)` works without configuration.
- **Row order without ORDER BY** — InnoDB doesn't guarantee insertion-order sequential-scan output. Any `myResults[N]` positional assertion on a chain without explicit `OrderBy(...)` is a flake hazard. Apply `RowOrderExtensions.SortedByAsync(keySelector)` symmetrically with the PG fix.
- **GROUP BY strictness** — MySQL 8 enforces `ONLY_FULL_GROUP_BY` by default. Tests selecting non-aggregated non-grouped columns may fail; classify as either a Quarry generator bug to fix, a test rewrite, or a follow-up issue.
- **Identifier case sensitivity** — Phase 2 pinned `lower_case_table_names=0`. If anything still surfaces, confirm the container actually has it set.
- **Latent generator bugs** — mirror of #267 (PG DISTINCT+ORDER BY) and #268 (chained-With dispatch). MySQL may expose its own analogues. File new issues as needed; track each with the same shape PR #266's review.md used.

### Classification rules

For each Phase 4 failure:

- **(a) Real bug** → fix in this PR. Generator code, harness wiring, or test logic.
- **(b) MySQL behaviour** → inline workaround with a comment naming the behaviour and (if applicable) the issue number.
- **(c) Latent generator bug** → file a follow-up issue with full diagnostics; apply a minimum workaround in the test file with a comment referencing the issue.

### Tests

- All Phase 4 mirror sites green at end of phase.
- Any new follow-up issues (#270, #271, ...) cross-referenced in the PR body.

### Commit

`Triage MySQL-only execution-mirror failures (#269)`

---

## Dependencies between phases

- Phase 2 depends on Phase 1 (container helper exists).
- Phase 3 depends on Phase 2 (harness can run real My queries).
- Phase 4 depends on Phase 3 (focused tests confirm wiring).
- Phase 5 depends on Phase 4 (failures exist to triage).

## Rollout / commit shape

5 phases × 1+ commits each. Squash to a single commit at REMEDIATE-time PR creation. PR title: `Mirror PG execution coverage to MySQL: Testcontainers.MySql + real MySqlConnection on My + cross-dialect mirror (#269)`.
