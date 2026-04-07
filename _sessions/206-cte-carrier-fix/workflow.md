# Workflow: 206-cte-carrier-fix

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: FINALIZE
status: active
issue: #206
pr: Dtronix/Quarry#212
session: 1
phases-total: 5
phases-complete: 5

## Problem Statement
**Issue #206: CTE carrier creation conflict for multiple With() calls**

`EmitCteDefinition` always creates a new carrier (`new CarrierClass { Ctx = @this }`). For multiple CTEs (`db.With<A>(...).With<B>(...)`), the second `With()` discards the first carrier, losing CTE A's state.

**Location:** `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs:EmitCteDefinition` (line ~111)

**Diagnostics:** Each `With()` interceptor creates its own carrier. The second one receives the first carrier (disguised as the context class via `Unsafe.As`) but creates a NEW carrier instead of extending the existing one.

**Suggested Approach (from issue):** The first `With()` should create the carrier; subsequent `With()` calls should cast the receiver back to the carrier and add CTE parameters to it. This requires the chain analyzer to distinguish "first CTE definition" from "subsequent CTE definition" and generate different interceptor bodies.

**Baseline Tests:** All 3,012 tests passing (97 Migration + 103 Analyzers + 2,812 Quarry).

## Decisions

**2026-04-06: Same-DTO-twice case (e.g., `db.With<X>(a).With<X>(b)`) handling**
Add a compile-time diagnostic detecting duplicate CTE names in a single chain. Two CTEs sharing one name produce an invalid `WITH` clause and the existing `EmitCteDefinition` source comment already calls this out as ambiguous. Out of scope: generating uniquely-aliased CTEs (X_1, X_2) for repeated DTOs — that's much larger and not requested.

**2026-04-06: First-CTE-site detection strategy**
Use a local count inside `EmitCteDefinition`: walk `chain.ClauseSites`, count `CteDefinition` kinds preceding the current site by `UniqueId`. If count == 0 → emit `new {carrier} { Ctx = @this }`; otherwise emit `Unsafe.As<{carrier}>(@this)`. No IR changes, no plumbing through `FileEmitter` — fully local. O(N) per site but N is small (chain length).

**2026-04-06: Test coverage**
Add comprehensive multi-CTE tests to `CrossDialectCteTests.cs`: (1) two distinct CTEs both with captured parameters, FromCte<A> as primary; (2) two CTEs with one used in JOIN; (3) three CTEs to validate generalization beyond two. All cross-dialect (sqlite/pg/mysql/ss) with executable assertions on lite. Plus a unit test for the new duplicate-CTE-name diagnostic.

**2026-04-06: Scope expansion — multi-CTE has TWO bugs, not one**
While writing the regression test, observed that issue #206 only describes the carrier-discard bug but multi-CTE chains are also broken at the SQL generation level. `SqlAssembler.cs:161` embeds `cte.InnerSql` as a raw string in the WITH clause; each inner CTE chain is assembled independently with parameter indices starting at `@p0`, so a multi-CTE outer SQL ends up with multiple `@p0` references that collide on named-placeholder dialects (sqlite/pg/ss). Confirmed via the generated `Chain_11._sql` literal in `TestDbContext.Interceptors...g.cs:129`. MySQL `?` is positional and self-corrects. Both bugs must be fixed for issue #206's example (`db.With<A>(...).With<B>(...)`) to actually work end-to-end. Fix both in this PR; the cleaner approach is to re-render inner CTE SQL at outer-assembly time using the existing `paramBaseOffset` parameter on `RenderSelectSql`, passing `cteDef.ParameterOffset` so the placeholders are rebased into the outer chain's global numbering. Caveat: assumes single-mask inner chains; multi-mask inner chains would need additional handling but are not exercised by any current test.

**2026-04-06: QRY083 dropped — conditional-CTE scenario is unreachable in practice**
Initial design included QRY083 to reject `CteDefinition`/`FromCte` sites with a non-null `NestingContext`. During Phase 5 implementation I verified that no C# expression shape can actually produce a `CteDefinition` site with `NestingDepth > baselineDepth`: fluent chains are single contiguous expressions, so all sites (including the execution terminal) share the same nesting depth and `relativeDepth == 0`. Partial-conditional CTE can't be expressed in user code. My earlier concern about "the new failure mode my fix could introduce" was based on a scenario that doesn't exist — the `Unsafe.As<{carrier}>(@this)` path is always reached only after the first `With<>` has executed. Dropped QRY083 entirely (descriptor, check, registry entry, tests). Total phase count drops to 5.

**2026-04-06: Carrier identity & receiver typing for subsequent With() calls**
Each `With<>` interceptor returns the carrier `Unsafe.As<contextClass>(__c)`, so subsequent calls receive `@this` as the contextClass type but the runtime instance is still the carrier from the first `With<>`. `Unsafe.As<{carrier}>(@this)` recovers it. The shared per-chain `CarrierPlan` instance guarantees the same carrier class is used by all sites in the chain, so the cast is type-safe at runtime.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-06 INTAKE | 2026-04-06 IMPLEMENT P1 | Loaded #206, designed fix, discovered second SQL placeholder collision bug, plan revised to 6 phases, P1 (SqlAssembler placeholder rebasing) committed |
