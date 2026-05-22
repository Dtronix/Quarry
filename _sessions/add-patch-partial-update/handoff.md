# Work Handoff: add-patch-partial-update

## Key Components

- **Patch struct**: per-entity nested mutable struct (`User.Patch`) with write-tracking property setters, `ulong __mask` field, and `_Mask_X` constants. Emitted by `EntityCodeGenerator`.
- **PatchAction<T> delegate**: single runtime definition in `Quarry`, used as `Set((ref User.Patch p) => …)`.
- **PatchInfo IR**: new model alongside `InsertInfo`, attached to `BoundCallSite`.
- **Two InterceptorKinds**: `UpdateSetPatch` (value overload), `UpdateSetPatchAction` (lambda overload).
- **`{__PATCH_SET__}` tokenized SQL placeholder**: emitted by `SqlAssembler`, recognized by `TerminalEmitHelpers.ParseSqlSegments`, expanded at runtime via fragment-table walk.
- **`__setShift` runtime shift variable**: joins existing `__colShift` in `ComputeShiftExprForIndex` to renumber WHERE / collection placeholders behind the runtime-assembled SET clause.
- **Carrier fields**: chains with Patch SET get `Patch` + `PatchMask` fields on the generated carrier class.

## Completions (This Session)

- INTAKE: worktree created (`add-patch-partial-update` branch). Baseline tests green: 3,149 + 201 + 146 = **3,496 pass / 0 fail**.
- DESIGN: full exploration of `EntityCodeGenerator`, `UsageSiteDiscovery` (UpdateSetPoco path at lines 473–486, 2611), `CallSiteBinder` (UpdateInfo at line 184), `ChainAnalyzer` (line 1150), `SqlAssembler.RenderUpdateSql` (line 732), and `TerminalEmitHelpers.ParseSqlSegments` / `EmitInlineSqlBuilder` / `ComputeShiftExprForIndex`. All decisions recorded in workflow.md `## Decisions`.
- PLAN: `plan.md` written with 10 phases. User approved; suspended before implementation.

## Previous Session Completions

None — first session.

## Progress

- Phase: PLAN complete. Ready to start IMPLEMENT phase 1.
- Phases complete: 0 / 10.

## Current State

- Working tree: clean except for `_sessions/` (will be in the suspend WIP commit).
- Branch: `add-patch-partial-update` at base `master` (`b758e83 Fix nested int-aggregate projection type resolution (#294) (#298)`).
- No code edits yet. No failed approaches to record.

## Known Issues / Bugs

None.

## Dependencies / Blockers

None.

## Architecture Decisions

- **Patch struct lives inline in `EntityCodeGenerator`**, not in a separate `.g.cs` file. One emission path; matches the pattern of having all entity-shape code in one place.
- **Patch is always Tier=Opaque** — runtime SET assembly, never prebuilt variants. Users wanting prebuilt SQL keep using `Set(new User { … })` (untouched `UpdateSetPoco`).
- **`__setShift` is ordered FIRST in the shift sum** (`ComputeShiftExprForIndex`) because SET comes before WHERE in SQL. Order matters for PostgreSQL/SqlServer/SQLite where placeholders carry indices; for MySQL it just dictates bind order.
- **Empty Patch (`__mask == 0`) is a runtime throw** in the generated terminal, not a silent no-op. SQL with empty SET clause is invalid in every dialect.
- **`PatchSetPlaceholderExpr`** is a new SqlExpr node — a dedicated type rather than a magic `LiteralExpr` — because it renders identically across all dialects (the literal token `{__PATCH_SET__}`) and signals special runtime handling downstream.
- **Hard 64-column cap via `ulong`** mask. Above 64, raise `QRY045` at generation time. Multi-word mask deferred.
- **`PatchAction<T>` is a single generic delegate** in the runtime, not per-entity. User writes `(ref User.Patch p) => …`; compiler infers `T = User.Patch` from the `Set` overload's parameter type.

## Open Questions

- Phase 4 / 5 boundary: should `PatchInfo` reuse `InsertColumnInfo` exactly, or have its own `PatchColumnInfo` clone? Lean toward reusing `InsertColumnInfo` since the shape is identical for our purposes (column metadata only). Decide during Phase 1 implementation.
- Phase 7 fragment binder shape: emit per-column static binder methods (one delegate target per column), OR inline a `switch` on `Bit` inside the assembly loop. Lean toward static methods (matches the per-column reader delegates pattern in `ReaderCodeGenerator`). Decide during Phase 7.

## Next Work (Priority Order)

1. **Phase 1: IR foundations** — `PatchInfo` model, `InterceptorKind.UpdateSetPatch`/`UpdateSetPatchAction`, `BoundCallSite.PatchInfo` property, `Quarry.PatchAction<T>` delegate, `QRY045` diagnostic descriptor. No behavior changes yet — just structural additions. Tests: `PatchInfo` equality round-trip.
2. **Phase 2: Patch struct emission** — extend `EntityCodeGenerator.GenerateEntityClass` to emit nested `Patch` struct after navigation properties, before the closing brace. Apply Identity + Computed exclusion. Emit QRY045 if updatable count > 64.
3. Subsequent phases proceed per `plan.md`.

For phase-by-phase details, see `plan.md`.

## Resume Checklist

When resuming:
1. Bootstrap finds this active suspended workflow, reads workflow.md and handoff.md, increments `session` to 2, sets `status: active`.
2. Verify baseline: `dotnet test --nologo --verbosity minimal` — should still be 3,496/0.
3. Recreate IMPLEMENT phase tasks (one per remaining phase in plan.md).
4. Start Phase 1.
