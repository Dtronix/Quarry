# Code Review: Carrier Class Deduplication (#240)

**Branch:** `240-dedup-carrier-classes`
**Commits:** 3 (896637d, ce48043, 80ff4f8)
**Files changed:** `FileEmitter.cs`, `CarrierGenerationTests.cs`

---

## 1. Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All three planned phases implemented in order with matching commit messages. | N/A (positive) | Implementation follows plan precisely. |
| `CarrierStructuralKey` is a private nested readonly struct inside `FileEmitter` as specified. | N/A (positive) | Matches design decision for co-location. |
| Plan specifies test 1 should "Verify: both interceptors reference `Chain_0`." The test only asserts the carrier class count is 1 -- it does not verify that both interceptor methods actually reference `Chain_0`. | Low | The most important invariant (both chains USE the shared class) is not explicitly verified. A bug where the second chain's ClassName is not set correctly would not be caught. |
| Plan specifies test 3 should "Verify: not merged (SQL differs)." The assertion uses `Is.GreaterThanOrEqualTo(1)` instead of `Is.EqualTo(2)`, which would pass even if dedup incorrectly merges the two carriers. | Medium | The test does not actually verify the negative case (non-merging). It passes regardless of whether dedup fires or not. |

## 2. Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The structural key compares Fields, MaskType, MaskBitCount, extractors, SqlVariants, ReaderDelegateCode, collection flag, and interfaces -- matching all components used by `EmitCarrierClass` to produce the class body. The `contextTypeName` parameter is a per-file constant and correctly omitted. | N/A (positive) | Key captures all structural components. |
| `Equals` short-circuits on `_hashCode != other._hashCode` before deep comparison. This is safe: different hashes guarantee inequality, same hashes proceed to full comparison. | N/A (positive) | Correct optimization. |
| `CarrierField` and `CapturedVariableExtractor` both implement `IEquatable<T>` with proper value semantics covering all structurally relevant properties. `AssembledSqlVariant` also implements `IEquatable<AssembledSqlVariant>`. The `EqualityHelpers.SequenceEqual` and `DictionaryEqual` methods correctly delegate to these. | N/A (positive) | Equality chain is sound. |
| Hash code uses only the first dictionary entry from `_sqlVariants` (via `foreach` + `break`). Dictionary iteration order is not guaranteed by the .NET spec, so two `Dictionary<int, AssembledSqlVariant>` instances with identical content could yield different "first" entries and thus different hash codes. This would cause dedup misses (false negatives) but never incorrect merges. | Low | In practice, both dictionaries are built by the same pipeline code in insertion-order, so this is extremely unlikely. Worst case is a missed dedup opportunity, not a correctness bug. Sorting by key before hashing would eliminate the theoretical concern. |
| The key does not explicitly capture `ResultTypeName` / `ExecutionSite.ResultTypeName` / `ProjectionInfo`, which `EmitCarrierClass` uses to compute the `_reader` field's generic type parameter (`System.Func<DbDataReader, T>`). If two carriers had identical `ReaderDelegateCode` but different resolved result types, the class text would differ but keys would match. | Low | In practice this cannot occur: the reader delegate lambda text encodes the result type implicitly (e.g., casting/constructing the result). The invariant is implicit rather than explicit. |
| Lookup registrations (carrierLookup, carrierClauseLookup, operandCarrierNames, carrierFirstClauseIds) still execute for every chain regardless of dedup, preserving the per-chain mapping invariant. | N/A (positive) | Downstream interceptor emission is unaffected. |
| `ImplementedInterfaces` and `BaseClassName` are set on every carrier's `CarrierPlan` before the dedup gate, ensuring downstream code that reads these properties still works for deduped carriers. | N/A (positive) | Matches plan's "Important detail." |
| The `EmitCarrierClass` call was moved inside the `else` branch (new carrier). The original code called it AFTER all lookup registrations. Now it is called BEFORE. This is safe because `EmitCarrierClass` only appends to `StringBuilder` and does not read any lookup dictionaries. | N/A (positive) | Emission order change is harmless. |
| Hash collisions: `GetHashCode` returns `int`, so collisions are possible. However, `Equals` performs full deep comparison, so collisions only affect performance (bucket chaining), not correctness. The dedup dictionary will never incorrectly merge two structurally different carriers. | N/A (positive) | Hash collisions are safely handled by Equals. |

## 3. Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | This is a compile-time source generator optimization. No runtime input validation, injection, auth, or data exposure vectors are introduced. The change does not affect the generated code's security properties -- it only deduplicates identical class definitions. |

## 4. Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Test 1 (`SameWherePattern_SharedClass`) verifies dedup fires by asserting exactly 1 carrier class. | N/A (positive) | Happy path is covered. |
| Test 1 does not verify that both interceptor method bodies reference the shared `Chain_0` class name. | Low | A bug where `carrierPlan.ClassName` is not assigned for the deduped carrier would cause downstream emission to use a wrong/empty class name. This test would still pass. Consider adding `Assert.That(Regex.Matches(code, "Chain_0").Count, Is.GreaterThan(1))` or checking that both methods cast to `Chain_0`. |
| Test 3 (`SameFieldsDifferentSql_SeparateClasses`) uses `Is.GreaterThanOrEqualTo(1)` which always passes if any carrier is generated. The plan required verifying that carriers are NOT merged. | Medium | This test provides no regression protection for the negative case. If a future change incorrectly ignores SQL differences in the key, this test would still pass. Should assert `Is.EqualTo(2)` or, if the SQL is actually identical due to parameterization, the test scenario needs reworking. |
| No edge-case tests for: carriers with different interfaces but same fields/SQL; carriers with collection vs. non-collection params; carriers with vs. without reader delegate code; empty carrier (no fields). | Low | These are implicit through the structural key, but explicit negative tests would catch regressions if key components are accidentally removed. |
| No tests verify that carrier class numbering is gap-free after dedup (e.g., 3 chains where 2 dedup -> Chain_0, Chain_1 not Chain_0, Chain_2). | Info | Minor, but would validate the plan's claim about sequential numbering. |

## 5. Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `CarrierStructuralKey` follows the existing pattern of private nested types in `FileEmitter` (cf. `QueryPlanReferenceComparer`). | N/A (positive) | Consistent with repo conventions. |
| Uses `IEquatable<T>`, `HashCode`, and `EqualityHelpers` -- all existing patterns in the codebase. | N/A (positive) | No new patterns introduced. |
| The struct is `readonly` which matches the codebase preference for immutable value types used as dictionary keys. | N/A (positive) | Consistent. |
| Test naming follows existing `CarrierGenerationTests` conventions. | N/A (positive) | Consistent. |

## 6. Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | The change is purely an optimization in generated output. No public API contracts are modified. The generated code is `file`-scoped (internal to each generated file), so consumers never reference carrier class names directly. Carrier class name numbering may change (gaps eliminated), but these names are not part of any stable contract. All 3,193 existing tests pass per the workflow log. |

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Plan Compliance | Test 1 doesn't verify both interceptors reference Chain_0 | Low | A | Strengthen assertion |
| 2 | Plan Compliance | Test 3 uses >= 1 instead of == 2 | Medium | A | Fix to == 2 |
| 3 | Correctness | Hash uses non-deterministic dict iteration | Low | D | Ignore — false negative only |
| 4 | Correctness | Key doesn't capture ResultTypeName explicitly | Low | D | Ignore — implicit via reader code |
| 5 | Test Quality | (same as #1) | Low | A | Merged with #1 |
| 6 | Test Quality | (same as #2) | Medium | A | Merged with #2 |
| 7 | Test Quality | No edge-case tests for interfaces/collection/reader | Low | A | Implement now (user overrode from C) |

## Issues Created
- None
