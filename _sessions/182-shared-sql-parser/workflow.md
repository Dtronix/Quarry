# Workflow: 182-shared-sql-parser
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #182
pr: #194
session: 1
phases-total: 5
phases-complete: 5
## Problem Statement
Implement a lightweight recursive-descent SQL parser in `Quarry.Shared/Sql/` that can be consumed by the source generator (RawSqlAsync optimization, QRY040 analyzer) and migration tooling (Quarry.Migration). The parser must be dialect-aware (SqlDialect passed at parse time), produce a partial AST with unsupported flags for unrecognized constructs, and support SELECT statements with JOINs, WHERE, GROUP BY, HAVING, ORDER BY, LIMIT/OFFSET, and common expressions (AND, OR, comparisons, IN, IS NULL, LIKE, BETWEEN, NOT, function calls, parameters).

Pre-existing test failures: None. Baseline: 61 analyzer tests + 2554 core tests all passing.
## Decisions
- 2026-04-05: AST nodes use sealed classes with IEquatable (not records) — netstandard2.0 target doesn't support records
- 2026-04-05: Parser files in `Quarry.Shared/Sql/Parser/` subdirectory, excluded from runtime Quarry.csproj via Compile Remove
- 2026-04-05: Follow existing `#if QUARRY_GENERATOR` conditional compilation pattern for dual-namespace
- 2026-04-05: Tests in Quarry.Tests via Generator assembly reference (ReferenceOutputAssembly + InternalsVisibleTo)
- 2026-04-05: Parse all JOIN types (INNER, LEFT, RIGHT, CROSS, FULL OUTER) — parser is self-contained, consumers decide actionability
- 2026-04-05: Non-throwing error strategy — unsupported constructs produce SqlUnsupported nodes, parse errors collected in result
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Started work on #182 — shared SQL parser |
