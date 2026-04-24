# Plan: 258-fix-npgsql-parameter-naming-redux

## Overview

Fix the real root cause of issue #258 (that PR #261 misdiagnosed and shipped in v0.3.1 / v0.3.2 unfixed). The empirical probe (`NpgsqlBindingProbeTests`) against real Npgsql 10 + PostgreSQL 17 shows that Npgsql switches between named and positional binding modes based on whether any `DbParameter` has a `ParameterName` set, *not* based on what's in `CommandText`. If the SQL contains `$N` placeholders (PG's native positional form) while any parameter has a name, Npgsql falls back to name lookup, finds no `@name` markers in the SQL, and sends a Bind message with zero parameter values — surfacing as `08P01: bind message supplies 0 parameters, but prepared statement "" requires N`. Both v0.3.0 (`$N` + `@pN` names) and v0.3.1/v0.3.2 (`$N` + `$N` names) fall into this failure mode.

The fix: keep `$N` in CommandText for PG (Quarry emits it already, Npgsql's native form, no rewriting) and stop assigning `ParameterName` when dialect is PostgreSQL. The generator currently produces strings like `"$1"`, `"$2"` for PG; we change those emission sites to produce `""`. For simpler cases (like `MigrationRunner.AddParameter`), `SqlFormatting.GetParameterName` returns `""` for PG and the rest of the code is unchanged.

Alongside the runtime fix, `QueryTestHarness.Pg` moves from `MockDbConnection` to a real `NpgsqlConnection` backed by a shared Testcontainers-managed PostgreSQL 17 container. `QueryTestHarness.CreateSchema()` and `SeedData()` already use quoted identifiers, so we port them to PG DDL and reuse the same seed rows. Existing SQL-string assertions keep working (they come from `ToDiagnostics()` at compile time, so the connection backing `Pg` doesn't matter for those). Execution-heavy tests (`ExecuteFetchAllAsync`, etc.) that currently run only on `Lite` keep running on `Lite`, but it's now a one-line change for any test to *also* exercise PG, and the four focused PG integration tests in Phase 5 exercise every generator + runtime path PR #261 touched.

## Key concepts

**Empty ParameterName is the Npgsql-native positional path.** Npgsql's parser scans `CommandText` once. If it sees any `@name`/`:name` placeholder, it enters named mode: it expects every `DbParameter` in the collection to have a `ParameterName` and uses those names to build the Bind frame. If it sees `$N` placeholders with parameters that have no names, it uses positional mode: Bind frame values come straight from the order parameters were added. Mixed state — `$N` in SQL + non-empty names — is the failure that #258 keeps stumbling into.

**Quarry already emits `$N` SQL correctly for PG.** `SqlFormatting.FormatParameter(PostgreSQL, i)` has always returned `${i+1}`. That stays. The bug is purely in what goes into `DbParameter.ParameterName`. This is why the fix touches ~6 call sites and changes only what value those sites emit for the name — no SQL text changes anywhere.

**Per-test isolation: transactional default + per-schema opt-out.** Tests run in parallel; a shared container is the only affordable option (starting a container per test is multi-second). The default isolation strategy is a per-test transaction on a shared baseline schema: one baseline schema `quarry_test` with tables + seed is committed once at container startup; each `QueryTestHarness.CreateAsync()` opens a fresh `NpgsqlConnection` from Npgsql's pool (~0.5ms, MVCC-isolated from siblings), issues `BEGIN`, sets `search_path = quarry_test`, and hands the connection to `Pg`; `DisposeAsync()` issues `ROLLBACK` and closes. Near-zero overhead per test. Npgsql auto-attaches commands to the connection's active transaction, so Quarry's own command-building participates transparently. Caveat: PG sequences (if any test used them) don't roll back with the transaction, so tests should assert row existence rather than exact auto-generated IDs — the existing harness already seeds explicit IDs, so this is consistent.

The opt-out `CreateAsync(useOwnSchema: true)` creates a uniquely-named PG schema (e.g., `test_{guid-suffix}`), runs DDL + seed into that schema, sets `search_path`, and drops the schema on dispose (~50ms overhead, but only a handful of tests need this). Used by: migration tests (Phase 5's `PostgresMigrationRunnerTests` — MigrationRunner issues its own transactions, incompatible with an outer BEGIN/ROLLBACK), and any test that asserts transaction-level behavior (COMMIT visibility, rollback semantics, SAVEPOINT interactions). `Lite` continues to use an in-memory SQLite DB (already parallel-safe). `My` and `Ss` continue to use `MockDbConnection` (out of scope for this bug).

**PG DDL port.** The existing SQLite schema uses quoted identifiers and basic types (INTEGER, TEXT, REAL). PostgreSQL accepts the same quoted-identifier syntax and equivalent types (INTEGER, TEXT, DOUBLE PRECISION). `REAL` in PG is single-precision — not desirable for money columns like `Total` — so we translate `REAL` → `DOUBLE PRECISION`. `INTEGER PRIMARY KEY` in SQLite implicitly auto-increments; in PG it's just a non-null PK, and the seed rows provide explicit IDs, so no `SERIAL`/`IDENTITY` is needed. `GENERATED ALWAYS AS (...) STORED` is supported in PG 12+ with equivalent syntax. `CREATE VIEW "Order" AS SELECT * FROM "orders"` works identically.

**Triage of existing cross-dialect tests that start executing on real PG.** The point of this harness upgrade is "free" PG coverage, but "free" only holds if existing tests pass. Most cross-dialect tests today only *compose* on `Pg` (via `.Prepare().ToDiagnostics()`), not execute — those stay unaffected. The tests that execute on `Lite` today would need an explicit `ExecuteXxxAsync` call on `Pg` to start covering PG execution; we don't do that wholesale in this PR. We only add the four focused Phase 5 integration tests that exercise the PR #261 regression surface. Any test that *happens* to exercise `Pg` execution (we'll discover these during Phase 7 full-suite run) must pass or be explicitly marked.

## Phases

### Phase 1 — Testcontainers infrastructure and rename probe to regression test
Move the existing `NpgsqlBindingProbeTests` (created during DESIGN for empirical verification) into its final home, upgrade assertion semantics, and make it start/stop the shared container via a test fixture helper. `Testcontainers.PostgreSql 4.*` is already added to `Quarry.Tests.csproj` from DESIGN. Add a `src/Quarry.Tests/Integration/PostgresTestContainer.cs` helper with an internal static `SharedAsync()` method that lazy-starts a single `PostgreSqlContainer` for the test run. It's referenced by both the probe tests and the upgraded harness.

Tests to add/modify:
- `src/Quarry.Tests/Integration/PostgresTestContainer.cs` — new helper, `Task<PostgreSqlContainer> SharedAsync()` (lazy-init, `AsyncLazy<T>` pattern), image `postgres:17-alpine`.
- `src/Quarry.Tests/Integration/NpgsqlBindingProbeTests.cs` — rename to `NpgsqlParameterBindingTests`. Route through `PostgresTestContainer.SharedAsync()` instead of creating its own. Comment header explains: "Regression documentation for GH-258 redux — proves that only the D (no ParameterName) configuration works on Npgsql 10 with `$N` CommandText."

### Phase 2 — Fix `SqlFormatting.GetParameterName` for PostgreSQL
One-line change at `src/Quarry.Shared/Sql/SqlFormatting.cs:73-84`:

```csharp
public static string GetParameterName(SqlDialect dialect, int index)
{
    return dialect switch
    {
        SqlDialect.PostgreSQL => "",           // was: $"${index + 1}"
        _ => $"@p{index}"
    };
}
```

Update the doc comment to reflect the new rule: "PostgreSQL uses `$N` positional placeholders in CommandText; ParameterName must be empty so Npgsql stays in positional mode." This one change is what fixes `MigrationRunner.AddParameter`, `MigrationRunner.UpdateHistoryStatusAsync`, `MigrationRunner.DeleteHistoryRowAsync`, etc. — they all route through `GetParameterName`.

Tests to modify:
- `src/Quarry.Tests/DialectTests.cs:112-124` — `GetParameterName_ReturnsNameForDbParameter`: flip the PG TestCases from `"$1"`/`"$6"` to `""`/`""`.
- `src/Quarry.Tests/DialectTests.cs:127-140` — `GetParameterName_MatchesFormatParameter_ForNamedDialects`: remove `SqlDialect.PostgreSQL` from the TestCase list (PG is no longer a name-binding dialect from Quarry's perspective). Add a new companion test `GetParameterName_IsEmpty_ForPostgreSQL` that asserts PG always returns `""`.

### Phase 3 — Fix generator emission paths (carrier + terminal)
`CarrierEmitter.FormatParamName` and `EmitParamNameExpr` currently return `"${i+1}"` / `"Quarry.Internal.ParameterNames.Dollar(...)"` on PG. Change both to return `""` / `"\"\""` for PG. `ParameterNames.Dollar` remains in `src/Quarry/Internal/ParameterNames.cs` — it's still used by `TerminalEmitHelpers.EmitCollectionPartsPopulation` to build `$N` *SQL text* for IN-clause placeholders, which is correct and unchanged.

Specific edits:
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs:1273-1281` — `FormatParamName(PostgreSQL, i)` returns `""` (drop the `${i+1}` branch, let it fall through to `_ => ""` for PG alongside MySQL's `"?"`). Actually, cleaner: `SqlDialect.PostgreSQL => "", SqlDialect.MySQL => "?", _ => $"@p{index}"`.
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs:1287-1295` — `EmitParamNameExpr(PostgreSQL, ...)` returns the C# string literal `"\"\""`. Drop the `ParameterNames.Dollar(...)` branch.
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs:583-585` — batch-insert `paramNameExpr`: for PG, `"\"\""` instead of `"Quarry.Internal.ParameterNames.Dollar(__paramIdx)"`.
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs:711-718` — `EmitDiagParamNameExprWithVar(PostgreSQL, ...)` returns `"\"\""`. Drop the `ParameterNames.Dollar` branch.
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs:897-914` — `EmitCollectionPartsPopulation`: **unchanged**. This emits SQL text (`$N`, `?`, `@pN`) into the generated IN-clause expansion, not parameter names. SQL text on PG correctly stays `$N`.

Tests to modify:
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs:3213-3249` — `CarrierGeneration_EntityInsert_EmitsDollarParameterNames_ForPostgreSQL`: rename to `CarrierGeneration_EntityInsert_EmitsEmptyParameterNames_ForPostgreSQL`. Flip assertions: code must contain `__p0.ParameterName = ""` (or equivalent), must not contain `"$1"` or `"@p0"`.
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs:3287-3322` — `CarrierGeneration_BatchInsert_UsesDollarParameterNames_ForPostgreSQL`: rename to `CarrierGeneration_BatchInsert_UsesEmptyParameterNames_ForPostgreSQL`. Assert code must contain `__p.ParameterName = ""` (the literal empty-string assignment), must not contain `ParameterNames.Dollar(__paramIdx)`.
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs:3252-3285` — `CarrierGeneration_EntityInsert_EmitsAtParameterNames_ForSQLite`: unchanged — SQLite path continues to use `@pN`.

### Phase 4 — Upgrade `QueryTestHarness.Pg` to a real Npgsql connection (transactional default)
Replace the `MockDbConnection`-backed `Pg` with a real `NpgsqlConnection` from the shared Testcontainers container, using transactional isolation by default with a per-schema opt-out.

Container-side (lives in `PostgresTestContainer` from Phase 1):
- `EnsureBaselineAsync()` — runs once per test process. Starts the container if needed. Runs `CREATE SCHEMA quarry_test`, all 11 `CREATE TABLE` statements (PG-translated from the existing SQLite DDL), the `CREATE VIEW`, and the seed INSERTs. Commits. This baseline is visible to every subsequent connection.

Harness-side (`src/Quarry.Tests/QueryTestHarness.cs`):
- Add an `_npgsqlConnection` field and an `_ownedSchema` field (null unless opt-out).
- `CreateAsync(bool useOwnSchema = false)`:
  - Calls `PostgresTestContainer.EnsureBaselineAsync()`.
  - Opens a fresh `NpgsqlConnection` (pool-backed).
  - If `useOwnSchema`: generate unique schema name, `CREATE SCHEMA <name>`, re-run DDL + seed into it, `SET search_path TO <name>`.
  - Else: `SET search_path TO quarry_test` + `BEGIN`.
  - Builds `Pg = new Pg.PgDb(_npgsqlConnection)` on the prepared connection.
- `DisposeAsync`:
  - If `_ownedSchema != null`: `DROP SCHEMA <name> CASCADE` + close.
  - Else: `ROLLBACK` + close (connection returns to pool).
- `MockConnection` remains (shared by `My`/`Ss`). `Pg` construction flips from `new Pg.PgDb(mockConnection)` to `new Pg.PgDb(_npgsqlConnection)`.

PG DDL port (inside `PostgresTestContainer`):
- `INTEGER PRIMARY KEY` stays as-is — PG accepts it (seed rows provide explicit IDs).
- `TEXT`, `INTEGER` stay.
- `REAL` → `DOUBLE PRECISION` (PG's `REAL` is single-precision; insufficient for money columns).
- `GENERATED ALWAYS AS (...) STORED` — PG 12+ supports identical syntax; keep as-is.
- `CREATE VIEW "Order" AS SELECT * FROM "orders"` — identical.

Tests to modify: none directly in this phase — the point is that no diagnostic-path test should break. Phase 7 validates this.

### Phase 5 — Focused PG integration tests (the four PR #261 regression guards)
Four new `[Category("NpgsqlIntegration")]` tests that exercise the code paths PR #261 touched end-to-end on real PG. All use `QueryTestHarness.Pg` (now real). These are the tests that would have caught the bug if they'd existed.

Tests to add:
- `src/Quarry.Migration.Tests/PostgresMigrationRunnerTests.cs` — `MigrationRunner_RunAsync_InsertsHistoryRow_OnPostgreSQL`: shares the container but uses its own per-test PG schema (MigrationRunner opens its own transactions, incompatible with the harness's outer BEGIN/ROLLBACK). Runs a one-migration `MigrationBuilder` that creates a dummy table, asserts row count in `__migrations` is 1 with status `'applied'`. (Note: this sits in `Quarry.Migration.Tests`, so `Quarry.Migration.Tests.csproj` also needs `Testcontainers.PostgreSql` added, plus a reference to the same `PostgresTestContainer` helper.)
- `src/Quarry.Tests/Integration/PostgresIntegrationTests.cs` — three tests driving generator code paths:
  - `EntityInsert_OnPostgreSQL_ExecutesSuccessfully`: `await t.Pg.Users().Insert(new User { ... }).ExecuteScalarAsync<int>()` returns the inserted `UserId`; follow-up SELECT verifies row present.
  - `InsertBatch_OnPostgreSQL_ExecutesSuccessfully`: `await t.Pg.Users().InsertBatch(...).Values(new[]{...}).ExecuteNonQueryAsync()` returns count, follow-up SELECT verifies rows.
  - `WhereIn_Collection_OnPostgreSQL_ExecutesSuccessfully`: `var users = await t.Pg.Users().Where(u => ids.Contains(u.UserId)).ExecuteFetchAllAsync()`; asserts matching UserIds.

Each test's existence is a regression guard: if someone later regresses any of `SqlFormatting.GetParameterName`, `CarrierEmitter.FormatParamName`, `TerminalBodyEmitter` batch path, or `TerminalEmitHelpers` IN-clause parts, at least one of these will fail loudly.

### Phase 6 — Helper deduplication (shared `PostgresTestContainer`)
Phase 1 put the helper in `Quarry.Tests/Integration/PostgresTestContainer.cs`. Phase 5's `Quarry.Migration.Tests` needs the same helper. Move the helper into a small shared file linked by both projects — similar to how `Quarry.Shared` files are linked via `<Compile Include="...">`. Add `Quarry.Migration.Tests.csproj` `<Compile Include="../Quarry.Tests/Integration/PostgresTestContainer.cs" LinkBase="Integration" />` and `<PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />`.

This is a housekeeping phase — no new tests, just eliminating duplication. Could merge into Phase 5, but worth isolating in commit history because it touches a second project's csproj.

### Phase 7 — Full-suite run and cross-dialect PG triage
Run `dotnet test Quarry.sln` and categorize all failures. Expected failures:
- None, if no existing cross-dialect test exercises `Pg` execution (the common case — most go through `Prepare().ToDiagnostics()` only).
- Some, if any test calls `Pg.X().ExecuteXxxAsync()` — surprising but possible. Triage each: (a) fix if it's a genuine bug exposed by real PG, (b) mark `[Platform("SQLite")]` / skip with explicit rationale if it's a known SQLite-specific behavior (date string formats, type affinity slop, etc.), or (c) mark `[Ignore]` with a TODO referencing a follow-up issue only if the failure is too deep to resolve in this PR.

Tests to modify: depends on triage results. Likely zero, but budget for up to a handful of small test annotations and possible minor fixes.

### Phase 8 — Clean up stale PR #261 doc artifacts
PR #261 added extensive anchor comments in `src/Quarry/Context/QuarryContext.cs:182-269` about why `@pN` naming is correct for `RawSqlAsync`. That commentary is still accurate *for RawSqlAsync* (user-written SQL uses `@pN` markers; Npgsql rewrites them; the runtime sets `ParameterName = "@pN"`). But the reasoning quoted PR #261's false premise about strict name-binding. Tighten the comment to reflect the new empirical understanding: Npgsql enters named mode when any parameter has a name. For user-facing SQL where the user controls the placeholder form, `@pN` is the only portable form. The migration path (MigrationRunner / generator) uses `$N` native PG + empty names because it controls both sides.

No behavior changes in this phase, just docs.

## Dependencies

Phase 1 → Phase 5 (need the container helper before the integration tests).
Phase 2 → Phase 5 (migration runner fix must land before the migration-runner PG test can pass).
Phase 3 → Phase 5 (generator fixes must land before the Insert / InsertBatch / WHERE-IN PG tests can pass).
Phase 4 → Phase 5 (`t.Pg` real connection is the test setup for Insert / InsertBatch / WHERE-IN tests).
Phase 5 → Phase 6 (helper is "done" after being used by both projects; deduplicate once there's real duplication).
Phases 1–6 → Phase 7 (full-suite triage comes last).
Phase 8 is independent and can land whenever.

## Risks and mitigations

**Risk: PG DDL port surfaces schema-level incompatibilities.** The existing harness uses `REAL` for `Total`, `Balance`, etc. — PG's `REAL` is single-precision (insufficient for money). Mitigation: port to `DOUBLE PRECISION`. Verify by running Phase 4's harness-setup in isolation before Phase 5's integration tests.

**Risk: Parallel test runs collide on the shared PG container.** Per-harness schema namespacing + `search_path` scoping is standard Npgsql/Postgres isolation. Fallback: add `[NonParallelizable]` to new PG integration tests if schema isolation proves flaky (very unlikely).

**Risk: Testcontainers / Docker unavailable on a developer machine.** Docker 29.1.2 is present locally, and `ubuntu-latest` CI has Docker. For developers without Docker, Testcontainers throws a clear error message on `SharedAsync()`. Acceptable — PG integration tests are not needed for most development loops; the default SQLite harness still runs. If we want to be nicer, wrap `PostgresTestContainer.SharedAsync()` in a helper that catches the "no Docker" exception and `Assert.Ignore`s with a clear message. Low priority.

**Risk: Phase 7 surfaces a large number of PG execution failures.** If this happens, it means the "free coverage" is actually work. Contingency: revert Phase 4 back to `MockDbConnection`, keep Phases 1–3, 5 (the focused integration tests), open a follow-up issue for harness upgrade. Only Phase 4 is speculative — Phases 1–3, 5 are load-bearing and must land.

**Risk: Npgsql 10 or Testcontainers 4.x package update breaks something.** Both already in the tree at known-working versions (Npgsql 10.* from PR #261; Testcontainers 4.* added in DESIGN).
