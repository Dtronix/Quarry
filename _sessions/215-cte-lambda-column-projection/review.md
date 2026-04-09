# Review: 215-cte-lambda-column-projection

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Implementation matches plan exactly: projection reduction added in ChainAnalyzer after lambda inner chain analysis; HashSet filter on CteColumn.ColumnName; new QueryPlan with reduced projection; isIdentity set to false. | Info | Full plan compliance, no deviations. |
| Both test files updated per Phase 2: expected SQL changed from 7 entity columns to 3 DTO columns; "known gap" comments removed; all 4 dialect variants verified. | Info | Full plan compliance. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Column name domain mismatch in filter: `CteColumn.ColumnName` uses the DTO's raw C# property name (from `CteDtoResolver.ResolveColumns` which sets `columnName: property.Name`), while `ProjectedColumn.ColumnName` in the identity projection uses the entity's schema-resolved column name (from `EnrichIdentityProjectionWithEntityColumns` via `ec.ColumnName`). When a non-`Exact` naming convention is active (e.g., `SnakeCase`), entity columns would be `order_id` while DTO CteColumns remain `OrderId`. The HashSet lookup would match zero columns, producing an empty projection that renders as `SELECT *`. | Medium | Breaks column reduction for any project using `SnakeCase`, `CamelCase`, or `LowerCase` naming conventions. The fix works correctly only when entity column names happen to equal DTO property names (the `Exact` / default convention). Consider matching on `PropertyName` instead of `ColumnName`, or applying the same naming convention transform to DTO column names before comparison. |
| Missing `navigationHops` and `isJoinNullable` in ProjectedColumn copy. The new `ProjectedColumn(...)` constructor call omits the last two optional parameters (`navigationHops` defaults to `null`, `isJoinNullable` defaults to `false`). | Low | Safe for identity projections, which never carry navigation hops or join-nullable flags (verified: `EnrichIdentityProjectionWithEntityColumns` also omits them). However, the copy is positional-argument-dependent -- if the `ProjectedColumn` constructor gains new required parameters in the future, this call site will break silently. Consider using named parameters for all arguments to match the style in `EnrichIdentityProjectionWithEntityColumns`. |
| `InnerSql` field becomes stale after projection reduction: `lambdaInnerSql` is rendered from the original (unreduced) plan, but `SqlAssembler` always prefers `cte.InnerPlan` when non-null. | Low | Not a runtime bug since the fallback path is only used when `InnerPlan` is null. But the stale `InnerSql` could mislead diagnostics or debugging tools that inspect the CteDef's `InnerSql` field. |
| `With<TEntity>` (single type param, DTO == entity) also triggers the reduction path. When TDto is the entity itself, `CteColumns` lists all entity public properties, and the filter retains all columns (since names match under `Exact` naming). The only observable change is `isIdentity` flips to `false` and ordinals are reassigned. | Low | `IsIdentity` is not checked by `SqlAssembler.RenderSelectSql` (which only iterates `.Columns`), so the generated SQL is unaffected. The flag change only matters in `QuarryGenerator.cs` bridge projection logic, which operates on the outer plan, not CTE inner plans. No functional impact, but unnecessary work is performed. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No test for DTO where property names differ from entity column names (e.g., a DTO property `Id` mapping to entity column `OrderId`). This would expose the column name domain mismatch described under Correctness. | Medium | The existing tests all use DTOs whose property names exactly match the entity property names (which equal column names under `Exact` naming). Adding a test with mismatched names would catch the naming convention bug. |
| No test for `With<TEntity>` (entity-as-DTO) to verify the reduction path is a no-op. The existing `LambdaCte_TwoChainedWiths_CapturedParams` test covers this indirectly (it still expects all 7 columns), but a dedicated assertion that the output is unchanged would be clearer. | Low | Ensures the `With<TEntity>` single-type-param path is not regressed by the filter. |
| No negative/edge-case test for empty `CteColumns` (e.g., a DTO with no public get/set properties). | Low | The `lambdaColumns.Count > 0` guard should skip reduction, but there is no explicit test to verify this. |
| Updated tests verify both SQL output (string comparison) and runtime execution (row counts and values), which is good end-to-end coverage for the happy path. | Info | The tests confirm the fix produces correct SQL and returns correct data from real database execution. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The `QueryPlan` copy uses positional constructor arguments (24 args), which is fragile. The codebase already has a precedent for this pattern (no `With`/copy method on `QueryPlan`), so it is consistent, but the class has grown to 24 constructor parameters. | Low | Any future addition to `QueryPlan`'s constructor will silently break this call site (compiler error if required, silent default if optional). Consider adding a `WithProjection(SelectProjection)` method to `QueryPlan` to centralize this copy pattern, similar to `QueryParameter.WithEnrichment`. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Non-lambda CTE path (`raw.CteInnerArgSpanStart` branch, line 844) is untouched. The reduction only applies inside the lambda branch, gated by `lambdaColumns.Count > 0 && lambdaInnerPlan.Projection.IsIdentity`. | Info | No risk of affecting the direct-form CTE path. |
| The change narrows the CTE inner SELECT, which is a behavioral change in generated SQL. Any downstream code or tests that depended on the inner CTE selecting all entity columns will break. | Low | The updated tests account for this. The narrower SELECT is the correct behavior (matches the non-lambda form). Users who relied on selecting extra columns from a CTE that were not in the DTO would see a runtime column-not-found error, but that usage pattern was never supported by the type system. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|-------------|
| Column name domain mismatch in filter | Correctness | A | Fix: match on PropertyName instead of ColumnName |
| No test for naming convention mismatch | Test Quality | D | Not valid — test infrastructure uses Exact naming only |
| Missing navigationHops/isJoinNullable in copy | Correctness | D | Safe for identity projections, matches existing pattern |
| Stale InnerSql after reduction | Correctness | D | Fallback only; no runtime impact |
| With<TEntity> single-type-param enters reduction | Correctness | D | No functional impact |
| No test for With<TEntity> no-op | Test Quality | D | Covered indirectly by existing tests |
| No test for empty CteColumns | Test Quality | D | Guarded by lambdaColumns.Count > 0 |
| Fragile 24-arg QueryPlan copy | Codebase Consistency | D | Consistent with codebase pattern |
| Behavioral change in generated SQL | Integration | D | Correct behavior, tests updated |
