# Plan: Fix #268 — chained-`With<>` dispatch (latent / hardening)

## Overview

The bug as described in #268 — chain dispatch resolving the wrong closure-field extractor by structural shape — is not reproducible against the current `master`. Audit details are in `workflow.md` Decisions; the short version is that `CarrierStructuralKey` (FileEmitter.cs:917) already keys on the full extractor list (which embeds `VariableName` and `DisplayClassName` per-clause via `CapturedVariableExtractor.Equals`), and `BuildExtractionPlans` (CarrierAnalyzer.cs:252-426) sources every extractor from its own clause site without any first-name-wins coalescing. With the workaround comment reverted, the generator emits Chain_3 (`orderCutoff` + `activeFilter`, `<>c__DisplayClass3_0`) and Chain_4 (`orderCutoff` + `activeFilter` + `qtyFilter`, `<>c__DisplayClass4_0`) as expected — no MissingFieldException to surface.

This work therefore lands as **lock-in + regression coverage**, not as a behavioral fix:

1. Revert the workaround in `Cte_TwoChainedWiths_DistinctDtos_CapturedParams` and delete the comment that references the (now-fixed) issue.
2. Add a generator-pipeline regression test that exercises the exact failure pattern from #268: two `.With<>().With<>()` chains in the same compilation unit whose closures have the same structural shape but different variable names. Assert each call site's interceptor reads its own closure variables — i.e., the emitted `__ExtractVar_*` references match the actual `<>c__DisplayClassN_M` field set.
3. Add focused unit tests on `CarrierStructuralKey` (FileEmitter.cs:917) that lock the dedup invariant: same-shape carriers must NOT merge when (a) extractor `VariableName` differs, or (b) extractor `DisplayClassName` differs. These run independent of the full pipeline — they pin the invariant directly.
4. Tighten the `CarrierStructuralKey` doc comment to state explicitly that `VariableName` and `DisplayClassName` are part of the dedup contract — so a future contributor optimizing carrier dedup does not relax it to types-only and re-introduce #268.

There is no production code change beyond the doc comment; all behavior continues unchanged. The risk is contained.

## Key concepts (for reviewers)

- **Carrier**: Generated `Chain_N` class that holds query state (parameters, mask) and implements query-builder interfaces for one chain.
- **Extractor**: `[UnsafeAccessor]` extern method on the carrier that reads a captured field off the C# compiler-generated closure type (`<>c__DisplayClass*`). Named `__ExtractVar_{varName}_{clauseIndex}`.
- **Carrier dedup**: Two structurally identical carriers (same fields, mask, extractors, SQL variants, reader code, interfaces) emit a single class definition; subsequent chains reuse the existing class name. Implemented via `CarrierStructuralKey` in FileEmitter.cs:917.
- **`CapturedVariableExtractor`**: Models/CapturedVariableExtractor.cs — equality covers `MethodName`, `VariableName`, `VariableType`, `DisplayClassName`, `CaptureKind`, `IsStaticField`. The dedup invariant relies on this — if any of those bits differ, dedup must NOT merge.

## Phase 1 — Add `CarrierStructuralKey` unit tests (lock the dedup invariant)

`CarrierStructuralKey` is currently a private nested struct inside `FileEmitter`. To unit-test it directly we have two options:

A. Make it `internal` (keep nested) and `[InternalsVisibleTo]` Quarry.Tests. The struct is already simple — exposing it to tests does not leak production surface.

B. Construct full `CarrierPlan` + `AssembledPlan` instances and run the dedup loop indirectly. This requires plumbing through a lot of the IR.

We will go with **Option A** — minimal change. `Quarry.Generator` already exposes internals to other test-side projects (verify by grep before editing).

Tests live in `src/Quarry.Tests/IR/` (where `CarrierAnalyzerTests.cs` already lives) in a new file `CarrierStructuralKeyTests.cs`:

- **Test 1 — same-shape carriers with different variable names DO NOT merge.** Build two `CarrierPlan` instances with identical `CarrierField`s (P0/decimal, P1/bool), identical SQL variants, identical interfaces, identical reader code, but extractor lists differing only in `VariableName` (`cutoff` vs `orderCutoff`). Build the keys; assert `key1.Equals(key2) == false` and `key1.GetHashCode()` may collide (acceptable) but `Equals` returns false.
- **Test 2 — same-shape carriers with different display-class names DO NOT merge.** Same idea but vary `DisplayClassName` (`<>c__DisplayClass3_0` vs `<>c__DisplayClass4_0`).
- **Test 3 — fully identical carriers DO merge.** Sanity check: two byte-identical inputs produce equal keys.

The test file references `Quarry.Generators.CodeGen` types and `Quarry.Generators.Models` types, mirroring the existing `CarrierAnalyzerTests.cs` style.

### Tests to add or modify in this phase

- New file: `src/Quarry.Tests/IR/CarrierStructuralKeyTests.cs` — three tests as above.
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — change `CarrierStructuralKey` access modifier from private to internal (no behavioral change), and tighten its `<summary>` doc comment.

## Phase 2 — Add full-pipeline regression test

A generator-pipeline test in the existing `CarrierGenerationTests.cs` style covers the end-to-end #268 failure pattern. Place it in the `Cte_*` region near line 2370 where existing CTE tests live:

```csharp
[Test]
public void Cte_ChainedWiths_DistinctClosureNames_DoNotCollide()
{
    // Regression for #268. Two methods each chain two .With<>(...).With<>(...) calls
    // with closure shapes that match in structure (decimal + bool) but differ in
    // variable names. Each method's chain must dispatch to its OWN carrier whose
    // [UnsafeAccessor] extractors target its own <>c__DisplayClass.
    var source = SharedSchema + @"...";
    var compilation = CreateCompilation(source);
    var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

    Assert.That(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);

    var code = ...; // Interceptors file content
    // Each method's first With_X interceptor should read the matching variable name
    // from a DisplayClass that contains that field. Two assertion families:
    //  (a) The set of __ExtractVar_*_0 method declarations on the carriers is
    //      { __ExtractVar_orderCutoff_0, __ExtractVar_orderTotal_0 } — both present.
    //  (b) For each Chain_N that declares __ExtractVar_orderCutoff_0, its UnsafeAccessorType
    //      attribute must reference a DisplayClass that does NOT contain the other method's
    //      DisplayClass number. (Use regex on the emitted class to extract DisplayClassN_M
    //      and confirm both display classes appear in distinct carriers.)
}
```

The synthetic source defines two test-class methods, each with two `.With<>(lambda).With<>(lambda)` calls capturing two locals of shape `(decimal, bool)`. Variable names differ: method A uses `orderCutoff` + `activeFilter`, method B uses `orderTotal` + `enabled`. The test asserts:

1. No generator errors.
2. The interceptors file contains both `__ExtractVar_orderCutoff_0` and `__ExtractVar_orderTotal_0` declarations.
3. The interceptor body for method A's first `With` references `Chain_X.__ExtractVar_orderCutoff_0(__target)` where `Chain_X`'s `[UnsafeAccessorType]` is method-A's `<>c__DisplayClass`. (And symmetrically for method B.)

The exact assertion form will be determined while writing — we may pattern-match the emitted text with regex or substring searches. The goal is to catch any future regression where dedup or dispatch starts collapsing same-shape closures by name.

A second regression test exercises the **shape-collision-with-third-chain** axis from the original issue: add a `Cte_ThreeChainedWiths`-style method to the same compilation alongside the two-Withs methods, so the chain registry has more entries and the carrier numbering is dense. Assert all three methods still dispatch correctly.

### Tests to add in this phase

- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` — two new `[Test]` methods in the CTE region.

## Phase 3 — Revert the workaround in `Cte_TwoChainedWiths_DistinctDtos_CapturedParams`

In `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs:216`, the test currently has a 10-line comment explaining the workaround and uses `cutoff` instead of `orderCutoff`. Now that the regression tests cover the failure pattern, restore the descriptive name:

- Replace `decimal cutoff = 100m;` → `decimal orderCutoff = 100m;`
- Replace all 4 occurrences of `cutoff)` (in the `.Where(o => o.Total > cutoff)` predicates across Lite/Pg/My/Ss) → `orderCutoff)`.
- Delete the workaround comment block (lines ~221-230).
- Update the seed-data comment further down that says "// With cutoff = 100" → "// With orderCutoff = 100".
- Update the line that says "would reset cutoff to default(decimal)" → "would reset orderCutoff to default(decimal)".

After this edit, the only reference to `cutoff` in the file is in `Cte_FromCte_CapturedParam` at line 74, which is the legitimate single-CTE captured-param test and predates the workaround.

### Tests to modify in this phase

- `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — revert variable rename.

(I have already locally applied this revert during DESIGN to confirm the bug is latent — Phase 3 will be a no-op edit if my dev-tree state survives, or will redo the edit otherwise. Either way the commit captures the final correct state.)

## Phase 4 — Tighten `CarrierStructuralKey` doc comment

In `src/Quarry.Generator/CodeGen/FileEmitter.cs:913-916`, the existing doc comment reads:

```csharp
/// <summary>
/// Structural key for carrier class deduplication. Two carriers with the same key
/// produce identical class text (modulo class name) and can share a single definition.
/// </summary>
```

Replace with an expanded doc block that names `VariableName` and `DisplayClassName` as load-bearing parts of the key, and references issue #268 so a future contributor optimizing the dedup understands what relaxation would re-introduce.

This phase ships in the same commit as Phase 1 (since Phase 1 already touches the same file and adds related tests).

## Phase ordering and commits

- **Commit A — Phase 1 + Phase 4**: doc comment + access-modifier change in `FileEmitter.cs` + new `CarrierStructuralKeyTests.cs`. One self-contained change.
- **Commit B — Phase 2**: new generator-pipeline regression tests in `CarrierGenerationTests.cs`.
- **Commit C — Phase 3**: revert workaround in `CrossDialectCteTests.cs`.

Phases are independent. A and B both depend on the existing generator behavior already being correct (verified in DESIGN). Phase C does not depend on A or B but is most useful after them, since the regression tests guarantee the latent bug is locked out before the workaround comes off.

Each commit will include the corresponding `_sessions/268-fix-chained-with-dispatch/` snapshot.

## Out of scope

- No change to dispatch logic (`CarrierAnalyzer`, `FileEmitter`, `InterceptorRouter`).
- No change to `CapturedVariableExtractor` or other models.
- No new diagnostic codes.
- The `CarrierStructuralKey` is not promoted to a public API — only `internal` for tests.
