## Summary
- Closes #305
- Fixes the QRY037 build failure for CTE chains that combine a captured parameter inside the `With<T>(...)` lambda with a captured parameter in an outer clause (`Where` after `FromCte<T>()`).

## Reason for Change
The inner+outer parameterized CTE shape — valid SQL on all four dialects — was unbuildable: the generated carrier declared the outer parameter's field (`P1`) but no clause interceptor assigned it, tripping the generator's QRY037 self-check.

**Root cause** (differs from the issue's suspicion): `ChainAnalyzer`'s parameter rebasing and `SqlAssembler`'s placeholder rebasing are both correct. The defect was in `AssembledPlan.BuildSiteParamsMap` / `BuildParamConditionalMap`: both walk clause entries accumulating the global parameter offset each emitter uses to route `__c.P{n}` assignments, but a `CteDefinition` entry (whose `Clause` is null — it is not clause-bearing) advanced the offset by 0 despite owning `InnerParameters.Count` slots in the chain's parameter list. Every param-bearing clause after a param-bearing `With()` therefore resolved its carrier P-fields short by the inner-param count: the outer `Where` wrote `P0` (the CTE's slot) and `P1` was never assigned. With zero inner params the skew is zero — exactly why inner-only, two-inner, and outer-only chains all worked.

**Fix**: advance the offset by the matching `CteDef.InnerParameters.Count`, matched by CTE short name via `CteNameHelpers.ExtractShortName` (the same first-match rule as `TransitionBodyEmitter.EmitCteDefinition`; duplicate names are already QRY082 errors, failed-analysis CTEs already QRY080). All emitter paths funnel through this one map, so the single fix point covers every consumer.

## Impact
- Inner+outer parameterized CTE chains (single- and multi-CTE) now build and bind correctly on all four dialects.
- No behavior change for any previously-buildable chain: the new branch fires only for `CteDefinition` entries, and every shape whose offsets would differ was a hard QRY037 build error before.
- Also closes the #303 audit gap: the inner+outer combination was the one parameterized-CTE shape whose MySQL inner-before-outer positional bind order could not be execution-verified.

## Plan items implemented as specified
1. `AssembledPlan` offset-walk fix + generation guard (`CteInnerAndOuterCapturedParams_NoQRY037_AssignsBothPFields`) — verified to fail without the fix.
2. Cross-dialect SQL + execution test (`Cte_FromCte_InnerAndOuterCapturedParams`) — exact WITH rendering and row verification on all four backends.
3. MySQL bind-order execution pin (`ParameterizedCteInnerAndOuterParams_OnMySQL_BindsInnerBeforeOuter`), exactly as specified in #305, plus retirement of the QRY037 caveats in the sibling `ParameterizedCte*` pins.

## Deviations from plan implemented
- The planned "inline branch" in both walks was refined into a shared `GetCteInnerParamCount` helper called from an inline branch in each walk (review F1, dismissed as benign — behavior identical, duplication reduced).

## Gaps in original plan implemented
Review findings addressed post-implementation:
- **F2 (M)**: `ConditionalOuterClause_AfterParameterizedCte_MySQL_NoQRY048` — the only shape that observes the `BuildParamConditionalMap` half of the fix (a conditional outer clause after a parameterized CTE on MySQL, newly reachable). Verified to fail when only that branch is removed.
- **F3 (L)**: `Cte_TwoChainedWiths_CapturedParams_AndOuterCapturedParam` — cumulative offset accumulation across two param-bearing CTEs plus an outer captured param, on all four dialects.
- **F4 (L)**: generation guard tightened to exact-count P0/P1 assertions (the pre-fix stomp signature was two P0 assignments and zero P1).

Deferred (review F5, class C, pending confirmation): consolidating the CteDef-by-short-name first-match lookup (now present in `AssembledPlan`, `CarrierAnalyzer`, and twice in `TransitionBodyEmitter`) into a single shared helper — a cleanup, not a defect.

## Performance Considerations
The added lookup is a short linear scan over `Plan.CteDefinitions` (almost always 0–3 entries) inside two already-cached map builders — no measurable generator cost.

## Breaking Changes
- Consumer-facing: none (previously-failing shapes now build; no existing SQL or binding changes — manifest goldens show additions only).
- Internal: none (private helpers only; no signature changes).
