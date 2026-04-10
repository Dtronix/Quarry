# Code Review: add-advanced-benchmarks

**Reviewer:** Claude Code (Opus 4.6)
**Date:** 2026-04-09
**Branch:** `add-advanced-benchmarks` (5 commits, 4 new files, 1 modified file)
**Scope:** 4 new benchmark classes + 7 new DTOs added to `Quarry.Benchmarks`

---

## 1. Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| CTE scenario 2 changed from "CTE+JOIN" (CTE joined with users table) to "CTE with projection" (CTE narrows columns, outer query selects subset). The plan specified `SELECT u.UserName, h.Total FROM users u INNER JOIN high_orders h ON u.UserId = h.UserId`; the implementation instead uses a two-stage projection CTE. | Medium | Scenario measures a different SQL pattern than planned. The CTE+JOIN scenario would test CTE-to-table join performance, which is a common real-world use case not currently covered. The replacement scenario (CteProjection) is functionally similar to SimpleCte -- both filter orders by Total > 50 and select OrderId, Total. |
| `CteJoinDto` (UserName + Total) was added to `Dtos.cs` but is never referenced by any benchmark method. It is a vestige of the original CTE+JOIN plan. | Low | Dead code. Minor clutter, but no functional impact. |
| Set operation scenarios simplified from plan. UNION ALL changed from "active users UNION ALL users with high-value orders (via JOIN)" to "active users UNION ALL users with UserId = 1". INTERSECT changed from "active users INTERSECT users who have orders (via JOIN)" to "active users INTERSECT users with non-null email". EXCEPT changed from "active users EXCEPT users with cancelled orders (via JOIN)" to "active users EXCEPT users with UserId <= 10". | Low | The simplified scenarios avoid JOINs in the second query arm, making them less complex than planned. However, the set operation itself (UNION ALL/INTERSECT/EXCEPT) is still the focus being benchmarked, so this is a reasonable simplification. |
| Plan specified 14 total scenarios across 4 classes. Implementation delivers 14 scenarios (Window: 4, CTE: 3, Subquery: 4, SetOps: 3) with 70 total benchmark methods (14 x 5 libraries). Count matches. | None | Matches plan. |
| Plan specified Phases 2-5 could be committed separately. Phases 2-5 were committed in 4 separate commits (d0c8d7c, ba3a217, 4ffa829, dbb7fa8). Phase 1 DTOs were bundled with Phase 2 commit. | None | Reasonable -- DTOs were needed for the first benchmark class. |
| Decision to use raw SQL escape hatch for unsupported features (EF Core SqlQueryRaw, SqlKata raw SQL) was followed consistently. | None | Matches plan. |

## 2. Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `SimpleCteFilterSql` selects all 6 entity columns in the CTE body (`OrderId, UserId, Total, Status, OrderDate, Notes`) but the plan specified only `SELECT OrderId, Total`. The raw SQL CTE is wider than necessary. | Low | The wider CTE is intentionally aligned with what `Quarry.With<Order>(...)` generates (full entity), so it is a fair comparison. However, the extra columns in the CTE body make the raw SQL slightly slower than the minimal version from the plan, which could marginally skew benchmark results against Raw/Dapper/EfCore/SqlKata. |
| `MultiCteSql` also selects all entity columns in both CTEs for the same reason (matching Quarry's behavior). | Low | Same reasoning as above. Fair comparison but not the minimal SQL a developer would write by hand. |
| `EfCore_UnionAll` uses `.Concat()` instead of EF Core's `.Union()` method. In SQL, `Concat` maps to `UNION ALL` (preserves duplicates) while `Union` maps to `UNION` (removes duplicates). This is correct -- the scenario is UNION ALL. | None | Correct usage. |
| LAG null handling: `OrderLagDto.PrevTotal` is `decimal?` and the Raw/SqlKata reader methods use `reader.IsDBNull(2) ? null : reader.GetDecimal(2)`. This correctly handles the NULL that LAG returns for the first row. | None | Correct. |
| `OrderRowNumberDto.RowNum` and `OrderRankDto.Rank` use `long` type. SQLite's ROW_NUMBER() and RANK() return INTEGER (64-bit), read via `reader.GetInt64()`. This is correct. | None | Correct. |
| `RunningSum` is `decimal` (non-nullable). Since `Total` is `REAL NOT NULL` in the schema, `SUM(Total) OVER (...)` can never be NULL (every partition has at least one row with a non-null Total). | None | Correct. |
| `EfCore_CountSubquery` uses `u.Orders.Count` (property, not method). EF Core translates the `ICollection<T>.Count` property to a correlated `COUNT(*)` subquery. This is semantically equivalent to `u.Orders.Count()` (method). | None | Correct, matches plan's alternative syntax. |
| `Quarry_IsActive` comparison: `u.IsActive == true` in MultiCte vs `u.IsActive` in other locations. Both are valid; Quarry's source generator handles both forms. | None | No concern -- stylistic only. |

## 3. Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All SQL strings are compile-time constants (`const string` or raw string literals). No string interpolation or user input concatenation. | None | Correct per plan. Benchmarks are internal-only code with no external input. |

No concerns.

## 4. Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No unit tests were added for the new benchmark classes. | None | Expected -- benchmark projects validate correctness through execution (BenchmarkDotNet runs each method), not through separate test assertions. The plan explicitly states "No tests needed" for DTOs and relies on build verification (source generator compilation) for Quarry queries. |

No concerns. This is standard practice for benchmark code.

## 5. Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Naming convention `{Library}_{Operation}` is followed consistently: `Raw_RowNumber`, `Dapper_RowNumber`, `EfCore_RowNumber`, `Quarry_RowNumber`, `SqlKata_RowNumber`. Matches existing benchmarks (`Raw_InnerJoin`, `Dapper_InnerJoin`, etc.). | None | Correct. |
| `[Benchmark(Baseline = true)]` is placed on the first `Raw_` method of each class (one per class). This matches existing benchmark classes (e.g., `AggregateBenchmarks`, `SelectBenchmarks`, `FilterBenchmarks`). | None | Correct. |
| Base class usage: all 4 new classes extend `BenchmarkBase` and use `Connection`, `QuarryDb`, `EfContext`, and `SqlKataCompiler` from the base. No custom `GlobalSetup` overrides needed. | None | Correct. |
| Raw ADO.NET reader pattern: `CommandBehavior.SingleResult \| CommandBehavior.SequentialAccess` is used in `Raw_*` methods but not in `SqlKata_*` methods (which use default `CommandBehavior`). | Low | This is **consistent with existing benchmarks** -- e.g., `JoinBenchmarks.Raw_InnerJoin` uses `SingleResult \| SequentialAccess` while `JoinBenchmarks.SqlKata_InnerJoin` uses default. So the new code correctly follows the established pattern, even though there is a minor performance asymmetry between Raw and SqlKata reader calls. |
| SqlKata compilation pattern matches existing benchmarks: `SqlKataCompiler.Compile(query)`, iterate `compiled.Bindings`, use `AddWithValue`. | None | Correct. |
| `using` directives: new files include `using Quarry.Benchmarks.Infrastructure;` which is already in `GlobalUsings.cs`. This is redundant but **matches every existing benchmark file** in the project. | None | Consistent with existing convention. |
| Import of `using Microsoft.Data.Sqlite;` appears in all 4 new files but `SqliteConnection` is never directly referenced (it comes from `BenchmarkBase.Connection`). The `SqliteCompiler` type comes from `SqlKata.Compilers`. | Low | Redundant import, but it is present in most existing benchmark files too (e.g., `ComplexQueryBenchmarks.cs`, `FilterBenchmarks.cs`), so it follows the codebase convention. |

## 6. Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No existing benchmark files were modified (only `Infrastructure/Dtos.cs` was extended with new DTOs appended at the end). | None | No risk to existing benchmarks. |
| No `.csproj` changes -- no new NuGet dependencies were added. | None | No supply chain risk. |
| No API changes to the Quarry library itself. All changes are confined to the `Quarry.Benchmarks` project. | None | No breaking changes. |
| Source generator successfully produced interceptor files for all 4 new benchmark classes (confirmed by presence of `BenchDb.Interceptors.*.g.cs` files in `obj/GeneratedFiles`). | None | Build verification passed. |

No concerns.

---

## Summary

The implementation is solid and well-aligned with the plan. The 4 new benchmark classes deliver 14 scenarios across 70 benchmark methods, covering Window Functions, CTEs, Subqueries, and Set Operations.

**Key deviations from plan:**
1. CTE scenario 2 was changed from "CTE+JOIN" to "CTE projection" -- different SQL pattern being tested.
2. Set operation second-arm queries were simplified to avoid JOINs.
3. `CteJoinDto` is unused dead code (leftover from the original CTE+JOIN plan).

**No correctness issues found.** Null handling for LAG, type mappings for window function return types, and SQL/DTO alignment are all correct.

**No security, integration, or breaking change concerns.**
