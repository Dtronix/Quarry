# Review: carrier-codegen-efficiency

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 1 implemented as planned: `NegateBoolean` helper added, `UnaryOpExpr` `Not` case rewired to bind operand with `inBooleanContext = true`, all four dialect test expectations updated. The implementation additionally handles the `ResolvedColumnExpr` path (navigation/join booleans) which the plan's code snippet only showed `SqlRawExpr`. This is a correct extension, not scope creep. | Info | Addresses a broader set of scenarios than the plan enumerated, which is appropriate. |
| Phase 1 `NegateBoolean` includes false-to-true reversal (double negation) which the plan's snippet omitted. The plan showed only `EndsWith(trueLit)` -> `falseLit`; the implementation also checks `EndsWith(falseSuffix)` -> `trueLit`. | Info | Correct deviation. The plan's snippet was incomplete; the implementation is more robust. |
| Phase 2 implemented as planned: `needsCarrierRef` guard wraps the `Unsafe.As` emission. Condition matches the plan exactly: `(clauseParams != null && clauseParams.Count > 0) \|\| clauseBit.HasValue`. | Info | Matches plan. |
| Phase 3 implemented as planned: single-word `readonly` insertion on `_sqlCache` field emission. | Info | Matches plan. |
| Phase 4 implemented as planned: `"@p" + __paramIdx` replaced with `Quarry.Internal.ParameterNames.AtP(__paramIdx)`. | Info | Matches plan. |
| Phase 5 implemented as planned with one important addition not in the plan: the `IsReaderSelfContained` guard that skips extraction when the reader references interceptor-class fields (`_entityReader_*`, `_mapper_*`). The plan did not mention this guard. | Low | Good defensive addition. Without it, readers that reference instance fields on the interceptor class would be broken when moved to a static carrier field. This is a necessary correctness guard, not scope creep. |
| Plan called for creating 2 tracking issues (carrier dedup, incremental SQL mask rendering). These are not present in the branch. | Low | The plan stated "after implementation" for issue creation. If this is pre-merge, they should be created before or during the PR process. |
| Workflow `phases-complete` shows 5 but `phase` shows `REVIEW`, which is correct for the current state. The workflow `Suspend State` section still shows stale session-1 data ("phase 0/5, before starting phase 1"). | Low | The suspend state was not updated after implementation completed. This is a bookkeeping issue only -- it would confuse a future session resuming from this state, but all phases are complete. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `NegateBoolean` `Substring` bounds are correct: `boolExpr.Substring(0, boolExpr.Length - trueSuffix.Length)` cannot underflow because `EndsWith` already confirmed the suffix is present and the suffix includes a leading space + `=` + space + literal, so the expression must be at least that long. | Info | No issue found. |
| `NegateBoolean` handles both `trueSuffix` and `falseSuffix`, supporting double negation (`!!col`) and ensuring idempotent correctness when `BindExpr` with `inBooleanContext=true` produces `col = FALSE` (e.g., if a negated boolean is re-negated). | Info | Robust handling. |
| The `ResolvedColumnExpr` path in the NOT handler (line 151) extracts `res.QuotedColumnName`. For navigation booleans, `BindNavigationAccess` (line 801) bakes the `= TRUE` suffix into `QuotedColumnName` itself, so `NegateBoolean` correctly finds and replaces it. For non-boolean navigation columns, `QuotedColumnName` has no suffix, `NegateBoolean` returns null, and the fallback `NOT(...)` wrapping applies. | Info | Correct for all paths analyzed. |
| Phase 2 `needsCarrierRef` condition: when `clauseParams` is null (no SetAction, no Clause), the condition evaluates to `false \|\| clauseBit.HasValue`. For clauses without parameters or mask bits, `__c` is correctly omitted. The downstream code at line 280 already gates on `clauseParams.Count > 0` and line 327 gates on `clauseBit.HasValue`, so no path uses `__c` when it is not emitted. | Info | Verified safe. |
| Phase 5 `IsReaderSelfContained` returns `false` when `ProjectionInfo` is null, preventing the `_reader` field from being emitted for chains that have no projection (e.g., non-query chains). The `ReaderDelegateCode != null` check in the caller provides a first gate, and `IsReaderSelfContained` provides a second. | Info | Defensive correctness. |
| Phase 5: When `IsReaderSelfContained` returns false (custom entity reader or custom type mapping), the terminal emitter falls back to `chain.ReaderDelegateCode` (inline lambda). This preserves the original behavior exactly for those cases. | Info | Correct fallback. |

## Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. All code generation changes produce deterministic output from compiler-validated metadata (entity definitions, type names). No user-supplied strings flow into SQL template generation without going through the existing parameter binding mechanism. The `NegateBoolean` helper only manipulates pre-validated column references and boolean literals produced by `FormatBoolean`. | Info | No injection surface introduced. |

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 1: All four dialect manifests updated (MySQL, PostgreSQL, SQLite, SQL Server). Cross-dialect SQL assertions in `CrossDialectDeleteTests` and `CrossDialectWhereTests` verify the new `col = 0`/`col = FALSE` output for all four dialects. `EndToEndSqlTests.Where_NegatedBooleanProperty` updated for SQLite dialect. | Info | Good multi-dialect coverage. |
| Phase 1: The join-based negation (`!u.IsActive` through `LeftJoin`) is covered by `JoinNullableIntegrationTests` (line 74) and the SQLite manifest shows the updated SQL for the join case (line 644 in the diff: `WHERE "t0"."IsActive" = 0`). | Info | Join path tested. |
| No new unit tests were added for Phases 2-5 to verify the structural changes in generated code (absence of `var __c`, presence of `readonly`, use of `ParameterNames.AtP`, presence of `_reader` field). | Medium | The existing test suite exercises these changes indirectly through end-to-end compilation and execution tests, which is reasonable for a source generator. However, a targeted test asserting `Does.Not.Contain("var __c = Unsafe.As")` for a parameterless/maskless clause would catch regressions more precisely. Similarly, a test asserting `Does.Contain("_reader =")` in the carrier class output would guard Phase 5. |
| No test for `NegateBoolean` returning null (non-boolean operand of NOT), ensuring standard `NOT(...)` wrapping is preserved. | Low | The existing test `NOT (@p0) OR "Email" IS NULL` in the manifests confirms parameterized NOT still uses standard wrapping. However, a direct unit test would be more explicit. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `NegateBoolean` uses `Substring(0, len)` instead of the range operator `[..^len]` that was shown in the plan. The actual codebase uses `Substring` extensively elsewhere (e.g., `CarrierEmitter.cs` line 1304-1305), so this is consistent with existing style. | Info | Consistent with codebase conventions. |
| `IsReaderSelfContained` is placed as a `static` method on `CarrierEmitter` and called from `TerminalBodyEmitter`. This cross-class reference follows the existing pattern where `TerminalBodyEmitter` already calls `CarrierEmitter.EmitCarrierExecutionTerminal` and other static methods. | Info | Consistent pattern. |
| The `_reader` field naming follows the existing `_sqlCache` and `_sql` field naming conventions on carrier classes (underscore-prefixed internal static fields). | Info | Consistent naming. |
| `FormatBoolean` was changed from `private` to `internal` visibility (it was already `internal` in origin/master based on the grep showing it is used across the codebase). | Info | No issue. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Adding `readonly` to `_sqlCache` (Phase 3): The array reference is assigned at field initialization and never reassigned. Only array elements are mutated via `_sqlCache[mask] = new CollectionSqlCache(...)`. `readonly` on the array reference does not prevent element mutation. This is safe. | Info | No breaking change. |
| Phase 5 reader field extraction changes generated code structure: terminals now reference `Chain_N._reader` instead of an inline lambda. This could theoretically affect consumers that inspect generated code textually (e.g., Roslyn analyzers or code-gen snapshot tests). The existing `CarrierGenerationTests` do not assert on reader lambda presence, so no test breakage. | Low | If any downstream tools or analyzers depend on the shape of generated interceptor code, this could cause issues. This is inherent to any code-gen optimization and is not a defect. |
| Phase 1 changes SQL output for all dialects, not just SQL Server. `NOT ("col")` becomes `"col" = 0`/`"col" = FALSE` everywhere. This is a semantic improvement (the new form is valid across all SQL dialects including SQL Server bit columns), but it changes the SQL string identity. Any application caching query plans by SQL text would see cache misses on first execution after upgrade. | Low | One-time cache invalidation on upgrade. This is expected for a bug fix that changes SQL output. |
| Phase 2 removes `var __c = Unsafe.As<Chain_N>(builder);` from parameterless clause bodies. Since `__c` was never used in those paths, removing it has zero runtime effect. The generated code compiles identically (the C# compiler would have already eliminated the dead variable). | Info | No behavioral change. |
| Phase 4 changes generated code from `"@p" + __paramIdx` to `Quarry.Internal.ParameterNames.AtP(__paramIdx)`. This adds a runtime dependency on the `Quarry.Internal.ParameterNames` class for batch insert terminals. This class is already in the `Quarry` assembly which is a required dependency, so no new assembly reference is needed. | Info | No new dependency introduced. |

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | 2 tracking issues not created | Low | A | Create issues for carrier dedup and SQL mask rendering |
| 2 | Plan Compliance | Stale Suspend State | Low | D | Irrelevant — all phases complete |
| 3 | Test Quality | No structural unit tests for Phases 2-5 | Medium | C | Create tracking issue |
| 4 | Test Quality | No NegateBoolean null return test | Low | D | Covered by manifest tests |
| 5 | Integration | SQL cache invalidation | Low | D | Expected for bug fix |
| 6 | Integration | Generated code structure change | Low | D | Expected for optimization |

## Issues Created
- #240: Deduplicate structurally identical carrier classes
- #241: Incremental SQL mask rendering for compile speed
- #242: Add structural unit tests for generated carrier code shape
