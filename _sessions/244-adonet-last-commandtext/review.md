# Review: 244-adonet-last-commandtext

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All four planned steps in Phase 1 are implemented: signature change, span-based filtering loop, post-loop ExtractStringValue, call-site update. | Info | Full plan adherence. |
| Both planned tests (`Detect_ReassignedCommandText_UsesLastBeforeExecute` and `Detect_CommandTextAfterExecute_Ignored`) are present and match the described scenarios. | Info | Full plan adherence. |

No concerns.

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Span comparison uses `assignment.Span.End <= executeInvocation.SpanStart`. Since these are separate statements separated by at least whitespace/semicolons, `<=` is functionally equivalent to `<` and correctly filters assignments that precede the Execute call. | Info | Correct. |
| The "last match wins" logic relies on `DescendantNodes()` returning nodes in document order. This is a guaranteed property of Roslyn's `DescendantNodes` traversal (pre-order depth-first), so overwriting `lastMatch` on each qualifying hit correctly yields the last assignment before the call. | Info | Correct. |
| The second test (`Detect_CommandTextAfterExecute_Ignored`) verifies that two sequential Execute calls with interleaved CommandText assignments each see only the correct SQL. This implicitly validates that the span filter works per-invocation, since `Detect` calls `TryDetect` for each `InvocationExpressionSyntax`. | Info | Good coverage of the multi-Execute scenario. |

No concerns.

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|

No concerns. This is a Roslyn-based static analysis tool; the change does not introduce any runtime execution paths, user input handling, or data flow that could create security issues.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The two new tests directly cover the bug scenario (reassigned CommandText) and the boundary condition (assignment after Execute). | Info | Good targeted coverage. |
| All existing tests (8 tests in the file) continue to exercise the original behavior, providing regression safety. | Info | No existing tests were modified or broken. |
| Minor: no test covers reassignment inside a nested block (e.g., `if` branch) before Execute, where `DescendantNodes` traversal order matters most. | Low | An edge case that could catch future regressions in the traversal assumption, but not a blocking concern for this PR. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new tests follow the exact same pattern as existing tests: `CreateCompilationForDetection` helper, `new AdoNetDetector()`, NUnit `Assert.That` with constraint syntax. | Info | Consistent style. |
| The method signature change adds the parameter in a logical position (after `commandVarName`, before `model`), and the multi-line formatting matches the codebase style. | Info | Clean formatting. |
| The added comment (`// When CommandText is reassigned, we want the last assignment before the Execute call.`) explains the "why" clearly. | Info | Good documentation. |

No concerns.

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `FindCommandTextAssignment` is `private static`, so the signature change has zero public API impact. | Info | No breaking change. |
| The `TryDetectSingle` public entry point (used by the analyzer) calls `TryDetect`, which now passes `invocation` through to the updated method. This means both the batch `Detect` path and the single-node analyzer path benefit from the fix. | Info | Both consumers are correctly updated. |
| `CollectParameterNames` does not apply similar positional filtering relative to the Execute call. If a parameter is added after Execute, it would still be collected. This is a pre-existing concern outside the scope of this PR. | Low | Not a regression introduced by this change; noted for future consideration. |

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Test Quality | No test for reassignment inside nested block | Low | D | Ignored — Roslyn traversal order is guaranteed |
| 2 | Integration | CollectParameterNames lacks positional filtering | Low | A | Fix now per user request |

## Issues Created
None
