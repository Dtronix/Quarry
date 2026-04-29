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
| CTE re-projection (`FromCte<T>`) | Round-trips the tuple type through Roslyn and re-binds | Medium — placeholder analysis runs before semantic-model resolves the wide tuple | **OK after Bug A fix** — confirmed by `Tuple_PostCteWideProjection` at arity 8 against all four dialects, exercising the late-rebuild type-name path in `ChainAnalyzer.cs:2258/2292`. Required `SqlAssembler.AppendProjectionColumnSql:1359` (Bug A) to be hardened against empty-string TableAlias |
| Post-CTE projection SQL alias prefix (`SqlAssembler.cs:1359`) | Treated empty-string `TableAlias` as a valid alias, emitting `""."col"` for every column in non-joined CTE post-Select chains | High — broke wide CTE projections at runtime | **Fixed in this PR** — `if (col.TableAlias != null)` → `if (!string.IsNullOrEmpty(col.TableAlias))`. Aligns with `ReaderCodeGenerator`'s convention. Surfaced by `Tuple_PostCteWideProjection` |
| FK `.Id` projection in non-joined CTE post-Select | Surfaced during the CTE wide-tuple test development. Multi-path issue: (a) `BuildColumnInfoFromTypeSymbol` may not detect `EntityRef<T,K>` as FK during discovery, (b) `TryParseNavigationChain` indiscriminately matches `o.UserId.Id` and produces `columnName="Id"`, (c) FK key-type extraction (`int` from `EntityRef<User,int>`) isn't done in any non-placeholder path | Medium — deeper than initial diagnosis; touches discovery + projection analysis + reader emission | **Deferred — see #280** — this PR's CTE wide-tuple test deliberately uses a non-FK 8th element (`Echo: o.OrderId`) to validate the late-rebuild path without entangling with the FK projection bug |
| Post-CTE chain-continuation methods (`OrderBy`/`Limit`/etc. on the result of `FromCte<T>`) | `IEntityAccessor<T>` does not expose `OrderBy`; `DiscoverPostCteSites` synthesizes a site anyway and emits a malformed interceptor signature `Order<Order>` because `currentBuilderTypeName` defaults to the entity name via the `TranslatedCallSite.BuilderTypeName` fallback chain | Medium — only triggers for invalid user-level chains (the C# compiler also rejects them in non-CTE contexts), but the synthesized interceptor crashes the build instead of producing a useful diagnostic | **Deferred — see #281** — the CTE test in this PR avoids OrderBy and instead sorts results in-memory after fetch |
| Carrier `TResult` generic argument (`CarrierBase<T, TResult>`) | C#-level generic substitution; opaque to Quarry | Low — CLR represents nested `ValueTuple` transparently | **OK** — execution succeeds at all four arities tested |
| Inline parameter binding for OrderBy/etc. that touches projection columns | None of these paths read inside the projected tuple | Low | **OK** — boundary not crossed |
| `QueryDiagnostics.ProjectionColumns` | Flat `IReadOnlyList<ProjectedColumn>` | Cosmetic | **OK** |

## Findings from the test suite

A new fixture `src/Quarry.Tests/SqlOutput/CrossDialectWideTupleTests.cs` exercises six tests with the `QueryTestHarness` cross-dialect pattern (SQLite in-memory + PostgreSQL, MySQL, SQL Server Testcontainers). All six tests pass:

- **`Tuple_7Elements_FlatLast`** — last arity before `TRest` nesting. Confirms the existing flat path still emits and materializes correctly. SQL is dialect-quoted as expected; element-name access (`results[0].UserName`) returns the right values.
- **`Tuple_8Elements_FirstNested`** — first `TRest` arity. The runtime tuple is `ValueTuple<…, ValueTuple<string>>` and `results[0].Status` reaches `Rest.Item1`. The named-element rewrite resolves through one level of `Rest` correctly.
- **`Tuple_10Elements_DeeperNested`** — three positions inside `Rest` (ordinals 7, 8, 9 → `Rest.Item1..Item3`). Includes an enum (`OrderPriority`) at `Rest.Item2` (ordinal 8) to confirm the enum-cast reader emission still applies through nesting, and a `DateTime` at `Rest.Item3` (ordinal 9).
- **`Tuple_16Elements_DeepDoubleNested`** — *two* nesting levels: `ValueTuple<U1..U7, ValueTuple<U8..U14, ValueTuple<U15, U16>>>`. Element access traverses `Rest.Rest.Item1..Item2` for the last two positions. Three-table join (Users × Orders × OrderItems) so the late-rebuild path in `ChainAnalyzer.cs` participates. Mid-`Rest` ordinals (4 and 5) are also asserted to close the coverage in the middle of the nested segment.
- **`Tuple_NullableInsideRest`** — 9 elements with the nullable `string? Notes` placed at ordinal 8 (`Rest.Item2`). Seed has both a non-null row (Order 1: `'Express'`) and a NULL row (Order 2). Confirms the `IsDBNull`-guarded reader path resolves the right ordinal through `Rest`, which the upcoming anon→tuple rewrite will routinely hit.
- **`Tuple_PostCteWideProjection`** — 8 elements projected from a `FromCte<Order>()`-rooted chain. Exercises the late-rebuild tuple type-name path in `ChainAnalyzer.cs` (lines 2258 / 2292) — the post-CTE Select runs through placeholder analysis without a SemanticModel at discovery time, then has its tuple type-name rebuilt from enriched columns. Element 8 (`Reorder`) lands at `Rest.Item1`; element 7 (`Notes`) is the last flat slot. Asserts both rows materialize correctly including the NULL `Notes` case. Sorts results client-side (no `OrderBy` in the chain) because `IEntityAccessor<T>` does not expose `OrderBy` (see Bug C in the audit table).

A first-pass run failed the original four tests, but the failure was a pure assertion-string bug in the test author's expected SQL: the generator emits explicit `ORDER BY col ASC` and the expected strings omitted ` ASC`. After the fixup, all six tests pass with no further changes. **No `TRest`-related defects were observed at runtime.**

Adding the CTE wide-tuple test surfaced three pre-existing generator bugs in the post-CTE pipeline that hadn't been covered by any prior test:

- **Bug A (fixed in this PR)** — `SqlAssembler.AppendProjectionColumnSql` treated an empty-string `TableAlias` as a valid alias and emitted `""."col"` for every column on non-joined CTE post-Select chains. The placeholder path in `ProjectionAnalyzer.AnalyzeSingleEntitySyntaxOnly` (line 213) builds the per-param lookup with `Alias: ""` to mean "no alias", and `ReaderCodeGenerator` already used `IsNullOrEmpty` to skip the prefix; `SqlAssembler` now matches that convention.
- **Bug B (deferred — #280)** — FK `.Id` projection (`o.UserId.Id`) in CTE post-Select context produces an empty column name and an unfilled cast type (`(?)r.GetValue(N)`) in the reader. The original diagnosis pointed at the placeholder path, but investigation showed at least three intertwined issues across `BuildColumnInfoFromTypeSymbol`, `TryParseNavigationChain`, and FK key-type extraction. The fix needs its own scope. The CTE test substitutes a non-FK 8th element (`Echo: o.OrderId`) so the late-rebuild path is still validated.
- **Bug C (deferred — #281)** — `OrderBy` (and presumably `Limit`/`Offset`/`ThenBy`) called directly on `IEntityAccessor<T>` (the return type of `FromCte<T>`) is invalid C# at the user level — `IEntityAccessor` does not expose those methods. `DiscoverPostCteSites` synthesizes a site anyway with `BuilderTypeName=null`, and `TranslatedCallSite.BuilderTypeName` falls back to the entity name, producing the malformed signature `public static Order<Order> OrderBy_X(this Order<Order> builder, …)`. The fix is either to emit a diagnostic and refuse to synthesize, or to surface the method on `IEntityAccessor<T>` via interceptor and route through the `IQueryBuilder` chain.

This is consistent with the audit table: the only places the generator constructs a tuple shape from non-syntactic sources (`BuildTupleTypeName`, `BuildTupleTypeNameFromSymbol`) emit flat syntax, and Roslyn's `TupleElements` returns the full element list flat. Everything else either copies user syntax or operates on `Columns.Count`, both of which are nesting-agnostic.

## Recommendations for the anonymous-type-to-named-tuple code-fix

**(a) Ship the rewrite without an arity guard.** The runtime evidence supports this: `TRest` works at every arity tested up through two nesting levels. The reader, type-name builder, and chain analyzer all behave correctly for wide tuples produced by a code-fix.

**(b) Fix `IsValidTupleTypeName:1650` before shipping.** Replace `inner.Split(',')` with `TypeClassification.SplitTupleElements(inner)` (already in the codebase, depth-aware). This is independent of `TRest` but more likely to be tripped by the rewrite path if a user's anonymous projection includes nested-generic types like `Dictionary<K,V>`. Cheap fix, no new code needed — just route through the existing depth-aware splitter. Should ship as a small standalone PR.

**(c) ~~Add an 8+ element CTE test before shipping.~~** Done. `Tuple_PostCteWideProjection` covers `db.With<Order>(…).FromCte<Order>().Select(8-tuple)` against all four dialects with full execution. The Medium-risk CTE row in the audit table is now marked **OK**.

**(d) Consider arity-aware UX in the code-fix itself.** Independent of the runtime path: the lightbulb could note in its description that "8+ element projections compile to nested `ValueTuple<…>` and may serialize differently than the original anonymous type" so the user understands the implication of accepting it. This is a code-fix concern, not a generator concern.

## Out of scope for this workflow

- Implementing the anonymous-type-to-named-tuple code-fix itself.
- Fixing `IsValidTupleTypeName` (recommendation b — separate PR).
- Adding the wide-tuple CTE test (recommendation c — separate PR).
- Any change to the generator pipeline.
