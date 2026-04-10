## Summary
- Closes #238

## Reason for Change
The 2026-04-09 benchmark run surfaced two benchmark failures (Quarry_Lag crash, Dapper_Lag type mismatch) and one measurement anomaly (BatchInsert10 0.94x). Additionally, benchmark ratio/alloc-ratio columns were skewed because multi-group benchmark classes shared a single Raw baseline.

## Impact
- **Source generator fix:** DTOs with nullable value type properties (`decimal?`, `int?`, etc.) now correctly receive `IsDBNull` guards in generated reader code when used with aggregate/window functions like `Sql.Lag()`, `Sql.Lead()`, etc.
- **Benchmark accuracy:** Each benchmark group now has its own Raw baseline, producing correct Ratio and AllocRatio comparisons.
- **Benchmark history:** Splitting classes changes fully-qualified method names tracked by `benchmark-action`. Historical trend lines for old class names will end; new ones start fresh. This is a one-time discontinuity.

## Plan items implemented as specified
1. **IsDBNull fix** — Added `IsConvertedTypeNullable` helper using Roslyn's `ConvertedType` to determine nullability from the assignment target type. Applied at 3 sites in `ProjectionAnalyzer.cs`.
2. **Dapper_Lag fix** — Created `DapperOrderLagDto` with `double?` to match SQLite's REAL type affinity.
3. **Benchmark splitting** — Split 12 multi-group classes into 32 single-group classes, each with its own `Raw_*` baseline.
4. **BatchInsert10 investigation** — Both implementations use identical multi-row `INSERT INTO ... VALUES` syntax. Raw has 3.6x higher StdDev (4.915 vs 1.363 μs). Previous run was 1.07x, this run 0.94x — classic run-to-run noise.

## Deviations from plan implemented
- None

## Gaps in original plan implemented
- Fixed orphaned XML doc comment on `ResolveAggregateClrType` discovered during review.

## Migration Steps
- None required

## Performance Considerations
- The `IsConvertedTypeNullable` helper adds one `GetTypeInfo` call per aggregate/window function column during source generation (compile-time only, not runtime).

## Security Considerations
- None

## Breaking Changes
- Consumer-facing: None. The generated reader code now correctly handles nullable value types where it previously crashed.
- Internal: Benchmark class names changed (12 classes → 32). No API surface affected.
