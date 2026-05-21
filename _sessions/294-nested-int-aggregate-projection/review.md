# Review: 294-nested-int-aggregate-projection

## Classifications
| # | Class | Rec | Sev | Section | Finding | Action Taken |
|---|-------|-----|-----|---------|---------|--------------|
| 1 | D | D | nit | Correctness | No explicit recursion-depth guard in `TryResolveSelectorClrType` | Not valid — finite parser AST; Roslyn caps real-world nesting. |
| 2 | D | D | nit | Correctness | HasManyThrough chains not directly exercised by new tests (helper already covered elsewhere) | Not valid — `ResolveSubqueryTargetEntity` already walks `ThroughNavigations`; no through-specific behavior in this path. |
| 3 | D | D | nit | Test Quality | `Quantity` test Alice=Bob=3 weakens row-shuffle detection (mitigated by OrderBy + UserName assertion) | Not valid — `OrderBy(UserId)` + per-row `UserName` assertion already pin row order. |

## Plan Compliance

No concerns.

The implementation matches the plan and Decisions verbatim. Phase 1 edits `TryResolveSelectorClrType` exactly as specified (new `EntityRegistry? registry` parameter, `SubqueryExpr` branch that recurses through `ResolveSubqueryResultType` with the parent's `targetEntity`). The single caller at line 1969 is updated. Phase 2 adds the three named tests (`Select_ProjectionNestedSumCount_TwoLevel_ItemCountPerUser`, `Select_ProjectionNestedSumSum_TwoLevel_QuantityPerUser`, `Select_ProjectionMixedSumIntCountDecimal_SiblingColumns`) in the planned file with cross-dialect SQL assertions. Phase 3 flips the workaround test to the 3-level `Sum/Sum/Count("urgent")` shape and drops the #294 paragraph. The rename to `_OrderTotalAndUrgentTagCount` is the only deviation from the plan's "recommend keeping the existing name" — but the plan explicitly listed renaming as acceptable and the new name better describes the test intent.

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Recursive `TryResolveSelectorClrType` has no explicit depth guard, but each recursion strictly descends one `SubqueryExpr` level in a finite parser-produced AST that cannot contain cycles. A stack overflow would require user code with absurd nesting depth (hundreds of levels), which the C# compiler/Roslyn would itself reject far earlier. | nit | Theoretical robustness only; not actionable. |
| The recursion correctly handles `HasManyThrough` because `ResolveSubqueryTargetEntity` is called inside `ResolveSubqueryResultType` with the parent's `targetEntity` as the new `outerEntity`, and that helper already walks `ThroughNavigations` before `Navigations`. Verified by tracing `Order → Items` (HasMany) → `OrderItem → Tags` (HasMany) in the new tests; no through-navigation aggregate is exercised in the new tests, but the existing `ResolveSubqueryTargetEntity` was already covered. | nit | Coverage gap is small; no behavioral defect. |
| `SubqueryKind.Exists`/`All` cannot appear as a `Selector` because the parser only emits those kinds for `Any`/`All` top-level boolean subqueries (selector is always null). The recursive path only matches `Sum/Min/Max/Avg/Count` selectors, all of which resolve to a sensible CLR type. No unhandled kind. | n/a | — |

## Security

No concerns.

The fix only changes type inference for an internal interceptor signature emitted at compile time; it does not influence SQL string assembly. SQL shape is identical before and after (confirmed by the manifest diff: the renamed test's new 3-level SQL was previously the 2-level decimal shape; the new tests' SQL is parameterized by the literal `'urgent'` string already escaped via the existing rendering pipeline). No new dependencies, no new boundary inputs.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Select_ProjectionNestedSumSum_TwoLevel_QuantityPerUser` expected results for Alice (3) and Bob (3) are identical numerically, which weakens the assertion's ability to catch row-shuffling bugs. Per seed data (Alice: qty 2+1=3, Bob: qty 3), the values are genuinely the same — but the `OrderBy` plus the `UserName` assertion already pins the row order, so this is a minor readability nit, not a correctness gap. | nit | Doesn't reduce coverage. |
| The three new tests execute end-to-end only against SQLite (matching the renamed test and the existing nested-aggregate-in-aggregate convention in this fixture). The justification — SQL Server's "Cannot perform an aggregate function on an expression containing an aggregate or a subquery" — is documented in each test's inline comment. SQL generation is still asserted across all four dialects via `AssertDialects`. Consistent with prior conventions. | n/a | — |
| The renamed `Select_ProjectionMixedNestingDepths_OrderTotalAndUrgentTagCount` test directly exercises the fixed code path: a `SubqueryExpr` selector inside another `SubqueryExpr` selector (3 levels deep — `Sum` → `Sum` → `Count` with predicate), which forces the recursion to fire twice for `UrgentTagCount`. Before the fix, this test would have failed with CS9144 at compile time, so the test directly gates the fix. | n/a | — |
| No diagnostic-emitting negative test exists (e.g., asserting that the generator no longer emits an interceptor-signature warning for the repro). The fix removes a CS9144 from `dotnet build`, not from a Quarry-emitted diagnostic, so a "no longer fails" build-time test is naturally covered by the new tests compiling at all. | n/a | — |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The updated XML doc on `TryResolveSelectorClrType` matches the surrounding doc-comment style (3-clause description, explicit null-return semantics, `<see cref>` references, issue number tag). Naming (`nestedSubquery`) follows local conventions. The `registry != null` null-guard mirrors the early-out pattern already used in `ResolveSubqueryTargetEntity`. | n/a | — |
| New tests follow the four-block `Lite/Pg/My/Ss` `.Prepare()` pattern, the `AssertDialects(...)` SQL-string assertion, and the comment-driven expected-result narrative used throughout `CrossDialectNestedSubqueryTests.cs`. Identifier quoting in expected SQL is correct per dialect (double-quote / backtick / bracket). | n/a | — |
| Renamed test name `Select_ProjectionMixedNestingDepths_OrderTotalAndUrgentTagCount` is descriptive and consistent with the fixture's `Select_Projection*` naming convention. | n/a | — |

## Integration / Breaking Changes

No concerns.

Pre-fix, any user code that hit the buggy decimal-default path could not compile (CS9144). Therefore no consumer can depend on the old behavior — there is no realistic breaking-change vector. Internally, `TryResolveSelectorClrType` is `private static` and has only one caller, both of which are updated. The signature change does not propagate to public API. No migration steps required.

## Issues Created
- (none yet)
