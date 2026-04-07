## Summary
- Closes #207
- Extracts two private helpers in `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` to remove ~180 lines of duplicated boilerplate, applied across 8 call sites — including the 3 named in the issue plus 5 other identical sites elsewhere in the file.
- Fixes one latent dead-code conditional (`#if ROSLYN_4_12_OR_GREATER` — symbol is undefined anywhere in the project) at `TryDiscoverExecutionSiteSyntactically`.

## Reason for Change
`DiscoverPostCteSites`, `DiscoverPreparedTerminalsForCteChain`, and `DiscoverPostJoinSites` each duplicated ~25 lines of `GetMethodLocation` + `#if QUARRY_GENERATOR` interceptable-location lookup + `GenerateUniqueId` boilerplate. Once extracted, the same helper turned out to apply at 5 other sites in the same file with the identical pattern.

## Impact
- **Code size**: net ~136 lines removed across the three refactor commits.
- **`#pragma warning disable RSEXPERIMENTAL002`** is now declared in exactly one place (`TryGetInterceptableLocationData`) instead of 8.
- **Behavior change at one site** (Phase 3): see "Breaking Changes" below.
- **No public API change**: both helpers and the new `CallSiteLocationInfo` struct are `private` to `UsageSiteDiscovery`.
- **All 3012 existing tests pass** after every commit on the branch.

## Plan items implemented as specified
- **Phase 1** — Introduce `TryGetInterceptableLocationData(InvocationExpressionSyntax, SemanticModel, CancellationToken) → (string Data, int Version)?`. Apply at 4 simple sites: `DiscoverRawCallSite` (root + main locations), `DiscoverCteSite`, `DiscoverRawSqlUsageSite`. Each call site preserves its prior null-check semantics (early-return vs. fallthrough-with-default).
- **Phase 2** — Introduce `CallSiteLocationInfo` (private readonly struct: filePath/line/column/data/version/uniqueId) and `TryGetCallSiteLocationInfo(invocation, methodName, sm, ct)`. Apply at the 3 sites named in #207. Loop semantics preserved: `break` for the while-walks in `DiscoverPostJoinSites` and `DiscoverPostCteSites`, `continue` for the foreach scan in `DiscoverPreparedTerminalsForCteChain`.
- **Phase 3** — Switch `#if ROSLYN_4_12_OR_GREATER` → `#if QUARRY_GENERATOR` at `TryDiscoverExecutionSiteSyntactically`, then apply `TryGetInterceptableLocationData` at that site. Drop the redundant `?? 1` from the constructor call now that the version is non-nullable.

## Deviations from plan implemented
None. Implementation matches `plan.md` exactly: 3 phases, 3 commits, 8 sites, helpers and struct shaped as planned.

## Gaps in original plan implemented
None. The "Enriching display class info" bullet from the issue's Suggested Approach was a no-op — `EnrichDisplayClassInfo` was already an extracted helper used by all three issue methods.

## Migration Steps
None. No public surface change; consumers see no difference.

## Performance Considerations
None. The compound helper performs the same `GetMethodLocation` → `GetInterceptableLocation` → `GenerateUniqueId` sequence the inline code already did, in the same order. No extra allocations beyond the small `CallSiteLocationInfo` value type (returned as `Nullable<>`).

## Security Considerations
None. Internal generator code reading syntax trees with no I/O, deserialization, or untrusted-input handling.

## Breaking Changes
### Consumer-facing
None.

### Internal
**Phase 3 enables a previously dead code path.** `TryDiscoverExecutionSiteSyntactically` previously guarded its interceptable-location lookup on `#if ROSLYN_4_12_OR_GREATER`. That preprocessor symbol is **never defined** anywhere in the project (verified across all `.csproj`, `.props`, and source files), so the block was unreachable: every call site discovered through this path was emitted with `interceptableLocationData = null` and `interceptableLocationVersion = 1`.

After the fix, this discovery path will emit real interceptable-location data when `QUARRY_GENERATOR` is defined (always true for the generator build). Downstream impact is at `FileEmitter.EmitInterceptor` (`src/Quarry.Generator/CodeGen/FileEmitter.cs:604-613`), which gates `[InterceptsLocation(...)]` emission on `!string.IsNullOrEmpty(site.InterceptableLocationData)`:
- **Before**: sites routed through the syntactic fallback emitted only the `// WARNING: Could not generate InterceptsLocation for this call site` comment and returned without writing the interceptor body.
- **After**: those same sites now emit a fully attributed interceptor.

This is a **strict capability gain** — interception that should always have been happening will now happen. The full `Quarry.Tests` suite (2812/2812) passes after the change, and a search of generated files for the old warning comment finds zero hits, indicating either no test fixture currently routes through this fallback or all such sites now emit successfully.

There is no test that directly asserts on the syntactic-fallback path's output shape; coverage is transitive through integration tests. A future test that needs to lock in the Phase 3 enablement would need to construct a scenario forcing the syntactic-fallback branch.
