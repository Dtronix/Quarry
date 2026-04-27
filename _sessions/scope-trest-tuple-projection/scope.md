# Scope: TRest tuple projection handling

## Background

C# `ValueTuple` has overloads for arities 1–8. The 8-arity overload's last type parameter is named `TRest` and the language requires it to itself be a `ValueTuple`. So a 10-element tuple compiles to `ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8, T9, T10>>` and `tuple.Item8` is rewritten by the C# compiler to `tuple.Rest.Item1`. Source code keeps the flat `(a, b, …, j)` syntax both for declaration and access; the nesting is invisible above the IL layer.

This matters for Quarry because the generator emits source code that becomes part of the user's compilation. As long as we emit the flat tuple syntax the user wrote, the C# compiler folds it into the right `ValueTuple` shape on our behalf. The risk surface is in places where the generator inspects a *type* (rather than copying syntax) and assumes a flat shape — and in any path that re-emits a tuple from generator-side metadata rather than from the source.

## Why this matters now

We are scoping an analyzer code-fix that rewrites anonymous-type projections (`new { … }`) to named tuples (`(Name: …, …)`). Anonymous types have no arity ceiling (`AnonymousObjectCreationExpressionSyntax` accepts any number of members), and `ProjectionAnalyzer.AnalyzeJoinedExpression:578` rejects them outright with `ProjectionFailureReason.AnonymousTypeNotSupported`. The rewrite would land users directly in `TRest` territory whenever their anonymous projection has 8+ members, so before shipping it we want certainty about whether the projection layer survives that case as-is.

## Audit table — locations with arity-sensitive logic

| Location | Behavior | Risk hypothesis | Verdict |
|---|---|---|---|
| `Projection/ReaderCodeGenerator.cs:149` `GenerateTupleReader` | Emits a flat `(read0, read1, …)` tuple literal; relies on the C# compiler to fold to `ValueTuple<…, TRest>` | Low — same syntax C# users write | **OK** — confirmed by execution at arities 7/8/10/16 |
| `Utilities/TypeClassification.cs:303` `BuildTupleTypeName` | Emits flat type-name syntax `(int, string Name, …)` | Low — same syntax accepted by C# parser | **OK** — used by the 8/10/16 test paths |
| `Projection/ProjectionAnalyzer.cs:1598` `BuildTupleTypeNameFromSymbol` | Iterates `INamedTypeSymbol.TupleElements`, which Roslyn returns flat regardless of `TRest` | Low — Roslyn handles the unfold | **OK** — the 8/10/16 tests exercise this path through the result-type symbol |
| `Projection/ProjectionAnalyzer.cs:1650` `IsValidTupleTypeName` | Naive `inner.Split(',')` to count and validate elements | Latent — fails for any arity that contains nested generic args (e.g., `(Dictionary<int, string>, int, …)`); independent of `TRest` | **Audit candidate** — the 16-element tests pass because the projected types are simple, but the rewrite path can produce nested generics. Should switch to `TypeClassification.SplitTupleElements:276` (depth-aware), which already exists |
| `Parsing/ChainAnalyzer.cs:2258` and `:2292` | Rebuilds tuple result-type name from enriched columns via `BuildTupleTypeName(columns, fallbackToObject: false)` | Low — emits flat syntax | **OK** — covered by the 16-element 3-table-join test which forces the late-rebuild path |
| Set-op projection mismatch (QRY072) | Compares `ProjectionInfo.Columns.Count` between two arms | Low — flat element count regardless of `TRest` | **OK by construction** — no `TRest` awareness required; not exercised by these tests but the mechanism is arity-symmetric |
| CTE re-projection (`FromCte<T>`) | Round-trips the tuple type through Roslyn and re-binds | Medium — placeholder analysis runs before semantic-model resolves the wide tuple | **Untested by this workflow**; recommend a follow-up CTE test with an 8+ element CTE projection before shipping the rewrite |
| Carrier `TResult` generic argument (`CarrierBase<T, TResult>`) | C#-level generic substitution; opaque to Quarry | Low — CLR represents nested `ValueTuple` transparently | **OK** — execution succeeds at all four arities tested |
| Inline parameter binding for OrderBy/etc. that touches projection columns | None of these paths read inside the projected tuple | Low | **OK** — boundary not crossed |
| `QueryDiagnostics.ProjectionColumns` | Flat `IReadOnlyList<ProjectedColumn>` | Cosmetic | **OK** |

## Findings from the test suite

A new fixture `src/Quarry.Tests/SqlOutput/CrossDialectWideTupleTests.cs` exercises four arities with the `QueryTestHarness` cross-dialect pattern (SQLite in-memory + PostgreSQL, MySQL, SQL Server Testcontainers). All four tests pass:

- **`Tuple_7Elements_FlatLast`** — last arity before `TRest` nesting. Confirms the existing flat path still emits and materializes correctly. SQL is dialect-quoted as expected; element-name access (`results[0].UserName`) returns the right values.
- **`Tuple_8Elements_FirstNested`** — first `TRest` arity. The runtime tuple is `ValueTuple<…, ValueTuple<string>>` and `results[0].Status` reaches `Rest.Item1`. The named-element rewrite resolves through one level of `Rest` correctly.
- **`Tuple_10Elements_DeeperNested`** — three positions inside `Rest` (ordinals 7, 8, 9 → `Rest.Item1..Item3`). Includes an enum (`OrderPriority`) at `Rest.Item3` to confirm the enum-cast reader emission still applies through nesting. Also includes a `DateTime` at `Rest.Item4` (well, ordinal 9 → `Rest.Item3`).
- **`Tuple_16Elements_DeepDoubleNested`** — *two* nesting levels: `ValueTuple<U1..U7, ValueTuple<U8..U14, ValueTuple<U15, U16>>>`. Element access traverses `Rest.Rest.Item1..Item2` for the last two positions. Three-table join (Users × Orders × OrderItems) so the late-rebuild path in `ChainAnalyzer.cs` participates. Pass.

A first-pass run failed all four tests, but the failure was a pure assertion-string bug in the test author's expected SQL: the generator emits explicit `ORDER BY col ASC` and the expected strings omitted ` ASC`. After the fixup, all four pass with no further changes. **No `TRest`-related defects were observed at runtime.**

This is consistent with the audit table: the only places the generator constructs a tuple shape from non-syntactic sources (`BuildTupleTypeName`, `BuildTupleTypeNameFromSymbol`) emit flat syntax, and Roslyn's `TupleElements` returns the full element list flat. Everything else either copies user syntax or operates on `Columns.Count`, both of which are nesting-agnostic.

## Recommendations for the anonymous-type-to-named-tuple code-fix

**(a) Ship the rewrite without an arity guard.** The runtime evidence supports this: `TRest` works at every arity tested up through two nesting levels. The reader, type-name builder, and chain analyzer all behave correctly for wide tuples produced by a code-fix.

**(b) Fix `IsValidTupleTypeName:1650` before shipping.** Replace `inner.Split(',')` with `TypeClassification.SplitTupleElements(inner)` (already in the codebase, depth-aware). This is independent of `TRest` but more likely to be tripped by the rewrite path if a user's anonymous projection includes nested-generic types like `Dictionary<K,V>`. Cheap fix, no new code needed — just route through the existing depth-aware splitter. Should ship as a small standalone PR.

**(c) Add an 8+ element CTE test before shipping.** The audit table marks CTE re-projection as **Untested by this workflow**. The CTE pipeline has its own type-resolution path (placeholder analysis at `ProjectionAnalyzer.AnalyzeJoinedExpressionWithPlaceholders`, then late enrichment) that the regular projection tests don't cover. A test of `db.With<TWideTuple>(…).FromCte<TWideTuple>()` at arity ≥ 8 would close the last gap.

**(d) Consider arity-aware UX in the code-fix itself.** Independent of the runtime path: the lightbulb could note in its description that "8+ element projections compile to nested `ValueTuple<…>` and may serialize differently than the original anonymous type" so the user understands the implication of accepting it. This is a code-fix concern, not a generator concern.

## Out of scope for this workflow

- Implementing the anonymous-type-to-named-tuple code-fix itself.
- Fixing `IsValidTupleTypeName` (recommendation b — separate PR).
- Adding the wide-tuple CTE test (recommendation c — separate PR).
- Any change to the generator pipeline.
