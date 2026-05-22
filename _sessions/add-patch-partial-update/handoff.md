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

- Phase: IMPLEMENT phase 6 complete. Ready to start phase 7.
- Phases complete: 6 / 10.

## Current State

- Working tree: clean.
- Branch: `add-patch-partial-update` at base `master` (`b758e83 Fix nested int-aggregate projection type resolution (#294) (#298)`).
- Tests green: 146 + 201 + 3175 = **3,522** (baseline 3,496 + 7 PatchInfoTests + 13 EntityCodeGeneratorPatchTests + 6 UsageSiteDiscoveryPatchTests).
- Phase 1 added: `Models/PatchInfo.cs` (reuses `WriteColumnInfo`), `InterceptorKind.UpdateSetPatch` + `InterceptorKind.UpdateSetPatchAction`, `BoundCallSite.PatchInfo`, `src/Quarry/PatchAction.cs` delegate, `DiagnosticDescriptors.PatchColumnLimitExceeded` (QRY045).
- Phase 1 refactor: `InsertColumnInfo` renamed to `WriteColumnInfo` (`Models/WriteColumnInfo.cs`).
- Phase 2 added: `EntityCodeGenerator.GeneratePatchStruct` + `GeneratePatchProperty` emit a nested `public struct Patch : Quarry.IPatchFor<TEntity>` on every entity with 1–64 updatable columns. Backing-field nullability resolved from `ColumnInfo.IsValueType`. QRY045 reported from `QuarryGenerator` before emission; struct emission self-suppresses for >64 updatable columns.
- Phase 3 added: `src/Quarry/IPatchFor.cs` marker interface (constrains the Patch Set overloads to the matching entity); `src/Quarry/Query/Modification/UpdateBuilderPatchExtensions.cs` static extension class with four `Set<T, TPatch>` overloads (value form + lambda form, on both `IUpdateBuilder<T>` and `IExecutableUpdateBuilder<T>`); `UsageSiteDiscovery` classifies the four `Set` forms via `methodSymbol.Parameters[0].Type` — `PatchAction<TPatch>` delegate → `UpdateSetPatchAction`, struct implementing `IPatchFor<>` → `UpdateSetPatch`, else falls through to non-generic UpdateSetAction / UpdateSetPoco. Initial attempt put the new overloads as DIMs on the builder interfaces; that broke existing `Set(T entity)` interceptor binding (Roslyn no longer routed the call through the emitted interceptor once the overload set included generic DIMs). Extension methods avoided the issue cleanly.
- Phase 4 added: `CallSiteBinder.Bind` populates `BoundCallSite.PatchInfo` via `PatchInfo.FromEntityInfo` for `UpdateSetPatch` / `UpdateSetPatchAction` kinds (IsLambdaForm flag tracks the overload). `CallSiteBinderPatchTests` (7 tests) covers: value-form population, lambda-form population, identity+computed exclusion, no-op for UpdateSetPoco / UpdateSetAction, no-throw on unknown entity, dialect quoting from context.
- Phase 5 added: new `SqlExprKind.PatchSetPlaceholder` enum value, new `PatchSetPlaceholderExpr : SqlExpr` node (renders as the literal `{__PATCH_SET__}` token in every dialect; exposes `PatchSetPlaceholderExpr.Token` const for the parser). `SqlExprRenderer.RenderExpr` switch-case renders the token verbatim. `ChainAnalyzer` adds a branch parallel to `UpdateSetPoco` for `UpdateSetPatch` / `UpdateSetPatchAction`: emits a single sentinel `SetTerm` carrying an empty `ResolvedColumnExpr` + `PatchSetPlaceholderExpr`, no per-column `QueryParameter` entries (runtime assembler handles them). `SqlAssembler.RenderUpdateSql` detects `activeSetTerms.Count == 1 && activeSetTerms[0].Value is PatchSetPlaceholderExpr` and emits ` {__PATCH_SET__}` instead of ` SET col = @pN, ...` — the runtime emitter writes ` SET ` itself. WHERE param indices remain at 0/$1/@p0 since Patch contributes zero compile-time parameters. `TerminalEmitHelpers` adds `SqlSegmentKind.PatchSet` + `SqlSegment.PatchSet()` factory; `ParseSqlSegments` recognises the `{__PATCH_SET__}` literal (ordinal compare, no regex). `EmitInlineSqlBuilder` is unchanged — Phase 6 adds the runtime SET-assembly case for the new `PatchSet` segment kind.
- Phase 6 added: `EmitInlineSqlBuilder` PatchSet case + a new `patchFragmentsRef` parameter (default `"__patchFragments"`). When any PatchSet segment is present the method emits `int __setShift = 0;` at the top and routes all scalar segments through `(idx + __colShift + __setShift)` (or `(idx + 1 + __colShift + __setShift)` for PG). The PatchSet case itself emits: the empty-mask `InvalidOperationException` guard (`__c.PatchMask == 0UL`), `__sb.Append(" SET ")`, then a `for` over `{patchFragmentsRef}.Length` that skips inactive bits, appends `, ` between active fragments, appends `__frag.Prefix` + dialect-correct placeholder (`@p{__setShift + __colShift}` for SQLite/SqlServer, `${__setShift + 1 + __colShift}` for PG, `?` for MySQL), and increments `__setShift`. Phase 7 wires the per-chain fragment table reference + carrier `Patch`/`PatchMask` fields + the actual `_BindPatch_X` static binder methods so end-to-end Patch chains execute. `ComputeShiftExprForIndex` Patch-awareness deferred to Phase 9 — that helper feeds the diagnostic-emit path, which is orthogonal to the execute-time SQL builder updated here and will be addressed alongside Patch + Where(collection) composition coverage.

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

- (Resolved 2026-05-22) Phase 4/5 column model: reused the existing model and renamed it `WriteColumnInfo` so the name reflects shared use across insert + patch.
- (Resolved 2026-05-22) Phase 7 binder shape: per-column static binder methods (matches the per-column reader delegates pattern in `ReaderCodeGenerator`).
- (Resolved 2026-05-22, Phase 6→7 transition) Fragment-table shape revised: the plan's original `(ulong Bit, string Prefix, Action<DbCommand, Patch, int> Bind)` is unworkable — `Action<>` cannot take `in TPatch` and `__cmd` does not exist during the inline SQL builder. New shape: `_PatchFragments` is `(ulong Bit, string Prefix)[]` used purely by the SQL builder; binding is done by a separate static `_BindPatchParams(DbCommand cmd, in Patch patch, ulong mask, int startIdx)` method (unrolled per-column ifs) called from the post-`__cmd` binding loop. Matches Quarry's existing post-command binding pattern.

## Next Work (Priority Order)

1. **Phase 7: end-to-end value-form `Set(Patch)`** — carrier fields (`Patch`, `PatchMask`), `_PatchFragments` static field, `_BindPatchParams` static method, `EmitCarrierSqlDispatch` Patch detection, `EmitUpdateSetPatch` interceptor body, `FileEmitter` dispatch, `InterceptorRouter` Clause category entries, terminal binding hook, cross-dialect + end-to-end tests. See `## Suspend State` in `workflow.md` for the detailed 9-step breakdown including the fragment-table shape revision.
2. **Phase 8: lambda-form `Set(PatchAction<Patch>)`** — `ClauseBodyEmitter.EmitUpdateSetPatchAction` is the only new piece; reuses everything from Phase 7. Interceptor body invokes `action(ref __c.Patch)` then mirrors `__c.PatchMask = __c.Patch.__mask`. Add cross-dialect + end-to-end SQLite tests with runtime conditional toggling.
3. **Phase 9: integration edge cases** — Patch + Where(captured) shift composition, Patch + Where(ids.Contains) collection-expansion composition, FK column update, enum cast, custom type mapping, sensitive redaction, IExecutableUpdateBuilder path. Also revisits `ComputeShiftExprForIndex` Patch-awareness for diagnostic-emit consistency.
4. **Phase 10: docs** — `docs/articles/modifications.md` adds a Patch subsection; `src/Quarry.Generator/llm.md` adds Patch to InterceptorKind table + a "Partial Updates via Patch" section + QRY045.

For phase-by-phase details, see `plan.md`.

## Resume Checklist

When resuming:
1. Bootstrap finds this active suspended workflow, reads workflow.md and handoff.md, increments `session` to 4, sets `status: active`.
2. Verify baseline: `dotnet test --nologo --verbosity minimal` — should still be **3,552 / 0** (146 + 201 + 3205).
3. Recreate IMPLEMENT tasks for Phases 7–10.
4. Start Phase 7 — see `## Suspend State` in `workflow.md` for the 9-step breakdown. Begin with step 1 (FieldRole enum value) so subsequent steps' code references compile.
