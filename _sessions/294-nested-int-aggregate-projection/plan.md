# Implementation Plan: nested int-aggregate projection type resolution

## Context

When the user writes a nested navigation aggregate as a projection element, e.g.:

```csharp
.Select(u => (u.UserName, ItemCount: u.Orders.Sum(o => o.Items.Count())))
```

the parser produces an outer `SubqueryExpr` (`SubqueryKind.Sum`, navigation `Orders`) whose `Selector` is itself an inner `SubqueryExpr` (`SubqueryKind.Count`, navigation `Items`).

The generator's projection-side type resolution lives in `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`:

- `ResolveProjectionSubqueryColumn` calls `ResolveSubqueryResultType` to determine the projected element's CLR type.
- `ResolveSubqueryResultType` calls `TryResolveSelectorClrType(selector, targetEntity)` to resolve the selector type.
- `TryResolveSelectorClrType` only matches `selector is ColumnRefExpr`. For nested `SubqueryExpr` selectors it returns `null`.
- With `selectorType == null`, the outer aggregate falls back to its default — e.g. `SubqueryKind.Sum => selectorType ?? "decimal"`. The generated interceptor signature then expects `decimal`, but the user's lambda — bound by the C# compiler against `Many<T>.Sum(Func<T, int>) → int` — returns `int`. The C# interceptor signature check rejects the mismatch with CS9144.

The fix threads `EntityRegistry` into `TryResolveSelectorClrType` and recurses into nested `SubqueryExpr` selectors using `ResolveSubqueryResultType`. The parent's `targetEntity` becomes the nested call's outer entity (e.g. the outer Sum's target `Order` is the inner Count's outer entity, and its target — `OrderItem` — flows from the existing `ResolveSubqueryTargetEntity` walk over `outerEntity.Navigations` / `outerEntity.ThroughNavigations`).

This is correct at arbitrary nesting depth: every level of the chain resolves its own target and propagates the inner result type up. `Count` always returns `"int"`; `Sum` returns its inner selector's type (`int`, `long`, `decimal`, `double`) or the default `"decimal"` when no further resolution is possible; `Avg` follows its own rules.

## Why this is safe

The SQL renderer already handles nested `SubqueryExpr` correctly — the only thing that was wrong was the inferred CLR type used for interceptor signature emission and reader-method selection (`TypeClassification.GetReaderMethod`). Fixing the type resolution doesn't change generated SQL shape; it only aligns the interceptor's tuple-element type with what the C# compiler already binds for `Many<T>`'s overload set.

SQL Server int-cast wrapping (`RequiresSqlServerIntCast`) only applies to window functions, not navigation aggregates, so no related work is needed.

## Phases

### Phase 1 — fix `TryResolveSelectorClrType` and thread the registry

Edits in `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`:

1. Change `TryResolveSelectorClrType` signature to accept `EntityRegistry? registry`:

   ```csharp
   private static string? TryResolveSelectorClrType(
       SqlExpr? selector,
       EntityInfo? targetEntity,
       EntityRegistry? registry)
   ```

2. Inside, after the existing `ColumnRefExpr` branch, add:

   ```csharp
   if (selector is SubqueryExpr nestedSubquery && registry != null)
   {
       // Recurse: targetEntity is the outer entity for the nested aggregate.
       // E.g., outer Sum target = Order → inner Count's outer = Order, target = OrderItem.
       return ResolveSubqueryResultType(nestedSubquery, targetEntity, registry, out _);
   }
   ```

3. Update the single caller inside `ResolveSubqueryResultType` (line 1969) to pass `registry`:

   ```csharp
   var selectorType = TryResolveSelectorClrType(subquery.Selector, targetEntity, registry);
   ```

No other callers exist (`TryResolveSelectorClrType` is `private static`), and `ResolveSubqueryResultType` already has `registry` in scope.

This phase is self-contained and immediately fixes the CS9144 case at every depth. After this commit, the repro from issue #294 — both 2-level (`Orders.Sum(o => o.Items.Count())`) and 3-level (`Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(...))))`) — compiles and binds to interceptors with `int` element types.

**Tests in this phase:** none added yet — the existing `Select_TwoSiblingProjectionSubqueries_AliasReusesPerColumn` and the un-flipped `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` continue to pass (decimal paths unchanged). Phase 2 introduces new tests; Phase 3 flips the existing one.

### Phase 2 — add cross-dialect tests for nested int aggregates

Add to `src/Quarry.Tests/SqlOutput/CrossDialectNestedSubqueryTests.cs`:

**Test 1:** `Select_ProjectionNestedSumCount_TwoLevel_ItemCountPerUser`
- Shape: `u.Orders.Sum(o => o.Items.Count())`
- Verifies the 2-level Sum-of-Count int case from the issue's primary repro.
- Expected SQL (sqlite): `SELECT "UserName", (SELECT SUM((SELECT COUNT(*) FROM "order_items" AS "sq1" WHERE "sq1"."OrderId" = "sq0"."OrderId")) FROM "orders" AS "sq0" WHERE "sq0"."UserId" = "users"."UserId") AS "ItemCount" FROM "users" WHERE "IsActive" = 1 ORDER BY "UserId" ASC`
- Expected results (seed): Alice → 2 (orders 1,2 each have 1 item), Bob → 1 (order 3 has 1 item).
- Style: full cross-dialect (SQLite/Pg/MySQL/SqlServer) `AssertDialects`, matching the surrounding tests.

**Test 2:** `Select_ProjectionNestedSumSum_TwoLevel_QuantityPerUser`
- Shape: `u.Orders.Sum(o => o.Items.Sum(i => i.Quantity))`
- Verifies Sum-of-Sum where the leaf column is `Col<int>` (Quantity), guaranteeing the inner ColumnRef-int path also propagates through the outer Sum as int (not decimal).
- Expected results (seed): Alice → 2+1=3, Bob → 3.

**Test 3:** `Select_ProjectionMixedSumIntCountDecimal_SiblingColumns`
- Shape: two sibling projection subqueries, one int (`ItemCount: u.Orders.Sum(o => o.Items.Count())`), one decimal (`OrderTotal: u.Orders.Sum(o => o.Total)`). Verifies sibling projections with mixed CLR types both resolve correctly.

All three tests follow the existing `AssertDialects` cross-dialect pattern and execute against all four containers when available.

### Phase 3 — flip existing test to use originally-planned 3-level Sum/Sum/Count

Update `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` in the same file:

- Change `ItemTotal: u.Orders.Sum(o => o.Items.Sum(i => i.LineTotal))` (decimal workaround) to the originally-planned `UrgentTagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))` (3-level int).
- Rename the test to reflect the new shape, or keep the name and just retarget — recommend keeping the existing name for git-blame continuity and adjust the doc comment.
- Drop the `// tracked separately in #294` paragraph from the doc comment.
- Recompute expected SQL for each dialect (3 layers of subqueries `sq0/sq1/sq2`).
- Recompute expected result values from seed:
  - Alice: order 1 → item 1 → 1 urgent tag; order 2 → item 2 → 1 urgent tag. Outer sum = 2.
  - Bob: order 3 → item 3 → 1 urgent tag. Outer sum = 1.

This collapses the workaround test back to the intent the test plan originally described.

### Phase 4 — final verification

Run the full suite (Quarry.Tests, Quarry.Analyzers.Tests, Quarry.Migration.Tests) — must match the baseline (3485 passing).

## Dependencies

- Phase 1 is a prerequisite for Phases 2 and 3 (without the fix, the new and flipped tests fail at compile time with CS9144).
- Phases 2 and 3 are independent of each other after Phase 1.
- Phase 4 closes the work.

## Test summary

| Phase | New / changed tests |
|-------|--------------------|
| 1 | None (no behavioral change for already-passing decimal paths) |
| 2 | +3 tests in `CrossDialectNestedSubqueryTests.cs` |
| 3 | Update `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` to 3-level int Sum/Sum/Count |
| 4 | Full-suite run; no new tests |
