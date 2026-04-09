# Review: 223-over-clause-failure-tests

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 4 planned test methods are present with exact names matching the plan | Info | Full scenario coverage achieved |
| Test 1 (block body): assertions match plan -- checks no QRY900, interceptors generated, no ROW_NUMBER() in output | Info | Correct RuntimeBuild fallback verification |
| Test 2 (unknown method): assertions match plan -- checks no QRY900, no QRY errors | Info | Graceful degradation verified |
| Test 3 (empty OVER): assertions match plan -- checks no QRY errors, interceptors generated, contains `ROW_NUMBER() OVER ()` | Info | Success path verified |
| Test 4 (non-fluent chain): assertions match plan -- checks no QRY900, no QRY errors | Info | Graceful degradation verified |
| Tests use SharedSchema + TestDbContext with Orders() as specified | Info | Follows plan implementation notes |
| Tests use ExecuteFetchAllAsync() as terminal as specified | Info | Correct generator trigger |
| Tests 2 and 4 filter to QRY-prefixed diagnostics only, as plan recommends for intentional compilation errors | Info | Avoids false failures from CS-prefixed errors |

No deviations from the plan detected.

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test 1 assertion `Does.Not.Contain("ROW_NUMBER()")` could theoretically match a substring of an unrelated comment or string in generated code, but in practice ROW_NUMBER() only appears in window function SQL output | Low | Very low false-positive risk given the generated code structure |
| Test 3 asserts `Does.Contain("ROW_NUMBER() OVER ()")` -- verified against source: `BuildOverClauseString` returns empty string for empty partition+order lists, and `GetWindowFunctionInfo` wraps it as `ROW_NUMBER() OVER ({overClause})`, producing `ROW_NUMBER() OVER ()` | Info | Assertion is correct -- the trailing space before `)` does not appear because `string.Join(" ", parts)` on an empty list returns `""` |
| Tests 2 and 4 have intentional compilation errors (ToString returns string not IOverClause; SomeMethod is undefined). The generator operates on syntax trees and may not even reach the chain in question if type resolution fails early. The tests correctly only assert on absence of QRY-prefixed diagnostics, not on generated output shape | Info | Appropriate assertions for error-recovery tests |
| `ParseOverClause` line 2163: `simple.Body as ExpressionSyntax` returns null for `BlockSyntax` (block body), confirming Test 1's expected behavior | Info | Source behavior matches test expectation |
| `WalkOverChain` line 2218-2219: `IdentifierNameSyntax` is the base case returning true, confirming Test 3's empty-OVER success path | Info | Source behavior matches test expectation |
| `WalkOverChain` line 2221-2222: non-InvocationExpressionSyntax returns false, confirming Test 4's SomeMethod(over) invocation expression would still enter the check but fail at line 2224 (not MemberAccessExpressionSyntax -- it's a plain InvocationExpression). Actually, `SomeMethod(over)` IS an `InvocationExpressionSyntax` whose `Expression` is `IdentifierNameSyntax` (not `MemberAccessExpressionSyntax`), so line 2224 returns false. Correct. | Info | Source behavior matches test expectation |

No correctness concerns.

## Security

No concerns. This is a test-only change with no user input handling, no secrets, no file I/O, and no network calls. Test sources are hardcoded string literals compiled in-memory.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Tests cover 3 distinct failure modes (block body, unknown method, non-fluent expression) plus 1 edge-case success path (empty OVER) | Info | Good coverage of the `ParseOverClause`/`WalkOverChain` code paths |
| Each failure test verifies two properties: no crash (QRY900 absent) and no spurious QRY errors | Info | Meaningful dual assertions |
| Test 1 additionally verifies the interceptors file IS generated but does NOT contain window function SQL -- this validates the RuntimeBuild fallback behavior end-to-end | Info | Strongest assertion among the failure tests |
| Tests 2 and 4 do not assert on generated output shape (no interceptor file check, no code content check) | Low | This is intentional per the plan since compilation errors may prevent the chain from being recognized at all. However, if the generator does produce an interceptor in these cases, the tests would not detect unexpected ROW_NUMBER() output. Consider adding a defensive check if interceptor IS generated. |
| No parameterized/data-driven test combining the failure cases | Info | Given there are only 4 tests and they have different assertion shapes, individual methods are appropriate |
| Tests exercise single-entity (non-joined) OVER clause paths only -- the `ParseJoinedOverClause` variant is not tested | Low | Out of scope for this issue, but worth noting for future coverage |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Naming follows existing `CarrierGeneration_Feature_Scenario` pattern used throughout the file | Info | Consistent naming |
| Test structure matches existing pattern: SharedSchema + source literal, CreateCompilation, RunGeneratorWithDiagnostics, assert on diagnostics/generated code | Info | Consistent structure |
| Uses `Assert.That` with NUnit constraint model (`Is.Null`, `Is.Not.Null`, `Is.Empty`, `Does.Contain`, `Does.Not.Contain`) matching existing tests | Info | Consistent assertion style |
| Interceptor tree lookup pattern `.FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"))` is identical to existing tests (e.g., lines 124-125, 160-161, 2685-2686) | Info | Consistent pattern |
| New tests are placed after `#endregion` (line 2708) but are not wrapped in their own `#region`/`#endregion` block. Existing test groups at lines 2271 and 2363 use `#region` | Low | Minor inconsistency -- existing grouped tests use `#region` blocks, but the new group uses a comment-only separator. Consider wrapping in `#region Window function OVER clause failure modes (#223)` / `#endregion` for consistency. |
| Comment separator style `// -- Window function OVER clause failure modes (#223) --` is distinctive but not used elsewhere in the file (existing groups use `#region`) | Low | Same as above |

## Integration / Breaking Changes

No concerns. This PR adds only test methods. No API changes, no production code modifications, no consumer-facing changes, no migration needs.

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Test Quality | Tests 2&4 don't defensively check for ROW_NUMBER() if interceptor generated | Low | A | Add conditional interceptor check |
| 2 | Test Quality | Joined OVER clause path not tested | Low | C | Create separate issue |
| 3 | Codebase Consistency | Missing #region wrapper | Low | A | Wrap in #region block |

## Issues Created
- #227: Add failure-mode tests for joined OVER clause lambdas (ParseJoinedOverClause)
