# Plan: 305-fix-cte-outer-param-assignment

## Background

A CTE chain combining a captured param inside `With<T>(...)` with a captured param in an
outer clause fails the build with QRY037: the carrier declares the outer param's field
(`P1`) but no interceptor body assigns it.

The chain's parameter model is correct end-to-end: `ChainAnalyzer` places CTE inner params
at global slots `[CteDef.ParameterOffset, ParameterOffset + InnerParameters.Count)` and
remaps outer clause params to follow them; `SqlAssembler` rebases inner placeholder
rendering with `paramBaseOffset: cte.ParameterOffset`; the carrier declares one `P{n}`
field per global slot. The defect is confined to `AssembledPlan`'s cached site-parameter
walk, which every emitter consults to decide *which* `P{n}` a clause interceptor assigns.

## Key concept: the offset walk

`AssembledPlan.BuildSiteParamsMap()` iterates `GetClauseEntries()` in chain order,
maintaining `globalParamOffset` — the first global slot owned by the current site. Each
entry advances the offset by the number of slots it owns (translated clause params,
UpdateSetPoco columns, set-operation operand params, UpdateSetAction params, Select
projection params). A `CteDefinition` entry owns `InnerParameters.Count` slots but has
`Clause == null` (CteDefinition is not clause-bearing in `CallSiteTranslator`), so it
falls through every case and advances the offset by 0. Every param-bearing clause after
it then receives an offset short by the total inner-param count, and
`CarrierEmitter.EmitCarrierClauseBody` emits `__c.P{offset+i} = ...` against the wrong
slots: the outer Where writes `P0` (stomping the CTE copy) and `P1` stays unassigned →
QRY037. Zero inner params ⇒ zero offset error ⇒ why inner-only / outer-only / two-inner
shapes all work.

`BuildParamConditionalMap()` walks the same way via `GetClauseParamCount` and has the
identical gap. Its consumer (`PipelineOrchestrator.RewriteMySqlBindMarkers`) treats
missing keys as unconditional/active, so CTE slots need no map entries — only the offset
must advance so post-CTE clause params are keyed at their true slots.

## Algorithm: the fix

In both walks, add a `CteDefinition` case that advances the offset by the matching
`CteDef.InnerParameters.Count`, matched by CTE short name — the same first-match-by-name
rule `TransitionBodyEmitter.EmitCteDefinition` uses (duplicate names are already compile
errors via QRY082; a failed-analysis CTE has no CteDef and the chain is already a QRY080
error, so advancing 0 there is consistent):

```csharp
else if (clause.Site.Kind == Models.InterceptorKind.CteDefinition)
{
    var cteName = CteNameHelpers.ExtractShortName(
        clause.Site.Bound.Raw.CteEntityTypeName ?? clause.Site.EntityTypeName);
    foreach (var cte in Plan.CteDefinitions)
    {
        if (cte.Name == cteName)
        {
            globalParamOffset += cte.InnerParameters.Count;
            break;
        }
    }
}
```

Placement: before the generic `clause.Site.Clause != null` case is not required
(CteDefinition sites always have null Clause) but the branch order must keep the existing
cases untouched. `BuildParamConditionalMap` needs the same branch inline in its loop
(alongside the existing set-operation `continue` case) because `GetClauseParamCount` is
static and has no access to `Plan.CteDefinitions`.

## Steps

- [x] **Step 1 — Fix `AssembledPlan` offset walk + generation guard test.**
  Modify `src/Quarry.Generator/IR/AssembledPlan.cs`: add the CteDefinition advance to
  `BuildSiteParamsMap` and `BuildParamConditionalMap` (as an inline branch mirroring the
  set-op handling). Add a generation test in
  `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` (QRY037 region): compile the
  issue #305 chain shape (captured inner + captured outer param) via the real generator
  pipeline; assert no QRY037 diagnostic and that the generated interceptor source assigns
  both `__c.P0` (CTE copy in the With interceptor) and `__c.P1` (outer Where interceptor).
  Run the full Quarry.Tests suite. Commit.
  Tests: new generation test; existing suite green (esp. `CrossDialectCteTests`,
  `MySqlBindOrderGenerationTests`, QRY037 self-check tests).

- [x] **Step 2 — Cross-dialect SQL + execution test.**
  Add to `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs`: inner+outer captured
  params (`With<Order>(orders => orders.Where(o => o.Total > threshold)).FromCte<Order>()
  .Where(o => o.OrderId >= minId).Select(...)`) built on all four dialects with
  `Prepare()`, `AssertDialects` on the rendered WITH statement (`@p0/@p1`, `$1/$2`, `?/?`,
  `@p0/@p1`), then execute-and-verify rows on all four backends (expect only order 3:
  `(3, 150.00m)`). Run suite. Commit.
  Tests: the new cross-dialect test itself.

- [ ] **Step 3 — MySQL bind-order execution pin.**
  Add `ParameterizedCteInnerAndOuterParams_OnMySQL_BindsInnerBeforeOuter` to
  `src/Quarry.Tests/Integration/MySqlIntegrationTests.cs` exactly as specified in issue
  #305 (companion to the three #304 `ParameterizedCte*` pins; same seed data). Also update
  the now-stale comments in `ParameterizedCteFilter_OnMySQL_BindsInnerParamsBeforeOuter` /
  `ParameterizedCteTwoInnerParams_OnMySQL_BindsBothInTextOrder` that describe QRY037 as
  blocking this shape. Run suite. Commit.
  Tests: the new MySQL integration test.

Dependencies: Step 1 unblocks 2 and 3 (the shape doesn't build before the fix). Steps 2
and 3 are independent of each other.
