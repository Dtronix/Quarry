# Workflow: generator-review-fixes

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 1
phases-total: 8
phases-complete: 6

## Problem Statement
A comprehensive 6-agent review of `Quarry.Generator` (~35K lines, 80+ files) identified correctness bugs, performance hotspots, structural duplication, caching issues, and extensibility bottlenecks.

**Critical bugs (C1-C5):**
- C1: SubqueryExpr selector parameters not extracted (`SqlExprClauseTranslator.cs:21-46`)
- C2: SubqueryExpr ImplicitJoins dropped during parameter extraction (`SqlExprClauseTranslator.cs:28-38`)
- C3: AssembledPlan.Equals omits 8 codegen-significant properties (`AssembledPlan.cs:153-166`)
- C4: QueryParameter.Equals omits 4 codegen-relevant properties (`QueryPlan.cs:444-463`)
- C5: CollectParameters misses RawCallExpr arguments (`SqlExprRenderer.cs:58-104`)

**High-severity performance (H1-H3):**
- H1: GetClauseEntries() allocates new list on every call (10+ per chain)
- H2: ResolveSiteParams() is O(N^2) in chain length
- H3: BuildParamConditionalMap() recomputed 3-4 times per terminal

**High-severity reliability (H4, H8-H9):**
- H4: ConsumedLambdaInnerSiteIds ThreadStatic side-channel data leak risk
- H8: Duplicated eligibility validation between CarrierEmitter and FileEmitter
- H9: PipelineErrorBag.DrainErrors() semantics are fragile

**High-severity caching (H5-H7):**
- H5: EntityRegistry all-or-nothing invalidation
- H6: .Collect() on raw call sites forces full re-enrichment
- H7: SqlExpr node hash functions exclude equality-significant properties

**Medium consolidation (M1-M12):**
- M1: ProjectionAnalyzer parallel WithPlaceholders/WithMetadata hierarchy (~400 lines duplication)
- M2: ChainAnalyzer CTE parameter remapping duplicated
- M3: ChainAnalyzer AnalyzeOperandChain duplicates 80% of AnalyzeChainGroup
- M4: UsageSiteDiscovery DiscoverPostJoinSites/DiscoverPostCteSites near-identical
- M5: Cross-file SQL literal formatters
- M6: InterceptorKind requires touching 4-6 locations to extend
- M7: InterceptorRouter.Categorize() defined but never used
- M8: SqlExprRenderer StringBuilder allocated per call
- M9: HasCarrierField linear scan
- M10: Annotator bare catch swallows all exceptions
- M11: Parser emits raw C# ternary as SqlRawExpr
- M12: SubqueryExpr.DeepEquals ignores resolution-phase fields

**Test baseline:** 3,236 tests all passing (103 Analyzers + 201 Migration + 2932 Quarry.Tests)

## Decisions
- 2026-04-11: Scope adjusted after verification. Dropped M3 (AnalyzeOperandChain merge -- deliberately lean), M6 (InterceptorKindMetadata -- limited benefit ~50 lines), M12 (SubqueryExpr.DeepEquals -- intentional design). Downgraded H4/H9 to consolidation phase (architectural, not correctness). H7 FunctionCallExpr hash is fine, only fix 3 node types.
- 2026-04-11: All 5 critical bugs (C1-C5) verified against source. All 3 allocation hotspots (H1-H3) verified. H8 duplication verified. M1, M2 consolidation verified.
- 2026-04-11: Pipeline B (interceptors) only runs at build time via RegisterImplementationSourceOutput, NOT incrementally during IDE typing. This downgrades H5/H6 from "every keystroke" to "every build" impact. H5, H6, H4, H9 deferred to separate issues. M1, M4 deferred (large refactors). Final scope: C1-C5, H1-H3, H7, H8, M2, M5, M8, M10, M11.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | PLAN | Started from 6-agent review discussion. Verified all findings against source. Adjusted scope (dropped M3/M6/M12, deferred H4/H5/H6/H9/M1/M4). 8 implementation phases planned. |
