# Workflow: 258-fix-npgsql-parameter-naming-redux

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: suspended
issue: #258 (closed by #261 in v0.3.1; customer report shows v0.3.2 still broken)
pr: 266
session: 1
phases-total: 8
phases-complete: 8

## Problem Statement
PR #261 (merged 2026-04-23, shipped in v0.3.1 and v0.3.2) claimed to close #258 but the same `08P01: bind message supplies 0 parameters, but prepared statement "" requires 8` reproduces against v0.3.2 on Npgsql 10 + PostgreSQL 17. A customer decompiled `Quarry.dll` 0.3.2 and confirmed `InsertHistoryRowAsync` is byte-identical to 0.3.1.

Root cause: PR #261 operated on a false premise ("Npgsql 10 strictly matches parameters by name") and changed `SqlFormatting.GetParameterName(PostgreSQL, i)` to return `$"${i+1}"` so the `DbParameter.ParameterName` would match the `$N` placeholder in SQL. In reality, Npgsql treats `$N` placeholders in `CommandText` as **positional** — bound by order in `cmd.Parameters`, not by name lookup. `ParameterName = "$1"` is not a valid Npgsql parameter name (names must be blank, `@name`, or `:name`); Npgsql 10's stricter parser does not reconcile the `$1` name with the `$1` positional placeholder and sends the Bind frame with zero parameter values.

The same class of change in PR #261 was extended to `CarrierEmitter.EmitCarrierInsertTerminal` and `TerminalBodyEmitter` batch-insert paths, so every generated `Insert(entity).Execute*Async()` and `InsertBatch(...).Values(...).Execute*Async()` on PostgreSQL is likely broken identically — not yet reported because most users test with SQLite.

Why the regression was not caught: `QueryTestHarness` provides a real in-memory SQLite connection for `Lite` but uses mock connections for `Pg`/`My`/`Ss` that only assert SQL-string shape. No test ever executes a parameterized command through a real Npgsql connection, so the actual Bind failure is invisible to the suite. `DialectTests.GetParameterName_ReturnsNameForDbParameter` only verifies the string value returned by `GetParameterName`, not that the produced name is accepted by a real provider.

Recommended fix (to be confirmed in DESIGN): use `@pN` uniformly for `DbParameter.ParameterName` across all dialects, and use `@pN` in PostgreSQL SQL text as well. Npgsql's `@name` placeholder parser rewrites to positional `$N` internally — this is the convention already documented for user `RawSqlAsync` in `QuarryContext.cs:182-262`. Revert the PR #261 changes in `SqlFormatting`, `CarrierEmitter`, and `TerminalBodyEmitter`. Add a real Npgsql integration test (Testcontainers-backed) for the migration history insert and entity insert paths so this regression category cannot recur silently.

### Baseline (v0.3.2 + HEAD 3acdba1)
All 3303 tests pass on master at fork point — 2985 (Quarry.Tests) + 201 (Quarry.Migration.Tests) + 117 (Quarry.Analyzers.Tests). No pre-existing failures carry over into this branch.

## Decisions

### 2026-04-24 — Root cause confirmed empirically (supersedes PR #261's theory)
An in-repo probe test (`NpgsqlBindingProbeTests`) against real Npgsql 10 + PostgreSQL 17 proved:
- `@pN` SQL + `@pN` name → works (Npgsql's `@name`→`$N` rewrite path).
- `$N` SQL + `@pN` name → fails with 08P01 (v0.3.0 state, original #258 bug).
- `$N` SQL + `$N` name → fails with 08P01 (v0.3.1/0.3.2 state after PR #261).
- `$N` SQL + empty `ParameterName` → works (native positional binding, no rewrite).
- `$N` SQL + unset `ParameterName` → works (native positional binding).

PR #261's stated theory that "Npgsql 10 strictly matches parameters by name" is false. Npgsql switches between named and positional binding modes based on whether any parameter has a ParameterName set — **not** based on what's in CommandText. When any parameter has a name, Npgsql tries named lookup against the SQL; if the SQL uses `$N` (positional) there are no `@name` markers to match, so the Bind frame ships with zero values.

### 2026-04-24 — Fix strategy: `$N` in SQL + empty `ParameterName` on PostgreSQL
User preference: "least amount of work for the database connector." Empirical result D is strictly cheaper than result A for Npgsql per-command:
- No `@name`→`$N` rewrite pass.
- No name→index map construction.
- No per-parameter name lookups.

On every dialect, the SQL text emitted by the generator stays exactly as it is today (`$N` on PG, `@pN` on SQLite/SqlServer, `?` on MySQL). Only `DbParameter.ParameterName` changes on PG — from `$N` (current) to empty string. Cross-dialect `SqlOutput` SQL-string assertions are untouched.

### 2026-04-24 — Integration test infrastructure: Testcontainers.PostgreSql
Added as a package reference on `Quarry.Tests` (Docker 29.1.2 is available on this machine; `ubuntu-latest` GH Actions runner has Docker by default). Probe tests will stay in the repo as regression documentation under `[Category("NpgsqlIntegration")]`.

### 2026-04-24 — Test coverage scope: upgrade `QueryTestHarness.Pg` to real Npgsql
User-chosen direction: replace `MockDbConnection` with a real `NpgsqlConnection` on `Pg`, backed by a shared Testcontainers PostgreSQL 17 container for the test run. `My` and `Ss` stay on mocks. Existing `Prepare().ToDiagnostics()` SQL-string assertions keep working (diagnostics are compile-time, connection-agnostic). Cross-dialect tests that currently call `ExecuteXxxAsync` only on `Lite` gain the ability to also execute on `Pg` with no new harness boilerplate.

Any existing test that fails when switched to real PG must be triaged as part of this PR: (a) genuine bug — fix; (b) SQLite-specific behavior — explicit marker and rationale. No silent skips.

## Suspend State

**Current phase:** IMPLEMENT (Phase 9 — Pg execution parity on CrossDialect tests). PR #266 is already open and CI-green on Phases 1–8 + REMEDIATE; Phase 9 is an expansion, not a blocker for that PR.

**In progress:** Mirroring `await lt.ExecuteXxxAsync()` blocks with `await pg.ExecuteXxxAsync()` (identical assertions) across ~25 `src/Quarry.Tests/SqlOutput/CrossDialect*Tests.cs` files. File 1 of ~25 (`CrossDialectOrderByTests.cs`) is edited — 4 tests mirrored.

**Immediate next step:** Fix the NUMERIC DDL bug in `PostgresTestContainer.CreateSchemaObjectsAsync` — decimal-backed columns (orders.Total, order_items.UnitPrice/LineTotal, accounts.Balance/credit_limit, products.Price/DiscountedPrice) must be `NUMERIC(18, 2)` not `DOUBLE PRECISION`. Npgsql refuses `GetDecimal` on `double precision`. See `handoff.md` for the exact column list.

**WIP commit hash:** `6e82f31` — amend on first real commit next session.

**Test status:** PR #266 CI green. Locally, `CrossDialectOrderByTests.OrderBy_Joined_RightTableColumn` fails on Pg with `System.InvalidCastException: Reading as 'System.Decimal' is not supported for fields having DataTypeName 'double precision'`. Three sibling OrderBy tests pass on Pg.

**Unrecorded context:**
- User directive for Phase 9: "Mirror Lite exactly — same assertions on both (Recommended)" + "None — attempt all, triage failures" for exclusions + "Pick the smallest file first".
- Handoff triggered after discovering DDL bug and before fix landed.
- Risk list for Phase 9 (see handoff.md §Known Issues): DateTime TEXT→DateTime materialization may break similarly to decimal; row-order assertions may flake without explicit ORDER BY; Col&lt;bool&gt; IsActive untested on real Npgsql.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-24 | | INTAKE completed — worktree created, baseline tests green (3303/3303); problem traced to PR #261's false name-binding premise + mock-only PG test infrastructure |
| 2 | 2026-04-24 | 2026-04-24 | DESIGN + PLAN + IMPLEMENT phases 1–3: Testcontainers helper, probe regression tests, SqlFormatting.GetParameterName fix, generator emission fixes. Tests green (2990 Quarry.Tests + 201 Migration.Tests + 117 Analyzers.Tests). |
| 3 | 2026-04-24 | 2026-04-24 | IMPLEMENT phases 4–7: QueryTestHarness.Pg upgraded to real Npgsql (transactional default + own-schema opt-out) + PG DDL port; 4 focused integration tests (Insert/InsertBatch/WHERE-IN/MigrationRunner); MigrationRunner DateTime bug fixed (was passing ISO strings to TIMESTAMP columns). Phase 6 + 7 absorbed — MigrationRunner lives in Quarry not Quarry.Migration so no cross-project dedup needed; no cross-dialect triage surfaced beyond the DateTime fix. Tests: 2994 + 201 + 117 = 3312. |
| 4 | 2026-04-24 | 2026-04-24 | Phase 8 doc cleanup + REVIEW (agent analysis, 25 findings) + REMEDIATE (9 A findings addressed, including critical collection-path bug at CarrierEmitter.cs:690 that the original REVIEW agent surfaced). PR #266 created on origin/master, CI green in 2m6s. Tests: 2996 + 201 + 117 = 3314. |
| 5 | 2026-04-24 | 2026-04-24 | Phase 9 started: mirror Pg execution in all CrossDialect*Tests (user directive "mirror Lite exactly"). File 1/25 (OrderByTests) 4 tests mirrored, 3 pass on Pg; Joined fails on decimal/DOUBLE PRECISION mismatch. Handoff triggered — next session to fix DDL to NUMERIC(18,2) and continue rollout. |
