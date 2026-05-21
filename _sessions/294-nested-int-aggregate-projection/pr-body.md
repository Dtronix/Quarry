## Summary
- Closes #294
- Fixes the generator's projection-type resolver so nested int aggregates (e.g. `u.Orders.Sum(o => o.Items.Count())`) infer the correct CLR type instead of defaulting to `decimal`.
- Eliminates the CS9144 interceptor signature mismatch that previously blocked nested int aggregate projections from compiling.

## Reason for Change
`ChainAnalyzer.TryResolveSelectorClrType` only handled `ColumnRefExpr` selectors. When an outer aggregate's selector was itself a nested `SubqueryExpr` (e.g. the inner `Count` in `Sum(o => o.Items.Count())`), the helper returned `null` and the outer aggregate fell back to its default — `decimal` for `Sum`. The user's lambda, bound by the C# compiler against `Many<T>.Sum(Func<T, int>) → int`, returned `int`. The interceptor signature emitted by the generator therefore disagreed with the user-supplied lambda type, and C# emitted CS9144 at compile time. Decimal-only nested chains coincidentally worked because both the buggy fallback and the user's actual type were `decimal`.

## Impact
- Nested int aggregate projections (`Sum(o => o.Items.Count())`, `Sum/Sum/Count`, `Sum(o => o.Items.Sum(i => i.Quantity))`, etc.) now compile and bind correctly at any nesting depth.
- No change to generated SQL — only the inferred CLR type used for interceptor signature emission and reader-method selection.
- Decimal nested chains continue to work unchanged.

## Plan items implemented as specified
- **Phase 1** — Extended `TryResolveSelectorClrType` to recurse into nested `SubqueryExpr` selectors via `ResolveSubqueryResultType`, threading `EntityRegistry` through. Single edit surface in `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`.
- **Phase 2** — Added three new cross-dialect tests in `CrossDialectNestedSubqueryTests.cs` covering 2-level Sum/Count (the issue's primary repro), 2-level Sum/Sum over `Col<int>`, and sibling int+decimal projections.
- **Phase 3** — Flipped the existing `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` (decimal-only workaround) to the originally-planned 3-level `Sum/Sum/Count("urgent")` shape, renamed to `Select_ProjectionMixedNestingDepths_OrderTotalAndUrgentTagCount`, and dropped the `#294` workaround paragraph from its doc comment.
- **Phase 4** — Full-suite verification: 3488 tests passing (baseline 3485 + 3 new tests).

## Deviations from plan implemented
- The flipped test was renamed (`_OrderTotalAndItemTotal` → `_OrderTotalAndUrgentTagCount`) instead of kept under its prior name. The plan allowed this; the new name better describes the test intent.

## Gaps in original plan implemented
- The plan's projected SQL for the flipped test matched the actual generator output exactly; no recomputation was needed beyond the planned restructure.
- Discovered during test execution: SQL Server rejects `SUM((SELECT COUNT(*)...))` and `SUM((SELECT SUM(...)...))` with "Cannot perform an aggregate function on an expression containing an aggregate or a subquery." All projection-side nested-aggregate tests therefore execute only against SQLite (matching the pre-existing convention in this fixture); SQL output is still asserted for all four dialects via `AssertDialects`. Each new test's comment documents the restriction.

## Migration Steps
None. The fix changes only an internal type-inference path; no public API surface.

## Performance Considerations
None. The recursion descends a finite parser-produced AST; the call site (`ResolveSubqueryResultType`) is on the per-projection-column path that already runs once per compile-time analyzed query. No measurable cost.

## Security Considerations
None. The fix changes CLR type inference for interceptor signatures; SQL string assembly is untouched, so there is no new SQL-injection or boundary-input vector.

## Breaking Changes
- Consumer-facing: None. Any user code that previously hit the buggy decimal-default path could not compile (CS9144). No consumer can have depended on the old behavior in production.
- Internal: `TryResolveSelectorClrType` signature gained a third `EntityRegistry? registry` parameter. It is `private static` with a single caller, both updated.

## Review
- 6-section structured review on `_sessions/294-nested-int-aggregate-projection/review.md`. 3 findings, all classified D (not valid — nit-level robustness observations: no explicit recursion-depth guard, HasManyThrough not directly exercised by new tests, sibling result values happen to be equal). No A/B/C items.
