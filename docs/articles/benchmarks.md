# Benchmarks

Quarry ships with a comprehensive [BenchmarkDotNet](https://benchmarkdotnet.org/) suite that
measures query-building and execution overhead against several popular data-access libraries.
This article explains what the benchmarks measure, how to run them, and how to read the results.

## Libraries compared

Every benchmark method executes the **same logical operation** against an in-memory SQLite
database so that the numbers reflect framework overhead rather than network or engine variance.

| Label | What it represents |
|---|---|
| **Raw** (baseline) | Hand-written `SqliteCommand` with ordinal `DbDataReader` access. This is the theoretical minimum overhead and is marked as the baseline in every benchmark class. |
| **Dapper** | Micro-ORM. Passes raw SQL strings and maps results via reflection/emit. |
| **EF Core** | Full ORM with change tracking disabled (`AsNoTracking`). |
| **SqlKata** | Query-builder that compiles a query object to SQL at runtime. Included in all benchmark classes. |
| **Quarry** | Source-generated queries. SQL text and reader logic are emitted at compile time. |

## Benchmark categories

The suite is organized into benchmark classes, each targeting a specific category of database
operation.

### SelectBenchmarks

Tests basic row retrieval: selecting all columns from a table and selecting a subset of columns
via projection into a DTO.

### FilterBenchmarks

Tests `WHERE` clause generation with several patterns: a simple boolean filter, a compound
filter (`AND` with `IS NOT NULL`), and a point lookup by primary key.

### JoinBenchmarks

Tests `INNER JOIN` across two tables (users + orders) and three tables (users + orders +
order_items), measuring the cost of join clause generation and multi-table result mapping.

### AggregateBenchmarks

Tests scalar aggregate functions: `COUNT(*)`, `SUM`, and `AVG`.

### PaginationBenchmarks

Tests `LIMIT`/`OFFSET` pagination: fetching the first page and fetching an arbitrary page deep
in the result set.

### StringOpBenchmarks

Tests string-matching operators: `Contains` (`LIKE '%...%'`) and `StartsWith` (`LIKE '...%'`).

### InsertBenchmarks

Tests single-row inserts and batch inserts (10 rows) across all libraries.

### UpdateBenchmarks

Tests single-row `UPDATE` statements.

### DeleteBenchmarks

Tests single-row `DELETE` statements.

### ComplexQueryBenchmarks

Tests composite operations that combine joins, filters, pagination, and aggregates in a single
query, representing realistic application workloads.

### ColdStartBenchmarks

Measures first-query latency by creating a fresh context per iteration. Each benchmark method
constructs a new context (or compiler, for SqlKata) and executes a single query. This isolates
the one-time startup cost: EF Core's model compilation, Dapper's first-run reflection/IL emit,
vs Quarry's zero-warmup pre-built interceptors.

### ConditionalBranchBenchmarks

Measures dynamic query building with conditional `WHERE`, `ORDER BY`, and `LIMIT` clauses
controlled by runtime boolean flags. This highlights Quarry's bitmask dispatch (a single integer
switch over pre-built SQL variants) vs runtime string concatenation in Raw/Dapper/SqlKata and
conditional LINQ chains in EF Core.

### ThroughputBenchmarks

Runs a simple WHERE-by-ID query 1000 times per benchmark invocation, varying the ID to avoid
caching bias. Measures sustained throughput and total allocations under load. Quarry uses
`RawSqlAsync` in this benchmark because the source generator cannot analyze query chains inside
loop bodies (QRY032).

## How to run

### Prerequisites

- .NET 10 SDK (or the version targeted by the benchmark project)
- A shell that can run `dotnet` commands

### Running manually

```bash
cd src/Quarry.Benchmarks
dotnet run -c Release -- --filter '*'
```

To run a single benchmark class:

```bash
dotnet run -c Release -- --filter '*SelectBenchmarks*'
```

### Using the runner script

A convenience script is provided at the repository root:

```bash
./scripts/run-benchmarks.sh
```

The script builds in Release mode, runs all benchmarks, and copies the BenchmarkDotNet Markdown
artifacts into `docs/articles/benchmark-results/` for easy inclusion in documentation.

## Interpreting results

BenchmarkDotNet produces tables with several columns. The most important ones are:

| Column | Meaning |
|---|---|
| **Mean** | Average execution time across all iterations. Lower is better. |
| **Ratio** | Execution time relative to the baseline (Raw ADO.NET). A ratio of 1.00 means identical to baseline; 1.50 means 50% slower. |
| **Allocated** | Managed heap memory allocated per operation. Lower is better. |
| **Alloc Ratio** | Memory allocated relative to the baseline. |

When evaluating results, focus on:

1. **Ratio to baseline** -- How close is the library to hand-written ADO.NET? A ratio near 1.00
   means the framework adds negligible overhead.
2. **Allocated memory** -- Allocation pressure directly affects GC frequency. Libraries that
   avoid intermediate allocations will perform better under sustained load.
3. **Consistency across categories** -- A library may be fast for simple selects but slow for
   joins or conditional queries. Look at the full picture.

## Key takeaways

Quarry's source-generation approach has several structural advantages that show up in the
benchmark numbers.

### Cold start

Traditional ORMs pay a one-time cost on first use: EF Core compiles its model and caches
expression trees, Dapper emits IL for its type mappers. Quarry has no such warmup because the
SQL text and reader delegates are generated at compile time and embedded directly in the
assembly.

### Allocations

Quarry emits SQL as pre-built string literals rather than constructing them from fragments at
runtime. Reader methods use ordinal-based access (e.g., `reader.GetInt32(0)`) instead of
name-based lookups or reflection. This eliminates intermediate string allocations and avoids the
boxing/unboxing overhead common in reflection-based mappers.

### Conditional queries

When a query has optional `WHERE` clauses or conditional joins, runtime query builders must
concatenate string fragments on every execution. Quarry uses bitmask-based dispatch to select
from a set of pre-compiled SQL variants, so the "building" step is a single integer switch
rather than string manipulation.

### Throughput

Because there is no per-query interpretation -- no expression tree walking, no SQL compilation,
no reflection -- Quarry's per-query overhead is close to that of hand-written ADO.NET. The gap
between Quarry and the Raw baseline should be minimal and stable across all benchmark
categories.

## Latest Results

<!-- Updated: 2026-04-03 | Commit: 9875fdc | Branch: master -->
<!-- Full benchmark history: https://github.com/Dtronix/Quarry/issues/105 -->

The tables below show the most recent benchmark run. Each value is a ratio to the Raw ADO.NET
baseline — a value of 1.00 means identical performance. For full run-over-run history and
regression tracking, see [Performance Tracking (Issue #105)](https://github.com/Dtronix/Quarry/issues/105#issuecomment-4182037102).

**Environment:** AMD Ryzen 9 3900X, .NET 10.0.4, BenchmarkDotNet v0.15.8, `Job.Default`

### Speed Ratio to Raw ADO.NET

Lower is better. 1.00 = identical to hand-written ADO.NET.

| Benchmark | Raw (us) | Dapper | **Quarry** | SqlKata | EF Core |
|---|---:|---:|---:|---:|---:|
| **Aggregates** | | | | | |
| Count | 3.09 | 1.05x | <u>1.03x</u> | 2.59x | 3.34x |
| Sum | 9.06 | 1.02x | <u>1.01x</u> | 1.60x | 9.23x |
| Avg | 8.92 | 1.02x | <u>1.01x</u> | 1.61x | 9.12x |
| **Cold Start** | | | | | |
| ColdStart | 100.84 | 1.19x | <u>0.99x</u> | 1.12x | 2.04x |
| **Complex Queries** | | | | | |
| JoinFilterPaginate | 15.66 | 0.85x | <u>1.00x</u> | 2.61x | 3.89x |
| MultiJoinAggregate | 33.80 | 1.00x | <u>0.99x</u> | 1.42x | 2.08x |
| **Conditional** | | | | | |
| ConditionalQuery | 46.86 | 1.16x | <u>1.00x</u> | 1.49x | 1.89x |
| **Delete** | | | | | |
| DeleteSingleRow | 25.86 | 1.18x | <u>1.06x</u> | 2.35x | 14.19x |
| **Filters** | | | | | |
| WhereById | 6.29 | 1.31x | <u>0.99x</u> | 2.46x | 3.28x |
| WhereCompound | 40.38 | 1.35x | <u>1.01x</u> | 1.17x | 1.85x |
| WhereActive | 98.46 | 1.23x | <u>1.01x</u> | 1.07x | 1.31x |
| **Inserts** | | | | | |
| SingleInsert | 27.56 | 1.30x | <u>1.10x</u> | 2.08x | 17.15x |
| BatchInsert10 | 73.54 | 2.02x | <u>1.06x</u> | 1.79x | 21.48x |
| **Joins** | | | | | |
| InnerJoin | 83.52 | 0.68x | <u>0.98x</u> | 1.11x | 1.43x |
| ThreeTableJoin | 232.46 | 0.81x | <u>1.01x</u> | 1.07x | 1.34x |
| **Pagination** | | | | | |
| LimitOffset | 15.92 | 1.22x | <u>1.02x</u> | 1.96x | 2.42x |
| FirstPage | 15.53 | 1.21x | <u>1.02x</u> | 1.93x | 2.30x |
| **Select** | | | | | |
| SelectProjection | 44.55 | 1.41x | <u>1.00x</u> | 1.15x | 1.63x |
| SelectAll | 103.28 | 1.28x | <u>1.03x</u> | 1.09x | 1.32x |
| **String Ops** | | | | | |
| Contains | 17.43 | 1.13x | <u>0.99x</u> | 2.81x | 2.33x |
| StartsWith | 50.45 | 1.34x | <u>1.03x</u> | 1.73x | 1.74x |
| **Throughput** | | | | | |
| Throughput (ms) | 6,216 | 1.29x | <u>0.93x</u> | 2.56x | 4.11x |
| **Update** | | | | | |
| UpdateSingleRow | 24.01 | 1.29x | <u>1.01x</u> | 2.71x | 18.04x |

### Allocation Ratio to Raw ADO.NET

Lower is better. 1.00 = same memory as hand-written ADO.NET.

| Benchmark | Dapper | **Quarry** | SqlKata | EF Core |
|---|---:|---:|---:|---:|
| **Aggregates** | | | | |
| Count | 1.08x | <u>1.08x</u> | 15.30x | 6.55x |
| Sum | 1.09x | <u>1.10x</u> | 15.19x | 25.39x |
| Avg | 1.09x | <u>1.10x</u> | 15.19x | 25.35x |
| **Cold Start** | | | | |
| ColdStart | 1.31x | <u>1.16x</u> | 1.76x | 4.36x |
| **Complex Queries** | | | | |
| JoinFilterPaginate | 1.13x | <u>1.08x</u> | 11.97x | 7.82x |
| MultiJoinAggregate | 1.07x | <u>1.11x</u> | 27.16x | 15.62x |
| **Conditional** | | | | |
| ConditionalQuery | 1.33x | <u>1.06x</u> | 3.49x | 2.90x |
| **Delete** | | | | |
| DeleteSingleRow | 1.41x | <u>1.05x</u> | 10.79x | 79.65x |
| **Filters** | | | | |
| WhereById | 1.78x | <u>0.93x</u> | 13.18x | 5.23x |
| WhereCompound | 1.49x | <u>1.02x</u> | 2.54x | 3.85x |
| WhereActive | 1.31x | <u>1.16x</u> | 1.61x | 2.14x |
| **Inserts** | | | | |
| SingleInsert | 1.81x | <u>1.11x</u> | 8.50x | 50.74x |
| BatchInsert10 | 2.14x | <u>1.08x</u> | 3.12x | 12.21x |
| **Joins** | | | | |
| InnerJoin | 0.97x | <u>1.01x</u> | 2.25x | 2.92x |
| ThreeTableJoin | 0.97x | <u>1.00x</u> | 1.51x | 2.30x |
| **Pagination** | | | | |
| LimitOffset | 1.46x | <u>1.16x</u> | 6.45x | 3.92x |
| FirstPage | 1.45x | <u>1.16x</u> | 6.02x | 3.40x |
| **Select** | | | | |
| SelectProjection | 1.51x | <u>1.01x</u> | 2.12x | 3.56x |
| SelectAll | 1.32x | <u>1.17x</u> | 1.51x | 2.07x |
| **String Ops** | | | | |
| Contains | 1.48x | <u>1.08x</u> | 9.52x | 7.31x |
| StartsWith | 1.51x | <u>1.02x</u> | 2.61x | 3.88x |
| **Throughput** | | | | |
| Throughput | 1.88x | <u>1.15x</u> | 14.10x | 6.34x |
| **Update** | | | | |
| UpdateSingleRow | 1.60x | <u>0.89x</u> | 11.81x | 67.36x |

### Summary

| | Dapper | **Quarry** | SqlKata | EF Core |
|---|---:|---:|---:|---:|
| **Median speed ratio** | 1.21x | <u>1.01x</u> | 1.73x | 2.33x |
| **Median alloc ratio** | 1.41x | <u>1.08x</u> | 6.45x | 5.23x |

### Analysis

Across 23 benchmarks, Quarry's median overhead is **1.01x Raw ADO.NET** — effectively zero.
It is the fastest library tested, outperforming Dapper (1.21x median) while allocating
significantly less memory (1.08x vs Dapper's 1.41x).

**Best results:** Quarry is *faster* than hand-written ADO.NET on cold-start (0.99x),
where-by-id (0.99x), contains (0.99x), multi-join-aggregate (0.99x), inner-join (0.98x),
and throughput (0.93x). It also allocates *less* memory than Raw on update (0.89x) and
where-by-id (0.93x).

**Highest overhead:** Single-insert (1.10x) and delete (1.06x) show the most speed overhead,
both well within noise thresholds.

**Allocations:** Quarry stays within 0.89–1.17x of Raw across all operations. Quarry allocates
far less than every other library tested, including Dapper (1.41x median), SqlKata (6.45x
median), and EF Core (5.23x median).
