# Review: #232

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan states "The main query side uses a Where clause with a parameter, so the operand's window function parameter gets a subsequent global index." The test actually uses `Where(o => true)` (no parameter), so the operand's projection parameter lands at `@p0` (index 0), not a subsequent index. | Low | The plan description is inaccurate, but the implementation and test are internally consistent and correct. The core fix is not affected. |
| Both plan phases implemented as specified: Phase 1 adds the merging block to `AnalyzeOperandChain`, Phase 2 adds the cross-dialect test. Code is an exact structural copy of the `AnalyzeChainGroup` block (lines 1262-1295). | N/A | Confirms plan compliance. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `String.Replace("@__proj0", ...)` could match inside `@__proj01` if there were 10+ projection parameters (prefix collision). | Low | This is a pre-existing pattern copied verbatim from `AnalyzeChainGroup` (lines 1280-1284), not introduced by this branch. Unlikely in practice (10+ window function variable args in a single projection). Not a regression. |
| Test does not exercise the case where the main query already has parameters (e.g., a parameterized `Where` clause) before the operand's projection parameters, which would test the global index offset/remapping more thoroughly. | Low | The `RemapParameters` call and `paramGlobalIndex` ref tracking handle this by construction (same mechanism proven in `AnalyzeChainGroup`), but a test with `@p0` on the main side and `@p1` on the operand side would provide stronger confidence. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The test covers all four dialects (SQLite, PostgreSQL, MySQL, SQL Server) and verifies parameterized output instead of raw `@__proj0` placeholders. It also executes the query against SQLite to verify runtime correctness. Good coverage for the happy path. | N/A | Positive observation. |
| No test for multiple projection parameters in a single set operation operand (e.g., two different variable window function args like `Ntile(a, ...), Lag(col, b, ...)`). | Low | The single-parameter case exercises the core path. Multi-parameter remapping uses the same loop, so risk is low, but coverage would be more complete. |
| No test for the parameter index offset scenario (main query has parameters, operand projection parameter gets a later global index). | Low | As noted in Correctness, the mechanism is shared with `AnalyzeChainGroup`, but a dedicated test would guard against regressions specific to the operand path. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new block is a verbatim copy of the existing `AnalyzeChainGroup` merging logic. While this means consistent behavior, it also means the same logic exists in two places with no shared extraction. | Low | This is consistent with the current codebase style (the plan explicitly calls for copying the block). A future refactor could extract a shared helper, but that is outside the scope of this issue. |
| Test follows the established pattern from `CrossDialectWindowFunctionTests.WindowFunction_Ntile_Variable` and `CrossDialectSetOperationTests` conventions (harness setup, `AssertDialects`, execution check). | N/A | Good pattern adherence. |

## Integration / Breaking Changes

No concerns. The change is purely additive: a new code path that only activates when set operation operands have non-empty `ProjectionParameters`. Queries without projection parameters in operands are unaffected (the `if` guard checks `Count > 0`). No API surface changes.

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Plan Compliance | Plan text inaccuracy about Where param | Low | D | Ignored — code is correct |
| 2 | Correctness | String.Replace prefix collision (10+ params) | Low | D | Pre-existing pattern, not this PR |
| 3 | Correctness/Test | No test for param index offset | Low | A | Fixed: added paramOffset to QuoteSqlExpression/AppendSelectColumns, added ParameterCount fixup in RenderSelectSql, added test |
| 4 | Test Quality | No multi-param projection test | Low | D | Same loop, low risk |
| 5 | Consistency | Duplicated merging block | Low | D | Intentional per plan |

## Issues Created
