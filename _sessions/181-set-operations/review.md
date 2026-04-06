# Review: #201 (Pass 5)

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | - | All 6 plan phases implemented. Cross-entity unions deferred with QRY073. QRY044 not needed (type system prevents it). |

The plan is faithfully implemented: SetOperatorKind enum, SetOperationPlan IR class, operand chain linking via OperandChainId, inline operand splitting with boundary detection, subquery wrapping for post-union WHERE/HAVING/GroupBy, direct ORDER BY/LIMIT application, dual-carrier operand approach, parameter remapping with global indices, and QRY070/QRY071/QRY072/QRY073 diagnostics. All design decisions from workflow.md are reflected in code.

## Correctness

| # | Finding | Severity | File(s) | Details |
|---|---------|----------|---------|---------|
| 1 | `BuildParamConditionalMap` does not advance past set operation operand parameters | Medium | `TerminalEmitHelpers.cs:120-132` | `GetClauseParamCount` (line 137) returns 0 for set operation clause entries because they have no `Clause`, no `UpdateInfo`, and no `SetActionParameters`. This means `globalOffset` in `BuildParamConditionalMap` does not advance past the operand's absorbed parameters. For chains with parameterized post-union clauses, the conditional map would assign incorrect global indices to those parameters. Since `ResolveSiteParams` was correctly fixed in pass 4 to account for set operation operand params (lines 42-48), this is the same class of bug left unfixed in a parallel code path. In practice, the `condMap` is consulted via `TryGetValue(p.GlobalIndex, ...)` and missing entries default to (false, null) -- meaning unconditional with no bit index. Since operand parameters and post-union parameters should never be conditional (they can't be inside if-blocks relative to the execution terminal), the resulting code is functionally correct for all current scenarios. The bug would only manifest if a user placed a post-union WHERE inside a conditional block, which is an unlikely but not impossible pattern. |
| 2 | `EmitDiagnosticClauseArray` clauseParamOffsets computation skips set operation operand params | Medium | `TerminalEmitHelpers.cs:289-295` | Same root cause as #1: `GetClauseParamCount` returns 0 for set operation sites, so `clauseParamOffsets` for any post-union clause will be offset by the number of missing operand parameters. For the three-layer test case (1 left param + 1 operand param + 1 post-union param), the post-union WHERE clause would get `clauseParamOffsets[uid] = 1` instead of 2. This means `EmitDiagnosticClauseArray` would emit `@p1` instead of `@p2` in the diagnostic clause parameter names for that clause. This only affects the `ToDiagnostics()` clause-level parameter display (not the SQL string itself, which is correct). No test currently validates per-clause diagnostic parameters for set operation chains, so this is latent. |
| 3 | `AssembledPlan.Equals` omits `IsOperandChain` (known/accepted) | Low | `AssembledPlan.cs:153-164` | Previously accepted. Two plans differing only in `IsOperandChain` would compare equal. Unreachable in practice because operand chains have fundamentally different plans. |
| 4 | `QueryPlan.GetHashCode` omits set operation fields (known/accepted) | Low | `QueryPlan.cs:127-130` | Previously accepted. Follows existing convention. |

### ResolveSiteParams Fix Verification

The pass 4 fix at `TerminalEmitHelpers.cs:42-48` is correct. When iterating clause entries, set operation sites now advance `globalParamOffset` by `chain.Plan.SetOperations[setOpIndex].Operand.Parameters.Count` and increment `setOpIndex`. The `setOpIndex < chain.Plan.SetOperations.Count` guard prevents out-of-bounds access if clause entries and set operation plans are mismatched. The fix ensures that post-union clause sites receive the correct global offset for carrier field assignment.

### Parameter Remapping Path Audit

The parameter remapping has three layers:
1. **ChainAnalyzer** (lines 857-877): Operand parameters are absorbed into the main chain's `parameters` list with correct `paramGlobalIndex` values. `SetOperationPlan.ParameterOffset` is set to the current `paramGlobalIndex` before absorption.
2. **SqlAssembler** (lines 275-287): `RenderSelectSql` is called recursively for operand plans with `paramIndex` as `paramBaseOffset`. The returned `ParameterCount` is used as the new `paramIndex`, ensuring post-union clauses use the correct offset. This is correct because `RenderSelectSql` returns `paramIndex` (which started at `paramBaseOffset` and advanced through the operand's params).
3. **SetOperationBodyEmitter** (lines 48-57): Copies operand carrier fields `__op.P{i}` to main carrier fields `__c.P{targetIdx}` where `targetIdx = setOp.ParameterOffset + i`. This matches the global index assigned in step 1.

All three layers are consistent. The parameter flow is: user code -> operand carrier P0..Pn -> set op interceptor copies to main carrier P{offset}..P{offset+n} -> terminal reads main carrier fields -> SQL uses correct @p indices.

## Security

No concerns. All parameter values flow through the existing `DbParameter` binding infrastructure. No raw string interpolation of user input. Set operation SQL keywords (`UNION`, `INTERSECT`, `EXCEPT`) are hardcoded string literals in `GetSetOperatorKeyword`. The `__set` alias in subquery wrapping is a hardcoded literal.

## Test Quality

| # | Finding | Severity | Details |
|---|---------|----------|---------|
| 1 | No test for ToDiagnostics() clause-level parameter display on set operation chains | Low | `EmitDiagnosticClauseArray` has the offset bug described in Correctness #2. A test calling `.ToDiagnostics()` and inspecting `Clauses[].Parameters` (not just `Sql`) for a parameterized post-union WHERE chain would expose this. Current tests only validate `diag.Sql`, which is rendered correctly by `SqlAssembler`. |
| 2 | No test for conditional (if-block) clauses combined with set operations | Low | The conditional clause machinery (bitmask dispatch) has not been tested in combination with set operations. For example, `if (flag) query = query.Where(...).Union(operand)`. The `BuildParamConditionalMap` offset bug (#1 in Correctness) would surface here if post-union clauses were conditional. This is an unusual pattern and unlikely in practice. |
| 3 | No test for set operation with DISTINCT on individual operand | Low | `db.Users().Distinct().Union(db.Users())` is a valid pattern. The code paths handle this (DISTINCT is set on the operand's QueryPlan, rendered in operand SQL), but no test validates it. |
| 4 | No test for set operation with HAVING that has parameters | Low | Post-union HAVING tests use column references (`Having(u => u.IsActive)`), not parameterized expressions. A parameterized HAVING (e.g., `Having(u => count > minCount)`) would test the GroupBy paramIndex advancement path noted in workflow.md as a known remaining item. |

Positive notes: 2875 tests pass, 0 skipped. Test coverage is strong: all 6 operators with cross-dialect SQL verification, SQLite execution with result count and value assertions, chained set operations, post-union WHERE/GroupBy/Having with subquery wrapping, two-layer and three-layer parameterized set operations with correct parameter indices across all 4 dialects. The three-layer param test (pass 4 addition) was the test that exposed the ResolveSiteParams bug and is now a regression guard.

## Codebase Consistency

| # | Finding | Severity | Details |
|---|---------|----------|---------|
| 1 | `GetClauseParamCount` does not handle set operation sites consistently with `ResolveSiteParams` | Medium | `ResolveSiteParams` was fixed in pass 4 to account for set operation operand params. But `GetClauseParamCount` (used by `BuildParamConditionalMap` and `EmitDiagnosticClauseArray`) was not updated with the same logic. This is a consistency issue -- two functions that compute global parameter offsets by iterating clause entries use different logic for set operation sites. The fix would be to add a set-operation-aware branch to `GetClauseParamCount` (or use a shared helper). |
| 2 | Remaining AnalyzeOperandChain duplication (known/accepted) | Low | ~215 lines duplicated from AnalyzeChainGroup. Shared helper extracted for identity projection enrichment; rest documented in workflow.md. |

Positive notes: New code consistently follows established patterns. The `QueryPlanReferenceComparer` is clean. `SetOperationBodyEmitter` follows the same structure as sibling emitters. All error paths use `PipelineErrorBag.Report`. `GetSetOperatorKeyword` now throws on unknown kind (was "UNION" default in earlier passes).

## Integration / Breaking Changes

| # | Finding | Severity | Details |
|---|---------|----------|---------|
| 1 | `IQueryBuilder<T>` gains 6 new default interface methods; `IQueryBuilder<TEntity, TResult>` gains 12. | Low | Additive-only, no binary break. Default implementations throw `InvalidOperationException`. Cross-entity overloads (`Union<TOther>`) are defined but not wired in codegen (QRY073 diagnostic guards). |
| 2 | `AssembledPlan` constructor gains `isOperandChain` parameter (default: false). | Low | Backward compatible. |
| 3 | `RawCallSite` constructor gains 3 nullable parameters with defaults. | Low | Backward compatible. |
| 4 | `ClauseRole.SetOperation` added to enum. | Low | Additive. Existing switch statements that don't handle it fall through to default cases. `IsDiagnosticClauseRole` correctly excludes it. |
| 5 | `InterceptorKind` gains 6 new values. | Low | Additive. Routed through all switches (InterceptorRouter, MapInterceptorKindToClauseRole, IsSetOperationKind). |

No breaking changes. All new public API uses default interface implementations. All internal changes use optional parameters with defaults.

## Summary

After 4 rounds of remediation, the implementation is solid. The ResolveSiteParams fix from pass 4 correctly addresses the parameter offset bug for carrier field assignment. The SQL assembly, chain analysis, and set operation body emission are all correct and produce verified output across 4 SQL dialects.

The primary remaining issue is a consistency gap: `GetClauseParamCount` (used by `BuildParamConditionalMap` and `EmitDiagnosticClauseArray`) does not account for set operation operand parameters, whereas the recently-fixed `ResolveSiteParams` does. This causes incorrect diagnostic clause-level parameter display for parameterized post-union chains. The actual SQL and parameter binding are unaffected. The severity is Medium because it affects diagnostic accuracy, but Low in terms of user impact since the SQL string (the primary diagnostic output) is correct.

## Classifications

| # | Finding | Section | Severity | Class | Recommendation |
|---|---------|---------|----------|-------|---------------|
| 1 | `BuildParamConditionalMap` / `GetClauseParamCount` missing set op operand param offset | Correctness + Consistency | Medium | A | Fix `GetClauseParamCount` to handle set operation sites the same way `ResolveSiteParams` does. Add a set-op-aware branch that uses `chain.Plan.SetOperations` to determine operand parameter count. |
| 2 | `EmitDiagnosticClauseArray` clauseParamOffsets same root cause | Correctness | Medium | D | Same root cause as #1. Fixing `GetClauseParamCount` resolves both. If #1 is deferred, this is also deferred. |
| 3 | No test for ToDiagnostics clause params on set op chains | Test Quality | Low | A | Add a test that inspects `diag.Clauses[].Parameters` for a parameterized post-union WHERE chain. This would be a regression guard for finding #1. |
| 4 | No test for conditional clauses + set operations | Test Quality | Low | D | Unusual pattern, unlikely to be hit. Defer. |
| 5 | No test for DISTINCT on operand | Test Quality | Low | D | Minor gap. Defer. |
| 6 | No test for parameterized post-union HAVING | Test Quality | Low | D | Known remaining item from workflow.md. Defer. |
| 7 | AssembledPlan.Equals omits IsOperandChain | Correctness | Low | D | Known/accepted from pass 4. |
| 8 | QueryPlan.GetHashCode omits set operation fields | Correctness | Low | D | Known/accepted from pass 4. |

Class key: A = Actionable (fix recommended), D = Deferred (acceptable as-is or deferred to a future pass).

## Issues Created
(none)
