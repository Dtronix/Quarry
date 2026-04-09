# Review: 217-lambda-cte-diagnostic-tests

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan Test 1 was `Cte_LambdaWith_IdentityLambda_EmitsQRY080` (`orders => orders`); implementation replaced it with `Cte_LambdaWith_NullBody_EmitsQRY080` (`orders => null!`). | Low | The substitution exercises the same diagnostic path (non-analyzable lambda body -> QRY080) and is arguably a better test since `orders => orders` may type-check as a valid chain pass-through in some analyzer versions. The plan itself anticipated adjustments ("may need adjustment depending on whether the compiler allows this"). Acceptable deviation, but should be noted. |
| Plan Test 3 called for `orders => orders.ToString()`; implementation changed to `orders => GetFallback()` (static method returning `IQueryBuilder<Order>`). | Low | Plan explicitly flagged a likely type-mismatch with `ToString()` and said "Will verify during implementation." The static-method variant avoids compilation errors that would prevent the generator from running, making the test actually exercisable. Good judgment call, consistent with plan's own caveat. |
| Plan specified 3 tests; implementation delivers 3 tests. Commit message matches plan ("Add lambda-form QRY080 diagnostic tests (#217)"). Single phase, single commit. | None | Full compliance on scope, commit hygiene, and deliverable count. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 3 new tests pass. Full suite runs 2834 tests, 0 failures (baseline was 2831). | None | No regressions introduced. |
| Each test correctly asserts QRY080 is present AND QRY900 is absent, matching the dual-assertion pattern established by the existing `Cte_With_NonInlineInnerArgument_EmitsQRY080` test. | None | Proper validation of both the positive diagnostic and the absence of misclassification. |
| Diagnostic assertion messages include full diagnostic dump (`diagnostics.Select(d => d.Id + ": " + d.GetMessage())`), providing useful failure context. | None | Follows existing pattern; aids debugging if tests ever fail. |

## Security
No concerns. All changes are test-only code with no runtime, network, or secret exposure.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The three tests cover distinct failure modes: (1) null-literal body, (2) external variable reference, (3) non-Quarry static method call. These represent three structurally different lambda bodies that all fail lambda inner chain detection. | None | Good coverage of the "non-analyzable lambda body" diagnostic surface area. |
| No identity-lambda test (`orders => orders`) is present. | Info | This was planned but replaced with the null-body test. An identity lambda is a plausible user mistake (especially for someone exploring the API). If the analyzer currently handles it correctly (emits QRY080), adding it as a fourth test would strengthen coverage. Not blocking. |
| Tests do not assert on the diagnostic's Location/SourceSpan (unlike the QRY082 test which validates span text). | Info | For QRY080, pinpointing the exact call site is less critical than for QRY082's "which duplicate" question. The existing non-lambda QRY080 test also does not assert on location, so this is consistent. Not a gap. |
| No block-body lambda test (`orders => { return null!; }`). | Info | All three tests use expression-body lambdas. A statement-body lambda might exercise a different code path in the syntax walker. Low risk since the discovery logic likely normalizes both forms, but worth noting for future coverage expansion. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test naming follows established `Cte_{Form}_{Scenario}_Emits{DiagId}` convention. | None | Consistent with `Cte_With_NonInlineInnerArgument_EmitsQRY080`, `Cte_FromCte_WithoutPrecedingWith_EmitsQRY081`, etc. |
| Test structure (SharedSchema + source, CreateCompilation, RunGeneratorWithDiagnostics, dual QRY080/QRY900 assertions) is identical to the existing QRY080 test. | None | Perfect pattern match. |
| Tests are placed inside the correct `#region CTE diagnostics (QRY080 / QRY081)`, immediately after the existing QRY080 test and before the QRY081 test. | None | Correct ordering by diagnostic ID. |
| Comment style (multi-line block comment explaining the scenario before each test) matches existing tests. | None | Consistent documentation approach. |
| All three lambda test bodies use `Order` entity with `OrderId` and `Total` fields, matching existing CTE test patterns. | None | Consistent schema usage. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No existing tests were modified. Only additive changes (3 new test methods + session files). | None | Zero risk of breaking existing functionality. |
| Full test suite passes (2834/2834) after the change. | None | Confirmed no regressions. |
| Session files (`plan.md`, `workflow.md`) are under `_sessions/` which is presumably gitignored or excluded from production builds. | None | No production impact. |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Plan Test 1 (IdentityLambda) replaced with NullBody variant | Plan Compliance | (D) Ignore | Plan anticipated adjustments |
| Plan Test 3 (ToString) replaced with static method variant | Plan Compliance | (D) Ignore | Plan anticipated adjustments |
| Missing identity-lambda test | Test Quality | (D) Ignore | Enhancement, not required for #217 |
| Missing block-body lambda test | Test Quality | (D) Ignore | Enhancement, not required for #217 |

## Issues Created
None.
