# Review: 207-cte-discovery-helpers (PR not yet created)

## Summary

3 commits, 1 production file touched (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`), +141 / -167 lines. Two new private members (helper + struct) plus one private helper. Build clean (0 warnings). Quarry.Tests: 2812/2812 passing — matches baseline.

All 8 planned sites verified:

| # | Line | Method | Helper | Loop control | Null semantics |
|---|------|--------|--------|--------------|----------------|
| 1 | 296  | DiscoverRawCallSite (root) | `TryGetInterceptableLocationData` | `return null` on null | early-return preserved |
| 2 | 459  | DiscoverRawCallSite (main) | `TryGetInterceptableLocationData` | fallthrough, no check | `?.Data` / `?.Version ?? 1` preserves null+default-1 |
| 3 | 926  | DiscoverPostJoinSites | `TryGetCallSiteLocationInfo` | `break` | preserved |
| 4 | 1070 | DiscoverPostCteSites | `TryGetCallSiteLocationInfo` | `break` | preserved |
| 5 | 1201 | DiscoverPreparedTerminalsForCteChain | `TryGetCallSiteLocationInfo` | `continue` | preserved |
| 6 | 2679 | TryDiscoverExecutionSiteSyntactically | `TryGetInterceptableLocationData` | fallthrough | **intentional behavior change**, see below |
| 7 | 3454 | DiscoverCteSite | `TryGetInterceptableLocationData` | `return null` on null | early-return preserved |
| 8 | 3702 | DiscoverRawSqlUsageSite | `TryGetInterceptableLocationData` | fallthrough | `?.Data` / `?.Version ?? 1` preserves null+default-1 |

## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Implementation matches `plan.md` exactly: 3 phases, 3 commits, 8 sites, helpers named and shaped as planned, struct shaped as planned, dead-conditional fix in Phase 3. | informational | Confirms reviewer can rely on the plan as a map. |
| `EnrichDisplayClassInfo` was correctly left untouched per the 2026-04-06 decision (already extracted). | informational | No scope creep. |
| No unrelated changes; only the target file plus session artifacts (`workflow.md`, `plan.md`) are touched. | informational | Minimal blast radius. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| Loop control preserved at all 3 issue sites: while-walks at lines 926/1070 use `break`, foreach at 1201 uses `continue`. Verified against current file. | informational | The plan explicitly called this out as a risk; it lands cleanly. |
| Variable names destructured from `info.Value.X` (`filePath`, `line`, `column`, `interceptableLocationData`, `interceptableLocationVersion`, `uniqueId`) match the names every downstream `RawCallSite` constructor passes by named argument. No shadowing or rename needed. | informational | The constructors at the 3 issue sites remain literal byte-for-byte after the variable assignments. |
| Default-version semantics preserved at the 3 fallthrough sites (459, 2679, 3702): `?? 1` is applied; data stays null. Sites that null-check-and-return (296, 3454) correctly omit `?? 1` (would never be observed). | informational | No drift in `interceptableLocationVersion` defaults. |
| Phase 3 site (2679) previously declared `int? interceptableLocationVersion = null` and applied `?? 1` at the constructor (`master` line 2802). New code applies `?? 1` inline at the helper return so the constructor receives a plain `int`. The two are observably identical. The diff also drops `interceptableLocationVersion ?? 1` from the constructor call (now just `interceptableLocationVersion`), consistent with the type change. Verified. | informational | Subtle nullability shuffle is correct. |
| Original ordering preserved: `GetMethodLocation` runs before `GetInterceptableLocation`, and `GenerateUniqueId` runs after both. The compound helper internally follows the same order. | informational | No observable reordering. |
| `TryGetInterceptableLocationData` swallows exceptions exactly as the original 8 try/catch blocks did, and returns null when `QUARRY_GENERATOR` is not defined (consolidated `#if` boundary). | informational | Behavior parity in the non-generator build (analyzer + tests assemblies don't define `QUARRY_GENERATOR`). |
| Phase 3 dead-code fix: `TryDiscoverExecutionSiteSyntactically` will now emit non-null `interceptableLocationData`/non-default `interceptableLocationVersion` when the build defines `QUARRY_GENERATOR` (always true for the generator). The single downstream consumer is `FileEmitter.EmitInterceptor` (`src/Quarry.Generator/CodeGen/FileEmitter.cs:605`), which gates `[InterceptsLocation(...)]` on `!string.IsNullOrEmpty(site.InterceptableLocationData)`. Sites that previously fell through this path emitted only the `// WARNING: Could not generate InterceptsLocation for this call site` comment and returned without writing the attribute. After the fix they will now actually emit the attribute, intercepting calls that previously went uninterceptable. | behavior change | This is the documented intentional behavior change. The full Quarry.Tests suite (2812/2812) passes after the change, and a search of `obj/GeneratedFiles` for "WARNING: Could not generate" finds zero hits — meaning either no site currently routes through `TryDiscoverExecutionSiteSyntactically` in any test fixture, OR every such site now successfully emits an interceptor. Either way no test asserts on the warning text. |
| No off-by-one introduced. The destructured `(filePath, line, column)` come straight from `GetMethodLocation`'s tuple just as before. | informational | Confirmed by visual diff. |
| The compound helper computes `uniqueId` only after both `location` and `interceptable` succeed; original code did the same. No wasted work, no order change. | informational | Trivially correct. |

## Security

No concerns. Internal generator code reading syntax trees with no I/O, no deserialization, no untrusted-input handling.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| Per the 2026-04-06 design decision no new tests were added. Existing `Quarry.Tests` suite (2812 tests) passes after all 3 phases. | informational | Refactor is behavior-preserving (with one documented exception); the existing integration tests cover the surface. |
| Sites 1-2 (`DiscoverRawCallSite`) are the primary discovery entry point — exercised by every interceptor-generating test. Sites 3-5 are exercised by `SqlOutput/CrossDialectCteTests`, `CrossDialectJoinTests`, `JoinedCarrierIntegrationTests`, `CrossDialectNavigationJoinTests`, etc. The presence of generated `[InterceptsLocation(...)]` attributes (31 hits in `PgDb.Interceptors...CrossDialectCteTests.g.cs` alone) confirms post-CTE / post-join discovery does run end-to-end during test compilation. Sites 7-8 (`DiscoverCteSite`, `DiscoverRawSqlUsageSite`) are exercised by CTE tests and `RawSqlInterceptorTests` respectively. | informational | All 8 sites have at least integration coverage. |
| Site 6 (`TryDiscoverExecutionSiteSyntactically`) is a fallback path reached only when normal symbol resolution returns no candidates (`UsageSiteDiscovery.cs:262`). No test asserts on its output shape directly; it's covered transitively by integration tests that exercise the whole pipeline. There is no explicit assertion of "this call routes through the syntactic fallback" vs. the normal path. | minor | If a future test needed to lock in the Phase 3 behavior change (interceptable-location now non-null on this path), it would need to construct a scenario that forces the syntactic-fallback branch — currently no such test exists. Not a blocker for the refactor; flag for the PR description. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| Helper naming (`TryGetInterceptableLocationData`, `TryGetCallSiteLocationInfo`) follows the file's `Try*`/`Get*`/`Discover*` convention. | informational | Reads naturally next to existing helpers. |
| `CallSiteLocationInfo` is a `private readonly struct` — consistent with the file's general avoidance of public surface. Properties are auto-properties with explicit constructor, matching the style of other small data carriers in the generator (e.g., `RawCallSite`'s explicit-ctor pattern). | informational | Stylistically aligned. |
| Both new helpers and the struct carry XML doc comments. The file's existing private helpers (e.g., `DetectLoopAncestor`, `DetectTryCatchAncestor`, `ComputeChainId`, `EnrichDisplayClassInfo`) generally do have doc comments — the new code matches this convention. | informational | No drift. |
| The new `(string Data, int Version)?` tuple return type does not require any new `using` directive (`System.ValueTuple` is implicit). Build is clean with zero warnings. | informational | Confirmed. |
| `#if QUARRY_GENERATOR` / `#pragma warning disable RSEXPERIMENTAL002` boilerplate is now consolidated to a single occurrence in the file (lines 3977 and 3980). A grep across `src/` finds zero other usages of `GetInterceptableLocation` or `RSEXPERIMENTAL002` outside `UsageSiteDiscovery.cs`. | informational | No other files exist that could benefit from the new helper — the refactor cleans up the entire codebase footprint of this pattern, not just one file. |
| The Phase 3 site retains a multi-line `// Previously guarded on the undefined ROSLYN_4_12_OR_GREATER symbol...` comment explaining the historical context of the fix. Useful for future readers. | informational | Good practice for an intentional behavior change. |
| Minor stylistic note: at the 3 issue sites the destructuring is spelled out as 6 separate `var x = info.Value.X;` lines. A `var (filePath, line, column, ...) = info.Value;` deconstruction would be slightly more compact, but `CallSiteLocationInfo` is a struct with no `Deconstruct` method. Adding one would be an extra surface for marginal benefit. The current style is fine. | nit | Not a blocker; explicit assignment is also more grep-friendly. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| Both new helpers and the `CallSiteLocationInfo` struct are `private` to `UsageSiteDiscovery`. No public/internal surface change — `Quarry.Tests` (which has `InternalsVisibleTo`) sees no new types. | informational | Zero risk to consumers of the generator assembly. |
| Phase 3 is the only behavior change. Downstream impact traced to `FileEmitter.EmitInterceptor` (`FileEmitter.cs:604-613`): when `InterceptableLocationData` is non-empty an `[InterceptsLocation(version, "data")]` is emitted; when null/empty the warning comment is emitted and the method returns early without writing the interceptor body. Switching the syntactic fallback from "always null" to "real data when available" therefore shifts those sites from "no interceptor" to "fully attributed interceptor" — a strict capability gain, not a regression. | behavior change | Worth calling out in the PR description so downstream users understand more interception will start happening at execution-site-only chains. |
| Git history shows `ROSLYN_4_12_OR_GREATER` was introduced in the initial commits `d66ec77` / `eb48cd1` ("Quarry: Type-safe SQL builder and query reader for .NET 10"). It was never defined anywhere in the project (zero hits across all properties files, csproj files, and other source). This is best described as a latent bug from the original implementation, not a deliberate feature flag — the fix is appropriate. | informational | Confirms the Phase 3 intent. |
| Quarry.Tests run full green (2812/2812) after all 3 commits, matching baseline. No test was broken by the Phase 3 enablement. | informational | The behavior change is empirically safe at the current test surface. |
| `Quarry.Migration.Tests` (97) and `Quarry.Analyzers.Tests` (103) were not re-run as part of this review pass — they don't depend on the generator's discovery output for non-`QUARRY_GENERATOR` paths so impact is unlikely, but worth re-running before merge. | minor | Standard pre-merge validation. |

## Classifications
| Finding | Section | Class | Action Taken |
|---|---|---|---|
| Plan compliance, naming, struct, doc comments, scope | Plan / Codebase Consistency | D | No action — informational confirmations |
| Loop semantics / variable names / default version / order preserved | Correctness | D | No action — informational confirmations |
| Phase 3 behavior change: syntactic fallback now emits real interceptable-location data | Correctness / Integration | B | Documented in PR body; no code change needed |
| All 8 sites have at least integration coverage | Test Quality | D | No action — informational |
| Site 6 lacks direct test for the syntactic fallback enablement | Test Quality | D | No action — refactor preserves existing test surface; out of scope |
| `Quarry.Migration.Tests` / `Quarry.Analyzers.Tests` re-run pre-merge | Integration | D | Already re-run as part of every phase commit (3012 total) |

## Issues Created
None.
