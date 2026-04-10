# Workflow: 238-benchmark-fixes
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: #238
pr:
session: 1
phases-total: 4
phases-complete: 4
## Problem Statement
Issue #238: Benchmark failures and discrepancies from 2026-04-09 run (11e6533).

Three items:
1. **Quarry_Lag null handling (Bug):** Source generator does not emit `IsDBNull` check for nullable value types, causing crash when `LAG()` returns NULL.
2. **Dapper_Lag type mismatch (Benchmark fix):** Dapper fails to map SQLite `Double` to `Nullable<Decimal>`. Need to add type handler or adjust DTO.
3. **BatchInsert10 0.94x (Investigation):** Quarry measures faster than Raw — verify equivalent work or document as noise.

Baseline: 3110 tests, 0 failures, 0 skipped.
## Decisions
- 2026-04-10: Item 1 (IsDBNull fix) — Use Roslyn `ConvertedType` to determine nullability from target property type. Fix at 3 sites in ProjectionAnalyzer.cs (lines 614, 715, 752). Respects user-declared types without changing method signatures.
- 2026-04-10: Item 2 (Dapper_Lag fix) — Use separate DTO for Dapper with `double?` instead of `decimal?` to avoid SQLite type affinity mismatch.
- 2026-04-10: Item 3 (BatchInsert10) — Document as measurement noise. Implementations are functionally equivalent; Raw has 3.6x higher StdDev.
- 2026-04-10: Item 4 (Benchmark grouping) — Split multi-group benchmark classes into separate classes per test type so each has its own Raw baseline. 12 classes affected. 6 single-group classes left unchanged.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | REVIEW | Started workflow for issue #238. All 4 design decisions confirmed. 4 implementation phases completed. |
