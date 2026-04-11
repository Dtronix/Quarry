# Plan: Incremental SQL Mask Rendering

## Overview

Currently, `SqlAssembler` calls `RenderSelectSql` (or `RenderDeleteSql`) once per mask value. For chains with N conditional terms, this produces up to 2^N calls, each re-rendering the entire SQL string from scratch — including mask-invariant sections like SELECT columns, FROM, JOINs, GROUP BY, HAVING, and pagination.

The optimization pre-renders each section once and assembles per-mask variants via string concatenation of pre-rendered parts. Only the mask-dependent sections (WHERE term selection, ORDER BY term selection, post-union WHERE term selection) vary, and even those are pre-rendered individually — the per-mask work is just selecting which pre-rendered strings to include.

## Key Concepts

**Mask-invariant paramIndex flow**: The main WHERE clause already iterates ALL terms (active + inactive) when computing parameter offsets, so `paramIndex` after WHERE is identical across all masks. This optimization extends the same pattern to ORDER BY and post-union WHERE, making the entire `paramIndex` flow mask-invariant. This means all shared sections receive the same `paramIndex` and produce identical output regardless of mask.

**Pre-rendered conditional terms**: Each WHERE term's SQL string is rendered once using its pre-computed parameter offset (which is determined by its position among ALL terms, not just active ones). Similarly for ORDER BY and post-union WHERE terms. Per mask, we select which pre-rendered strings to include and join them with the appropriate separator (AND for WHERE, comma for ORDER BY).

**Segment-based assembly**: The SQL is broken into segments:
- `prefix`: CTE + SELECT + FROM + JOINs (shared)
- `WHERE`: assembled per mask from pre-rendered terms
- `middle`: GROUP BY + HAVING (shared)
- `setOps`: set operation body + wrapping (shared)
- `postUnionWhere`: assembled per mask from pre-rendered terms (when set operations exist)
- `postUnionMiddle`: post-union GROUP BY + HAVING (shared)
- `orderBy`: assembled per mask from pre-rendered terms
- `pagination`: LIMIT/OFFSET (shared, with SQL Server ORDER BY (SELECT NULL) handled per mask)

## Phase 1: Fix ORDER BY and post-union WHERE paramIndex pre-computation

**File**: `src/Quarry.Generator/IR/SqlAssembler.cs`

**Goal**: Make the paramIndex flow mask-invariant by applying the all-terms pre-computation pattern (already used for main WHERE) to ORDER BY and post-union WHERE. This is both a correctness fix (for the theoretical case of parameterized conditional ORDER BY) and a prerequisite for the batch optimization.

**Changes**:

1. **ORDER BY** (lines 401-415): Replace the current active-only iteration with an all-terms iteration that pre-computes parameter offsets, matching the WHERE pattern.

Current:
```csharp
var activeOrders = GetActiveTerms(plan.OrderTerms, mask);
if (activeOrders.Count > 0)
{
    sb.Append(" ORDER BY ");
    for (int i = 0; i < activeOrders.Count; i++)
    {
        if (i > 0) sb.Append(", ");
        var o = activeOrders[i];
        var paramsBefore = CountParameters(o.Expression);
        sb.Append(SqlExprRenderer.Render(o.Expression, dialect, paramIndex));
        paramIndex += paramsBefore;
        sb.Append(o.IsDescending ? " DESC" : " ASC");
    }
}
```

New:
```csharp
var activeOrderSet = new HashSet<OrderTerm>(GetActiveTerms(plan.OrderTerms, mask));
var activeOrderRendered = new List<string>();
var orderParamOffset = paramIndex;
foreach (var o in plan.OrderTerms)
{
    var termParamCount = CountParameters(o.Expression);
    if (activeOrderSet.Contains(o))
    {
        var rendered = SqlExprRenderer.Render(o.Expression, dialect, orderParamOffset);
        activeOrderRendered.Add(rendered + (o.IsDescending ? " DESC" : " ASC"));
    }
    orderParamOffset += termParamCount;
}
if (activeOrderRendered.Count > 0)
{
    sb.Append(" ORDER BY ");
    sb.Append(string.Join(", ", activeOrderRendered));
}
paramIndex = orderParamOffset;
```

2. **Post-union WHERE** (lines 357-371): Apply the same all-terms pre-computation pattern.

Current:
```csharp
var postWhereActive = GetActiveTerms(plan.PostUnionWhereTerms, mask);
if (postWhereActive.Count > 0)
{
    sb.Append(" WHERE ");
    for (int i = 0; i < postWhereActive.Count; i++)
    {
        if (i > 0) sb.Append(" AND ");
        var w = postWhereActive[i];
        var termParamCount = CountParameters(w.Condition);
        if (postWhereActive.Count > 1) sb.Append('(');
        sb.Append(RenderWhereCondition(w.Condition, dialect, paramIndex));
        if (postWhereActive.Count > 1) sb.Append(')');
        paramIndex += termParamCount;
    }
}
```

New — same pattern as main WHERE: iterate all post-union WHERE terms, accumulate paramIndex from all, render only active non-trivial ones with pre-computed offsets.

**Tests**: Run full suite (3190 tests). Since ORDER BY expressions almost never have parameters in practice, this should be a no-op change for all existing tests. Any failure would indicate a real correctness issue.

## Phase 2: Batch SELECT rendering

**File**: `src/Quarry.Generator/IR/SqlAssembler.cs`

**Goal**: Add `RenderSelectSqlBatch` that renders all mask variants at once by pre-rendering shared segments and assembling per mask.

**New method signature**:
```csharp
private static void RenderSelectSqlBatch(
    QueryPlan plan, SqlDialect dialect,
    IReadOnlyList<int> masks,
    Dictionary<int, AssembledSqlVariant> results)
```

**Algorithm**:

1. **Render shared prefix** into a StringBuilder, tracking `paramIndex`:
   - CTE definitions (WITH clause)
   - SELECT columns
   - FROM table
   - Explicit JOINs
   - Implicit JOINs
   Result: `prefixStr`, `paramIndexAfterPrefix`

2. **Pre-render WHERE terms** — iterate ALL WhereTerms:
   - For each term, compute its `termParamOffset` from running counter
   - Render: `RenderWhereCondition(w.Condition, dialect, termParamOffset)` → string
   - Store: `(term, renderedString, isActive: term.BitIndex == null)`
   - Advance counter by `CountParameters(w.Condition)` for ALL terms
   Result: list of `(WhereTerm, string rendered)`, `paramIndexAfterWhere`

3. **Render shared middle** (GROUP BY + HAVING) using `paramIndexAfterWhere`:
   Result: `middleStr`, `paramIndexAfterMiddle`

4. **Handle set operations** (if any):
   - Render set operation body (UNION/INTERSECT/EXCEPT + operand SQL)
   - If post-union clauses exist, prepare wrapping: the prefix + per-mask WHERE + middle + set-op body becomes the inner query wrapped in `SELECT * FROM (...) AS "__set"`
   - Pre-render post-union WHERE terms (same all-terms pattern)
   - Render post-union GROUP BY + HAVING
   Result: `setOpsStr`, post-union term strings, `postUnionMiddleStr`, `paramIndexAfterPostUnion`

5. **Pre-render ORDER BY terms** — iterate ALL OrderTerms:
   - For each term, compute `termParamOffset`, render expression + direction → string
   - Advance counter for ALL terms
   Result: list of `(OrderTerm, string rendered)`, `paramIndexAfterOrder`

6. **Pre-render pagination** using `paramIndexAfterOrder`:
   - Generate the pagination string
   - For SQL Server, track whether `ORDER BY (SELECT NULL)` is needed (per mask, when no active ORDER BY)
   Result: `paginationStr`, `finalParamIndex`

7. **Assemble per mask**:
   ```
   For each mask:
     whereClause = assemble_where_clause(activeTerms)
     orderByClause = assemble_orderby_clause(activeTerms)
     
     if set_operations with wrapping:
       innerSql = prefixStr + whereClause + middleStr + setOpsStr
       sql = "SELECT * FROM (" + innerSql + ") AS \"__set\"" + postUnionWhere + postUnionMiddle + orderByClause + pagination
     else:
       sql = prefixStr + whereClause + middleStr + orderByClause + pagination
     
     results[mask] = new AssembledSqlVariant(sql, finalParamIndex)
   ```

   WHERE assembly helper:
   ```
   Filter active terms (BitIndex == null or bit set in mask)
   Filter out trivial-true conditions
   If 0 active: return ""
   If 1 active: return " WHERE " + rendered
   If N active: return " WHERE (" + terms.Join(") AND (") + ")"
   ```

   ORDER BY assembly helper:
   ```
   Filter active terms
   If 0 active: return "" (+ SQL Server ORDER BY (SELECT NULL) if pagination present)
   If N active: return " ORDER BY " + terms.Join(", ")
   ```

**Integration in `Assemble`**: Before the existing per-mask loop, add:
```csharp
if (plan.Kind == QueryKind.Select && plan.PossibleMasks.Count > 1)
{
    RenderSelectSqlBatch(plan, dialect, plan.PossibleMasks, sqlVariants);
    maxParamCount = sqlVariants.Values.Max(v => v.ParameterCount);
}
else
{
    // existing loop
}
```

**Tests**: Run full suite. The batch rendering must produce identical SQL to the per-mask rendering. Any test failure indicates a bug in the batch implementation.

## Phase 3: Batch DELETE rendering

**File**: `src/Quarry.Generator/IR/SqlAssembler.cs`

**Goal**: Add `RenderDeleteSqlBatch` — simpler than SELECT because DELETE only has prefix + WHERE (no middle, no ORDER BY, no pagination).

**New method**:
```csharp
private static void RenderDeleteSqlBatch(
    QueryPlan plan, SqlDialect dialect,
    IReadOnlyList<int> masks,
    Dictionary<int, AssembledSqlVariant> results)
```

**Algorithm**:
1. Render prefix: `DELETE FROM table` → string
2. Pre-render WHERE terms (same all-terms pattern)
3. Per mask: prefix + assembled WHERE clause

**Integration in `Assemble`**: Extend the conditional:
```csharp
if (plan.PossibleMasks.Count > 1)
{
    if (plan.Kind == QueryKind.Select)
        RenderSelectSqlBatch(...);
    else if (plan.Kind == QueryKind.Delete)
        RenderDeleteSqlBatch(...);
    else
        // existing loop
}
```

**Tests**: Run full suite.

## Phase 4: Unit tests

**File**: `src/Quarry.Tests/Generation/SqlAssemblerTests.cs` (new file)

**Goal**: Add targeted unit tests that verify the incremental rendering produces identical output to the per-mask rendering, including edge cases.

**Test cases**:
1. **No conditional terms**: Single mask (mask=0). Verify batch path produces same result as single-call path.
2. **Conditional WHERE only**: 2 conditional WHERE terms (4 masks). Verify all 4 SQL variants match the per-mask rendering.
3. **Conditional ORDER BY**: 1 conditional ORDER BY term (2 masks). Verify ORDER BY appears/disappears correctly per mask.
4. **Mixed conditional WHERE + ORDER BY**: Both conditional WHERE and ORDER BY terms. Verify correct assembly of both.
5. **DELETE with conditional WHERE**: Verify batch DELETE rendering matches per-mask.
6. **Parameterized ORDER BY (correctness fix)**: ORDER BY expression with a parameter and a conditional term. Verify parameter indices align with carrier GlobalIndex when a preceding conditional ORDER BY term is inactive.

These tests construct `QueryPlan` objects directly and call the assembler, then compare output against the per-mask method. This validates the optimization without requiring full chain analysis.

**Tests**: Run full suite to ensure new tests pass and no regressions.
