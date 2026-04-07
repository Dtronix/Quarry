# Review: #206 CTE carrier creation conflict for multiple With() calls

## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 1 (`SqlAssembler` rebasing) was a mid-implementation scope expansion not in the original issue. The expansion is clearly documented in `workflow.md` under the 2026-04-06 "Scope expansion" decision with a diagnosis of the second latent bug and a justification for fixing it together. The implementation matches the recorded decision: uses existing `paramBaseOffset` parameter on `RenderSelectSql` with `cte.ParameterOffset`, with the documented `mask=0` and null-plan fallback caveats. | Info | Confirms the scope creep was discussed and recorded before implementation, not slipped in silently. |
| Phase 5 originally contained QRY083 (conditional-CTE diagnostic). `workflow.md` documents the drop with rationale ("no C# expression shape can actually produce a `CteDefinition` site with `NestingDepth > baselineDepth`"). `plan.md` was updated to describe Phase 5 as a "final sweep" only. Code was cleanly removed — no dead QRY083 registry entries, descriptors, or tests remain. | Info | Plan/decisions and code are in sync; no orphaned artifacts. |
| Plan's Phase 2 algorithm at lines 36–54 specifies the detection loop exactly as implemented at `TransitionBodyEmitter.cs:125–135`. | Info | Literal plan-to-code match. |
| Plan's Phase 4 specifies the `HashSet<string>(StringComparer.Ordinal)` just above the outer loop and `DiagnosticInfo` emission at the "second-or-later occurrence." `ChainAnalyzer.cs:664` and `:687–693` match exactly. | Info | Literal plan-to-code match. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| The `isFirstCteSite` detection loop in `TransitionBodyEmitter.cs:125–135` relies on `UniqueId` equality, where `UniqueId` is a 4-byte (8 hex char / 32-bit) MD5 prefix of `filepath:line:column:methodName` (see `UsageSiteDiscovery.GenerateUniqueId`). Two distinct source positions could theoretically collide, but the keys are disjoint per location so practical collisions are not possible unless the hash happens to map two different sites to the same 32-bit value. The rest of the generator already uses `UniqueId` as an identity key (carrier lookups, chain lookups, trace logs), so this loop inherits the same collision risk as the rest of the codebase — no new exposure. | Info | Documents an inherited assumption; no additional action needed. |
| The detection loop in `TransitionBodyEmitter.cs` walks `chain.ClauseSites` and breaks on the first site whose `UniqueId` matches the current site's. If the current site is somehow missing from `ClauseSites` (shouldn't happen in practice), the loop would instead return the first prior CTE site's result — giving `isFirstCteSite = false` for what could be the first and only CTE site. Not a reachable state today, but the loop does not explicitly assert the current site was found. | Low | Defensive completeness. A single-line sanity assertion or fallback would eliminate the latent concern. |
| The placeholder-rebasing path in `SqlAssembler.cs:179–187` only handles `mask=0` for the inner CTE plan. If a future inner chain gets multi-mask conditional clauses, the outer WITH clause would use the base variant only. This is explicitly documented in the code comment and matches the plan's guidance, but no diagnostic or assertion rejects a multi-mask inner chain — it would silently use mask 0. | Low | Latent hazard if conditional clauses start appearing in CTE inner chains. A debug assertion on `cte.InnerPlan.PossibleMasks.Count == 1` would make the limitation fail-loud. |
| `SqlAssembler.cs:191` advances `paramIndex += cte.InnerParameters.Count` unchanged from before, but this is only correct because the outer `RenderSelectSql` is always called with `paramBaseOffset = 0` at top level (CTEs are not recursively nested inside set-operation operands today). If a CTE ever appeared inside a set-operation operand, `paramIndex` (starting at `paramBaseOffset`) would be reset by the inner CTE re-render at `cte.ParameterOffset`, diverging from outer `paramIndex`. Pre-existing limitation; not introduced by this PR. | Info | Documented for future reference; not a blocker. |
| The null-plan fallback path in `SqlAssembler.cs:184–187` (`cte.InnerPlan == null` → raw `cte.InnerSql`) is reachable only when inner-chain analysis failed, in which case `QRY080` is already emitted (error severity) so compilation fails before the SQL is ever used. The fallback exists only so downstream tooling (manifest emitter, diagnostics) sees a non-empty string. The SQL it produces would have the pre-fix collision bug, but since it never runs, this is benign. | Info | Confirms the fallback is inert by construction. |
| The `seenCteNames` HashSet uses `StringComparer.Ordinal`, so CTE name matching is case-sensitive. C# type names are case-sensitive so you can't express two CTE DTOs `Order` and `order` in the same compilation — case-insensitive DB collation behavior is moot. `CteNameHelpers.ExtractShortName` is the only name source and is used consistently by both the analyzer and `EmitCteDefinition`, so name comparisons stay canonical across both code paths. | Info | Correct as-is. |
| `ChainAnalyzer.cs:687–693` emits QRY082 **and still adds** the duplicate `CteDef` to `cteDefinitions`. The comment (`:683–686`) explicitly explains this is intentional so downstream code (placeholder rebasing, carrier emission) sees a coherent plan. `EmitCteDefinition`'s cteDef lookup will match the FIRST entry both times, but since QRY082 is an `Error` severity diagnostic, user code won't compile — the broken generated code never runs. | Info | Correctly implemented. |
| The `Unsafe.As<{carrier.ClassName}>(@this)` path in `TransitionBodyEmitter.cs:151` is type-safe at runtime because (a) a single `CarrierPlan` instance is shared across all sites in the chain (carrier class is identical for every CTE site), and (b) the first `With<>` already constructed the carrier and `Unsafe.As<contextClass>` reinterpret-cast it — recovering it with `Unsafe.As<{carrier}>` is a no-op. The workflow.md decision "Carrier identity & receiver typing for subsequent With() calls" documents this invariant. | Info | Safe by construction. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| Both new multi-CTE tests (`Cte_TwoChainedWiths_DistinctDtos_CapturedParams`, `Cte_ThreeChainedWiths_AllUsedDownstream`) assert SQL strings for all four dialects **and** execute against SQLite with row-count + content checks. Row-content assertions would actually fail under the pre-fix carrier-discard bug (comment at line 226–227: "With the bug, the discarded carrier would reset orderCutoff to default(decimal) = 0 and all 3 rows would match"). Assertions are meaningful, not just "no throw". | Info | Strong regression coverage. |
| Edge case not tested: a 2-CTE chain where the FIRST CTE has ZERO inner parameters and the second CTE has one or more. Both new multi-CTE tests always have parameters in every CTE, so `cte.ParameterOffset` is always non-trivial for later CTEs. A `.With<A>(db.Orders())` (no WHERE) + `.With<B>(db.Users().Where(u => u.X == captured))` would exercise the edge case where the second CTE's inner params start at offset 0 but the second `With<>` interceptor still needs to take the Unsafe.As branch. This would also validate that `RenderSelectSql(innerPlanWithNoParams, mask:0, dialect, paramBaseOffset:0)` for the first CTE produces the same output as the current pre-rendered string (and doesn't crash). | Low | Missing coverage for a parameter-offset boundary. Worth adding before merge. |
| No test covers a 2-CTE chain where only the **outer** query has a captured parameter and the CTEs have none — validating that the outer's paramIndex correctly continues from `cte.InnerParameters.Count` summed across both CTEs when both are 0. (Pre-existing single-CTE coverage handles `inner=0, outer=1` but not multi-CTE.) | Low | Minor coverage gap. |
| The QRY082 test (`Cte_TwoWiths_SameDto_EmitsQRY082`) matches the existing QRY080/QRY081 test shape: same structure (source string + `CreateCompilation` + `RunGeneratorWithDiagnostics`), same assertion pattern (`diagnostics.FirstOrDefault(d => d.Id == "QRY08x")`), same QRY900-exclusion check. | Info | Consistent with existing conventions. |
| The QRY082 test does not verify WHICH site the diagnostic points to. The plan text implies it should be on "the second `With<>` call" (line 123) and the implementation keys on `raw.Location` for the duplicate entry (which is the second call). A `qry082.Location.GetLineSpan()` assertion would pin this down and prevent a regression where the diagnostic fires on the first call. | Low | Location precision isn't currently verified. |
| The 3-CTE test uses the same expected result as the 2-CTE test (same rows from the `Order` CTE), so the third CTE's effect on result isn't directly observed — only its SQL shape is. Since the outer query projects only from `Order`, the other CTEs exist purely to exercise the emission/SQL paths. This is explicitly noted in the test comment and is acceptable for what the test claims to validate (generalization, not cross-CTE join semantics). | Info | Acceptable; properly scoped. |
| Manifest snapshots were regenerated for all four dialects with identical new entries (cross-verified via grep). `Total discovered` counts bumped consistently (+7 rows per dialect corresponding to new tests + helper sites). | Info | Snapshot consistency verified across dialects. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY082 descriptor declaration in `DiagnosticDescriptors.cs:785–795` matches the exact shape of QRY080/QRY081 above it (id, title, messageFormat, category, severity, isEnabledByDefault, description). | Info | Consistent. |
| QRY082 diagnostic emission in `ChainAnalyzer.cs:687–693` uses the same `diagnostics?.Add(new DiagnosticInfo(...))` pattern as `CteInnerChainNotAnalyzable` (:739–742) and `FromCteWithoutWith` (:764–767) in the same file. | Info | Consistent. |
| QRY082 registration in `QuarryGenerator.cs:771` sits alongside the other CTE deferred descriptors in the existing `s_deferredDescriptors` collection — no new plumbing. | Info | Consistent. |
| The section banner comment in `DiagnosticDescriptors.cs:753` was properly updated from `(QRY080–QRY081)` to `(QRY080–QRY082)` per the plan. | Info | Consistent. |
| `TransitionBodyEmitter.EmitCteDefinition` XML doc comment (lines 92–104) was updated to describe the first-vs-subsequent emission paths as the plan required. | Info | Consistent. |
| The inline comment block at `TransitionBodyEmitter.cs:164–168` was updated to reference QRY082 instead of leaving the old ambiguity warning — the "ambiguity" concern is now rejected at compile time. | Info | Consistent. |
| The SQL rebasing block at `SqlAssembler.cs:162–187` has a long explanatory comment (~20 lines) consistent with the surrounding code's commenting style and explaining the `mask=0` rationale, the fallback behavior, and the collision scenario. | Info | Above-average comment quality. |
| The `isFirstCteSite` loop could alternatively be expressed as `chain.ClauseSites.Take(...).Any(...)` or similar LINQ, but the imperative loop with early `break` matches the existing codebase convention (the rest of `TransitionBodyEmitter` uses raw for loops). | Info | Consistent with codebase idioms. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| No public API signatures changed. All modifications are internal (generator implementation, internal emission). | Info | No external break. |
| Pre-existing single-CTE chains hit `isFirstCteSite == true` exactly as before and the placeholder rebasing with `paramBaseOffset = 0` produces byte-identical output for the single-CTE case (verified indirectly by the fact that all pre-existing CTE tests' expected SQL strings are unchanged in the diff). | Info | Fully backwards-compatible. |
| Manifest regeneration is consistent across all four dialect snapshots (sqlite, postgresql, mysql, sqlserver). Each manifest gained identical structural entries; `Total discovered` increments by the same +7 on each. | Info | Snapshot consistency confirmed. |
| `QRY082` is Error severity. Any user with a pre-existing (broken) `db.With<X>(...).With<X>(...)` chain in their codebase will go from silently-miscompiled runtime SQL failure to a compile-time error. This is a behavior change but strictly an improvement — the prior code path was already broken at runtime. | Info | Safe behavior change (failing loud instead of silent). |
| No migration steps required for consumers. | Info | — |

## Classifications

| Finding | Section | Class | Action Taken |
|---|---|---|---|
| Missing edge-case test: 2-CTE chain where first CTE has zero inner params, second has N | Test Quality | A | Add `Cte_TwoChainedWiths_FirstEmptySecondCaptured_CapturedParam` to CrossDialectCteTests.cs |
| QRY082 test does not verify diagnostic location points at second With<> | Test Quality | A | Extend `Cte_TwoWiths_SameDto_EmitsQRY082` with a `Location.GetLineSpan()` assertion |
| SQL rebasing silently uses mask=0 for multi-mask inner CTE plans | Correctness | A | Add a debug assertion `cte.InnerPlan.PossibleMasks.Count == 1` in `SqlAssembler` CTE loop |
| `isFirstCteSite` loop does not assert current site exists in ClauseSites | Correctness | A | Add a sanity check after the loop in `TransitionBodyEmitter.EmitCteDefinition` |
