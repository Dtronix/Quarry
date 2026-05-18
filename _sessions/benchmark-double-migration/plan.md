# Plan: benchmark-double-migration

## Overview

This branch lands two coupled changes:

1. **Benchmark suite migration** from `decimal` to `double` for the `Total`,
   `UnitPrice`, and `LineTotal` columns. The motivation is benchmark integrity:
   `Microsoft.Data.Sqlite.GetDecimal` is implemented as `decimal.Parse(GetString(...))`,
   and that driver-side string-parse path dominates per-cell read cost for
   anyone calling `GetDecimal` (Raw, Quarry-generated reader, SqlKata raw fallback).
   Dapper sidesteps it by going through the `DbDataReader` indexer (boxed double →
   unbox.any → `(decimal)(double)` conversion). The result is a measurement that
   reflects a SQLite driver implementation choice rather than library overhead.
   Switching to `double` removes the slow path entirely: every library reads
   native REAL → CLR double, so the inter-library comparison reflects what each
   library actually adds on top of the floor.

2. **Generator bug fix** in `ProjectionAnalyzer.ResolveAggregateClrType`. The
   migration surfaced a latent bug: when an entity's column type is anything other
   than `decimal`, `Sql.Sum(o.Total)` / `Sql.Avg(o.Total)` aggregate type
   resolution falls through to a hardcoded `"decimal"` default. Root cause is
   timing: the resolver is called in Stage 1 (UsageSiteDiscovery) before Quarry
   regenerates the entity class, so the SemanticModel sees `o.Total` as an
   ErrorType expression. The current Try 2 (invocation return type via Roslyn
   overload resolution) silently picks `Sum(decimal)` as the "best applicable
   candidate" against an Error-typed argument and returns `decimal`. The
   schema-driven Try 3 (column lookup, which has the correct `double` answer)
   is never reached.

The two changes are tightly coupled: without the fix, three benchmarks
(`AggregateSumBenchmarks`, `AggregateAvgBenchmarks`, `WindowRunningSumBenchmarks`)
cannot compile against the new schema and have been parked as `.cs.disabled` in
the worktree.

## Key Concepts

**ResolveAggregateClrType resolution priority.** The fixed method consults three
sources in this order:

1. **Column lookup.** If the aggregate argument is a direct entity-property
   access (`o.PropertyName`), look up the property in the schema-parsed
   `columnLookup`. This is authoritative because the schema parser runs as part
   of Pipeline A entity generation and has full knowledge of the user-declared
   type — independent of what the SemanticModel can see at the moment of the call.

2. **SemanticModel argument type.** Once Roslyn has resolved the regenerated
   entity, `GetTypeInfo(o.Total)` returns the real type. This handles cases
   where the argument isn't a direct entity property (computed expressions,
   captured locals).

3. **Gated SemanticModel invocation-return type.** Only consulted when the
   argument was resolvable above. This guard exists because Roslyn's overload
   resolution against an Error-typed argument silently picks an arbitrary
   "best candidate" — currently the `decimal` overload of `Sql.Sum`/`Sql.Avg`,
   but this is implementation-defined. Gating it on a resolved argument prevents
   the fallback from fabricating a wrong answer.

4. **Default** (passed by caller — `"decimal"` for Sum/Avg, `"object"` for
   Min/Max).

The sibling `ResolveJoinedAggregateClrType` (line 2628) already uses
column-lookup-only and is not affected.

**Schema → entity → reader path.** The Quarry generator emits an entity class
(`Order`) from the schema (`OrderSchema.Col<T> ColumnName`), and the
`ReaderCodeGenerator` emits `r.GetXxx(N)` based on the CLR type. Changing
`Col<decimal>` → `Col<double>` causes:
- Entity property type changes from `decimal` to `double`
- Reader emits `r.GetDouble(N)` instead of `r.GetDecimal(N)`
- `ExecuteScalarAsync<T>` chain uses `T = double` end-to-end
- Aggregate `Sql.Sum(o.Total)` resolves to `Sum(double) → double`

The `Precision(p, s)` modifier on `Col<decimal>` is dropped — Precision is only
meaningful for fixed-precision decimal types, not IEEE-754 doubles.

## Algorithm: fixed ResolveAggregateClrType

```csharp
private static string ResolveAggregateClrType(
    ExpressionSyntax argumentExpression,
    InvocationExpressionSyntax invocation,
    SemanticModel semanticModel,
    Dictionary<string, ColumnInfo> columnLookup,
    string lambdaParameterName,
    string defaultType)
{
    // Try 1: schema-driven column lookup. Authoritative for direct entity
    // property access. Works even when the SemanticModel can't resolve the
    // entity yet (e.g., Stage 1 UsageSiteDiscovery on a freshly changed schema).
    if (argumentExpression is MemberAccessExpressionSyntax memberAccess &&
        memberAccess.Expression is IdentifierNameSyntax identifier &&
        identifier.Identifier.Text == lambdaParameterName)
    {
        var propertyName = memberAccess.Name.Identifier.Text;
        if (columnLookup.TryGetValue(propertyName, out var column) &&
            !TypeClassification.IsUnresolvedTypeNameLenient(column.ClrType))
        {
            return column.ClrType;
        }
    }

    // Try 2: SemanticModel argument type. Handles computed expressions,
    // captured locals, and resolved-entity scenarios.
    var argTypeInfo = semanticModel.GetTypeInfo(argumentExpression);
    var argResolved = argTypeInfo.Type != null &&
                      argTypeInfo.Type.TypeKind != TypeKind.Error;
    if (argResolved)
    {
        var name = GetSimpleTypeName(argTypeInfo.Type!);
        if (!TypeClassification.IsUnresolvedTypeNameLenient(name))
            return name;
    }

    // Try 3 (gated): invocation return type. Only trust this when the argument
    // was resolvable — Roslyn's overload resolution against an Error-typed
    // argument silently picks an arbitrary "best applicable candidate"
    // (typically the decimal overload for Sum/Avg) and returns a non-error
    // ReturnType that looks like a real answer but isn't.
    if (argResolved)
    {
        var invMethodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (invMethodSymbol?.ReturnType != null &&
            invMethodSymbol.ReturnType.TypeKind != TypeKind.Error)
        {
            var name = GetSimpleTypeName(invMethodSymbol.ReturnType);
            if (!TypeClassification.IsUnresolvedTypeNameLenient(name))
                return name;
        }
    }

    return defaultType;
}
```

The comment block above the method body documents the priority and why the
gating exists.

## Phases

### Phase 1 — Generator fix: reorder/gate + typed unresolved sentinel + tests

The root cause turned out to be deeper than originally diagnosed. The reorder
helps but is not sufficient on its own: in Stage 1 (UsageSiteDiscovery) the
`columnLookup` passed to `ResolveAggregateClrType` is intentionally empty (the
analysis is syntax-only for the incremental-pipeline cache contract), so the
column-lookup branch can't succeed there. The actual mechanism is two-stage:
Stage 1 returns a sentinel value, and Stage 4 (`ChainAnalyzer.BuildProjection`)
walks aggregate columns whose `ClrType` is unresolved and enriches them via
`TryResolveAggregateTypeFromSql`. Min/Max already work this way; Sum/Avg
defaulted to the bare string `"decimal"`, which Stage 4's `IsUnresolvedTypeName`
check treats as a resolved type, so enrichment was skipped and the bogus
"decimal" answer flowed to the carrier interface.

**Files modified:**

- `src/Quarry.Generator/Utilities/TypeClassification.cs` — introduce a public
  named constant for the unresolved-type sentinel (currently the bare string
  `"?"` is recognized by both Is\* helpers; the constant replaces magic strings
  at the call sites that produce it). Documented as "the canonical sentinel
  for a CLR type produced by Stage 1 syntax-only analysis when column metadata
  isn't available yet; ChainAnalyzer's enrichment pass converts it to the real
  type once EntityRegistry data is in scope." Both `IsUnresolvedTypeName`
  (strict) and `IsUnresolvedTypeNameLenient` already match `"?"`, so no
  detector changes are needed.

- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` —
  - Apply the reorder + gating fix on `ResolveAggregateClrType` (column lookup
    first; SemanticModel argument type second; gated SemanticModel invocation
    return third; default last). XML-doc updated to reflect the priority and
    why Try 3 is gated.
  - At the 6 Sum/Avg call sites of `ResolveAggregateClrType` /
    `ResolveJoinedAggregateClrType` (4 in regular aggregate paths, 2 in
    window-function paths, 2 in joined-window paths), replace the default
    argument with `TypeClassification.UnresolvedTypeMarker`. This was
    previously a bare `"decimal"` (the bug) and was interim-fixed during
    IMPLEMENT to a bare `"object"` (still a magic-string sentinel) before
    the user pushback. The typed constant is the final form.
  - Leave Min/Max defaults at `"object"` for now — they're consumed by code
    paths beyond the aggregate-type system and the broader migration off
    `"object"`-as-sentinel is deferred to the Known Follow-Ups section.

**Why the reorder + gate matters even though the sentinel default also fires.**
The reordered priority handles the cases where the column lookup IS populated
(joined contexts after Bind, and any future Stage 1 enhancements that flow
column metadata earlier). The gating on Try 3 is future-proofing: if Roslyn
changes its overload-resolution heuristics against Error-typed arguments, the
gate ensures we still bail to the sentinel rather than emit a fabricated
answer. Together they produce a resolver that is correct under the current
pipeline AND robust against the kinds of regressions that made this bug
invisible for years.

**Tests added:**

- `src/Quarry.Tests/Generation/AggregateTypeResolutionTests.cs` (new) — five
  tests covering `Sql.Sum` over `Col<double>`, `Col<decimal>`, `Col<int>`,
  `Col<long>` and `Sql.Avg` over `Col<double>`. Each asserts that the
  generated interceptor's carrier interface uses the correct CLR type and
  does NOT fall back to `decimal`. Pattern follows `Generation/CarrierGenerationTests.cs`.

**Cross-dialect SQL output test (deferred to follow-up).** Originally planned
under the "Both" decision in DESIGN. Re-evaluated during IMPLEMENT: the bug is
purely in CLR-type resolution (the carrier's `IQueryBuilder<T, TResult>`
type parameter) and is dialect-independent. The cross-dialect harness requires
adding a `Col<double>` column to the DDL baseline of every container
(SQLite/PG/MySQL/SQL Server) and the existing fixtures all use DECIMAL(18,2)
/ NUMERIC(18,2). The DDL-broadening cost outweighs the marginal coverage —
the unit tests above already exercise the bug at the layer where it lives.
The cross-dialect addition is tracked as a known follow-up; revisit once the
container DDL fixtures gain a `double` column for unrelated reasons.

**Verification:** All 5 unit tests fail on master and pass after the fix.
Full test suite is 3482/3482 (baseline 3477 + 5 new).

### Known follow-ups (out of scope for this branch)

These were identified during DESIGN/IMPLEMENT but deferred to keep the branch
focused. Each warrants its own issue:

1. **Migrate the rest of `"object"`-as-sentinel usage in ProjectionAnalyzer**
   to `TypeClassification.UnresolvedTypeMarker`. The aggregate paths are
   migrated here; Min/Max defaults and the wider `IsUnresolvedTypeName` /
   `IsUnresolvedTypeNameLenient` callers continue to use `"object"`.
   Touch points: every site where `IsUnresolvedTypeName(x) || x == "object"`
   appears, and every defaulting site that hardcodes `"object"`.

2. **Move from string sentinels to a type-safe `ResolvedClrType` discriminated
   union** (`Resolved(string name)` vs `Unresolved(reason?)`). Would eliminate
   the strict-vs-lenient `IsUnresolvedTypeName` distinction entirely and make
   the "needs ChainAnalyzer enrichment" intent visible at the type level.
   Affects `ProjectedColumn`, `ProjectionInfo`, and all consumers.

3. **Cross-dialect aggregate-type test** with `Col<double>` columns. Defer
   until container DDL fixtures gain a `double` column or a lightweight
   parallel-schema mechanism is introduced for SqlOutput tests.

4. **Pass EntityRegistry into Stage 1 syntax-only analysis** so the sentinel
   becomes unnecessary entirely. Removes the Stage 1 → Stage 4 indirection
   for aggregate types. Cache-invalidation cost: Stage 1's incremental cache
   currently keys on syntax tree alone; widening it to include the registry
   means any schema change invalidates every Stage 1 site result. Trade-off
   needs measurement before committing.

### Phase 2 — Migrate benchmark schema/entities/DTOs to double

**Files:**
- `src/Quarry.Benchmarks/Schemas/OrderSchema.cs` — `Col<decimal> Total => Precision(18, 2)` → `Col<double> Total { get; }`
- `src/Quarry.Benchmarks/Schemas/OrderItemSchema.cs` — `Col<decimal> UnitPrice`/`LineTotal` → `Col<double>`
- `src/Quarry.Benchmarks/Infrastructure/Entities.cs` — `EfOrder.Total`, `EfOrderItem.UnitPrice`/`LineTotal` → `double`
- `src/Quarry.Benchmarks/Infrastructure/Dtos.cs` — `OrderSummaryDto.Total`, `UserOrderDto.Total`, `UserOrderItemDto.Total`, `OrderLagDto.{Total, PrevTotal}`, `OrderRunningSumDto.{Total, RunningSum}`, `OrderIdTotalDto.Total` → `double`. Delete the `DapperOrderLagDto` workaround class (no longer needed).
- `src/Quarry.Benchmarks/Infrastructure/DatabaseSetup.cs` — `decimal` seed literals (`10.0m`, `1.5m`, `5.0m`, `2.5m`) → `double` (`10.0`, `1.5`, `5.0`, `2.5`); local `decimal unitPrice` → `double unitPrice`.

**Tests added/modified:** None at this layer. Validated indirectly by Phase 3.

**Depends on:** Phase 1 (the generator fix must land first so the benchmark
project can compile against the new schema).

### Phase 3 — Update benchmark reader calls and restore disabled files

**Files (modified — `reader.GetDecimal(N)` → `reader.GetDouble(N)`):**
- `src/Quarry.Benchmarks/Benchmarks/CteSimpleBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/CteMultiBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/CteProjectionBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/ComplexJoinFilterPaginateBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/JoinInnerBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/JoinThreeTableBenchmarks.cs`
- `src/Quarry.Benchmarks/Benchmarks/WindowLagBenchmarks.cs` (also: drop the
  Dapper-specific `DapperOrderLagDto` workaround — Dapper can now use the
  regular `OrderLagDto`)

**Files (modified — `Task<decimal>` / `ExecuteScalarAsync<decimal>` / `Convert.ToDecimal` → `double`):**
- `src/Quarry.Benchmarks/Benchmarks/AggregateSumBenchmarks.cs` (restored)
- `src/Quarry.Benchmarks/Benchmarks/AggregateAvgBenchmarks.cs` (restored)
- `src/Quarry.Benchmarks/Benchmarks/WindowRunningSumBenchmarks.cs` (restored)

**Files (deleted):** the three `.cs.disabled` parked files
- `AggregateSumBenchmarks.cs.disabled`
- `AggregateAvgBenchmarks.cs.disabled`
- `WindowRunningSumBenchmarks.cs.disabled`

**Tests added/modified:** None. The benchmark suite is the test for this phase —
it must compile and at least one benchmark from each affected category must run
to completion (smoke test via `dotnet run --project src/Quarry.Benchmarks -c
Release -- --filter "*CteSimpleBenchmarks*" --filter "*AggregateSumBenchmarks*"
--filter "*WindowRunningSumBenchmarks*"`).

**Depends on:** Phase 1 and Phase 2.

### Phase 4 — Remove now-obsolete documentation comments

**Files:**
- `src/Quarry.Benchmarks/Benchmarks/CteSimpleBenchmarks.cs` — drop the canonical
  multi-paragraph NOTE block above the class declaration (no longer applicable
  to the migrated suite; analysis lives in PR/commit history).
- The seven cross-reference comment blocks (`// Reader floor here is bounded by
  Microsoft.Data.Sqlite.GetDecimal...`) in `ComplexJoinFilterPaginateBenchmarks`,
  `CteMultiBenchmarks`, `CteProjectionBenchmarks`, `JoinInnerBenchmarks`,
  `JoinThreeTableBenchmarks`, `WindowLagBenchmarks`, `WindowRunningSumBenchmarks`.

**Tests added/modified:** None.

**Depends on:** Phase 3 (only meaningful after benchmarks have been migrated).

### Phase 5 — Full suite validation

Run the full test suite (`dotnet test -c Release`). Must pass at 3477/3477 like
the baseline. Run a smoke pass on representative benchmarks (CteSimple, WindowLag,
AggregateSum) to confirm the suite executes end-to-end.

**Tests added/modified:** None — validation only.

**Depends on:** Phases 1–4.

## Notes on dependencies

Phases 1–4 are linearly dependent on the preceding phase:
- Phase 1 (generator fix) must land before Phase 2 (schema migration), otherwise
  the benchmark project cannot compile against `Col<double> Total`.
- Phase 2 (schema/entity/DTO migration) must land before Phase 3 (reader call
  updates and restoration), because reader code in the benchmarks references
  DTO properties whose types just changed.
- Phase 3 must precede Phase 4, since the comment removal is meaningful only
  after the readers no longer call `GetDecimal`.
- Phase 5 is the final gate.

Each phase is committed independently so the branch history reads cleanly and
each commit is independently revertable.
