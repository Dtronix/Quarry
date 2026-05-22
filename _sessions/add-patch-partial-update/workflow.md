# Workflow: add-patch-partial-update

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 3
phases-total: 10
phases-complete: 6

## Problem Statement

Today `Update().Set(...)` supports three forms, all of which lock in the column set at compile time:

- `Set(u => u.X = v)` and block lambda — column set fixed by lambda body.
- `Set(new User { X = v })` — column set fixed by initializer syntax at the call site (`UpdateSetPoco`).

There is no way to provide a partial update where the column set varies at runtime — e.g., a helper that takes optional inputs and updates only the non-null ones. The only workaround is chained `if (cond) builder.Set(...)` calls, which: (1) consume conditional-bit budget shared with `Where` conditionals (8-bit ceiling), (2) bloat the call chain, (3) cannot cross method boundaries (the literal-initializer trick only works at the call site).

**Solution direction (approved 2026-05-22):** generate a `User.Patch` mutable struct per entity. Property setters track which fields were assigned via a `ulong` mask. Two new Set overloads:

- `Set(User.Patch patch)` — accepts a Patch value; handles cross-method-boundary composition.
- `Set(PatchAction<User> action)` — accepts a builder lambda mutating a Patch by ref; lambda runs verbatim with full C# semantics (no IR reconstruction).

Both paths are Tier=Opaque: runtime SET assembly using a per-chain fragment table; reuses existing collection-expansion `__shift` machinery for dialect-aware parameter renumbering. Existing `Set(new User { ... })` (UpdateSetPoco) and assignment-lambda (`UpdateSetAction`) paths are unchanged.

**Baseline tests:** 3,496 passing, 0 failing across Quarry.Analyzers.Tests (146), Quarry.Migration.Tests (201), Quarry.Tests (3149). No pre-existing failures to exclude.

## Decisions

### 2026-05-22 — API shape
Two new Set overloads backed by a generated `User.Patch` struct. The existing `Set(new User { ... })` path is unchanged and remains the optimal choice for literal column sets. Patch is the minimum addition needed to cover the cases POCO write-tracking cannot reach.

### 2026-05-22 — Hybrid SQL strategy
Use the existing tokenized-suffix shift machinery (`ComputeShiftExprForIndex`, `Quarry.Internal.ParameterNames`) for runtime SET assembly. Patch usage is always Tier=Opaque — mask is runtime-determined, SET clause assembled at execute time. No prebuilt-variant fast path for Patch (anyone wanting prebuilt SQL writes `new User { ... }`).

### 2026-05-22 — Reject Option C (conditional-aware Set(Action<T>))
Reconstructing arbitrary C# condition expressions from lambda syntax has a long restriction list and silent-miscompilation hazards. The natural alternative ("just invoke the lambda") requires entity setters to track writes — which is the Patch struct. The lambda surface collapses into A′ as `Set(PatchAction<User>)`.

### 2026-05-22 — Empty Patch is a runtime throw
A Patch with `__mask == 0` at execute time throws `InvalidOperationException`. No compile-time prevention is possible because the default-constructed struct state is zero-mask.

### 2026-05-22 — Defer Patch.From(entity) seeding
Ship v1 with empty-Patch-only — mask grows only via setters. Seeded patches (e.g., `Patch.From(existing)`) raise semantics questions about whether mask should auto-fill from the source; leave to a follow-up.

### 2026-05-22 — Patch emission inline in EntityCodeGenerator
Emit the `Patch` nested struct inside `EntityCodeGenerator.GenerateEntityClass` (same file as the entity). One emission path to maintain; matches the existing pattern where all entity-shape code lives in one place.

### 2026-05-22 — PatchAction delegate
Define `public delegate void PatchAction<T>(ref T patch)` in `Quarry` runtime (single shared definition). Usage: `.Set((ref User.Patch p) => { ... })`. Generic `T` is the Patch type; inferred from the `Set` overload's parameter signature.

### 2026-05-22 — Patch column inclusion
Include all columns except Identity and Computed. Reuse `InsertInfo.FromEntityInfo` filtering (which already implements this exclusion). FKs (`EntityRef<...>`) are included as updatable — matches existing `Set(new User { Ref = ... })` semantics.

### 2026-05-22 — Empty Patch is a runtime throw
Generated terminal throws `InvalidOperationException("Set received a Patch with no fields assigned.")` when `__mask == 0`. Loud failure; no silent no-op. SQL with empty `SET` clause is invalid in every dialect anyway.

### 2026-05-22 — InterceptorKind names
`InterceptorKind.UpdateSetPatch` for `Set(User.Patch)`. `InterceptorKind.UpdateSetPatchAction` for `Set(PatchAction<User.Patch>)`. Mirrors existing `UpdateSetPoco` / `UpdateSetAction` naming.

### 2026-05-22 — PatchInfo IR + `{__PATCH_SET__}` tokenized placeholder
New `PatchInfo` model alongside `InsertInfo`. Tokenized SQL gets a new `SqlSegmentKind.PatchSet` segment. `__setShift` joins `__colShift` in `ComputeShiftExprForIndex`, ordered first in the sum (SET comes before WHERE in SQL). Clean separation from Insert/Update POCO paths.

### 2026-05-22 — ulong mask, hard cap at 64 updatable columns
Single `ulong __mask` field on Patch. Entities with >64 updatable columns raise **QRY045** at generation time (first open slot in the QRY030–QRY044 range; QRY045–049 are unallocated). 64 columns is well above realistic schemas; existing Where-conditional cap is 8.

### 2026-05-22 — Rename `InsertColumnInfo` → `WriteColumnInfo`
Now that the type is referenced by both `InsertInfo` and `PatchInfo` (and likely future batch-update flows), the `Insert` prefix misleads. Renamed in place and pulled into its own `Models/WriteColumnInfo.cs` for clarity. No external consumers (type is `internal`).

### 2026-05-22 — Phase 7 binder shape: per-column static methods
Generated SET binders take the form `_BindPatch_{Column}(DbCommand, in Patch, int paramIdx)` — one static method per Patch column. The per-chain fragment table holds `(Bit, Prefix, Action<...> Bind)` triples that point at those methods. Mirrors the per-column reader delegate pattern in `ReaderCodeGenerator`; keeps the runtime SET loop body tiny; debuggable into a named method. Locked now so Phase 5/6 fragment-table emission and Phase 2 mask-bit ordering align.

### 2026-05-22 — Patch Set overloads as extension methods (not DIMs)
Initially tried adding the new patch overloads as default interface methods (DIMs) alongside the existing `Set(T)` and `Set(Action<T>)` on `IUpdateBuilder<T>` / `IExecutableUpdateBuilder<T>`. With the generic DIMs in place, the existing `Set(T entity)` interceptors stopped binding — Roslyn no longer routed the user's `.Set(new User { ... })` call through the emitted `Set_<id>(this IUpdateBuilder<User>, User entity)` interceptor, even though overload resolution clearly picked the non-generic Set(T) DIM and the interceptor signature matched. Switched to extension methods in a static helper class (`UpdateBuilderPatchExtensions`) — instance-method lookup still picks up the existing DIMs for non-Patch args (interceptor binds fine), and extension lookup finds the Patch overloads when DIMs aren't applicable (User.Patch isn't a User, lambdas with `ref TPatch` parameter aren't `Action<T>`). Same compile-time enforcement via `IPatchFor<T>` constraint; no impact on the existing UpdateSetPoco / UpdateSetAction paths.

## Suspend State

**Current phase:** IMPLEMENT — about to start phase 4 of 10. Phases 1–3 complete and committed.

**Status at suspend:** Working tree clean. Branch `add-patch-partial-update` is **4 commits ahead of origin** (commits `09ad46a`, `63278f6`, `0fae28c`, `c276b0b` not yet pushed — push is a user decision and was not authorized).

**Last commit (HEAD):** `c276b0b feat(generator): discover UpdateSetPatch + UpdateSetPatchAction call sites`.

**Test status:** All passing — 146 + 201 + 3175 = **3,522 / 0** (3,496 baseline + 26 new across Phases 1–3).

**Immediate next step:** Resume into IMPLEMENT phase 4 — **bind `PatchInfo` from `EntityInfo` in `CallSiteBinder`**. Plan/handoff describe the change: in `CallSiteBinder.cs` (~line 184), add a branch parallel to the existing `UpdateInfo` build — when `raw.Kind` is `UpdateSetPatch` or `UpdateSetPatchAction`, populate `BoundCallSite.PatchInfo` via `PatchInfo.FromEntityInfo(entry.Entity, dialect, isLambdaForm: raw.Kind == UpdateSetPatchAction)`. Add binder tests asserting `PatchInfo.Columns.Count` matches expected updatable count and that Identity / Computed columns are excluded.

**No WIP commit needed** — working tree was clean at suspend.

**Unrecorded context:** None. All design decisions for Phases 1–3 are recorded in `## Decisions` (incl. the mid-Phase-3 DIM-vs-extension pivot, the `WriteColumnInfo` rename, and the Phase 7 binder-shape lock-in). Plan and handoff are current. Phase-4 open question carry-over: none — the column model is settled (`WriteColumnInfo`), the lambda/value discrimination is settled (covered by `IsPatchType` / `IsPatchActionDelegateType` in Phase 3), and `PatchInfo.FromEntityInfo` already exists.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-22 INTAKE | 2026-05-22 PLAN (approved, suspended before IMPLEMENT) | Bootstrapped from in-session discussion. Worktree created. Baseline tests green (3,496/0). All design decisions recorded. plan.md written with 10 phases (originally 11, phases 5–6 combined per user). Approved by user; suspended for next session. |
| 2 | 2026-05-22 IMPLEMENT (resume) | 2026-05-22 IMPLEMENT (suspended after Phase 3) | Resumed from suspend. Completed Phases 1–3 of 10. Phase 1 (IR foundations) + a follow-on refactor renaming `InsertColumnInfo` → `WriteColumnInfo`. Phase 2 (Patch struct emission) — mid-phase fix to use `ColumnInfo.IsValueType` instead of name-heuristic for non-nullable-reference detection (custom-mapped value types like `Money` broke otherwise). Phase 3 (call-site discovery) — initial DIM attempt broke existing `Set(T entity)` interceptor binding; pivoted to extension methods (`UpdateBuilderPatchExtensions` + `IPatchFor<T>` marker), discovery classifies via `methodSymbol.Parameters[0].Type`. WIP commit `3432ac2` left as predecessor (FINALIZE squash-merge will collapse it). Tests: 3,522/0. Branch +4 unpushed commits at suspend. |
| 3 | 2026-05-22 IMPLEMENT (resume Phase 4) | 2026-05-22 IMPLEMENT Phase 6 complete | Resumed from suspend. Baseline reverified: 3,522/0. Phase 4 complete: `CallSiteBinder` populates `PatchInfo` for UpdateSetPatch/UpdateSetPatchAction kinds; added `CallSiteBinderPatchTests` (7). Phase 5 complete: new `SqlExprKind.PatchSetPlaceholder` + `PatchSetPlaceholderExpr` node renders as literal `{__PATCH_SET__}`; ChainAnalyzer emits a single sentinel SetTerm for Patch sites (zero per-column QueryParameters); `SqlAssembler.RenderUpdateSql` detects the placeholder and skips the ` SET ` keyword (runtime emitter owns it); `TerminalEmitHelpers.ParseSqlSegments` adds `SqlSegmentKind.PatchSet` recognition. Added `SqlAssemblerPatchTests` (7) + `ParseSqlSegmentsPatchTests` (6). Phase 6 complete: `EmitInlineSqlBuilder` handles `SqlSegmentKind.PatchSet` — declares `int __setShift = 0;` at top when any PatchSet segment exists, scalar segments add `+ __setShift` to their index expression, PatchSet case emits the empty-mask guard + ` SET ` literal + per-fragment runtime loop (dialect-correct placeholder via `__setShift + __colShift`, or `__setShift + 1 + __colShift` for PG, or `?` for MySQL). New `patchFragmentsRef` parameter (default `__patchFragments`) lets Phase 7 wire in the real per-chain table reference. `ComputeShiftExprForIndex` Patch-awareness deferred to Phase 9 (diagnostic-path concern). Added `EmitInlineSqlBuilderPatchTests` (10). Tests: 3,552/0. |
