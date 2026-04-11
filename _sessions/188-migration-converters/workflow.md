# Workflow: 188-migration-converters
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #188
pr:
session: 1
phases-total: 7
phases-complete: 7
## Problem Statement
Extend the Quarry.Migration package (#185) beyond Dapper with additional source framework converters: EF Core, Raw ADO.NET, and SqlKata. The Dapper converter already exists and establishes the architecture. Dependencies: #185 (Dapper converter), #182 (shared SQL parser for ADO.NET).

Baseline: All 3,112 tests pass (97 Migration, 103 Analyzers, 2,912 Quarry). No pre-existing failures.

## Decisions
- **2026-04-10 D1: Scope** — All three converters (EF Core, ADO.NET, SqlKata) in one PR.
- **2026-04-10 D2: Diagnostic IDs** — Unique per converter: QRM01x=EF Core, QRM02x=ADO.NET, QRM03x=SqlKata.
- **2026-04-10 D3: EF Core detection** — Semantic validation via Roslyn (confirm DbContext/DbSet types from Microsoft.EntityFrameworkCore).
- **2026-04-10 D4: ADO.NET parameter tracking** — Track DbCommand variable across full method body to collect all Parameters.Add/AddWithValue calls.
- **2026-04-10 D5: Code fixes** — All three converters get code fix providers. EF Core/SqlKata replace full chain. ADO.NET replaces Execute call only + TODO comment for dead parameter code.
- **2026-04-10 D6: EF Core unsupported constructs** — QRM012 (converted with warnings), not QRM013. Convert what we can, add warning comments for Include/AsNoTracking/FromSqlRaw/ExecuteUpdate/Delete.
- **2026-04-10 D7: SqlKata detection** — Semantic validation (confirm SqlKata.Query type via Roslyn).

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | PLAN | Set up worktree, rebased on master, baseline tests all green (3,112 pass). DESIGN complete — all 7 decisions confirmed. Transitioning to PLAN. |
