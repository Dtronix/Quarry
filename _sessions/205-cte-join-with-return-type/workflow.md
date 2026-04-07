# Workflow: 205-cte-join-with-return-type
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #205
pr:
session: 2
phases-total: 6
phases-complete: 2
## Problem Statement
CTE chains that use context-specific methods after `With()` (e.g., `db.With<A>(inner).Users().Join<A>(...).Select(...)`) fail during source generation because `QuarryContext.With<TDto>()` returns the base `QuarryContext` type, and methods like `Users()` only exist on the derived (generated) context class.

Cascade effect during semantic model analysis in `Quarry.Generator`:
1. `.Users()` fails to resolve on the base `QuarryContext`
2. All subsequent chain methods cascade into error types
3. The chain is silently dropped (no interceptors generated)

`FromCte()` works because it is defined on the base `QuarryContext`. The breakage is specific to entity-accessor methods that only exist on the generated derived context.

Key files referenced in the issue:
- `src/Quarry/Context/QuarryContext.cs` — base class `With()` return type
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — discovery cascade failure
- `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — `EmitCteDefinition`

Approaches identified in the issue:
1. Self-referencing generic `QuarryContext<TSelf>` where `With()` returns `TSelf` (clean, but public API change)
2. Expand `DiscoverPostCteSites` with full entity-type tracking through the chain
3. `RegisterPostInitializationOutput` for CTE methods (limited by needing context class names at compile time)

Prior attempts noted in the issue:
- `TryResolveViaChainRootContext` — resolves individual methods against the concrete context but doesn't fix cascading type errors
- `DiscoverPostCteSites` — forward-scans from `With()` to create synthetic sites but generates incorrect builder types for Join/Select

Baseline: 3012 tests pass (97 Migration + 103 Analyzers + 2812 main). No pre-existing failures.
## Decisions
- 2026-04-06: **Approach: Option A (Hybrid) — add `QuarryContext<TSelf>` as a new generic subclass of the non-generic `QuarryContext`.** Non-breaking. Internals (`QueryExecutor`, carriers, `IEntityAccessor<T>`) continue referencing the non-generic base. Users migrate by changing inheritance to `class MyDb : QuarryContext<MyDb>`. Rejected Option B (full breaking conversion) and Option C (discovery-only workaround expansion).
- 2026-04-06: **Generator integration: Path 2 — conditional emission.** `ContextParser` detects whether the user inherits the generic base and records it on `ContextInfo`. `ContextCodeGenerator.GenerateCteMethods` skips the `new With<TDto>` / `new With<TEntity, TDto>` shadow when the base is already typed. Rejected Path 1 (always emit shadow + `#pragma warning disable CS0109`) because cleaner emitted output is worth ~10 extra lines.
- 2026-04-06: **FromCte shadow unchanged.** `FromCte<TDto>` already returns `IEntityAccessor<TDto>` (a base-library type), no drift, no change needed.
- 2026-04-06: **`DiscoverPostCteSites` stays as-is.** It self-skips invocations that resolve normally (UsageSiteDiscovery.cs:1078-1085), so it becomes a harmless no-op for migrated users. Not deleted in this PR — deletion requires migrating all in-repo contexts, filed as follow-up.
- 2026-04-06: **Legacy contexts remain supported.** Users inheriting the non-generic `QuarryContext` keep current behavior: `With/FromCte` chains work, `With/entity-accessor` chains silently drop as today. A diagnostic nudging migration is a future improvement, not part of this PR.
- 2026-04-06: **Architectural principle to be captured in PR body:** "Typed API declarations belong in the hand-written base library so the source generator's own discovery SemanticModel can see them. Implementations belong in generator output." The codebase already follows this for entity accessors (user writes `partial IEntityAccessor<User> Users();`); `With<TDto>` violated it by putting the typed return in generator output only. This pattern should guide future chain-method additions and a follow-up cleanup of `NavigationList<T>` declaration (which would dissolve `DiscoverPostJoinSites` the same way).
- 2026-04-06: **Follow-up issues to file during REMEDIATE (class C):** (1) migrate all in-repo contexts to `QuarryContext<TSelf>` and delete `DiscoverPostCteSites`; (2) apply the same principle to `NavigationList<T>` (user-declared partial navigation properties, generator emits body) to eliminate `DiscoverPostJoinSites`.
- 2026-04-06: **New test context rather than migrating existing ones.** Add `CteChainTestDbContext : QuarryContext<CteChainTestDbContext>` reusing existing entity types. Keeps blast radius on existing test fixtures to zero and isolates the new-path tests.

## Suspend State
- Current phase: **PLAN**, awaiting explicit user approval of `plan.md`
- Sub-step: plan.md written and presented to the user with 6 phases; last assistant message summarized scope and asked "Does this look right, or are there phases you want split further, reordered, or scoped differently before IMPLEMENT starts?" — user replied with "Handoff and push to remote" instead of answering, so plan approval is **pending** on resume
- Working tree: clean after the suspend commit (all session artifacts staged and committed)
- WIP commit: tip of `205-cte-join-with-return-type` at suspend (commit subject starts with `[WIP] #205 PLAN phase suspended`). Session-only; no code changes yet.
- Test status: baseline 3012 tests passing (97 Migration + 103 Analyzers + 2812 main), 0 pre-existing failures. No code changes have been made on this branch yet — only session artifacts exist.
- Decisions locked in during DESIGN (see `## Decisions`): Option A (Hybrid `QuarryContext<TSelf>`), Path 2 (conditional generator emission), legacy contexts remain supported, new `CteChainTestDbContext` fixture instead of migrating existing ones, architectural findings to be captured for PR body, follow-ups to be filed during REMEDIATE.
- **Immediate next step on resume:** re-present the plan summary to the user and get explicit approval (or apply whatever edits they request) before transitioning PLAN → IMPLEMENT.
- Unrecorded context: none. All design rationale lives in `## Decisions` and `plan.md`. The architectural principle discussion from the session (why `QuarryContext<TSelf>` helps beyond just #205) is captured inline in plan.md under "Key concept" and will be expanded in Phase 6's `architectural-findings.md` during IMPLEMENT.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #205 loaded, worktree created, baseline green (3012 tests) |
| 1 | DESIGN | PLAN | Design approved: Option A (QuarryContext<TSelf> generic subclass) + Path 2 (conditional generator emission). Principle captured for PR body. |
| 1 | PLAN | PLAN | plan.md written (6 phases). Suspended by user before explicit plan approval; session pushed to remote for handoff. |
| 2 | PLAN | — | Resumed from suspend. Re-presenting plan for approval. |
