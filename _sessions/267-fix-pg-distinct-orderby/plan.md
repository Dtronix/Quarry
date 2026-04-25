# Plan: Fix DISTINCT + ORDER BY non-portable SQL (#267)

## Overview

PostgreSQL rejects `SELECT DISTINCT <projection> ... ORDER BY <expr>` when `<expr>` is not in the SELECT projection (42P10). SQL Server does the same under standard-conforming behavior. SQLite and MySQL tolerate the construct but with implementation-defined semantics — they pick one row per projection key arbitrarily, then order by the non-projected column whose value depends on which row was picked.

The fix: apply a derived-table wrap to **all four dialects** whenever DISTINCT combines with an ORDER BY that references a non-projected expression. The wrap moves DISTINCT and the ORDER BY columns into an inner SELECT, then the outer SELECT projects only the original columns and applies ORDER BY + pagination:

```
SELECT <d>.<proj_aliases>
  FROM (
    SELECT DISTINCT <proj_cols [AS proj_aliases]>, <orderby_cols [AS order_aliases]>
      FROM ... [JOINs] [WHERE] [GROUP BY] [HAVING]
  ) AS <d>
 ORDER BY <d>.<alias> [ASC|DESC], ...
[LIMIT/OFFSET applied to outer]
```

Result shape changes for affected chains: row count is now `count(distinct (proj_cols, orderby_cols))` instead of `count(distinct proj_cols)`. This is the standard-SQL-compliant semantic and is identical across all four dialects.

## Key concepts

**Detection by rendered-string equality.** The generator already renders both projection columns and ORDER BY expressions into dialect-specific SQL fragments. Detection compares those rendered strings: every active ORDER BY expression's rendered SQL must appear in the set of rendered projection-column references, otherwise the wrap is needed. Bare-column references (`"t1"."Total"`) and complex expressions (`LOWER("t0"."UserName")`) are handled uniformly. A complex ORDER BY expression that doesn't match any projection column triggers the wrap.

**Aliasing scheme.** The wrap introduces an inner SELECT (with extended projection and inner aliases) and an outer SELECT (referencing the inner aliases via a derived-table alias). To avoid collision with user-supplied column names:
- Derived-table alias: `__d`.
- Projection-column inner aliases: `__c0`, `__c1`, ... numbered by ordinal. Projection columns that already carry an explicit alias (e.g., aggregates that emit `AS Item2`) keep that alias.
- ORDER BY-only inner aliases: `__o0`, `__o1`, ... numbered per ORDER BY term that is not already in the projection. ORDER BY terms whose expression renders identically to a projection column reuse the projection's `__c{i}` alias in the outer ORDER BY.

**Reader-code compatibility.** Reader code reads result columns by ordinal, not by name. The outer SELECT preserves the original projection-column ordinals (same count, same order), so no reader-code changes are required.

**Multi-mask handling.** Conditional ORDER BY terms (mask-driven) make detection per-mask: a mask where every active ORDER BY term is in projection emits the flat SQL; a mask where at least one active term is non-projected emits the wrap. The batch-rendering fast path in `RenderSelectSqlBatch` falls back to per-mask single-rendering when any mask in the plan can need the wrap.

**Excluded paths.**
- Set operations (`plan.SetOperations.Count > 0`) keep the existing post-union derived-table wrap. The DISTINCT + ORDER BY wrap is not applied on top.
- Non-Select queries (DELETE / UPDATE / INSERT) are unaffected — DISTINCT only applies to SELECT.

## Algorithm

Detection (returns true ⇒ emit wrap for this mask):

```
NeedsDistinctOrderByWrap(plan, mask, dialect):
    if not plan.IsDistinct:                            return false
    if plan.SetOperations.Count > 0:                   return false
    activeOrderTerms = GetActiveTerms(plan.OrderTerms, mask)
    if activeOrderTerms.Count == 0:                    return false
    projColumnSqls = { RenderProjectionColumnRef(c, dialect) for c in plan.Projection.Columns }
    foreach term in activeOrderTerms:
        rendered = SqlExprRenderer.Render(term.Expression, dialect, paramOffset: 0)
        if rendered not in projColumnSqls:
            return true
    return false
```

Note: the dialect parameter is still threaded through because `RenderProjectionColumnRef` and `SqlExprRenderer.Render` produce dialect-specific quoting (`"x"` vs `[x]` vs `` `x` ``). The dialect does not gate the wrap decision.

Wrap rendering (single mask):

```
RenderSelectSqlWithDistinctOrderByWrap(plan, mask, dialect, paramBaseOffset):
    activeOrder = GetActiveTerms(plan.OrderTerms, mask)
    projColumnSqls = { RenderProjectionColumnRef(c, dialect): index for c in plan.Projection.Columns }

    # Decide outer ORDER BY alias for each active term
    extraOrderExprs = []         # (expr, "__o{i}") to add to inner SELECT
    outerOrderByAliases = []     # parallel to activeOrder
    nextO = 0
    foreach term in activeOrder:
        rendered = SqlExprRenderer.Render(term.Expression, dialect, paramOffset: 0)
        if rendered in projColumnSqls:
            outerOrderByAliases.append(GetInnerProjAlias(plan.Projection.Columns[projColumnSqls[rendered]], idx))
        else:
            alias = "__o{nextO}"
            extraOrderExprs.append((term.Expression, alias))
            outerOrderByAliases.append(alias)
            nextO += 1

    # ── Outer prefix: WITH (CTE) + SELECT outer projection FROM (
    sb = new StringBuilder
    paramIndex = paramBaseOffset
    AppendCtePrefix(sb, plan, dialect, ref paramIndex)             # shared with flat path
    sb.Append("SELECT ")
    AppendOuterProjection(sb, dialect, plan.Projection.Columns)
    sb.Append(" FROM (")

    # ── Inner SELECT DISTINCT proj + extra orderby cols
    sb.Append("SELECT DISTINCT ")
    AppendInnerProjection(sb, dialect, plan.Projection.Columns, paramIndex)  # adds "AS __c{i}" aliases
    foreach (expr, alias) in extraOrderExprs:
        sb.Append(", ")
        sb.Append(SqlExprRenderer.Render(expr, dialect, paramIndex))
        sb.Append(" AS ")
        sb.Append(QuoteIdent(dialect, alias))

    # ── Inner FROM/JOINs/WHERE/GROUP BY/HAVING — same as RenderSelectSql
    AppendFromAndJoins(sb, plan, dialect, ref paramIndex)
    AppendWhere(sb, plan, mask, dialect, ref paramIndex)
    AppendGroupAndHaving(sb, plan, dialect, ref paramIndex)
    sb.Append(") AS ")
    sb.Append(QuoteIdent(dialect, "__d"))

    # ── Outer ORDER BY + pagination
    if outerOrderByAliases.Count > 0:
        sb.Append(" ORDER BY ")
        for i in range(activeOrder.Count):
            if i > 0: sb.Append(", ")
            sb.Append(QuoteIdent(dialect, "__d"))
            sb.Append('.')
            sb.Append(QuoteIdent(dialect, outerOrderByAliases[i]))
            sb.Append(activeOrder[i].IsDescending ? " DESC" : " ASC")
    AppendPagination(sb, plan, dialect, hasOrderBy: outerOrderByAliases.Count > 0, ref paramIndex)

    return new AssembledSqlVariant(sb.ToString(), max(paramIndex, paramBaseOffset + plan.Parameters.Count))
```

**Parameter indexing.** ORDER BY expressions in the simple case are bare column references with zero parameters. If a future user expression in ORDER BY carries parameters (e.g., `OrderBy(u => u.Total + capturedThreshold)`), those parameters are part of `plan.Parameters` and threaded through `paramIndex` exactly as in the flat path. The wrap rearranges where the ORDER BY expression is rendered (inside the inner SELECT, then aliased) but does not change the parameter-slot count or order.

**SQL Server pagination fallback.** The flat path adds `ORDER BY (SELECT NULL)` for SQL Server when pagination is requested without an ORDER BY. The wrap path only fires when ORDER BY exists, so this fallback is not needed inside the wrap.

## Phases

### Phase 1 — Implement detection + wrap rendering

Files:
- `src/Quarry.Generator/IR/SqlAssembler.cs` — main change.

Steps:
1. Add private helper `RenderProjectionColumnRef(ProjectedColumn col, SqlDialect dialect)` that returns the dialect-quoted column-ref string matching what `AppendSelectColumns` produces (e.g., `"t0"."UserName"`, or for aggregates the rendered SQL expression).
2. Add private helper `NeedsDistinctOrderByWrap(QueryPlan plan, int mask, SqlDialect dialect)` per the algorithm above. **Dialect-agnostic** — applies to all four dialects.
3. Add private method `RenderSelectSqlWithDistinctOrderByWrap(plan, mask, dialect, paramBaseOffset)` that renders the wrap shape. It mirrors the structure of `RenderSelectSql` but:
   - Adds outer SELECT + inner SELECT + derived alias.
   - Inner projection emits `<col_sql> AS "__c{i}"` for non-aggregate columns; aggregates keep their existing alias.
   - Adds extra `<expr> AS "__o{j}"` entries for ORDER BY expressions not in projection.
   - Outer ORDER BY references inner aliases.
   - Pagination applied to outer.
4. Wire dispatch in `RenderSelectSql`: at the top, after computing active ORDER BY for the mask, if `NeedsDistinctOrderByWrap` returns true, return `RenderSelectSqlWithDistinctOrderByWrap` instead of the rest of the function.
5. Update `Assemble` in `SqlAssembler` to fall back from the batch fast path to per-mask single rendering when the conservative check `MayNeedDistinctOrderByWrap(plan, dialect)` returns true ("IsDistinct AND OrderTerms.Count > 0 AND no SetOperations AND there exists at least one OrderTerm whose expression doesn't match any projection column"). Otherwise the batch path stays as-is.

Tests to add (in `src/Quarry.Tests/SqlOutput/CrossDialectCompositionTests.cs` or a new file `CrossDialectDistinctOrderByTests.cs`):
- `Distinct_OrderBy_NotInProjection_AllDialects_WrapsInSubquery` — all four dialects emit the wrap.
- `Distinct_OrderBy_InProjection_AllDialects_NoWrap` — when ORDER BY column IS in projection, all dialects emit flat SQL (regression guard for the common case).
- `Distinct_OrderBy_TupleProjection_AllDialects_WrapsInSubquery` — multi-column projection to a tuple/DTO.
- `Distinct_OrderBy_Descending_AllDialects_PreservesDirection` — descending ORDER BY in the wrap.
- `Distinct_OrderBy_MixedInAndOutOfProjection_AllDialects_AliasesEachCorrectly` — `OrderBy(u.UserName).ThenBy(o.Total)` where UserName is in projection but Total is not.

Update existing `Join_Distinct_OrderBy_Limit` test:
- Change all four `AssertDialects` SQL strings to the wrap form (with dialect-appropriate quoting and pagination keywords).
- Update `Assert.That(results, Has.Count.EqualTo(2))` to `Has.Count.EqualTo(3)` for SQLite execute mirror, with a one-line comment explaining the wrap semantic.

Commit: "Fix DISTINCT + ORDER BY on non-projected column emits subquery wrap across all dialects (#267)"

### Phase 2 — Re-enable PG execute mirror in `Join_Distinct_OrderBy_Limit`

Files:
- `src/Quarry.Tests/SqlOutput/CrossDialectCompositionTests.cs` — only the `Join_Distinct_OrderBy_Limit` test.

Steps:
1. Remove the comment block that explains the PG-skip.
2. Add `var pgResults = await pg.ExecuteFetchAllAsync();` followed by `Assert.That(pgResults, Has.Count.EqualTo(3));` plus a one-line comment.

Commit: "test: re-enable PG execute mirror for Join_Distinct_OrderBy_Limit (#267)"

### Phase 3 — Verify and stabilize

Steps:
1. Run full `Quarry.Tests` suite.
2. Run `Quarry.Analyzers.Tests` (any existing analyzer tests that depend on emitted SQL).
3. Confirm only the pre-existing baseline failure (`RunAsync_InsertsHistoryRow_OnPostgreSQL`) is still failing.
4. If new failures surface, fix at root cause. No commit unless changes are needed.

No commit unless fixes are required.

## Dependencies

Phase 1 must land before Phase 2 (Phase 2's PG execute would fail without the rewrite). Phase 3 follows Phase 2.

## Risk notes

- **Behavior change for SQLite/MySQL users.** The chain `OrderBy(non-projected).Distinct().Select(proj)` now returns one row per `(proj, orderby)` pair instead of one row per `proj` with an arbitrary `orderby` value. This is the standard-SQL semantic. The previous behavior was implementation-defined leniency. Document in release notes.
- **Other Distinct tests in the suite.** A scan across the test suite found Distinct usage in `CrossDialectComplexTests`, `CrossDialectSelectTests`, `CrossDialectSetOperationTests`, `EndToEndSqlTests`, `Integration/LoggingIntegrationTests` — none of them combine DISTINCT with an ORDER BY whose expression is outside the projection. They should remain unchanged.
- **Carrier/reader emission.** Reader code uses ordinals, not names. Aliases inside the wrap don't affect the reader. Verified by inspection of `BuildReaderDelegateCode` (returns null; emitter handles via ordinals).
