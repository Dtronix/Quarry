# Review: 227-joined-over-failure-tests

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 4 planned tests are present with correct names, in the correct location (new `#region` after #223 block), in the correct file. | info | Full plan adherence on structure. |
| Tests 2 (UnknownMethod) and 4 (NonFluentChain) assert QRY032 is produced instead of asserting "no QRY errors" + "no ROW_NUMBER() in generated code" as the plan specified. | low | The plan's assertions were written assuming the joined path would behave identically to the single-entity path (silent RuntimeBuild fallback). The actual generator behavior for joined projections is to emit QRY032 when a tuple element is not analyzable. The implemented assertions are *more correct* than the plan because they match the real code path. Each test includes an explanatory comment documenting why the behavior differs. |
| Test 1 (BlockBody) matches the plan exactly: no QRY900, interceptors file exists, no ROW_NUMBER(). | info | Direct plan match. |
| Test 3 (EmptyOver) matches the plan exactly: no QRY errors, interceptors file exists, contains `ROW_NUMBER() OVER ()`. | info | Direct plan match. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Tests 2 and 4 do not check for interceptors file or absence of `ROW_NUMBER()` in generated code, unlike their single-entity counterparts. | low | Since QRY032 indicates the generator rejected the projection at analysis time, checking generated SQL is arguably redundant. However, adding a guard assertion that interceptors either are absent or do not contain `ROW_NUMBER()` would make the tests more robust against future changes where QRY032 becomes a warning rather than a hard stop. This is a minor hardening opportunity, not a bug. |
| The join condition `u.UserId == o.UserId` correctly references columns present in `SharedSchema` (`UserSchema.UserId` and `OrderSchema.UserId`). | info | No schema mismatch. |
| Test source snippets intentionally produce compilation errors (`over.ToString()` returns `string`, `SomeMethod` is undefined). The comments document this is intentional and the generator is expected to handle syntax-level analysis gracefully. This matches the pattern established by the single-entity #223 tests. | info | Consistent with existing test philosophy of testing generator robustness on ill-typed source. |

## Security
No concerns.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 4 tests follow the established test structure: inline source with `SharedSchema`, `CreateCompilation`, `RunGeneratorWithDiagnostics`, NUnit assertions. Consistent with the 100+ existing tests in the file. | info | Good codebase consistency. |
| Each test covers a distinct failure mode of the OVER lambda parsing, providing meaningful coverage of `ParseJoinedOverClause` edge cases. | info | The 4 cases (block body, unknown method, empty/identity, non-fluent invocation) cover the realistic failure modes of the `WalkOverChain` method when called through the joined code path. |
| Test 3 (EmptyOver) is a positive test confirming valid SQL generation, providing a useful baseline to distinguish from the failure cases. | info | Important for regression detection -- if the joined path breaks entirely, this test catches it. |
| The QRY032 divergence from single-entity behavior is well-documented in test comments. | info | Future maintainers can understand why the joined tests differ from their #223 counterparts. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test naming follows the existing `CarrierGeneration_{Feature}_{Scenario}_{ExpectedOutcome}` convention. | info | Consistent. |
| Region naming follows the `#region {topic} (#{issue})` convention used by #223. | info | Consistent. |
| The `TestDbContext` in each test's source declares both `Users()` and `Orders()` accessors, which is the minimal addition needed over the single-entity tests (which only declare `Orders()`). | info | Clean, minimal divergence from the template. |

## Integration / Breaking Changes
No concerns. This branch adds only test code; no production source files are modified.

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | Tests 2 and 4 assert QRY032 instead of plan's "no QRY errors" + "no ROW_NUMBER()" | low | D | Intentional deviation — matches actual generator behavior |
| 2 | Correctness | Tests 2 and 4 omit generated-code guard assertions | low | D | QRY032 rejects the chain; guard assertion is redundant |

## Issues Created
None.
