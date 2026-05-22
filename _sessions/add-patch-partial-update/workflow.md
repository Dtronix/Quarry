# Workflow: add-patch-partial-update

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: suspended
issue: discussion
pr:
session: 1
phases-total: 10
phases-complete: 0

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

## Suspend State

**Current phase:** IMPLEMENT, about to start phase 1 of 10.

**Status at suspend:** DESIGN and PLAN complete. plan.md written and approved (with one revision: phases 5–6 combined into a single phase, total now 10). No code changes yet. Baseline tests green (3,496/0).

**Immediate next step:** Resume into IMPLEMENT phase 1 — IR foundations. Recreate IMPLEMENT phase tasks at resume.

**WIP commit hash:** identifiable as the HEAD commit on `add-patch-partial-update` with `[WIP]` prefix (find via `git log -1 --format=%H` while no other commits are on top). Must be amended on the first real commit in IMPLEMENT phase 1 — that is, stage the first phase's changes and run `git commit --amend` to fold them into the WIP, then rewrite the message to drop the `[WIP]` marker.

**Test status:** All passing — baseline established at INTAKE.

**Unrecorded context:** None. All design decisions are in `## Decisions`. Plan is in `plan.md`. handoff.md exists with a brief orientation for the resuming session.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-22 INTAKE | 2026-05-22 PLAN (approved, suspended before IMPLEMENT) | Bootstrapped from in-session discussion. Worktree created. Baseline tests green (3,496/0). All design decisions recorded. plan.md written with 10 phases (originally 11, phases 5–6 combined per user). Approved by user; suspended for next session. |
