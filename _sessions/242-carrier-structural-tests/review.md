# Review: 242-carrier-structural-tests

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test 1 assertion strategy changed: plan specified `Does.Not.Contain("var __c = Unsafe.As")` but implementation uses `Regex.Matches` count `Is.EqualTo(1)` | Low | The implementation is actually **stronger** than the plan — counting occurrences is more precise than a blanket `Does.Not.Contain` since the carrier cast legitimately appears once in the terminal preamble. The plan's approach would have failed because the string does appear in the generated code (just once, in the terminal). This is a justified deviation. |
| Test 2 dropped the belt-and-suspenders `Does.Not.Contain("static Quarry.Internal.CollectionSqlCache?[] _sqlCache")` negative assertion | Low | Plan explicitly called this "redundant but cheap." Dropping it is defensible since the positive `Does.Contain("static readonly ...")` assertion fully covers the readonly requirement — the substring match already proves `readonly` is present. No regression risk. |
| All four tests present with correct names matching plan exactly | N/A | Full scope implemented: `CarrierGeneration_ParameterlessClause_OmitsCarrierCast`, `CarrierGeneration_CollectionParam_HasReadonlySqlCache`, `CarrierGeneration_BatchInsert_UsesParameterNameCache`, `CarrierGeneration_SelfContainedReader_EmitsReaderField`. |
| Test placement matches plan — all four added at end of file in a dedicated region | N/A | Plan specified "at the end of the existing test class, before the closing `}`." Implementation adds them in a `#region Structural Shape Assertions` block in exactly that position. |
| Single-phase, single-commit scope matches plan | N/A | No scope creep detected. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test 1 regex pattern `var __c = Unsafe\.As<` correctly escapes the dot in `As` | N/A | The regex is well-formed and will match precisely the intended pattern. |
| Test 2 uses `System.Threading.Tasks.Task` instead of `Task` in the source string | N/A | This is required because the test source adds `using System.Collections.Generic;` but does not add `using System.Threading.Tasks;` — the `IReadOnlyList<int>` parameter needs the Collections using. This matches the existing `CarrierGeneration_CollectionParam_EmitsNullBangInitializer` test at line 1596 which uses the same fully-qualified form for the same reason. |
| All four tests null-assert `interceptorsTree` before dereferencing with `!` | N/A | Correct pattern: `Assert.That(interceptorsTree, Is.Not.Null, ...)` precedes `interceptorsTree!.GetText()`. |

No correctness concerns.

## Security
No concerns. These are unit tests running a source generator in-process. No external input, no network calls, no file system writes beyond test execution.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Tests are structural assertions only — they verify code shape, not compilation or execution of the generated code | Low | This is by design (the plan explicitly scopes these as "shape assertions"), and existing end-to-end tests provide compilation/execution coverage. However, a generated code snippet that matches the expected strings but fails to compile would not be caught by these tests alone. This is acceptable given existing test infrastructure. |
| Tests 1 and 4 use identical query chains (`db.Users().Select(u => u).ExecuteFetchAllAsync()`) | Info | Both test different aspects of the same generated output (carrier cast omission vs. reader field extraction). This is intentional and noted in the plan. If the generator changes how it handles this chain, both tests could break simultaneously, which is actually desirable since both features should be validated. |
| No negative/failure-mode tests (e.g., what happens when dead-code removal should NOT omit the cast) | Low | The plan scoped these as positive structural assertions only. Failure-mode coverage (e.g., verifying the cast IS present when parameters exist) is partially covered by other existing tests like `CarrierGeneration_WithParameters`. |
| Test 3 negative assertion `Does.Not.Contain("\"@p\" + __paramIdx")` guards against regression to string concatenation | N/A | Good defensive assertion. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New `#region Structural Shape Assertions` follows existing region convention | N/A | Other regions: `#region Issue: No concrete QueryBuilder<T> references in generated code`, `#region CTE diagnostics (QRY080 / QRY081)`, `#region Window function OVER clause failure modes (#223)`, etc. The new region name is consistent in style. |
| Test naming follows `CarrierGeneration_*` convention | N/A | All 80+ existing tests in this file use the same prefix pattern. |
| Interceptor tree lookup uses `.Contains(".Interceptors.")` pattern matching the majority of existing tests | N/A | Consistent with lines 124, 160, 192, etc. (Some older tests use `.Contains("Interceptors")` without the dots, but the majority use the dotted form.) |
| `System.Text.RegularExpressions.Regex.Matches` is fully qualified in Test 1 | Info | The file has no `using System.Text.RegularExpressions;` at the top. Fully qualifying is acceptable but differs from the pattern of top-level using directives. Since this is the only regex usage in the entire file, a new using for one call is arguably worse than fully qualifying. No action needed. |
| Assert message strings follow existing descriptive style | N/A | Messages like "Should generate interceptors file" and "Carrier cast should appear once (terminal only)..." are consistent with existing test messages. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No API changes, no new dependencies, no configuration changes | N/A | Pure test additions. |
| No changes to production code | N/A | Only `CarrierGenerationTests.cs` modified. |
| Session metadata files (`plan.md`, `workflow.md`) are new but are in the `_sessions/` directory | N/A | These are workflow artifacts, not production files. |

## Classifications

- **Overall risk**: Low. Four additive test methods with no production code changes.
- **Deviation from plan**: Two minor justified deviations (stronger regex assertion in Test 1, dropped redundant negative assertion in Test 2). Both are improvements over the plan.
- **Test coverage impact**: Positive. Adds targeted structural regression guards for four codegen optimizations that previously had only indirect coverage through end-to-end tests.

## Issues Created

None. No issues warrant creation — the deviations from plan are improvements, and the implementation is clean.
