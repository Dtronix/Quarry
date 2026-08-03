# Review: 305-fix-cte-outer-param-assignment

## Classifications

| ID | Class | Rec | Sev | Section | Finding | Action Taken |
|----|-------|-----|-----|---------|---------|--------------|
| F1 | D | D | L | Plan Compliance | Plan said "inline branch"; implementation factored counting into shared `GetCteInnerParamCount` helper — benign, behavior identical | dismissed (benign refinement, noted for the record) |
| F2 | B | B | M | Test Quality | Conditional-map half of fix has no alignment-observing test (conditional outer clause after parameterized CTE on MySQL is newly reachable, untested) | Added ConditionalOuterClause_AfterParameterizedCte_MySQL_NoQRY048 (MySqlBindOrderGenerationTests) — verified it fails when only the conditional-map branch is removed |
| F3 | B | B | L | Test Quality | No multi-CTE (two param-bearing Withs) + outer captured param test — cumulative offset accumulation uncovered | Added Cte_TwoChainedWiths_CapturedParams_AndOuterCapturedParam (CrossDialectCteTests) — 2 param CTEs + outer param, SQL + execution on all 4 dialects |
| F4 | B | B | L | Test Quality | Generation test regexes match anywhere in file; wrong-interceptor assignment would still pass | Tightened CteInnerAndOuterCapturedParams_NoQRY037_AssignsBothPFields to exact-count assertions (1x P0, 1x P1) |
| F5 | C | C | L | Codebase Consistency | `GetCteInnerParamCount` is the 3rd/4th copy of the CteDef-by-short-name first-match loop — consolidation opportunity | dismissed without issue — user declined filing the follow-up on 2026-08-03; consolidation of the CteDef-by-name lookup remains an open (unfiled) cleanup opportunity |

## Plan Compliance

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F1 | Plan specified the CteDefinition advance "inline" in both walks (BuildParamConditionalMap "needs the same branch inline in its loop... because GetClauseParamCount is static"); the implementation instead factors the name-match lookup into a new private instance helper `GetCteInnerParamCount` (AssembledPlan.cs:291) called from an inline branch in each walk. | L | Benign deviation — the branch is still inline per walk and only the counting is shared; behavior is identical to the plan's snippet and duplication is reduced. Noted for the record only. All three plan steps otherwise match exactly: fix + QRY037-region generation test (CarrierGenerationTests.cs:4489, inside the QRY037 region at 4349–4662), the four-dialect Prepare/AssertDialects/execute test with the specified placeholder shapes and `(3, 150.00m)` expectation, the MySQL pin named exactly as planned, and both stale QRY037 comments updated. No scope creep. |

## Correctness

No concerns.

(Verified: CteDefinition maps to a ClauseRole so its entries do appear in `GetClauseEntries()` and the new branches are reachable; branch placement before `Clause != null` is safe because CteDefinition sites always have null Clause and cannot match the set-op or UpdateSetPoco cases; multi-CTE chains accumulate correctly because clause entries walk in chain order and ChainAnalyzer assigns each `CteDef.ParameterOffset` in the same order; zero-inner-param CTEs advance 0 (unchanged behavior); a failed-analysis CTE has no CteDef, `GetCteInnerParamCount` returns 0, and the chain already carries QRY080; duplicate names are first-match, consistent with TransitionBodyEmitter.EmitCteDefinition:199-203 and already rejected by QRY082; `ExtractShortName(null)` returns null which never equals a non-null `CteDef.Name`, so no NRE; `CteEntityTypeName` is always populated for With<T> sites (UsageSiteDiscovery.cs:3784-3787, With is always generic); `RewriteMySqlBindMarkers` (PipelineOrchestrator.cs:641) treats missing conditional-map keys as active, matching the skip-without-entries choice.)

## Security

No concerns.

(The change emits no new code or SQL text; it only realigns integer offsets computed from generator-internal metadata. CTE names derive from C# type identifiers already constrained by the compiler and handled by the pre-existing quoting/emission paths.)

## Test Quality

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F2 | The `BuildParamConditionalMap` half of the fix is only structurally exercised (the branch runs for MySQL CTE chains via `RewriteMySqlBindMarkers`), but no test observes its key *alignment*: with only unconditional clauses, every param is treated as active regardless of map keys, so a misaligned offset is invisible. The shape that would observe it — a conditional outer clause (`if (x) q = q.Where(...)`) after a parameterized CTE on MySQL — was previously unreachable (QRY037) and is now newly buildable, yet remains untested. | M | A future regression in this walk would silently mislabel slots and surface only as a hard-to-diagnose QRY048/identity-bind failure (or wrong active-set validation) for conditional+CTE chains on MySQL. The plan's own working notes flagged this combination as "must be fixed consistently"; a pin for the newly reachable shape would close the loop. |
| F3 | No test covers a multi-CTE chain (two `With<T>` with inner params each) combined with an outer captured param — the shape that exercises *cumulative* offset accumulation across multiple CteDefinition entries. Existing `LambdaCte_TwoChainedWiths_CapturedParams` (LambdaCteTests.cs:104) has no outer param; the new tests cover a single CTE only. | L | The per-entry advance makes cumulative correctness nearly self-evident, but multi-CTE was listed as a design boundary condition and this newly-enabled shape (three global slots: two inner + one outer) has zero coverage on any dialect. |
| F4 | The generation test's regex assertions (`__c\.P0\s*=`, `__c\.P1\s*=`) match anywhere in the interceptor file, not scoped to the With/Where interceptor bodies respectively, and are largely implied by the QRY037-absence assertion (the self-check already guarantees every declared P-field is assigned somewhere). A regression that assigns P1 from the wrong interceptor would pass this test. | L | Low residual risk in practice — the cross-dialect and MySQL execution tests catch slot-value swaps at runtime — but the generation test's stated intent ("the outer Where interceptor must assign... not stomp P0") is stronger than what it asserts. |

## Codebase Consistency

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F5 | `GetCteInnerParamCount` is the third/fourth copy of the "resolve a CteDefinition site to its CteDef by `ExtractShortName(CteEntityTypeName ?? EntityTypeName)` first-match" loop — the same pattern exists in CarrierAnalyzer.BuildLambdaInnerExtractionPlan (CarrierAnalyzer.cs:456-462) and twice in TransitionBodyEmitter.EmitCteDefinition (TransitionBodyEmitter.cs:173-221). | L | The duplication faithfully follows the established repo pattern (and the fix's comments cross-reference it), but a shared `FindCteDef(site, cteDefinitions)` helper next to `CteNameHelpers` would give the "MUST use the same helper" doc contract in CteDef.cs:121-128 a single enforcement point. Purely a consolidation opportunity, not a defect. Otherwise the change is idiomatic: the new branch mirrors the adjacent `setOpIndex` handling shape, comment style/density matches the file, and the conditional-map branch uses the same `continue` pattern as the set-op case. |

## Integration / Breaking Changes

No concerns.

(The change is purely additive in effect: the new branch fires only for CteDefinition entries, and for every previously-buildable chain shape the computed offsets are unchanged — chains with zero-inner-param CTEs advance by 0 as before, and all shapes where the offset would differ were hard build errors (QRY037) on master. Manifest golden churn is exactly the expected additions — one new `With(...).FromCte(...).Where(...).Select(...)` Prepare entry per dialect from CrossDialectCteTests plus one MySQL ExecuteFetchAllAsync entry from the integration pin, with counts advancing accordingly (+2 MySQL, +1 elsewhere) and no modifications to any existing rendered SQL. No public API surface changed.)
