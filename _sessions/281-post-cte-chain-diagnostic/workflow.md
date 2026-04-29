# Workflow: 281-post-cte-chain-diagnostic

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REVIEW
status: active
issue: #281
pr:
session: 1
phases-total: 4
phases-complete: 4

## Problem Statement
Issue #281: Calling chain-continuation methods (`OrderBy`, `ThenBy`, `Limit`, `Offset`, `Distinct`, `WithTimeout`) directly on the result of `FromCte<T>()` produces a malformed C# 12 interceptor where `EntityType<EntityType>` appears as both the receiver and return type. This blows up the build with `CS0308: The non-generic type 'X' cannot be used with type arguments`.

Root: `IEntityAccessor<T>` doesn't expose chain-continuation methods, so Roslyn cannot resolve the call. `DiscoverPostCteSites` synthesizes a site anyway, but `currentBuilderTypeName` is `null` at the synthesis point. The fallback chain in `TranslatedCallSite` then surfaces the entity name, which `ClauseBodyEmitter` uses verbatim as a generic type.

Issue suggests two approaches:
- (a) Reject + diagnose with a `QRY***` descriptor; skip site synthesis when the prior step returns `IEntityAccessor<T>` and the called method is a chain-continuation method.
- (b) Extend the chain — synthesize a valid interceptor whose receiver is `IEntityAccessor<T>` and return is `IQueryBuilder<T>`.

Baseline (2026-04-29): all 3364 tests pass (Quarry.Tests 3035, Quarry.Analyzers.Tests 128, Quarry.Migration.Tests 201). No pre-existing failures.

## Decisions

### 2026-04-29 — Approach (b): extend the chain
Implement approach (b) from the issue: add the missing chain-continuation methods to `IEntityAccessor<T>` as default-throwing interface methods so the natural syntax compiles. The bug then disappears not by trapping the malformed code path but by removing it: Roslyn resolves the methods, the post-CTE walker breaks out at `parentSymbolInfo.Symbol is IMethodSymbol`, and normal `DiscoverRawCallSite` produces a site with `builderTypeName = containingType.Name = "IEntityAccessor"`. The emitter already handles that case via `IsEntityAccessorType` / `BuildReceiverType`, producing `IQueryBuilder<T> OrderBy(this IEntityAccessor<T> builder, ...)`.

### 2026-04-29 — Scope: all interceptable methods not on IEntityAccessor<T>
Add: `OrderBy`, `ThenBy`, `Limit`, `Offset`, `Having`, and the full set-op family (`Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll` — direct and lambda forms). All as default-throwing interface methods returning `IQueryBuilder<T>`. Carriers inherit defaults; only intercepted calls override.

Note on semantics: `ThenBy` without a prior `OrderBy` and `Having` without a prior `GroupBy` would still produce SQL that fails at the database (or in carrier dispatch). That's a separate runtime/SQL concern — out of scope for this fix, which is about not emitting CS0308-bait.

### 2026-04-29 — No new diagnostic ID needed
Approach (b) makes the user-level call legal, so the QRY083 diagnostic envisioned for approach (a) is unnecessary. The "plumb diagnostics out of DiscoverPostCteSites" decision from the third question becomes inapplicable; if a future case slips through (interceptable method not on IEntityAccessor), it would surface as a normal Roslyn CS0117 / CS1061 at the user's call site. We will NOT add a QRY083 in this PR.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-29 INTAKE | 2026-04-29 REMEDIATE | Loaded #281, baseline 3364 green. DESIGN: chose approach (b), all interceptable methods. PLAN: 4 phases. IMPLEMENT: added 13 chain-continuation methods to IEntityAccessor<T>; added 2 cross-dialect SQL tests + 1 generator-output test (3367 total). REVIEW: 25 findings, 3A/2B/0C/20D after user override (13:C→A). REMEDIATE: dropped "slim" framing, moved test out of QRY080/081 region, added CS0308 recompile assertion, added `Tuple_PostCteWideProjection_OrderBy`, added `Cte_FromCte_AllChainContinuations_BindAndEmitCleanly` covering all 13 methods (3369 total, all green). Filed #283 for ThenBy/Having semantic-foot-gun analyzer follow-up. Rebasing and opening PR. |
