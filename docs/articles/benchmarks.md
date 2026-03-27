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

## Results

Results are generated by running the benchmark suite. See the instructions above.

After running, the BenchmarkDotNet Markdown output will be available in the
`docs/articles/benchmark-results/` directory. Each benchmark class produces its own results
file that can be reviewed or included here.

To regenerate results, run:

```bash
./scripts/run-benchmarks.sh
```
