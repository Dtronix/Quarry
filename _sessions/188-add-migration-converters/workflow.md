# Workflow: 188-add-migration-converters
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: suspended
issue: #188
pr:
session: 1
phases-total: 7
phases-complete: 0
## Problem Statement
Add EF Core, ADO.NET, and SqlKata converters to Quarry.Migration package. Dependencies #185 (Dapper converter) and #182 (shared SQL parser) are both closed and implemented. Baseline: all 3112 tests pass (97 Migration, 2912 Core, 103 Analyzers), 0 pre-existing failures.
## Decisions
- 2026-04-10: Extract ISqlCallSite interface so ChainEmitter can serve both Dapper and ADO.NET converters. DapperCallSite and AdoNetCallSite both implement it.
- 2026-04-10: EF Core detection via compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") — same pattern as Dapper's SqlMapper check.
- 2026-04-10: Diagnostic IDs offset by 100: Dapper=QRM001-003, EF Core=QRM101-103, ADO.NET=QRM201-203, SqlKata=QRM301-303. Allows expansion within each converter.
- 2026-04-10: SqlKata column resolution: schema lookup first, PascalCase fallback with warning diagnostic. Consistent with Dapper behavior.
- 2026-04-10: ADO.NET detection scoped to single method body — track DbCommand variable, collect CommandText + Parameters.Add within same method. Cross-method tracking out of scope.
- 2026-04-10: Each converter is a standalone pipeline: Detector + Emitter + Public Facade + Analyzer + CodeFix. EF Core and SqlKata get new emitters; ADO.NET reuses ChainEmitter via ISqlCallSite.
## Suspend State
- Current phase: IMPLEMENT phase 1/7, not yet started
- In progress: Nothing — plan just approved, no code changes yet
- Immediate next step: Begin Phase 1 — create ISqlCallSite.cs interface, make DapperCallSite implement it, update ChainEmitter.Translate() signature
- WIP commit: N/A
- Test status: All 3112 tests passing (baseline)
- Unrecorded context: None — all decisions recorded above
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | PLAN | Started from issue #188. Explored architecture, confirmed design for 3 converters. Plan approved, suspended for handoff. |
