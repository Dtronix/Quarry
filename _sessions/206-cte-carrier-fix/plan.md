# Plan: 206-cte-carrier-fix

## Overview

Fix `EmitCteDefinition` so that chained `db.With<A>(...).With<B>(...)` calls properly extend a single carrier instead of each call discarding the prior carrier and creating a fresh one.

The fix has three complementary parts:

1. **Code generator change** in `TransitionBodyEmitter.EmitCteDefinition`: detect whether the current `CteDefinition` site is the *first* CTE site in the chain. The first site creates the carrier (`new Chain_N { Ctx = @this }`); every subsequent site reinterprets the incoming `@this` (which is actually the carrier from the previous `With<>` call, disguised as the context class via `Unsafe.As`) and copies its inner CTE parameters into the existing carrier's parameter slots.

2. **Compile-time diagnostic `QRY082`** for the same-DTO-twice case (`db.With<X>(a).With<X>(b)`): two CTEs sharing one short name produce an invalid SQL `WITH` clause and the existing name-based `CteDef` lookup is silently ambiguous. The diagnostic surfaces this as a clear compile error rather than silent miscompilation.

3. **Compile-time diagnostic `QRY083`** for conditional `With<>`/`FromCte<>` (sites with non-null `NestingContext`): the chain analyzer's existing CTE build loop ignores `NestingContext`, so a conditional `With<>` already produces broken SQL today (the `WITH` clause renders unconditionally, parameters aren't extracted). The first-vs-subsequent emission strategy in this fix would also introduce a new failure mode in this same latent area: if the source-order first `With<>` is conditionally skipped at runtime but a later `With<>` runs, the emitted `Unsafe.As<{carrier}>(@this)` reinterprets the real context as a carrier — undefined behavior. The diagnostic both makes the pre-existing latent bug visible AND prevents the new failure mode.

The first-vs-subsequent decision in (1) is computed *locally* inside `EmitCteDefinition` by counting `CteDefinition`-kind sites in `chain.ClauseSites` that precede the current site (matched by `UniqueId`). This avoids any IR or `FileEmitter` plumbing changes — the information is already present on the `AssembledPlan` passed to the emitter. Because (3) rejects conditional CTE sites entirely, the emitter never has to worry about runtime control-flow vs source-order divergence.

## Key Concepts

### Carrier sharing across a chain

A single `CarrierPlan` instance (one `Chain_N` file-local sealed class) is created per outer query chain and shared across **every** interceptor in that chain. The carrier class declares parameter fields `P0, P1, P2, ...` for *all* parameters in the chain — including parameters captured by inner queries that feed CTE definitions. Each `CteDef` carries a `ParameterOffset` saying where in the shared parameter array its inner-query parameters live (e.g., CTE A occupies P0–P1, CTE B occupies P2–P4).

### How `Unsafe.As` glues the chain together

Carriers are file-local sealed classes — they do **not** inherit from the user's context class (e.g., `QuarryContext`). Inter-call type morphing happens via `System.Runtime.CompilerServices.Unsafe.As<T>(o)`, which is a no-op reinterpret cast. `EmitCteDefinition` currently ends with `return Unsafe.As<{contextClass}>(__c)` so the carrier is *typed* as the context class for the next call's receiver, while the runtime object remains the carrier. The fix exploits this by using `Unsafe.As<{carrier.ClassName}>(@this)` in the second-and-subsequent emission path to recover the carrier reference, since C# overload resolution dispatched the second `With<>()` to its own carrier-aware interceptor.

### Why position-based detection is correct

`ChainAnalyzer` builds `clauseSites` and `chain.Plan.CteDefinitions` from the **same** forward iteration over the chain (lines 661–714 of `ChainAnalyzer.cs`). The Nth `CteDefinition` site in `clauseSites` corresponds to the Nth entry in `CteDefinitions` whenever inner-chain analysis succeeds. For the first-vs-subsequent decision we don't even need the index — we just need "is anything before me of kind `CteDefinition`?". Counting works regardless of inner-chain analysis failures because we count *sites*, not successfully-analyzed `CteDef` records.

## Algorithm

### Emission decision

```csharp
// Inside EmitCteDefinition, before the carrier creation line:
bool isFirstCteSite = true;
for (int i = 0; i < chain.ClauseSites.Count; i++)
{
    var s = chain.ClauseSites[i];
    if (s.UniqueId == site.UniqueId) break;        // reached current site
    if (s.Bound.Raw.Kind == InterceptorKind.CteDefinition)
    {
        isFirstCteSite = false;
        break;
    }
}

// Then:
if (isFirstCteSite)
    sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = @this }};");
else
    sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(@this);");
```

The rest of the body (inner-parameter copy from `__inner` carrier to `__c.P{ParameterOffset+i}`, and the trailing `Unsafe.As<contextClass>(__c)` return) is unchanged.

### Duplicate CTE name detection (in `ChainAnalyzer`)

After the existing forward pass that builds `cteDefinitions`, scan it once more for duplicate `Name` values. For each duplicate, emit a `QRY082` diagnostic on the location of the offending `With<>` site. Implementation goes inside the existing `for (int i = 0; i < clauseSites.Count; i++)` loop that already processes `CteDefinition` sites (lines 661–714), tracking seen names in a `HashSet<string>` and emitting on the second-or-later occurrence.

## Phases

### Phase 1 — Fix SQL placeholder rebasing in `SqlAssembler`
**Goal:** Make multi-CTE outer SQL use a single global parameter index space for placeholders. After this phase, the outer SQL's WITH clauses contain `@p0`, `@p1`, ... in the right order — though execution still crashes due to the carrier discard bug fixed in Phase 2.

**Files modified:**
- `src/Quarry.Generator/IR/SqlAssembler.cs` — modify `RenderSelectSql` (lines 146–167) so the WITH-clause embedding re-renders each inner CTE's SQL via `RenderSelectSql(cteDef.InnerPlan, mask: 0, dialect, paramBaseOffset: cteDef.ParameterOffset)` instead of using the pre-rendered `cteDef.InnerSql` raw string.
  - This requires `cteDef.InnerPlan` to be non-null. When it is null (inner-chain analysis failed), fall back to the existing `cteDef.InnerSql` raw string with a TODO/comment noting the limitation. Failed-analysis chains already get a `QRY080` diagnostic so the user is informed.
  - The inner plan's mask=0 variant is used unconditionally because the inner chain doesn't carry conditional clauses through the CTE boundary in the existing analyzer (the inner chain is analyzed as standalone, before being embedded). If a future inner chain has multiple masks, mask=0 is still its only-or-canonical variant. Document this in a code comment.
  - The `paramIndex += cte.InnerParameters.Count;` advance on line 164 stays correct.
  - The unused `paramBaseOffset` overload of `RenderSelectSql` is now used. No signature change needed.

**Tests to add or modify:** none new in this phase. The regression test introduced in Phase 3 will validate end-to-end. Verification for this phase is via:
1. All existing tests must pass (single-CTE chains use the same code path; the inner chain re-render with `paramBaseOffset = 0` produces SQL byte-identical to today's pre-rendered string).
2. Inspect a generated interceptor file for a multi-CTE chain (e.g., dump `Chain_11._sql` from the test project's `obj/GeneratedFiles/...`) and confirm the second CTE's `WHERE` clause now uses `@p1` (or `$2`) instead of `@p0`/`$1`.

**Expected state:** All existing tests green. Multi-CTE generated SQL strings now use unique placeholder names per CTE — verified by file inspection, not by a new test (the new test arrives in Phase 3 once both fixes are in place).

### Phase 2 — Fix `EmitCteDefinition` (carrier discard)
**Goal:** Implement the first-vs-subsequent CTE site detection and the differentiated carrier creation. After this phase, multi-CTE chains have a correct carrier AND correct SQL (Phase 1).

**Files modified:**
- `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — modify `EmitCteDefinition`:
  - Add the local `isFirstCteSite` count loop described in **Algorithm → Emission decision**.
  - Replace the unconditional `var __c = new {carrier.ClassName} { Ctx = @this };` with the conditional form.
  - Update the method's XML documentation to reflect that the first call creates the carrier and subsequent calls extend it.
  - Update the inline comment block on lines 128–132 — the multi-CTE caveat about ambiguity is still valid for the *same-DTO* case (handled by Phase 5), but the "discarded carrier" concern goes away.

**Tests to add or modify:** none new in this phase. Phase 3's regression test validates end-to-end after this lands. Verification:
1. All existing tests must pass (single-CTE chains hit the `isFirstCteSite == true` branch identically to today).
2. Inspect a generated interceptor file: the second `With<>` interceptor body now starts with `var __c = Unsafe.As<Chain_N>(@this);` instead of `var __c = new Chain_N { Ctx = @this };`.

**Expected state:** All existing tests green. Multi-CTE chains now have both correct SQL (Phase 1) and correct carrier behavior. Verified end-to-end by Phase 3's tests.

### Phase 3 — Regression test + comprehensive multi-CTE coverage
**Goal:** Add the multi-CTE regression test and broader coverage to validate Phase 1+2 end-to-end.

**Files modified:**
- `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — add a new `#region CTE With() chained (multiple distinct CTEs)` containing:
  1. `Cte_TwoChainedWiths_DistinctDtos_CapturedParams` — the regression test for issue #206. Two distinct CTEs (`Order`, `User`) where BOTH inner queries capture a local variable. `FromCte<Order>` as primary; outer projects `(o.OrderId, o.Total)`. Cross-dialect SQL assertions where the second CTE's WHERE clause uses a distinct parameter placeholder (`@p1`/`$2`). Executable assertion on lite checking row count and content.
  2. `Cte_ThreeChainedWiths_AllUsedDownstream` — three distinct CTEs chained (`Order`, `User`, `OrderSummaryDto`) to validate the fix generalizes beyond two. At least two of the three carry captured parameters at distinct `ParameterOffset` values. Cross-dialect assertions and executable assertions on lite.

**Tests to add or modify:** the two new tests above.

**Expected state:** All tests green including the new multi-CTE coverage. This is the first phase that validates Phase 1+2 end-to-end.

**Note:** removed the prior `Cte_TwoChainedWiths_JoinedInOuterQuery` test from this phase. After investigation, CTE-to-CTE joins require careful handling of the join API and aren't a primary need for validating issue #206's fix. If desired as additional coverage, can be added in Phase 6 or as a separate issue.

### Phase 4 — Duplicate-CTE-name diagnostic (`QRY082`)
**Goal:** Add a compile-time diagnostic for `db.With<X>(a).With<X>(b)` (two CTEs with the same short name in one chain).

**Files modified:**
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — add a new `DiagnosticDescriptor DuplicateCteName` after `FromCteWithoutWith` (lines 768–779):
  - Id: `"QRY082"`
  - Title: `"Duplicate CTE name in chain"`
  - Message format: `"Multiple With<{0}>(...) calls in the same chain produce duplicate CTE name '{0}'. Each CTE in a chain must use a distinct DTO type so the generated WITH clause has unique aliases."`
  - Category, severity, default-enabled, description matching the existing CTE diagnostics block.
  - Update the section banner comment from `(QRY080–QRY081)` to `(QRY080–QRY082)`.
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — inside the existing `for` loop at lines 661–754 that processes `CteDefinition` sites, track seen short names in a `HashSet<string>(StringComparer.Ordinal)` declared just above the loop (alongside `cteDefinitions`). On each successful add to `cteDefinitions`, attempt to add the short name to the set; if the add returns `false`, append a `DiagnosticInfo` for `DuplicateCteName` with `raw.Location` and `cteName` (mirroring the existing `CteInnerChainNotAnalyzable` and `FromCteWithoutWith` emission patterns at lines 723 and 748).

**Tests to add or modify:**
- `src/Quarry.Analyzers.Tests/...` — find the existing diagnostic-test fixture for QRY080/QRY081 (likely `CteDiagnosticTests` or similar; verify before adding) and add a `[Test]` exercising `db.With<Order>(...).With<Order>(...)` and asserting that `QRY082` is reported on the second `With<>` call. If a directory with diagnostic tests doesn't exist for QRY080/081, add the new test alongside whichever fixture exercises CTE generator output and produce the analyzer-style diagnostic assertion that matches the codebase pattern.

**Expected state:** New diagnostic test passes; QRY082 is wired through the existing deferred-diagnostics channel; all other tests remain green.

### Phase 5 — Conditional-CTE diagnostic (`QRY083`)
**Goal:** Reject `CteDefinition` and `FromCte` sites with non-null `NestingContext` at compile time. Closes the latent conditional-CTE bug and the matching new failure mode in the carrier fix.

**Files modified:**
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — add a new `DiagnosticDescriptor ConditionalCteNotSupported`:
  - Id: `"QRY083"`
  - Title: `"Conditional CTE clauses are not supported"`
  - Message format: `"With<{0}>(...) and FromCte<{0}>() must appear unconditionally in the chain. Wrapping a CTE clause in an if-block produces an invalid query because the WITH clause renders even when the call does not execute. Move the conditional logic outside the chain or branch on whole chains."`
  - Category, severity, default-enabled, description matching the existing CTE diagnostics block.
  - Update the section banner comment from `(QRY080–QRY082)` to `(QRY080–QRY083)`.
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — inside the `for (int i = 0; i < clauseSites.Count; i++)` loop at lines 661–754:
  - Before the `if (raw.Kind == InterceptorKind.CteDefinition)` branch's body executes (or symmetrically in both branches), check `if (raw.NestingContext != null && raw.NestingContext.NestingDepth > baselineDepth)` (use the same `baselineDepth` calculation as the conditional-clause loop on lines 572–574; this requires moving or duplicating that calculation, OR computing it inline here). On match, append a `DiagnosticInfo` for `ConditionalCteNotSupported` with `raw.Location` and the short DTO name. Continue processing (don't `continue;` past the CteDef build) so we still get a coherent (if broken) plan, but the diagnostic surfaces as a compile error.
  - **Subtle point:** the existing `baselineDepth` on line 574 is computed inside the same `Analyze*` method as the CTE loop, so it's already in scope. Use it directly.
  - Apply the same `NestingContext` check to the `else if (raw.Kind == InterceptorKind.FromCte)` branch (lines 729–753).

**Tests to add or modify:**
- `src/Quarry.Analyzers.Tests/...` — locate the existing CTE diagnostic test fixture (per Phase 4) and add tests:
  1. `db.With<Order>(...)` inside an `if` block — asserts `QRY083` reported.
  2. `db.FromCte<Order>(...)` inside an `if` block — asserts `QRY083` reported.

**Expected state:** New diagnostic tests pass. Existing tests remain green (no current test exercises conditional CTE — verified during DESIGN). Full suite green.

### Phase 6 — Final test sweep & comment tidy
**Goal:** One last full-suite run plus a tidy of related comments and any leftover dead notes from the bug.

**Files modified:**
- `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — ensure the lines 128–132 inline comment now correctly references `QRY082` (the new diagnostic) for same-DTO ambiguity instead of `#206`. Update the multi-CTE section to note that conditional CTE is rejected by `QRY083`.

**Tests to add or modify:** none. Run the full suite (`dotnet test`).

**Expected state:** All tests green, branch ready for REVIEW.

## Phase Dependencies

Phase 1 (SQL placeholder rebasing) and Phase 2 (carrier discard fix) are independent of each other in terms of code locations but BOTH are needed before Phase 3's tests can pass end-to-end. Sequencing: Phase 1 → Phase 2 → Phase 3 keeps each commit committable on its own (each phase keeps existing tests green) while making the regression-test-arrives-with-the-fix story clean.
Phase 4 (QRY082) is independent of Phases 1–3 but sequenced after them so the regression-fix story is told first in commit history.
Phase 5 (QRY083) is independent of Phases 1–4 but sequenced after Phase 4 so both diagnostic phases are adjacent.
Phase 6 depends on all prior phases.
