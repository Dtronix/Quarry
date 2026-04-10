# Review: #233

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan called for 4 tests only, but implementation also includes a generator fix in `ProjectionAnalyzer.ResolveJoinedAggregate` (adding `tableAlias` extraction and passing it to `ProjectedColumn`). | Info | This is a reasonable scope addition. Without the `tableAlias` fix, joined aggregate/window columns with unresolved types (e.g., LAG/LEAD returning "object") could not be enriched during `ChainAnalyzer.EnrichColumns`, because `TryResolveAggregateTypeFromSql` and the per-alias enrichment path both require a non-null `TableAlias` to look up the correct entity's column metadata. The tests would have failed without this fix, so including it is justified. |
| All 4 planned tests were implemented exactly as specified: `WindowFunction_Joined_Lag_VariableOffset`, `WindowFunction_Joined_Lead_VariableOffset`, `WindowFunction_Joined_Lag_VariableDefault`, `WindowFunction_Joined_Ntile_Variable`. | Pass | Full plan compliance on the test side. |
| Tests placed in `#region Joined Queries` after `WindowFunction_Joined_SumOver` and before `#endregion`, matching plan. | Pass | Correct placement. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The `tableAlias` extraction logic correctly handles the case where the first argument is not a member access (e.g., NTILE where `args[0]` is an integer variable `buckets`). The `is MemberAccessExpressionSyntax` pattern match fails gracefully, leaving `tableAlias = null`. This is safe because NTILE returns "int" which is never an unresolved type, so the enrichment path won't need the alias. | Pass | Edge case handled correctly. |
| `Count(*)` with zero arguments: `args.Count > 0` is false, so `tableAlias` remains `null`. `Count(*)` returns "int", not an unresolved type, so no enrichment is needed. | Pass | Edge case handled correctly. |
| The `AnalyzeJoinedInvocation` method (line 593-626) -- the *other* call site that creates `ProjectedColumn` from `ResolveJoinedAggregate`-like logic -- does NOT set `tableAlias`. This means standalone joined aggregates like `(u, o) => Sql.Min(o.Total)` that return "object" may not enrich correctly. | Low | Pre-existing issue, not introduced by this PR. The fix was only applied to the tuple/initializer path (`ResolveJoinedAggregate` at line 717), which is the path exercised by the new tests. The standalone invocation path is a separate concern. |
| Manifest output files updated correctly for all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server) with the expected SQL and parameter types. Counts incremented by 4 in each manifest. | Pass | Output artifacts are consistent. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 4 tests cover the key variable-parameterized overloads: LAG with variable offset (2-arg), LEAD with variable offset (2-arg), LAG with variable default (3-arg with literal offset + variable default), and NTILE with variable buckets. These map directly to the `BuildJoinedLagLeadSql` and `BuildNtileSql` code paths. | Pass | Good coverage of the parameterized joined window function paths. |
| Each test exercises all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server) with dialect-specific parameter placeholders (`@p0`, `$1`, `?`, `@p0`). | Pass | Dialect coverage matches existing test patterns. |
| Tests use SQL-only assertions (`AssertDialects`) without `ExecuteFetchAllAsync`, matching the pattern of the existing single-entity variable tests (lines 774-858) which also omit execution assertions. | Pass | Consistent with existing variable-parameterization tests. The parameterized values are runtime-only and the test harness validates SQL generation, not execution. |
| No negative/failure-mode tests, but these already exist in the broader test suite (issues #227, #223) for malformed OVER clause lambdas. | Info | Appropriate scope for this issue. |
| Missing coverage: `Lead` with 3 arguments (variable default) is not tested. Only `Lag` has the 3-arg variable-default variant. | Low | The `BuildJoinedLagLeadSql` method handles LAG and LEAD identically (same code path), so this is a minor gap. Adding a `WindowFunction_Joined_Lead_VariableDefault` test would provide completeness but is not strictly necessary. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New tests follow the exact same pattern as existing joined tests (lines 337-383): `await using var t`, destructure to `(Lite, Pg, My, Ss)`, build 4 dialect queries with `.Prepare()`, assert with `QueryTestHarness.AssertDialects`. | Pass | Matches established conventions precisely. |
| Test naming follows existing convention: `WindowFunction_Joined_{Function}_{Variant}`. | Pass | Consistent with `WindowFunction_Joined_RowNumber`, `WindowFunction_Joined_SumOver`, etc. |
| The generator code comment ("Extract the table alias from the first column argument...") is clear and explains the *why*. | Pass | Good documentation practice consistent with codebase style. |
| Variable naming in tests (`offset`, `defaultVal`, `buckets`) matches the single-entity counterparts. | Pass | Consistent naming. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The `tableAlias` change only adds information that was previously missing (it was implicitly `null`). All existing `ProjectedColumn` construction sites that don't pass `tableAlias` get the default `null`, so no existing behavior changes. | Pass | Backward compatible -- additive only. |
| The `tableAlias` extraction runs for all aggregate calls in `ResolveJoinedAggregate`, including non-window functions (e.g., `Sql.Sum(o.Total)` without OVER). For these, the alias will now be set where it was previously null. This *improves* enrichment for joined aggregate columns that return unresolved types, enabling the `perAliasLookup` path in `TryResolveAggregateTypeFromSql`. | Info | Net positive side effect. Previously, joined aggregate columns with "object" type (e.g., `Sql.Min(o.Total)` in a tuple) could not be enriched because `tableAlias` was null. Now they can be. All 3101+ existing tests pass, confirming no regressions. |
| The line 1371 guard (`col.TableAlias == null && !col.IsAggregateFunction`) ensures the implicit-join qualification pass does not overwrite the new `tableAlias` on aggregate columns. | Pass | No interference with implicit join logic. |

## Classifications

(leave empty -- classification happens on main context)

## Issues Created

