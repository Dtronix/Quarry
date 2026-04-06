# Review: #208

**Branch:** `187-cte-derived-tables` (18 commits, 1902 lines added across 24 files)
**Reviewer scope:** Full diff `origin/master...HEAD`, plan.md, workflow.md Decisions
**Note:** This is a SECOND review pass after rebase on master. Items previously classified (D) Ignored are not re-surfaced unless new evidence has emerged.

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 6 plan said "Suppress inner chains: mark analyzed inner chains so the emitter skips carrier generation for them." Implementation does the OPPOSITE — `ChainAnalyzer.cs:151` explicitly adds the inner chain to `results` so it gets a carrier and interceptors. This is now permanent dead-end infrastructure (the inner carrier is allocated then immediately discarded by the outer With() interceptor). | Medium | The inner-chain-as-distinct-AnalyzedChain approach pollutes manifests (`Where(...).Orders()` appears in all 4 dialect manifests as a phantom chain), wastes carrier allocations at runtime, and creates the false impression that the inner query is independently executable. The plan's suppression model would have avoided this. |
| Phase 6 plan said "CTE inner parameters are added to the outer carrier's parameter list. Extraction plans for CTE inner parameters come from the inner chain's clause sites." This was NOT implemented — the CteDefinition interceptor body in `TransitionBodyEmitter.cs:108-115` does only `new {carrier} { Ctx = @this }` with no parameter extraction. | High | Without runtime extraction, any CTE inner query that uses captured variables will silently bind default values (0/null) for the inner-query parameters. See Correctness section for the full bug description. |
| `CteDtoResolver.Resolve()` (the EntityInfo builder) is still dead code, only `ResolveColumns()` is called from `UsageSiteDiscovery.cs:3570`. Marked TODO but not removed. | Low | Already noted in the prior review as (B). Now confirmed still untouched after rebase. The TODO comment is in place. Not regressing — included only as plan-compliance status. |
| Phase 9 implements 1 of 8 planned tests (`Cte_FromCte_SimpleFilter`). The test uses the existing `Order` entity as the CTE DTO rather than a dedicated DTO class. | Medium | Already known/accepted (handoff documents this as deferred). Surfaced here because the deviation now spans an entire production-ready PR — there is no merge-blocking pressure to implement the missing tests, but the gap leaves several critical code paths (carrier param extraction, multi-CTE, CTE+Join, dedicated DTO entities) completely unverified. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **CTE inner parameters are silently dropped at runtime.** `ChainAnalyzer.cs:659-681` adds CTE inner-chain `QueryParameter`s to the outer plan's `parameters` list, so `CarrierAnalyzer` allocates carrier fields P0..P(N-1) for them. But `EmitCteDefinition` (`TransitionBodyEmitter.cs:94-115`) never assigns those fields — it just does `new Chain_X { Ctx = @this }`. At runtime the CTE @p0..@p(N-1) slots are bound to `default(T)` values from the never-initialized carrier fields. The current `Cte_FromCte_SimpleFilter` test masks this because its inner WHERE uses a literal `100` (no captured params), so the bug doesn't trip. Any inner query like `Lite.Orders().Where(o => o.Total > cutoff)` will produce silently wrong results. | High | Silent data corruption for any non-trivial CTE. The pipeline computes parameter slots correctly, allocates the carrier fields correctly, but the runtime extraction step is missing entirely. No diagnostic surfaces this. |
| `ChainAnalyzer.cs:642-684` silently drops `CteDefinition` sites whose inner chain has no entry in `cteInnerResults` (e.g., the inner chain failed analysis or its argSpanStart didn't match). When this happens, no `CteDef` is added but the `FromCte` site at `685-690` still rewrites `primaryTable` to the CTE name. Result: `SELECT ... FROM "OrderCountDto"` referencing a CTE that was never declared in WITH. No error, no diagnostic. | High | The generator emits invalid SQL that fails at runtime with a "no such table" error from the database driver, with no source-side diagnostic to point at the offending With() call. |
| `ContextCodeGenerator.cs:152, 161, 170` emits CTE methods with `new` keyword. These shadow (not override) the base class methods. If user code holds a `QuarryContext` reference and calls `.With<TDto>(...)` on it, the BASE class method (which throws `NotSupportedException`) runs, not the derived shadowed one. The interceptor only matches the call-site type, not vtable dispatch. | Medium | A subtle source of `NotSupportedException` for users who pass their context as the base type. The base class methods in `QuarryContext.cs:137,148,158` always throw, so this is an upgrade hazard not a silent corruption — but the error message is unhelpful ("must be intercepted by the Quarry source generator" without indicating that base-typing is the problem). |
| `EmitFromCte` (`TransitionBodyEmitter.cs:122-131`) does `Unsafe.As<IEntityAccessor<TDto>>(@this)`. When `FromCte<T>()` is called WITHOUT a preceding `With<T>()`, `@this` is the actual context (not a carrier disguised as context), and the cast reinterprets context memory as a carrier. Subsequent clause interceptors will write into garbage offsets. No validation. Prior review marked this (D) Ignored — re-surfacing only because the fact that `EmitCteDefinition` and `EmitFromCte` together rely on this disguise pattern means the disguise is load-bearing and undocumented. | Low | Already classified (D). Mentioned for completeness — no new evidence beyond noting that the carrier-as-context disguise is now an architectural invariant with no compile-time enforcement. |
| `ChainAnalyzer.cs:144` parses the cte-inner suffix via `LastIndexOf(':')` then `int.TryParse` on the substring. Chain IDs of the form `path:offset:varName` would normally fail TryParse (varName is non-numeric). But if `varName` were ever a digits-only identifier (legal C# allows e.g. `_1` but not pure digits — so probably impossible), the parse would silently mis-extract. Brittle coupling to chain-ID format. | Low | Format-string-driven extraction from a stringly-typed key is fragile. A typed `(chainId, cteArgSpanStart)` pair would be safer. Not currently exploitable. |
| `RawCallSite.cs:282` Clone() does NOT propagate `IsCteInnerChain`/`CteEntityTypeName`/`CteInnerArgSpanStart`/`CteColumns` via the constructor parameters in the `Clone()` method body — actually they ARE passed in lines 281-285. Verified correct. | -- | Verified — Clone() correctly forwards all 4 new fields. |

## Security

No concerns.

(CTE names derive from compile-time DTO class names, quoted via `SqlFormatting.QuoteIdentifier`. No user-supplied strings flow into SQL identifiers. Inner SQL is generated by the same pipeline as the rest of the codebase. No new dependencies.)

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The single test `Cte_FromCte_SimpleFilter` uses a literal `100` in the inner WHERE, which is inlined as a SQL constant rather than parameterized. The test therefore exercises ZERO inner-chain parameters, which is the exact code path with the silent-data-corruption bug above. | High | Even a minimal regression test against captured CTE params (e.g., `var cutoff = 100m; ... .Where(o => o.Total > cutoff)`) would have caught the runtime extraction gap. The test as written cannot fail in any way that exposes the bug. |
| `CrossDialectCteTests.cs:20` uses `Order` (a real schema entity) as both the With type argument and the FromCte type argument. The CTE name becomes "Order" while the inner table is "orders" — distinct identifiers in case-sensitive dialects but visually confusing. A dedicated DTO class (`OrderTotalDto`) would have surfaced any DTO-specific column-resolution issues. | Medium | The handoff already documents this as deferred. Combined with the lack of inner-param coverage, the test verifies only the SQL-string assembly path, not any of the runtime binding logic that distinguishes CTEs from plain queries. |
| No negative tests: `FromCte<T>()` without preceding `With<T>()`, mismatched type args (`With<A>(...).FromCte<B>()`), CTE referencing a DTO with zero properties, and CTE inner chain with set operations are all uncovered. | Low | Without negative tests, regressions in the discovery/analysis fallbacks (`TryResolveViaChainRootContext`, `DetectCteInnerChain` candidate-symbols path) will pass CI silently. |
| `CrossDialectCteTests.cs:8` has `[TestFixture]` marked `internal class` — consistent with sibling test classes, no concern. | -- | Verified consistent. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Two methods named `GetShortTypeName` exist with different semantics: `InterceptorCodeGenerator.Utilities.cs:495` strips `global::` and returns the rest unchanged, while `ChainAnalyzer.cs:1973` extracts the substring after the LAST `.`. The new ChainAnalyzer copy is used only for CTE name extraction. The naming collision is confusing — the two are not interchangeable. | Medium | The new ChainAnalyzer helper duplicates a name without sharing semantics. A reader assuming the codebase has one canonical helper will misuse it. Either rename to `ExtractTypeNameSegment` or replace its single use site (line 653, 688) with inline `Substring(LastIndexOf('.') + 1)`. |
| `DiscoverPostCteSites` (`UsageSiteDiscovery.cs:1053-1192`, ~140 lines) and `DiscoverPreparedTerminalsForCteChain` (`1196-1292`, ~100 lines) duplicate large chunks of `RawCallSite` construction, interceptable-location resolution, and discovery enrichment that already exist in the main `DiscoverRawCallSite` path. Tracked as #207 but the duplication is now the largest single contribution to UsageSiteDiscovery's net diff. | Medium | Already issue #207 — re-surfacing because the duplication is the largest growth area in the file (~250 lines). Any future change to the discovery protocol (e.g., new RawCallSite fields, new interceptable-location version) must be replicated in 3+ places. Refactoring is increasingly painful the longer this waits. |
| `QuarryGenerator.cs:233` reformats a 4-line `catch { return ImmutableArray.Empty; }` to 1 line with no behavioral change. | Low | Already noted in prior review (D). Re-surfacing only because the rebase preserved this stylistic regression instead of cleaning it up. |
| `UsageSiteDiscovery.cs:173` adds an unrelated blank line at the top of `DiscoverRawCallSite()`. | Low | Cosmetic. Already noted in prior review. |
| `ChainAnalyzer.cs:154` uses `catch { }` (silent swallow) for inner-chain analysis failures, while the rebased outer-chain analysis at lines 177-185 uses `catch (Exception ex) when (ex is not OperationCanceledException) { PipelineErrorBag.Report(...); }`. The inner-chain pass uses the WIP simplification that the rebase explicitly upgraded for outer chains (per workflow.md "kept master's better error reporting"). | Medium | Inner-chain analysis failures are now invisible — the user sees "no SQL was generated for the CTE" with no diagnostic. The rebase consciously preserved master's better error reporting on the outer pass; the inner pass should match. |
| `ChainAnalyzer.cs:683` has a stray blank line followed by `}` inside the `CteDefinition` branch. Cosmetic, but the empty-line-before-brace is inconsistent with the rest of the file. | Low | Cosmetic. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `RawCallSite` constructor gains 4 new optional parameters bringing the total to ~50 positional params. The CTE additions are appended after the rebased `operandChainId/operandArgEndLine/operandArgEndColumn` block — but the constructor's existing parameter ordering is becoming a maintenance hazard (the file already has multiple "added in commit X" parameter blocks). | Low | Internal type — no breaking change. Surfaced only because the file's growth makes it increasingly easy to mis-order arguments at call sites. A builder pattern or `with` expressions (record init) would scale better. |
| `QueryPlan` constructor gains `cteDefinitions` parameter at the END after the rebased set-operation params. `Equals` and `GetHashCode` updated correctly. | Low | Optional, default `null`, no breakage. |
| `InterceptorKind` adds `CteDefinition` and `FromCte` after the set-operation values added by master. Position is non-terminal (still before `Unknown`) so existing serialization-unsafe code is fine since enums are internal. | Low | Internal enum, no breakage. |
| `ClauseRole` adds `CteDefinition` and `FromCte` BEFORE existing `ChainRoot` value. If any code assumes specific enum integer values for these clauses, it would break. Spot-check shows no integer-value-dependent code, but worth confirming. | Low | Internal enum. Position-shift hazard documented. |
| New public API `With<TDto>`, `With<TEntity, TDto>`, `FromCte<TDto>` on `QuarryContext` base class. Always throws unless intercepted. Naming may collide with user-defined extension methods named `With` on contexts. | Medium | The base class always throws — users currently calling a custom `With(...)` extension method on their context could see ambiguity errors at compile time. The likelihood is low but the public API is now fixed. |
| Inner chain emits its own carrier (deviation from plan, see Plan Compliance) which means EVERY CTE-using application now allocates two carriers per CTE call site. For high-throughput query paths the unnecessary allocation is real overhead. | Low | Performance regression vs. the plan's suppression model. Acceptable for the FromCte-only scope, but should be flagged before any wider CTE rollout. |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Inner chains kept as AnalyzedChain (Phase 6 deviation) | Plan Compliance | A | Implementation justified (inner chain may also be called standalone) — keep behavior, document rationale in code comment, no refactor |
| CTE inner parameter extraction not implemented in EmitCteDefinition | Plan Compliance | A | Same root cause as Correctness #1 — fix in EmitCteDefinition |
| CteDtoResolver.Resolve() dead code | Plan Compliance | A | Delete dead method |
| Phase 9 only 1 of 8 tests, uses Order entity | Plan Compliance | A | Add captured-param test, dedicated-DTO test, negative test |
| CTE inner parameters silently dropped at runtime | Correctness | A | Fix EmitCteDefinition to assign carrier param fields from innerQuery carrier |
| Silent drop of CteDefinition when cteInnerResults missing | Correctness | A | Add else branch with PipelineErrorBag diagnostic; mark FromCte unresolved so SQL not emitted |
| `new` keyword shadowing on context CTE methods | Correctness | A | Improve base-class error message to mention typing |
| EmitFromCte Unsafe.As without preceding With | Correctness | A | Detect at chain analysis time; emit diagnostic |
| cte-inner suffix LastIndexOf(':') brittle parsing | Correctness | A | Replace with named-segment parsing |
| Single test uses literal 100 (zero inner params) | Test Quality | A | Add Cte_FromCte_CapturedParam test |
| Test uses `Order` entity not dedicated DTO | Test Quality | A | Add Cte_FromCte_DedicatedDto test |
| No negative tests | Test Quality | A | Add FromCte-without-With negative test |
| Two GetShortTypeName methods with different semantics | Codebase Consistency | A | Rename ChainAnalyzer helper to ExtractShortTypeName |
| DiscoverPostCteSites/DiscoverPreparedTerminalsForCteChain duplication (#207) | Codebase Consistency | C | Already tracked as #207 — large refactor, keep deferred |
| QuarryGenerator.cs:233 reformat | Codebase Consistency | A | Revert reformat |
| UsageSiteDiscovery.cs:173 blank line | Codebase Consistency | A | Remove cosmetic blank line |
| ChainAnalyzer.cs:154 catch swallow | Codebase Consistency | A | Mirror outer pattern: catch (Exception ex) when (...) + PipelineErrorBag.Report |
| ChainAnalyzer.cs:683 stray blank line | Codebase Consistency | A | Remove |
| RawCallSite constructor parameter sprawl | Integration | C | Architectural — defer to a separate refactor PR |
| QueryPlan constructor | Integration | D | Verified no impact |
| InterceptorKind position | Integration | D | Verified no impact |
| ClauseRole position | Integration | D | Verified no impact |
| New With/FromCte API naming collision risk | Integration | A | Document in XML doc |
| Inner chain emits own carrier (perf) | Integration | A | Same as Plan Compliance #1 — code comment documenting rationale |
