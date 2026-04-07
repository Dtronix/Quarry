# Workflow: 207-cte-discovery-helpers

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #207
pr:
session: 1
phases-total: 3
phases-complete: 3

## Problem Statement
Refactor CTE discovery boilerplate into shared helpers.

`DiscoverPostCteSites` and `DiscoverPreparedTerminalsForCteChain` in
`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` duplicate ~180 lines of
boilerplate from existing discovery methods (`RawCallSite` construction,
interceptable location resolution, enrichment logic).

Suggested approach (from issue): extract shared helpers for
- Getting interceptable location data (`#if QUARRY_GENERATOR` block)
- Creating a synthetic `RawCallSite` from a parent invocation context
- Enriching display class info

Goal: reduce duplication between `DiscoverPostJoinSites`,
`DiscoverPostCteSites`, and `DiscoverPreparedTerminalsForCteChain`.

### Baseline (pre-existing test status)
All tests passing on master at 5f5f758:
- Quarry.Migration.Tests: 97/97
- Quarry.Analyzers.Tests: 103/103
- Quarry.Tests: 2812/2812
- Total: 3012 passing, 0 failing

## Decisions
- **2026-04-06** — Scope: apply the new helper to all 8 boilerplate sites in `UsageSiteDiscovery.cs`, not just the 3 named in the issue. The `#if QUARRY_GENERATOR` interceptable-location block is identical at all of them.
- **2026-04-06** — Two helpers:
  1. `TryGetInterceptableLocationData(InvocationExpressionSyntax, SemanticModel, CancellationToken) -> (string Data, int Version)?` — replaces just the `#if QUARRY_GENERATOR` block at all 8 sites.
  2. `TryGetCallSiteLocationInfo(InvocationExpressionSyntax, string methodName, SemanticModel, CancellationToken) -> CallSiteLocationInfo?` — compound helper for the 3 issue methods. Bundles `GetMethodLocation` + interceptable location + `GenerateUniqueId` into one call. Returns null if either `GetMethodLocation` or interceptable location data is null. Internally uses helper #1.
- **2026-04-06** — Fix `#if ROSLYN_4_12_OR_GREATER` → `#if QUARRY_GENERATOR` at line 2759 in `TryDiscoverExecutionSiteSyntactically`. The `ROSLYN_4_12_OR_GREATER` symbol is undefined anywhere in the project, so the block currently never executes. This is an intentional behavior change — call sites discovered via that path will now carry real interceptable location data instead of always-null. Existing tests must remain green; flag in PR description.
- **2026-04-06** — Do not extract a unified `BuildSyntheticPostSite` helper. The `RawCallSite` constructors at the 3 issue sites differ meaningfully (`builderKind`, `joinedEntityTypeNames`, `builderTypeName`, projection-info entity counts, disqualifier sets) — bundling them would push too many parameters through one helper.
- **2026-04-06** — Do not add direct unit tests for the helpers. Refactor is behavior-preserving (except the dead-code fix). Existing 3012 tests cover discovery flows that exercise both helpers.
- **2026-04-06** — `EnrichDisplayClassInfo` is already a small extracted helper (line 4024) used by all 3 issue methods. No further work needed on the issue's third bullet ("Enriching display class info").

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Started workflow for issue #207 |
