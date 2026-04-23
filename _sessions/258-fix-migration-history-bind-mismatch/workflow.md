# Workflow: 258-fix-migration-history-bind-mismatch

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #258
pr:
session: 1
phases-total: 2
phases-complete: 2

## Problem Statement
`Quarry.Migration.MigrationRunner.InsertHistoryRowAsync` fails with PostgreSQL error `08P01: bind message supplies 0 parameters, but prepared statement "" requires 8` on Npgsql 10 + PostgreSQL 17. The user-supplied migration DDL runs successfully, but recording the history row afterwards fails, breaking the whole migration pipeline.

Environment: Quarry 0.3.0, Npgsql 10.0.2, .NET 10, PostgreSQL 17 (Testcontainers).

Root-cause hypothesis from reporter: `InsertHistoryRowAsync` builds the history-insert `NpgsqlCommand` in a way that reuses / caches a prepared statement handle but sends a Bind frame without the parameter bindings, which Npgsql 10 is stricter about than Npgsql 9.

Test baseline at workflow start: 3242 tests pass (Quarry.Tests 2938, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 103), 0 failures. Two NU1903 warnings on `System.Security.Cryptography.Xml` 9.0.0 in Quarry.Tests — unrelated, pre-existing.

## Decisions
- **2026-04-22 — Root cause:** `SqlFormatting.GetParameterName` always returns `@p{index}`, but `SqlFormatting.FormatParameter` returns `${index+1}` for PostgreSQL. `MigrationRunner.AddParameter` and `MigrateCommands.AddParameter` set the resulting mismatched name on `DbParameter.ParameterName`. Npgsql 9 silently bound positionally; Npgsql 10 strictly matches by name, so parameters named `@pN` do not bind to `$N` placeholders, producing a Bind frame with 0 parameters and the `08P01: bind message supplies 0 parameters, but prepared statement "" requires 8` error. The generated query code path (`CarrierEmitter` → `ParameterNames.Dollar`) already uses `$N` names for PostgreSQL, so the generated runtime code is unaffected; the bug is isolated to the two ad-hoc `AddParameter` helpers.
- **2026-04-22 — Fix location:** Make `SqlFormatting.GetParameterName` dialect-aware so the ParameterName matches `FormatParameter` output:
  - PostgreSQL → `$N` (1-based, matches `$N` placeholder)
  - SQLite / SqlServer → `@pN` (matches `@pN` placeholder)
  - MySQL → `@pN` (placeholder is `?` positional; name is arbitrary but must be unique per parameter)
  Both `MigrationRunner.AddParameter` and `Quarry.Tool.MigrateCommands.AddParameter` pick up the fix automatically via the shared helper.
- **2026-04-22 — Testing:** Unit-test-only approach. Update `DialectTests.GetParameterName_ReturnsNameForDbParameter` cases for all dialects, and add an invariant test asserting that for each dialect `GetParameterName(d, i)` is consistent with `FormatParameter(d, i)` (i.e., PostgreSQL names equal placeholders; SQLite/SqlServer names equal placeholders; MySQL names are unique across indices). No PostgreSQL integration test — no Docker dependency in CI.
- **2026-04-22 — Npgsql version:** Upgrade `Npgsql` package reference from `9.*` to `10.*` in `Quarry.Tests.csproj` and `Quarry.Tool.csproj` so CI exercises the strict Npgsql 10 binding behavior. Consumers on Npgsql 10 are the ones currently broken; keeping the repo on Npgsql 9 masks regressions.
- **2026-04-22 — Scope:** Fix both call sites in one PR via the single shared-helper change. Same root cause, one authoritative edit.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-22 | | INTAKE: loaded issue #258, created worktree and branch, baseline tests all green. |
