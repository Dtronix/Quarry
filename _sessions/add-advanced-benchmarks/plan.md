# Plan: add-advanced-benchmarks

## Overview

Add 4 new benchmark classes (WindowFunctionBenchmarks, CteBenchmarks, SubqueryBenchmarks, SetOperationBenchmarks) to the Quarry.Benchmarks project, comparing Quarry against Raw ADO.NET, Dapper, EF Core, and SqlKata across 14 scenarios total.

Each benchmark follows the existing pattern: extends `BenchmarkBase`, uses the shared in-memory SQLite database (100 users, 100 orders, ~250 order items), and includes all 5 library implementations per scenario.

For libraries without native support for a feature, we use their raw SQL escape hatch (EF Core: `Database.SqlQueryRaw<T>()`, SqlKata: `SelectRaw`/raw query building) — per design decision.

## New DTOs Required

New result types in `Dtos.cs` for projections that don't match existing DTOs:

```csharp
// Window function results
public class OrderRowNumberDto { OrderId, RowNum }
public class OrderRunningSumDto { OrderId, Total, RunningSum }
public class OrderRankDto { OrderId, Rank }
public class OrderLagDto { OrderId, Total, PrevTotal }

// CTE results
public class OrderIdTotalDto { OrderId, Total }

// Subquery results  
public class UserIdNameDto { UserId, UserName }
```

## Library Support Matrix

| Feature | Raw | Dapper | EF Core | SqlKata | Quarry |
|---------|-----|--------|---------|---------|--------|
| Window Functions | Native SQL | Native SQL | `SqlQueryRaw` | `SelectRaw` | Native API |
| CTEs | Native SQL | Native SQL | `SqlQueryRaw` | Raw SQL string | Native API |
| Subqueries (EXISTS) | Native SQL | Native SQL | Native `.Any()` | `WhereExists` | Native `Many<T>.Any()` |
| Set Operations | Native SQL | Native SQL | Native `.Union()` etc. | Native `.Union()` etc. | Native API |

## Phases

### Phase 1: Add DTOs

Add the new DTO classes to `Infrastructure/Dtos.cs`. These are simple POCOs with auto-properties that all libraries will project into.

**Files modified:** `Infrastructure/Dtos.cs`

**No tests needed** — DTOs are data-only classes used by benchmarks.

### Phase 2: WindowFunctionBenchmarks

Create `Benchmarks/WindowFunctionBenchmarks.cs` with 4 scenarios × 5 libraries = 20 methods.

**Scenario 1: ROW_NUMBER** — `ROW_NUMBER() OVER (PARTITION BY Status ORDER BY Total)`
- Raw/Dapper: Direct SQL string
- EF Core: `Database.SqlQueryRaw<OrderRowNumberDto>(sql)`
- SqlKata: `new Query("orders").SelectRaw("OrderId, ROW_NUMBER() OVER (PARTITION BY Status ORDER BY Total) AS RowNum")`
- Quarry: `QuarryDb.Orders().Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.PartitionBy(o.Status).OrderBy(o.Total))))`

**Scenario 2: Running SUM** — `SUM(Total) OVER (PARTITION BY Status)`
- Same pattern, projecting OrderId + Total + RunningSum
- Quarry: `Sql.Sum(o.Total, over => over.PartitionBy(o.Status))`

**Scenario 3: RANK** — `RANK() OVER (PARTITION BY Status ORDER BY Total)`
- Same pattern as ROW_NUMBER but with RANK()
- Quarry: `Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total))`

**Scenario 4: LAG** — `LAG(Total, 1) OVER (ORDER BY OrderDate)`
- Projects OrderId + Total + PrevTotal
- Quarry: `Sql.Lag(o.Total, 1, over => over.OrderBy(o.OrderDate))`

**Files created:** `Benchmarks/WindowFunctionBenchmarks.cs`
**Build verification:** `dotnet build` the benchmarks project to confirm source generation works for all Quarry queries.

### Phase 3: CteBenchmarks

Create `Benchmarks/CteBenchmarks.cs` with 3 scenarios × 5 libraries = 15 methods.

**Scenario 1: Simple CTE** — Filter orders with Total > 50, select from CTE
```sql
WITH cte AS (SELECT OrderId, Total FROM orders WHERE Total > 50)
SELECT OrderId, Total FROM cte
```
- Raw/Dapper: Direct SQL
- EF Core: `Database.SqlQueryRaw<OrderIdTotalDto>(sql)`
- SqlKata: Full raw SQL string (SqlKata has no CTE support), compile manually
- Quarry: `QuarryDb.With<Order>(orders => orders.Where(o => o.Total > 50)).FromCte<Order>().Select(o => (o.OrderId, o.Total))`

**Scenario 2: CTE + JOIN** — CTE of high-value orders joined with users
```sql
WITH high_orders AS (SELECT OrderId, UserId, Total FROM orders WHERE Total > 50)
SELECT u.UserName, h.Total FROM users u INNER JOIN high_orders h ON u.UserId = h.UserId
```
- Raw/Dapper: Direct SQL  
- EF Core: `SqlQueryRaw`
- SqlKata: Raw SQL string
- Quarry: Uses CTE API — need to verify if Quarry supports joining a CTE result with another table. If not, will use the closest equivalent (may be CTE with full entity then filter).

**Scenario 3: Multi-CTE** — Two CTEs, query from one
```sql
WITH high_orders AS (...), active_users AS (...)
SELECT OrderId, Total FROM high_orders
```
- Raw/Dapper: Direct SQL
- EF Core: `SqlQueryRaw`
- SqlKata: Raw SQL string
- Quarry: `QuarryDb.With<Order>(...).With<User>(...).FromCte<Order>().Select(...)`

**Files created:** `Benchmarks/CteBenchmarks.cs`
**Build verification:** Same as Phase 2.

### Phase 4: SubqueryBenchmarks

Create `Benchmarks/SubqueryBenchmarks.cs` with 4 scenarios × 5 libraries = 20 methods.

**Scenario 1: EXISTS** — Users who have at least one order
```sql
SELECT UserId, UserName FROM users WHERE EXISTS (SELECT 1 FROM orders WHERE orders.UserId = users.UserId)
```
- Raw/Dapper: Direct SQL
- EF Core: `.Where(u => u.Orders.Any())` — native navigation support
- SqlKata: `new Query("users").WhereExists(q => q.From("orders").WhereRaw("orders.UserId = users.UserId").SelectRaw("1"))`
- Quarry: `QuarryDb.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName))`

**Scenario 2: Filtered EXISTS** — Users with at least one order > 50
```sql
SELECT UserId, UserName FROM users WHERE EXISTS (SELECT 1 FROM orders WHERE orders.UserId = users.UserId AND Total > 50)
```
- EF Core: `.Where(u => u.Orders.Any(o => o.Total > 50))`
- Quarry: `QuarryDb.Users().Where(u => u.Orders.Any(o => o.Total > 50))`

**Scenario 3: Scalar COUNT subquery** — Users with more than 2 orders
```sql
SELECT UserId, UserName FROM users WHERE (SELECT COUNT(*) FROM orders WHERE orders.UserId = users.UserId) > 2
```
- EF Core: `.Where(u => u.Orders.Count > 2)` or `.Where(u => u.Orders.Count() > 2)`
- Quarry: `QuarryDb.Users().Where(u => u.Orders.Count() > 2)`

**Scenario 4: Aggregate SUM subquery** — Users whose total order value > 200
```sql
SELECT UserId, UserName FROM users WHERE (SELECT SUM(Total) FROM orders WHERE orders.UserId = users.UserId) > 200
```
- EF Core: `.Where(u => u.Orders.Sum(o => o.Total) > 200)`
- Quarry: `QuarryDb.Users().Where(u => u.Orders.Sum(o => o.Total) > 200)`

**Files created:** `Benchmarks/SubqueryBenchmarks.cs`
**Build verification:** Same.

### Phase 5: SetOperationBenchmarks

Create `Benchmarks/SetOperationBenchmarks.cs` with 3 scenarios × 5 libraries = 15 methods.

**Scenario 1: UNION ALL** — Active users UNION ALL users with high-value orders
```sql
SELECT UserId, UserName FROM users WHERE IsActive = 1
UNION ALL
SELECT u.UserId, u.UserName FROM users u INNER JOIN orders o ON u.UserId = o.UserId WHERE o.Total > 100
```
- Raw/Dapper: Direct SQL
- EF Core: `.Union()` — native support
- SqlKata: `.UnionAll()` — native support
- Quarry: `.UnionAll(...)` — native support

**Scenario 2: INTERSECT** — Active users that also have orders
```sql
SELECT UserId, UserName FROM users WHERE IsActive = 1
INTERSECT
SELECT u.UserId, u.UserName FROM users u INNER JOIN orders o ON u.UserId = o.UserId
```
- All libraries: similar approach

**Scenario 3: EXCEPT** — Active users minus those with cancelled orders
```sql
SELECT UserId, UserName FROM users WHERE IsActive = 1
EXCEPT
SELECT u.UserId, u.UserName FROM users u INNER JOIN orders o ON u.UserId = o.UserId WHERE o.Status = 'cancelled'
```
- All libraries: similar approach

**Files created:** `Benchmarks/SetOperationBenchmarks.cs`
**Build verification:** Same.

### Phase 6: Final build + verify

1. Build the entire Quarry.Benchmarks project in Release mode to verify source generation.
2. Run the full test suite to confirm no regressions.
3. Commit all changes together.

**Files modified:** None new — verification only.

## Dependencies

- Phase 1 must complete before Phases 2-5 (DTOs are used by all benchmarks).
- Phases 2-5 are independent of each other and can be committed separately.
- Phase 6 depends on all prior phases.

## Notes

- The Quarry queries use tuple projections `(o.OrderId, RowNum: Sql.RowNumber(...))` which the source generator maps to named tuple results. For benchmarks returning `List<T>`, we project into DTOs instead.
- EF Core's `Database.SqlQueryRaw<T>()` (added in EF Core 8) maps raw SQL to unmapped types — ideal for window functions and CTEs where LINQ has no support.
- SqlKata's `SelectRaw()` allows embedding raw SQL expressions in an otherwise fluent query. For CTEs, the entire SQL must be raw since SqlKata has no CTE builder.
- All raw SQL strings are written as string literals (not interpolated) to avoid parameter injection concerns in benchmarks.
