# Review: branch `268-fix-chained-with-dispatch` (PR pending)

## Classifications
| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---|---|---|---|---|---|
| 1 | Correctness | Dead `displayClassMatch` regex variable in pipeline test | low | A | A | Removed; pipeline test rewritten around `AssertEveryDispatchArrowResolves` helper. |
| 2 | Correctness | `Does.Not.Contain("orderTotal")` assertion is tautological — display-class names never embed variable names | medium | A | A | Replaced with dispatch-arrow consistency check that verifies every `Chain_N.__ExtractVar_X(__target)` reference resolves to an extractor `X` actually owned by `Chain_N`. |
| 3 | Correctness | `TwoVsThreeWiths` test doesn't pressure dedup (different SQL/fields already differ) | low | A | A | Replaced with `ChainedWith_SameNamesDifferentMethods_DispatchByDisplayClass` — same names + types in both methods, only the host method (DisplayClass) differs. |
| 4 | Correctness | `carrierBlocks.Count >= 2` is a soft floor (wording nit) | nit | D | D | Dismissed as wording nit. |
| 5 | Correctness | `CarrierStructuralKeyTests` only pins extractor axis; SqlVariants identical | nit | D | D | Dismissed; test narrowness matches stated intent. |
| 6 | Test Quality | Pipeline test does not assert interceptor body links Chain_X to MethodA's display class (plan required this) | medium | A | A | `AssertEveryDispatchArrowResolves` walks every `With_*` interceptor body and asserts the carrier it dispatches to actually owns the named extractor. |
| 7 | Test Quality | Subtle PR #266 variant (correct carrier count, wrong Name↔DisplayClass pairing) not pinned | medium | A | A | Same fix as #6 — dispatch-arrow check fails on this variant. |
| 8 | Test Quality | Missing unit-test row for `VariableType` axis on `CarrierStructuralKey` | low | A | A | Added `DifferentVariableTypes_DoNotMerge` test (decimal vs int). |
| 9 | Test Quality | Synthetic source realism — confirmation only | nit | D | D | |
| 10 | Codebase Consistency | No `#region` wrapper in `CarrierStructuralKeyTests.cs` (3 tests, flat) | nit | D | D | |
| 11 | Codebase Consistency | New region uses ASCII rule banner — matches convention | nit | D | D | |
| 12 | Codebase Consistency | `internal` flip on `CarrierStructuralKey` ctor scoped narrowly — confirmation | nit | D | D | |
| 13 | Codebase Consistency | XML doc style consistent — confirmation | nit | D | D | |
| 14 | Integration | Access change does not enlarge public surface — confirmation | nit | D | D | |
| 15 | Integration | No new deps / project changes — confirmation | nit | D | D | |

## Plan Compliance

No concerns.

(All four planned items shipped as scoped: Phase 1 — `CarrierStructuralKey` access modifier flipped from `private` to `internal` on both the struct and its constructor (FileEmitter.cs:927, 939), three unit tests added in `CarrierStructuralKeyTests.cs`. Phase 2 — two regression tests added in the new `// ── Chained-With dispatch (issue #268) ───` region in `CarrierGenerationTests.cs:3698`. Phase 3 — workaround comment block and `cutoff` rename reverted in `CrossDialectCteTests.cs`. Phase 4 — doc-comment expanded with the named load-bearing fields and #268 reference. Commit split matches plan: A = doc + access + unit tests, B = pipeline tests, C = workaround revert. No extra scope detected.)

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| `ChainedWith_SameShapeDifferentNames_DispatchToOwnClosure` (CarrierGenerationTests.cs:3805-3807) computes a `displayClassMatch` regex result and never reads it; the variable is dead code that obscures intent. | low | Reviewer / future maintainer will assume the value is asserted somewhere. Either remove or wire into an assertion. |
| `ChainedWith_SameShapeDifferentNames_DispatchToOwnClosure` (CarrierGenerationTests.cs:3816) asserts `orderCutoffDisplayClass` `Does.Not.Contain("orderTotal")`. The C# compiler names display classes `<>c__DisplayClass{ordinal}_{depth}` — they never embed captured-variable names — so this assertion is tautological and would not fail in the original `#268` failure mode (where the display-class string was numerically wrong, e.g. `<>c__DisplayClass1_0` referenced from a carrier owning `MethodA`'s extractor). To actually catch the bug, the test should pin the orderCutoff carrier's display-class number to the same one the MethodA `[InterceptsLocation]` body references, or compare it against the orderTotal carrier's display class and assert inequality. | medium | The test family's headline correctness check silently passes for the failure mode it claims to cover. |
| `ChainedWith_TwoVsThreeWiths_DoNotShareCarrier` (CarrierGenerationTests.cs:3821) compares a 2-With chain (2 fields, 2 extractors, distinct SQL) against a 3-With chain (3 fields, 3 extractors, distinct SQL). Field count and SQL already differ, so dedup cannot collapse them on shape grounds — the `Intersect ... Is.Empty` assertion would also pass under a (hypothetical) buggy types-only dedup. The comment at line 3911-3912 acknowledges this. The test is a coverage placeholder rather than a regression catch for #268. | low | The test still serves as smoke coverage for the 2-vs-3 boundary, but it does not exercise dedup pressure; consider adding a variant where the third With's variable shares name + type with one of the first two so SQL still differs but the "shape collision" axis is real. |
| `ChainedWith_SameShapeDifferentNames_DispatchToOwnClosure` `carrierBlocks.Count, Is.GreaterThanOrEqualTo(2)` (line 3776) is a soft floor: a regression that collapsed both methods into a single shared carrier would still satisfy `>= 2` if any unrelated `Chain_N` class were also emitted (e.g., from a CTE inner). The follow-on `Intersect ... Is.Empty` assertion is the actual non-merge gate — that one is sound, so this is only a wording nit. | nit | Reads tighter as `carrierBlocks.Count >= 2` plus a separate "MethodA's orderCutoff and MethodB's orderTotal must live in distinct carriers" check, which is what the next assertion already does. |
| `CarrierStructuralKeyTests` constructs `Dictionary<int, AssembledSqlVariant>` with a single entry `[0] = ("SELECT 1", 0)` for both `keyA` and `keyB`. `CarrierStructuralKey.Equals` calls `EqualityHelpers.DictionaryEqual(_sqlVariants, ...)`. If a future contributor relaxes the equality of `AssembledSqlVariant` or removes that check, the unit tests still pass because `_sqlVariants` is byte-identical. The tests therefore pin only the extractor-list contribution, not the broader composite key — but this matches the test's stated intent ("differ only in extractor names/display-classes"). Acceptable as written but worth flagging that a single-axis test does not guard the whole key. | nit | Plan said "lock the dedup invariant" specifically for VariableName/DisplayClassName — the tests honour that, just narrowly. |

## Security

No concerns.

(Generator-only change. No new external input paths, no I/O surface, no deserialization, no reflection that wasn't already there. The access-modifier flip on `CarrierStructuralKey` only affects in-process visibility within the generator assembly via `[InternalsVisibleTo("Quarry.Tests")]` already present in `Quarry.Generator.csproj:32`.)

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| The pipeline regression test does not assert on the *interceptor body* — only on the carrier class declarations. The original #268 failure mode was MissingFieldException at `.Prepare()`, which is a runtime-only failure where the carrier's extractor names looked plausible in source but pointed at a foreign closure. A direct guard would be: extract MethodA's `[InterceptsLocation]` body, find the `Chain_X.__ExtractVar_orderCutoff_0(__target)` reference, and assert `Chain_X` is the carrier whose `[UnsafeAccessorType]` matches MethodA's `<>c__DisplayClass`. The current test stops one step short of that linkage. | medium | The plan (Phase 2 bullet 3, "The interceptor body for method A's first `With` references `Chain_X.__ExtractVar_orderCutoff_0(__target)` where `Chain_X`'s `[UnsafeAccessorType]` is method-A's `<>c__DisplayClass`") explicitly required this dispatch-arrow assertion; the implementation skipped it. |
| The "two methods, same shape, different names" failure mode IS exercised in `ChainedWith_SameShapeDifferentNames_DispatchToOwnClosure`, but the assertion that distinguishes a buggy carrier merge from correct distinct emission is `orderCutoffCarriers.Intersect(orderTotalCarriers), Is.Empty`. That is sufficient to detect a full-merge regression (both methods routed to the same Chain_N). It does NOT detect the more subtle PR #266 bug variant where two distinct carriers exist but one carrier's `[UnsafeAccessor]` got the wrong VariableName from the other method's closure (e.g., Chain_3 has extractor `Name = "cutoff"` while its `[UnsafeAccessorType]` is MethodA's display class which has field `orderCutoff`). For that variant, the assertion would pass — both `__ExtractVar_orderCutoff_0` and `__ExtractVar_orderTotal_0` would still appear, just on the wrong carriers. | medium | This is the exact mis-dispatch shape from the original report, and the regression test does not pin against it. Pair the carrier-set check with a per-carrier `Name = "..."` ↔ display-class consistency check. |
| The unit tests in `CarrierStructuralKeyTests` are tight on the named axes (variable name, display class) but do not include a negative case for `VariableType` differing — also part of `CapturedVariableExtractor.Equals` and explicitly named in the doc comment as load-bearing. A 4th test confirming `(decimal vs int) → not equal` would close the matrix. | low | Doc comment promises `VariableType`, `CaptureKind`, `IsStaticField` are all part of the contract; tests only pin two of the five extractor axes. |
| Synthetic source for both pipeline tests uses `await db.With<Order>(...).With<User>(...).FromCte<Order>().Select(...).ExecuteFetchAllAsync();` — this DOES exercise the chained-With dispatch path the bug touches; sources are realistic. Compilation goes through `CreateCompilation` + `RunGeneratorWithDiagnostics` which mirrors the surrounding pattern in this fixture. | nit | (Confirming sources realism — no concern, just noting verification.) |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| `CarrierStructuralKeyTests.cs` follows existing `CarrierAnalyzerTests.cs` style (`namespace Quarry.Tests.IR`, `[TestFixture]`, NUnit `Assert.That`). However, where `CarrierAnalyzerTests` uses `#region` blocks per logical group, `CarrierStructuralKeyTests` has no regions — three flat tests. This is a stylistic divergence that's negligible at three tests but starts diverging if more are added. | nit | Easy to align with a single `#region Dedup invariant` wrapper if future tests grow the file. |
| The two new regression tests in `CarrierGenerationTests.cs` use the `// ── ... ───` ASCII rule banner on line 3698, matching the existing convention at lines 1817, 2053, 3450. The region structure is consistent with the surrounding file. Helper methods (`CreateCompilation`, `RunGeneratorWithDiagnostics`) and the regex pattern `file sealed class (Chain_\d+).*?^\}` with `Singleline | Multiline` reuse the same idioms as adjacent tests. | nit | (Confirming consistency — no concern.) |
| The `internal CarrierStructuralKey(...)` constructor change matches the surrounding container's convention: `FileEmitter` is `internal sealed class`, and other nested helper types in the same file (e.g., `QueryPlanReferenceComparer` at line 906 is `private sealed class`) are kept private when they're not test-targeted. Promoting only `CarrierStructuralKey` is a minimal, targeted relaxation justified by the test it enables. The doc comment explicitly names the testing rationale. | nit | Reasonable scoping — no concern. |
| Doc-comment style on `CarrierStructuralKey` uses `<para>` and `<see cref>` tags consistent with the surrounding XML doc style in the file (e.g., `CapturedVariableExtractor.cs` uses `<see cref>` and `<c>` similarly). The `&lt;&gt;` escapes inside `With&lt;&gt;` are correctly escaped for XML. | nit | (Confirming style — no concern.) |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| `CarrierStructuralKey` and its constructor moved from `private` to `internal`. Containing class `FileEmitter` is `internal sealed class`, and `Quarry.Generator.csproj:32` already declares `[InternalsVisibleTo Include="Quarry.Tests"]`. The struct is only reachable through the `internal` `FileEmitter` (still `internal`), so this does not enlarge the assembly's public surface. No other assembly than `Quarry.Tests` can reach the new symbol. | nit | Confirms the access change is safe — no public API impact. |
| No new dependencies, no new NuGet refs, no project file changes outside the unaffected `.csproj` settings. The `_sessions/...` markdown files are workflow artefacts and do not ship. | nit | Confirms no integration impact. |

## Issues Created
- (none yet)
