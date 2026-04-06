# Review: 187-cte-derived-tables

**Branch:** `187-cte-derived-tables` (18 commits, 1766 lines added across 22 files)
**Reviewer scope:** Full diff from `master...HEAD`, plan.md, workflow.md Decisions section

---

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phases 1-8 implemented as designed; phase 9 partially complete (1 of 8 planned tests) | Medium | Only the `FromCte` pattern is proven end-to-end. The CTE+Join, captured variables, multiple CTEs, LEFT JOIN, and identity projection test cases remain unimplemented. |
| CTE+Join pattern (the primary use case in the plan's API Surface section) is blocked by a semantic model limitation | High | The headline scenario from the plan -- `db.With<OrderCountDto>(...).Users().Join<OrderCountDto>(...)` -- does not work. `With<TDto>()` returns `QuarryContext` during source generation, so `.Users()` cannot be resolved on the base class. This is a fundamental architectural gap, not a simple bug. |
| `CteDtoResolver.Resolve()` (pseudo-EntityInfo builder from Phase 3) is implemented but never called | Low | Phase 5 planned to register CTE DTOs in EntityRegistry via this method. The binding pipeline for CTE join targets was never wired up, consistent with CTE+Join being blocked. Dead code should be removed or commented as future-use. |
| `With<TEntity, TDto>` two-type-argument overload: discovery and base class method exist, but interceptor emission always generates `IQueryBuilder<TDto>` (single type param) | Medium | `EmitCteDefinition` hardcodes `paramType = $"IQueryBuilder<{dtoType}>"` regardless of whether the call site used `With<TEntity, TDto>`. If a user calls the two-type-argument overload, the generated interceptor signature will not match and interception will fail at runtime (throwing `NotSupportedException`). |
| Plan specified `SqlExprKind.Cte` addition to `SqlExpr.cs` -- not implemented | Low | Marked as "if needed" in the plan. Not needed for the current FromCte path since CTE columns are resolved via the entity projection system rather than a new SqlExpr kind. Acceptable deviation. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| CTE inner chain's `Where` clause renders in the inner SQL, producing a standalone `Where(...).Orders()` manifest entry | Low | The manifest files show a spurious `Where(...).Orders()` entry (the CTE inner chain) alongside the correct `With(...).FromCte(...)` entry. This is cosmetic (the inner chain gets its own carrier and interceptor) but may confuse manifest readers. The inner chain appears as if it were a user-facing query. |
| `EmitFromCte` receives `@this` typed as context class but does `Unsafe.As<IEntityAccessor<TDto>>(@this)` | Medium | This works when the carrier was created by a preceding `CteDefinition` and the context reference is actually a carrier. But if `FromCte` is called without a preceding `With()` (user error), the `Unsafe.As` will reinterpret the context object as an entity accessor, leading to undefined behavior rather than a clear error. No validation exists. |
| `EmitCteDefinition` always creates a new carrier (`new {carrier.ClassName} {{ Ctx = @this }}`) | Medium | For multiple CTEs (`db.With<A>(...).With<B>(...)`), the second `With()` call also creates a new carrier, discarding the first. The plan identified this ("carrier creation conflict") as a known issue. Multiple CTEs will silently produce incorrect results. |
| ChainAnalyzer iterates `clauseSites` in reverse to build CTE definitions, then reverses | Low | Functionally correct, but fragile -- the reverse iteration assumes no other code modifies the list order between construction and reversal. |
| Parameter merging for CTE inner queries: parameters are appended (not prepended) to the outer list despite the comment saying "Prepend" | Medium | The code uses `parameters.Add(...)` which appends. The comment says "Prepend CTE inner parameters." In practice this works because CTE definitions are processed before outer clause parameters are added (the CTE loop runs first). But the comment is misleading, and if the processing order ever changes, parameter indices will be wrong. |
| `DetectCteInnerChain` candidate symbols fallback uses `CandidateSymbols[0]` without checking array bounds beyond `.Length > 0` | Low | Safe as written (Length > 0 is checked), but could be more defensive by verifying the candidate is actually a `With` method on a Quarry context type before returning true. |

## Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | CTE names are derived from DTO class names (compile-time constants) and quoted through `SqlFormatting.QuoteIdentifier`. No user-supplied strings flow into SQL identifiers. Inner SQL is fully assembled by the generator pipeline. |

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Only 1 test (`Cte_FromCte_SimpleFilter`) out of 8 planned test cases | High | Missing coverage for: CTE+Join with aggregation, captured parameters, multiple CTEs, CTE with WHERE on both sides, LEFT JOIN to CTE, CTE select-all-columns, and CTE as simple derived table without filter. |
| The single test uses existing entity type (`Order`) as the CTE DTO rather than a dedicated DTO class | Medium | The plan specified dedicated DTO classes (`OrderCountDto`, `UserTotalDto`) to test the DTO-as-entity pattern. Using `Order` (a real schema entity) bypasses the CTE-specific column resolution path -- the entity already has full `EntityInfo` in the registry, so CteDtoResolver is not truly exercised. |
| No negative tests | Low | No tests verify error handling for: `FromCte` without preceding `With`, CTE with zero-property DTO, CTE with duplicate names, or CTE inner query that fails analysis. |
| Test verifies SQL output for all 4 dialects and executes against SQLite -- good pattern | -- | The test follows the existing `QueryTestHarness.AssertDialects` pattern and includes runtime execution verification. This is the correct test structure. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `CteDef` and `CteColumn` are in a separate file (`CteDef.cs`) rather than in `QueryPlan.cs` | -- | Consistent with the codebase pattern of one-class-per-file. |
| `CteDtoResolver` follows the same static-class pattern as other resolvers in the IR namespace | -- | Consistent. |
| Empty `catch { }` blocks in `ChainAnalyzer.Analyze()` were reformatted from `catch\n{\n}` to `catch { }` | Low | Minor formatting change in pre-existing code. The empty catches are a pre-existing pattern in the codebase (source generators must not crash), but the reformatting is an unrelated style change. |
| `UsageSiteDiscovery.cs` added a blank line at line 167 inside `DiscoverRawCallSites` | Low | Spurious whitespace-only change. |
| `QuarryGenerator.cs` reformatted a catch block from 4 lines to 1 line | Low | Unrelated style change. |
| `DiscoverPostCteSites` and `DiscoverPreparedTerminalsForCteChain` are large methods (100+ and 80+ lines respectively) that duplicate significant boilerplate from existing discovery methods | Medium | The `RawCallSite` construction, interceptable location resolution, and enrichment logic is copy-pasted rather than extracted into shared helpers. This increases maintenance burden -- any change to the discovery protocol must be replicated in 3+ places. |
| XML doc comments on new public API methods in `QuarryContext.cs` are thorough and consistent with existing style | -- | Good. |
| Generated context methods use `new` keyword to shadow base class methods -- consistent with the existing override pattern for entity accessors | -- | Correct approach for the generated context. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New public API surface on `QuarryContext` base class: `With<TDto>()`, `With<TEntity, TDto>()`, `FromCte<TDto>()` | Medium | These are new public methods on the base class. They throw `NotSupportedException` without interception. Users who have methods named `With` or `FromCte` on their context-derived classes may experience naming conflicts. However, the `new` keyword on the generated class shadows appropriately. |
| `InterceptorKind` and `ClauseRole` enum values added at non-terminal positions | Low | `CteDefinition` and `FromCte` are added before the existing `Unknown` sentinel in `InterceptorKind`, and before `ChainRoot` in `ClauseRole`. Since these enums are internal and not serialized, this is safe. |
| `QueryPlan` constructor gains a new optional parameter `cteDefinitions` | Low | Optional parameter with default `null`, so all existing call sites continue to work. `Equals` and `GetHashCode` updated to include it. No breaking change. |
| `RawCallSite` constructor gains 4 new optional parameters | Low | All optional with defaults, so existing construction sites are unaffected. `Equals`, `GetHashCode`, and `Clone` updated. No breaking change. |
| Manifest output changes (+2 entries per dialect) | Low | The new manifest entries (`Where(...).Orders()` and `With(...).FromCte(...)`) are additive. The `Where(...).Orders()` entry is a side effect of CTE inner chains getting their own carriers -- it represents real generated code, but the chain name is confusing because it omits the CTE context. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|-------------|
| `With<TEntity, TDto>` interceptor signature mismatch | Correctness | **(A)** | Fix EmitCteDefinition to handle two-type-arg overload |
| Test uses schema entity as CTE DTO instead of dedicated DTO | Test Quality | **(A)** | Add test with dedicated DTO class |
| Only 1 of 8 planned tests implemented | Test Quality | **(B)** | Add more FromCte-only tests (DTO, multiple CTEs via FromCte) |
| `CteDtoResolver.Resolve()` is dead code | Plan Compliance | **(B)** | Mark with TODO comment for CTE+Join follow-up |
| CTE+Join blocked by semantic model limitation | Plan Compliance | **(C)** | Create tracking issue |
| `EmitCteDefinition` always creates new carrier (breaks multiple CTEs) | Correctness | **(C)** | Create tracking issue |
| Discovery method boilerplate duplication | Codebase Consistency | **(C)** | Create tracking issue |
| `EmitFromCte` no validation of preceding `With()` | Correctness | **(D)** | Ignore — user error case |
| Spurious `Where(...).Orders()` manifest entry | Correctness | **(D)** | Ignore — cosmetic |
| Formatting/whitespace changes | Codebase Consistency | **(D)** | Ignore |
| Parameter comment says prepend but appends | Correctness | **(D)** | Ignore — functionally correct |
| DetectCteInnerChain candidate[0] bounds | Correctness | **(D)** | Ignore — safe as written |

## Issues Created

- #205: CTE+Join chains blocked by With() return type during source generation
- #206: CTE carrier creation conflict for multiple With() calls
- #207: Refactor CTE discovery boilerplate into shared helpers

---

## Summary

The branch implements a solid foundation for CTE support across 8 of 9 planned phases. The IR types (`CteDef`, `CteColumn`), discovery pipeline, two-pass chain analysis, WITH clause SQL rendering, and interceptor emission are all in place. The `FromCte` pattern (CTE as primary FROM source) works end-to-end across all 4 SQL dialects with runtime verification.

However, the primary use case -- CTE joined to a real table -- is blocked by a fundamental limitation: `QuarryContext.With<TDto>()` returns the base class type during source generation, preventing the semantic model from resolving subsequent context-specific methods like `.Users()`. Three potential fixes are identified in the workflow (self-referencing generic, syntactic discovery expansion, or `RegisterPostInitializationOutput`), but none has been implemented.

**Recommendation:** This branch is not merge-ready. The CTE+Join blocker and the `With<TEntity, TDto>` interceptor signature bug should be resolved before merging. Additional FromCte-only tests (with dedicated DTO classes, captured variables, multiple CTEs in FromCte-only scenarios) could be added in parallel to improve coverage of the working path.
