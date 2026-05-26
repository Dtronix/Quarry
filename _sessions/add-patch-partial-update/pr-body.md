## Summary
- Adds two new `.Set(...)` overloads on `Update()` chains for runtime-conditional column sets, backed by a generated per-entity `Patch` mutable struct with write-tracking property setters and a `ulong` mask.
- Existing `Set(new T { … })` (`UpdateSetPoco`) and `Set(u => u.X = v)` (`UpdateSetAction`) paths are untouched.

## Reason for Change
Today's `Update().Set(...)` locks the column set at compile time (either via the assignment lambda or the entity initializer). There is no way to vary the column set per call — e.g., a helper that takes optional inputs and updates only the non-null ones. The only workaround is chained `if (cond) builder.Set(...)` calls, which (1) consume the 8-bit conditional-bit budget shared with `Where` conditionals, (2) bloat the call chain, (3) cannot cross method boundaries (the literal-initializer trick only works at the call site).

## Impact
- Every entity gets a nested `public struct Patch : Quarry.IPatchFor<TEntity>` (1–64 updatable columns; >64 raises QRY045 at generation time).
- New `Quarry.PatchAction<T>` delegate; new `Quarry.IPatchFor<T>` marker; new `Quarry.UpdateBuilderPatchExtensions` static class providing the two extension overloads `Set<T, TPatch>(IUpdateBuilder<T>, TPatch)` and `Set<T, TPatch>(IUpdateBuilder<T>, PatchAction<TPatch>)` (plus mirrors on `IExecutableUpdateBuilder<T>`).
- New diagnostics: **QRY045** (Error, >64 updatable columns), **QRY046** (Warning, `Set` argument syntactically references `.Patch` but doesn't match a supported construction shape).
- Patch chains are `OptimizationTier.PrebuiltDispatch` for the chain shape but always opaque for SQL — every execute rebuilds the SET clause from the runtime mask.

## Plan items implemented as specified
- **Phase 1** — IR foundations: `PatchInfo`, `InterceptorKind.UpdateSetPatch` + `UpdateSetPatchAction`, runtime `PatchAction<T>` delegate, QRY045 descriptor.
- **Phase 2** — `EntityCodeGenerator.GeneratePatchStruct` emits the nested struct inline; Identity / Computed excluded; backing-field nullability resolved from `ColumnInfo.IsValueType`.
- **Phase 4** — `CallSiteBinder` populates `BoundCallSite.PatchInfo` via `PatchInfo.FromEntityInfo`.
- **Phase 5** — `PatchSetPlaceholderExpr` renders `{__PATCH_SET__}`; `ChainAnalyzer` emits a sentinel `SetTerm`; `SqlAssembler` skips the `SET` keyword in the prefix; `TerminalEmitHelpers.ParseSqlSegments` recognizes the token.
- **Phase 6** — `EmitInlineSqlBuilder` PatchSet case: empty-mask handling, per-fragment runtime loop, dialect-correct placeholder, `__setShift` accumulation.
- **Phase 8** — `Set(PatchAction<TPatch>)` lambda overload wired end-to-end (cross-dialect + runtime-conditional tests).
- **Phase 10** — Docs (`modifications.md`, `Quarry.Generator/llm.md`, root `llm.md`).

## Deviations from plan implemented
- **Phase 3 was rewritten mid-implementation** from semantic overload-resolution to **syntax-only** Patch classification. The original design used Roslyn's `methodSymbol.Parameters[0].Type` + an `IPatchFor<>` check, but that fails in the real IIncrementalGenerator pipeline because the SyntaxProvider's `SemanticModel` doesn't see the generator-emitted `Entity.Patch` struct — Roslyn binds `Set(somePatch)` to the SetPoco DIM and discovery emits the wrong-shape interceptor. The replacement classifier (`IsPatchConstructionExpression`, `IsPatchVariableReference`) inspects the argument expression syntax only and supports `new X.Patch{}` / `default(X.Patch)` / local-variable initialized to those.
- **Patch overloads as extension methods, not DIMs.** Initial DIM placement on `IUpdateBuilder<T>` / `IExecutableUpdateBuilder<T>` broke existing `Set(T entity)` interceptor binding — once the generic DIMs were in the overload set, Roslyn stopped routing the user's call through the emitted interceptor. Moved to `UpdateBuilderPatchExtensions` static class with `IPatchFor<T>` constraint; instance-method lookup picks up existing DIMs for non-Patch args, extension lookup finds Patch overloads.
- **Phase 7 fragment-table shape revised.** Plan's `(ulong Bit, string Prefix, Action<DbCommand, Patch, int> Bind)` proved unworkable (Action<> can't take `in TPatch`, `__cmd` doesn't exist during inline SQL building). Final shape: `(ulong Bit, string Prefix)[]` consumed by the SQL builder, plus a separate static `_BindPatchParams(DbCommand, in Patch, ulong mask, int startIdx, long opId)` method with unrolled per-column if-blocks.
- **`InsertColumnInfo` → `WriteColumnInfo` rename.** Internal type; `PatchInfo` reuses it. No public API impact.
- **Per-carrier `_mapper_X` field emission.** The Patch binder runs on the file-scoped `Chain_N` carrier class — a sibling of the file static interceptor class in the same namespace — so the interceptor class's `private static` `_mapper_X` fields are unreachable. `EmitPatchSupport` emits a per-carrier mirror field with the same name convention so the binder's template doesn't need to know which copy it's reading.

## Gaps in original plan implemented
- **`ChainAnalyzer.MapInterceptorKindToClauseRole` Patch entries** were missing; Patch sites silently dropped from `AssembledPlan.GetClauseEntries`, so the carrier-only emission gate skipped them entirely. Added both kinds → `ClauseRole.UpdateSet`.
- **SqlAssembler space fix:** the assembler emitted `UPDATE "users" {__PATCH_SET__}` (trailing space) and the runtime emitter prepended `" SET "`, producing `UPDATE "users"  SET ...` (double space). Runtime now owns the entire SET clause spacing; assembled form is `UPDATE "users"{__PATCH_SET__} WHERE ...`.
- **`InterceptorCodeGenerator.CollectMappingInstances`** explicitly excludes `PatchInfo.Columns` (with comment); the per-Patch-column binder lives on the file-scoped `Chain_N` carrier class and would not be able to see the file-scope `_mapper_*` field anyway. Per-carrier mapper fields fill the gap.

## REVIEW + REMEDIATE
- Multi-agent REVIEW pass produced `_sessions/add-patch-partial-update/review.md` with 65 findings across six sections (Plan Compliance, Correctness, Security, Test Quality, Codebase Consistency, Integration / Breaking Changes). User decision: **C → A**, implement all A/B/C immediately. Final classification: 18A / 3B / 0C / 44D.
- All 21 actionable findings have an **Action Taken** entry in review.md's Classifications table.
- Notable behavioral fixes: empty-mask `.ToDiagnostics()` no longer throws (emits `SET /* empty Patch */` sentinel); `_BindPatchParams` now emits `ParameterLog.Bound`/`BoundSensitive`; Patch + `Where(ids.Contains)` collection expansion shifts placeholders past the runtime SET binds via a popcount of `__c.PatchMask`.

## Migration Steps
- None for library consumers — the new `Set` overloads are additions, no existing API changed.
- If a user's codebase already has a nested type named `Patch` inside an entity class, that user will hit CS0102 (duplicate member). Workaround: rename the user-defined nested type.
- Entities with >64 updatable columns will now fail at build time with QRY045. Workaround: drop columns, split the entity, or keep using `Set(new T { … })` (Patch struct emission is self-suppressed for those entities).

## Performance Considerations
- Patch chains bypass the prebuilt `_sql` / `_sqlCache` fast paths. Every execute rebuilds the SET clause via a `StringBuilder` walk over `_PatchFragments`. For chains where the column set is fixed at the call site, users should stay on `Set(new T { … })` for the prebuilt-SQL path.
- Per-carrier mapper instances: when a chain uses a custom-mapped Patch column, one extra mapper instance is allocated per Patch-using carrier (structurally deduplicated carriers share). Trade chosen over widening the file-scope field's visibility.
- `System.Numerics.BitOperations.PopCount` on the runtime mask runs once per Patch execute to pre-compute `__setShift`; negligible relative to the SQL build + DB round-trip.

## Security Considerations
- `_PatchFragments.Prefix` strings are dialect-quoted column names sourced from the schema (compile-time C# code). No user input flows into them. Runtime SQL assembly has no user-controllable concatenation; only static fragment prefixes and dialect-correct placeholders are appended.
- `_BindPatchParams` honors `Modifiers.IsSensitive`: sensitive Patch columns route to `ParameterLog.BoundSensitive` (records that a binding occurred, no value); non-sensitive columns log the value via `Bound`.
- Diagnostic SQL output for Patch chains contains either an expanded `SET col = @pN, …` clause (populated mask) or a `SET /* empty Patch */` sentinel (default-constructed). Parameter values never leak through diagnostic SQL.

## Breaking Changes
**Consumer-facing:**
- New nested type `Patch` on every entity class — collides with any user-defined nested type at the same name (CS0102).
- New runtime exception path: `InvalidOperationException("Set received a Patch with no fields assigned.")` from execution terminals when the runtime mask is zero. `.ToDiagnostics()` does NOT throw — it emits a `SET /* empty Patch */` sentinel.
- New compile-time errors: QRY045 (>64 updatable columns) and QRY046 (`Set` argument's syntax references `.Patch` but isn't a recognized construction shape).
- Public API additions: `Quarry.IPatchFor<T>`, `Quarry.PatchAction<T>`, `Quarry.UpdateBuilderPatchExtensions` static class.

**Internal:**
- `InsertColumnInfo` renamed to `WriteColumnInfo`. Type is `internal`; no external consumers in the NuGet surface.
- `RawCallSite.PatchUnrecognizedShape` mutable property added (transient flag, not part of equality).
- `FieldRole.PatchMask` new enum value (`internal`).

## Test plan
- [x] All Patch-related unit tests pass: `PatchInfoTests`, `EntityCodeGeneratorPatchTests`, `UsageSiteDiscoveryPatchTests`, `CallSiteBinderPatchTests`, `SqlAssemblerPatchTests`, `ParseSqlSegmentsPatchTests`, `EmitInlineSqlBuilderPatchTests`.
- [x] Cross-dialect end-to-end execution (Lite + Pg + My + Ss): single column, two columns, three columns, captured WHERE, empty-mask throw, empty-mask ToDiagnostics, FK column, enum column, custom Money mapper, ExecutableUpdateBuilder (Where-first), collection-expansion WHERE, conditional WHERE, lambda-form single column, lambda-form runtime-conditional, lambda-form empty-mask throw.
- [x] Full suite: 146 + 201 + 3230 = **3,577 passed / 0 failed**.
- [ ] CI green on push.
