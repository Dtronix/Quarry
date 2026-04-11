# Workflow: 246-add-shared-migration-interfaces
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #246
pr:
session: 1
phases-total: 2
phases-complete: 1
## Problem Statement
Add shared interfaces (`IConversionEntry`, `IConversionDiagnostic`) for migration converter result types across Dapper, EF Core, ADO.NET, and SqlKata converters. Currently 9 public result types share identical shapes but no common base, limiting uniform processing.

Baseline: all 3178 tests pass (163 Migration, 103 Analyzers, 2912 Quarry). No pre-existing failures.
## Decisions
- 2026-04-10: IConversionEntry includes 7 common properties: FilePath, Line, ChainCode, Diagnostics (IReadOnlyList<IConversionDiagnostic>), IsConvertible, HasWarnings, and OriginalSource (unified name for OriginalSql/OriginalCode). Concrete types retain their existing named properties; OriginalSource is additive.
- 2026-04-10: IConversionDiagnostic includes: Severity (string), Message (string).
- 2026-04-10: Diagnostics on IConversionEntry uses explicit interface implementation to bridge from strongly-typed IReadOnlyList<XxxDiagnostic> to IReadOnlyList<IConversionDiagnostic> via covariance.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Loaded issue #246, created worktree, baseline 3178 tests all green |
