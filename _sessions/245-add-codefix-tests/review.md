# Review: 245-add-codefix-tests

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| SqlKata test renamed from `SimpleWhereQuery_ReplacesWithChainApi` (plan) to `SimpleQuery_ReplacesWithChainApi` (implementation), testing a bare `new Query("users")` instead of `new Query("users").Where("user_id", ">", 5)` | Low | The comment in the test explains this is intentional: the code fix uses `AncestorsAndSelf()` to find `ObjectCreationExpressionSyntax`, which only works for bare creation nodes. The plan's `.Where(...)` scenario would not exercise the code fix path correctly. This is a well-documented deviation, not an omission. |
| All 23 tests delivered across 4 files, matching the plan's 4 phases (6 + 6 + 5 + 6) | -- | Full compliance with planned test count and scope. |
| All planned test scenarios covered: registration (FixableDiagnosticIds, FixAllProvider), replacement, await preservation, using directives, non-fixable rejection, TODO comment (ADO.NET) | -- | No scope creep. No missing scenarios. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `UsingDirectivesAdded` tests in all four files include `using Quarry;` in the input source, meaning the test only verifies that `using Quarry.Query;` is newly added while `using Quarry;` is preserved | Low | The test name implies both directives are being added fresh, but the Schema base class requires `using Quarry;` to compile. This is a structural limitation, not a logic error. The `EnsureUsing` idempotency (already-present using is not duplicated) is implicitly tested, which is valuable. The missing case is adding `using Quarry;` from scratch, but that is not possible given the schema requirement. |
| `AdhocWorkspace` is not disposed after use in `ApplyCodeFixAsync` | Low | `AdhocWorkspace` implements `IDisposable`. In test code this is unlikely to cause issues since the test runner process reclaims resources, but it is technically a resource leak. Existing analyzer tests in the repo do not use workspaces at all, so there is no precedent either way. |
| No concerns with logic, boundary conditions, or race conditions in the test helper methods | -- | The `ApplyCodeFixAsync` helper correctly handles the null/empty diagnostic path, the non-fixable diagnostic path, and the no-code-action path. |

## Security
No concerns.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Happy path tests use `Does.Contain` substring matching rather than exact output comparison | Low | `Does.Contain(".Users()")` confirms the schema-matched table method appears in output, but does not verify the overall structure of the generated chain. This is a pragmatic tradeoff: exact string matching would be brittle against whitespace/trivia changes in Roslyn output. The existing converter and emitter tests already validate exact chain code output at a lower level, so the code fix tests appropriately focus on integration-level concerns (did the replacement happen, was await preserved, were usings added). |
| Three functional tests in ADO.NET, Dapper, and EF Core reuse the same input source for multiple test methods (replacement, await, usings) | Low | Ideally each test would use a distinct input to isolate what is being tested. However, this mirrors the existing test patterns in the repo and keeps the test file size manageable. The assertions are distinct and meaningful for each test. |
| Non-fixable diagnostic tests correctly verify both `source Is.Null` and `actions Is.Empty`, covering the complete negative path | -- | Good dual assertion ensures neither a transformed document nor a registered code action leaks through. |
| SqlKata has no `AwaitPreserved` test, but the plan did not call for one (SqlKata queries are synchronous `new Query(...)` expressions) | -- | Consistent with plan and with the production code fix which handles the non-await path for SqlKata. |
| No test for `QRM002`/`QRM012`/`QRM022`/`QRM032` (conversion with warnings) actually triggering and being fixed | Medium | The `FixableDiagnosticIds` registration tests confirm these IDs are declared, but no functional test exercises a scenario that produces a `0x2`-tier diagnostic. If the code fix had a bug specific to warning-tier conversions (e.g., handling the warning message differently), it would not be caught. This is a coverage gap for the "conversion with warnings" tier. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Stub duplication follows the established pattern in existing detector/converter/analyzer tests | -- | Each test file carries its own framework stub and QuarryStub, consistent with `DapperMigrationAnalyzerTests`, `EfCoreMigrationAnalyzerTests`, etc. No shared test utilities are introduced, matching the repo convention. |
| Test class naming (`XxxMigrationCodeFixTests`) follows the existing `XxxMigrationAnalyzerTests` convention | -- | Consistent naming. |
| File-scoped namespace, `[TestFixture]`, NUnit `Assert.That` style all match existing tests | -- | No style deviations. |
| Helper method is `private static async Task<...>` matching the `GetDiagnosticsAsync` pattern in analyzer tests | -- | Consistent helper design. |
| Comment style (`// -- Registration tests --`, `// -- Functional tests --`) matches section headers used in other test files | -- | Consistent formatting. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
No concerns. This is a test-only change adding 4 new test files. No production code is modified. No APIs are changed. No new dependencies are introduced. The test project already has all required NuGet package references.

## Classifications
(leave empty -- main context fills this in)

## Issues Created
(leave empty -- main context fills this in)
