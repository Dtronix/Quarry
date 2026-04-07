# Plan: 207-cte-discovery-helpers

## Goal
Reduce duplicated boilerplate in `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`
by extracting two helpers and applying them across the file. Bonus: fix one
dead-code conditional that has been silently suppressing interceptable-location
data at one call site.

## Helpers to introduce

### 1. `TryGetInterceptableLocationData`
A single-purpose helper that wraps the `#if QUARRY_GENERATOR` block. Today, that
block appears nearly verbatim 8 times in the file: each time it instantiates two
local variables, opens a `try`, calls `semanticModel.GetInterceptableLocation`
inside `#pragma warning disable RSEXPERIMENTAL002`, copies `Data`/`Version` out
on success, and swallows any exception.

The helper signature:

    private static (string Data, int Version)? TryGetInterceptableLocationData(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)

Returns the location data on success, or `null` if Roslyn returned null, threw,
or the file isn't being compiled with `QUARRY_GENERATOR` defined. Callers
preserve their existing fallthrough behavior by either propagating the null
(early return) or applying their own default version (e.g., `?? 1`).

The implementation lives in one place inside the `#if QUARRY_GENERATOR` /
`#else` partition: under `QUARRY_GENERATOR` it does the real Roslyn call;
otherwise it returns null. Callers no longer need their own `#if`.

### 2. `TryGetCallSiteLocationInfo`
A compound helper that bundles three repeated steps used together by the three
methods named in issue 207:

    var location = GetMethodLocation(invocation);
    if (location == null) return/break/continue;
    var (filePath, line, column) = location.Value;
    // ... 14-line interceptable-location block ...
    if (interceptableLocationData == null) return/break/continue;
    var uniqueId = GenerateUniqueId(filePath, line, column, methodName);

The signature:

    private static CallSiteLocationInfo? TryGetCallSiteLocationInfo(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)

`CallSiteLocationInfo` is a private readonly struct local to
`UsageSiteDiscovery` (not part of the public API):

    private readonly struct CallSiteLocationInfo
    {
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public string InterceptableLocationData { get; }
        public int InterceptableLocationVersion { get; }
        public string UniqueId { get; }
        public CallSiteLocationInfo(string filePath, int line, int column,
            string interceptableLocationData, int interceptableLocationVersion,
            string uniqueId) { ... }
    }

The helper returns null when either `GetMethodLocation` returns null OR the
interceptable location data is unavailable. Callers translate that null into
their loop control (`break` for the while-walks, `continue` for the foreach in
`DiscoverPreparedTerminalsForCteChain`).

`TryGetCallSiteLocationInfo` is used **only** by the three methods named in
the issue. The other 5 sites use the lower-level
`TryGetInterceptableLocationData` directly because their surrounding context
varies too much (different null-check semantics, late uniqueId computation,
different return shapes).

## Map of every site touched

| # | Line | Method | Helper applied |
|---|------|--------|----------------|
| 1 | 296  | `DiscoverRawCallSite` (root location) | `TryGetInterceptableLocationData` |
| 2 | 472  | `DiscoverRawCallSite` (main location) | `TryGetInterceptableLocationData` |
| 3 | 960  | `DiscoverPostJoinSites`               | `TryGetCallSiteLocationInfo` |
| 4 | 1123 | `DiscoverPostCteSites`                | `TryGetCallSiteLocationInfo` |
| 5 | 1271 | `DiscoverPreparedTerminalsForCteChain`| `TryGetCallSiteLocationInfo` |
| 6 | 2757 | `TryDiscoverExecutionSiteSyntactically` | `TryGetInterceptableLocationData` (after conditional fix) |
| 7 | 3545 | `DiscoverCteSite`                     | `TryGetInterceptableLocationData` |
| 8 | 3807 | `DiscoverRawSqlUsageSite`             | `TryGetInterceptableLocationData` |

## Phases

### Phase 1 — Introduce `TryGetInterceptableLocationData` and apply at 5 simple sites
Add `TryGetInterceptableLocationData` near the bottom of the file (alongside
`EnrichDisplayClassInfo`). Apply it at the 5 sites that use only this helper:
296, 472, 3545, 3807, plus 2757 with the conditional unchanged for now (we'll
fix it in Phase 3 to keep the diff small per phase). Each replacement removes
~14 lines and substitutes ~3 lines (helper call + null-conditional reads of
`.Data`/`.Version`).

**Behavior:** unchanged. Each site preserves its prior null-check semantics
(early return vs. fallthrough vs. ignore).

**Tests:** existing — no new tests. Run full test suite, expect 3012 passing.

**Commit:** `refactor(generator): extract TryGetInterceptableLocationData helper`

### Phase 2 — Introduce `TryGetCallSiteLocationInfo` and apply at the 3 issue sites
Add the `CallSiteLocationInfo` struct and the `TryGetCallSiteLocationInfo`
helper near `TryGetInterceptableLocationData`. Apply it inside
`DiscoverPostJoinSites` (line 953-979), `DiscoverPostCteSites` (line 1116-1142),
and `DiscoverPreparedTerminalsForCteChain` (line 1265-1290).

For each of the 3 sites, the replacement looks like:

    var info = TryGetCallSiteLocationInfo(parentInvoc, methodName, semanticModel, cancellationToken);
    if (info == null)
        break;   // or "continue" in the foreach case
    var (filePath, line, column) = (info.Value.FilePath, info.Value.Line, info.Value.Column);
    var interceptableLocationData = info.Value.InterceptableLocationData;
    var interceptableLocationVersion = info.Value.InterceptableLocationVersion;
    var uniqueId = info.Value.UniqueId;

This collapses ~25 lines per site to ~7. Total: ~75 lines removed, ~21 added,
net ~54 lines smaller (matching the order-of-magnitude in the issue).

**Behavior:** unchanged. The struct returns the same fields the inline code
computed. Loop semantics preserved (`break` for while-walks, `continue` for
foreach).

**Tests:** existing — no new tests. Run full test suite, expect 3012 passing.

**Commit:** `refactor(generator): extract TryGetCallSiteLocationInfo for CTE/post-join discovery`

### Phase 3 — Fix the dead `ROSLYN_4_12_OR_GREATER` conditional
At line 2759, change `#if ROSLYN_4_12_OR_GREATER` to `#if QUARRY_GENERATOR` so
the block actually runs in `TryDiscoverExecutionSiteSyntactically`. Then apply
`TryGetInterceptableLocationData` to that site (which Phase 1 left alone for
clarity).

**Behavior:** changed at one path. Sites discovered through
`TryDiscoverExecutionSiteSyntactically` will now carry real
`InterceptableLocationData`/`InterceptableLocationVersion` instead of always
`null`/`null`-coerced-to-`1`. This was a latent bug masked by an undefined
preprocessor symbol; fixing it should be a no-op for any code path that doesn't
depend on those fields being null at this site.

**Tests:** existing 3012 must remain green. If any test is sensitive to the
previously-null interceptable data at this path, that's evidence of a
regression — pause, investigate, and either fix or revert this phase.

**Commit:** `fix(generator): use QUARRY_GENERATOR conditional for syntactic execution-site discovery`

## Dependencies
Phases must run in order: Phase 2 depends on Phase 1's helper, Phase 3 depends
on Phase 1's helper.

## Out of scope
- Renaming variables across the file (cosmetic noise).
- Touching any other discovery file.
- Adding new overloads to `RawCallSite`.
- Direct unit tests for the helpers (per design decision — integration coverage
  is sufficient).
