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
session: 4
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

### 2026-05-22 — Phase 7 fragment-table shape: split (Bit, Prefix)[] + separate _BindPatchParams method
The plan's original shape `(ulong Bit, string Prefix, Action<DbCommand, Patch, int> Bind)` is unworkable for two independent reasons: (1) `Action<>` cannot take `in TPatch`, and the Patch struct holds the mask so passing by value would copy it; (2) `__cmd` does not exist when the inline SQL builder runs — `var __cmd = ...CreateCommand()` happens AFTER `sql = __sb.ToString()` in `TerminalBodyEmitter` (~line 567), so calling Bind during SQL building is structurally impossible.

Revised shape (confirmed 2026-05-22): `_PatchFragments` is `(ulong Bit, string Prefix)[]` consumed by the inline SQL builder, plus a separate static `_BindPatchParams(DbCommand cmd, in {EntityType}.Patch patch, ulong mask, int startIdx)` method with unrolled per-column if-blocks called from the post-`__cmd` binding loop. Matches Quarry's existing post-command binding pattern (UpdateSetPoco does the same). Trade: walks the active bits twice (once for SQL, once for binding); negligible for the 64-column max; in exchange, zero allocation, no delegate dispatch, and reviewer-familiar shape.

### 2026-05-22 — Phase 3 discovery: syntax-only Patch classification (rewrite)
The Phase 3 implementation that classifies via Roslyn's overload-resolved `methodSymbol.Parameters[0].Type` works in the isolated `UsageSiteDiscoveryPatchTests` harness (which pre-runs the generator via `RunGeneratorsAndUpdateCompilation`) but **fails in the real generator pipeline**: the SyntaxProvider's `SemanticModel` sees the pre-generator compilation, so Roslyn binds `Set(somePatch)` to the SetPoco DIM and discovery emits the wrong interceptor — the final build fails with CS9144 when the user compilation (which DOES see the generated `User.Patch`) tries to apply it.

Two `IIncrementalGenerator` instances don't help — they each receive the same pre-generator compilation and don't see each other's outputs. A supplemental-compilation approach (manually building a `Compilation` containing the entity outputs and re-binding) works but is heavyweight: re-parses N entity sources on every discovery pass, cascades cache invalidation through Stage 3/4, and risks regressing every other call-site classification.

**Decision:** classify Patch sites by argument SYNTAX. Detect `Set(new X.Patch { … })` directly, walk back to the variable declarator for `Set(somePatchVar)`, and detect `Set((ref X.Patch p) => …)` via the lambda's `ref` modifier. No semantic-model dependency on the Patch type.

**Why:** Cheap (no Compilation rebuild), local (only `UsageSiteDiscovery` changes), correct for the documented v1 patterns (object initializer + pre-built variable + ref lambda). Out-of-scope exotic patterns (e.g. `Patch.From(entity)`, ternary over patches) fall through to UpdateSetPoco and produce a clean CS9144 at the user's call site — actionable. The `UsageSiteDiscoveryPatchTests` harness can be augmented with a regression test that DOES NOT pre-run the generator, exposing the real-generator scenario.

**How to apply:** see updated Phase 3 in plan.md for the exact classifier shape and helper signatures. Phase 3 should be re-implemented before Phase 7's remaining work — the wrong-overload binding currently propagates through Stages 4–6 and produces broken interceptors.

### 2026-05-22 — Patch Set overloads as extension methods (not DIMs)
Initially tried adding the new patch overloads as default interface methods (DIMs) alongside the existing `Set(T)` and `Set(Action<T>)` on `IUpdateBuilder<T>` / `IExecutableUpdateBuilder<T>`. With the generic DIMs in place, the existing `Set(T entity)` interceptors stopped binding — Roslyn no longer routed the user's `.Set(new User { ... })` call through the emitted `Set_<id>(this IUpdateBuilder<User>, User entity)` interceptor, even though overload resolution clearly picked the non-generic Set(T) DIM and the interceptor signature matched. Switched to extension methods in a static helper class (`UpdateBuilderPatchExtensions`) — instance-method lookup still picks up the existing DIMs for non-Patch args (interceptor binds fine), and extension lookup finds the Patch overloads when DIMs aren't applicable (User.Patch isn't a User, lambdas with `ref TPatch` parameter aren't `Action<T>`). Same compile-time enforcement via `IPatchFor<T>` constraint; no impact on the existing UpdateSetPoco / UpdateSetAction paths.

## Suspend State

**Current phase:** IMPLEMENT — mid Phase 7 of 10. Phases 1–6 complete and committed.

**Status at suspend:** Working tree has uncommitted WIP — partially-implemented Phase 7 plus an unsuccessful Phase 3 discovery experiment that needs to be rewritten as syntax-only classification. Branch `add-patch-partial-update` is **10 commits ahead of origin** at the last good commit (Phases 1–6 + suspend-state commits not yet pushed — push is a user decision and was not authorized). The session-3 WIP commit on top adds Phase 7 carrier/binder code, the syntax-only-classification plan update, and the failing-test scaffolding that surfaced the discovery bug.

**Last clean commit (before session 4 WIP):** `3942aa6 chore(session): refresh suspend state`. The session-4 WIP commit adds Phase 7 progress on top.

**Test status:** **Build is broken** at the session-4 WIP commit — six new tests in `CrossDialectUpdateTests.cs` (`Update_SetPatch_*`) fail to compile with CS9144 (interceptor signature mismatch). All previously passing tests still pass (146 + 201 + 3205 = 3,552). The CS9144 failures are the symptom that drove the syntax-only-classification decision.

**Immediate next step on resume:** Implement the syntax-only Patch classification in `UsageSiteDiscovery.cs` (see Phase 3 in plan.md, "Implementation note (revised 2026-05-22 mid-Phase-7)" and the 2026-05-22 syntax-only decision in `## Decisions`). Once `Update_SetPatch_*` tests compile cleanly, resume the Phase 7 work below from step 1 — most of it is already partially in place.

**Sub-step breakdown for Phase 7 once discovery is fixed:**

1. **`FieldRole` enum** (`src/Quarry.Generator/Models/CarrierField.cs`): add `Patch` value alongside Entity/Limit/Mask/etc.

2. **`CarrierAnalyzer.AnalyzeNew`** (~line 222 alongside `hasSetPoco` detection): when chain has any `UpdateSetPatch` or `UpdateSetPatchAction` site, add two carrier fields:
   - `Patch` typed `{EntityType}.Patch` (value type, role `FieldRole.Patch`, isReferenceType: false)
   - `PatchMask` typed `ulong` (role `FieldRole.Patch`)

3. **`CarrierEmitter`** (per-chain emission):
   - Emit a static readonly `_PatchFragments` field of type `(ulong Bit, string Prefix)[]` populated from `chain.ClauseSites`' `BoundCallSite.PatchInfo.Columns`. Prefix is `"\"col\" = "` (dialect-quoted column + ` = `).
   - Emit a static `_BindPatchParams(System.Data.Common.DbCommand cmd, in {EntityType}.Patch patch, ulong mask, int startIdx)` method with unrolled if-blocks per column. Each block: bit check, create parameter (dialect-correct name via `Quarry.Internal.ParameterNames.AtP(startIdx + offset)` for SQLite/SqlServer/MySQL, empty for PG), bind typed value (FK `.Id` extraction, enum cast, custom mapper, sensitive redaction — same logic as `GetInsertColumnBinding` in TerminalEmitHelpers), add to `cmd.Parameters`, increment local offset.

4. **`CarrierEmitter.EmitCarrierSqlDispatch`** (~line 1102): extend the `hasCollections` check to ALSO detect Patch chains (any segment kind = PatchSet) and route Patch chains through the inline-builder path. Patch chains bypass the `_sqlCache` (since SQL varies with `__c.PatchMask` and per-mask caching is a follow-up optimization). When calling `EmitInlineSqlBuilder`, pass the carrier-qualified fragment table reference: `patchFragmentsRef: $"{carrier.ClassName}._PatchFragments"`.

5. **`ClauseBodyEmitter.EmitUpdateSetPatch`**: emit the value-form interceptor body. Signature (extension method on the Patch overload):
   ```csharp
   public static IUpdateBuilder<User> Set(this IUpdateBuilder<User> b, User.Patch patch)
   {
       var __c = Unsafe.As<Chain_N>(b);
       __c.Patch = patch;
       __c.PatchMask = patch.__mask;
       return Unsafe.As<IUpdateBuilder<User>>(b);
   }
   ```
   The `patch.__mask` access works only because the Patch struct's `__mask` field is `internal` and we're in the same assembly. Update the existing Patch struct emission in Phase 2 if needed to confirm `internal ulong __mask;`.

6. **`FileEmitter`** (~line 827): add `case InterceptorKind.UpdateSetPatch:` dispatch → `ClauseBodyEmitter.EmitUpdateSetPatch(...)`.

7. **`InterceptorRouter.Categorize`** (~line 26 in the Clause group): add `case InterceptorKind.UpdateSetPatch:` and `case InterceptorKind.UpdateSetPatchAction:` to the Clause category.

8. **Parameter binding loop**: in `TerminalBodyEmitter` execution-terminal emit paths (e.g. `EmitNonQueryTerminal`), after `__cmd` creation and the existing scalar param binding loop, when the chain has Patch, emit: `{carrier.ClassName}._BindPatchParams(__cmd, in __c.Patch, __c.PatchMask, __setShift /* or 0 */);`. The `startIdx` argument: 0 for chains where SET is the only param source; offset for chains that intermix (Phase 9 — but value-form Set(Patch) has no compile-time scalar SET params, so 0 is correct).
   - The current __cmd-binding code in CarrierEmitter (around `EmitCarrierSqlDispatch` callers) is where this hook lands; identify the right insertion point with care because there are multiple terminal emit paths.

9. **Tests**:
   - `src/Quarry.Tests/SqlOutput/CrossDialectUpdateTests.cs`: new tests for `Set(User.Patch)` value form across 4 dialects (1, 2, 3 columns set).
   - `src/Quarry.Tests/SqlOutput/EndToEndSqlTests.cs` (or similar): end-to-end SQLite test that builds a Patch, executes the update, asserts row state.
   - `User.Patch` already emits in Phase 2; entity sample schemas in `Quarry.Tests.Samples` are the right place to exercise new overloads if existing samples don't already.

**WIP commit:** session-4 work is captured in `1ed7d3d [WIP] Phase 7 generator wiring + syntax-only discovery decision` on top of `3942aa6` — includes the Phase 7 generator changes (FieldRole.Patch, CarrierAnalyzer Patch fields, CarrierEmitter EmitPatchSqlDispatch + EmitPatchSupport, EmitCarrierCommandBinding shift-aware names, ClauseBodyEmitter EmitUpdateSetPatch + EmitUpdateSetPatchAction, FileEmitter dispatch, InterceptorRouter Clause entries, EmitInlineSqlBuilder caller-declared __setShift), the failing cross-dialect Patch tests in `CrossDialectUpdateTests.cs`, the unsuccessful semantic-based discovery experiments in `UsageSiteDiscovery.cs`, the EmitInlineSqlBuilderPatchTests assertion update, the plan.md Phase 3 rewrite, and this workflow.md update.

**Order of work on resume:**
1. Implement syntax-only Patch classification in `UsageSiteDiscovery.cs` per the updated Phase 3 in plan.md. Drop the experimental semantic checks (`IsPatchType(argType)`, `containingType.Name == "UpdateBuilderPatchExtensions"`). Keep the lambda `ref`-modifier detection.
2. Add a regression test to `UsageSiteDiscoveryPatchTests` that DOES NOT call `RunGeneratorsAndUpdateCompilation` — proves discovery classifies correctly against the pre-generator SemanticModel.
3. Verify `Update_SetPatch_*` cross-dialect tests compile and pass.
4. Verify the previously-passing Phase 1–6 tests still pass (~3,552 baseline).
5. Continue Phase 7 sub-steps (most of the carrier/binder/emitter wiring is already in place from the WIP commit; once classification routes to the right path, the existing generator code should produce correct interceptors).
6. Continue to Phase 8, 9, 10.

**Unrecorded context:** None. The syntax-only-classification decision and rationale are in `## Decisions` (2026-05-22). Plan.md Phase 3 carries the implementation specifics. The CS9144 build error pinpoints the root cause for the next session.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-22 INTAKE | 2026-05-22 PLAN (approved, suspended before IMPLEMENT) | Bootstrapped from in-session discussion. Worktree created. Baseline tests green (3,496/0). All design decisions recorded. plan.md written with 10 phases (originally 11, phases 5–6 combined per user). Approved by user; suspended for next session. |
| 2 | 2026-05-22 IMPLEMENT (resume) | 2026-05-22 IMPLEMENT (suspended after Phase 3) | Resumed from suspend. Completed Phases 1–3 of 10. Phase 1 (IR foundations) + a follow-on refactor renaming `InsertColumnInfo` → `WriteColumnInfo`. Phase 2 (Patch struct emission) — mid-phase fix to use `ColumnInfo.IsValueType` instead of name-heuristic for non-nullable-reference detection (custom-mapped value types like `Money` broke otherwise). Phase 3 (call-site discovery) — initial DIM attempt broke existing `Set(T entity)` interceptor binding; pivoted to extension methods (`UpdateBuilderPatchExtensions` + `IPatchFor<T>` marker), discovery classifies via `methodSymbol.Parameters[0].Type`. WIP commit `3432ac2` left as predecessor (FINALIZE squash-merge will collapse it). Tests: 3,522/0. Branch +4 unpushed commits at suspend. |
| 3 | 2026-05-22 IMPLEMENT (resume Phase 4) | 2026-05-22 IMPLEMENT Phase 6 complete (suspended before Phase 7) | Resumed from suspend. Baseline reverified: 3,522/0. Phase 4 complete: `CallSiteBinder` populates `PatchInfo` for UpdateSetPatch/UpdateSetPatchAction kinds; added `CallSiteBinderPatchTests` (7). Phase 5 complete: new `SqlExprKind.PatchSetPlaceholder` + `PatchSetPlaceholderExpr` node renders as literal `{__PATCH_SET__}`; ChainAnalyzer emits a single sentinel SetTerm for Patch sites (zero per-column QueryParameters); `SqlAssembler.RenderUpdateSql` detects the placeholder and skips the ` SET ` keyword (runtime emitter owns it); `TerminalEmitHelpers.ParseSqlSegments` adds `SqlSegmentKind.PatchSet` recognition. Added `SqlAssemblerPatchTests` (7) + `ParseSqlSegmentsPatchTests` (6). Phase 6 complete: `EmitInlineSqlBuilder` handles `SqlSegmentKind.PatchSet` — declares `int __setShift = 0;` at top when any PatchSet segment exists, scalar segments add `+ __setShift` to their index expression, PatchSet case emits the empty-mask guard + ` SET ` literal + per-fragment runtime loop (dialect-correct placeholder via `__setShift + __colShift`, or `__setShift + 1 + __colShift` for PG, or `?` for MySQL). New `patchFragmentsRef` parameter (default `__patchFragments`) lets Phase 7 wire in the real per-chain table reference. `ComputeShiftExprForIndex` Patch-awareness deferred to Phase 9 (diagnostic-path concern). Added `EmitInlineSqlBuilderPatchTests` (10). Tests: 3,552/0. Post-suspend: discussed failure discoveries with user (OptimizationTier.Opaque mismatch + Phase 7 fragment-table shape problem); locked revised fragment-table shape — `(ulong Bit, string Prefix)[]` + separate `_BindPatchParams` static method — in `## Decisions` (`8db3bb8`). |
| 4 | 2026-05-22 IMPLEMENT (resume Phase 7) | 2026-05-22 IMPLEMENT mid-Phase-7 (suspended after discovery rewrite decision) | Resumed from suspend. Baseline verified 3,552/0. Implemented the Phase 7 fragment-table + binder emission (`FieldRole.Patch`, CarrierAnalyzer Patch fields, `EmitPatchSqlDispatch`, `EmitPatchSupport` with `_PatchFragments[]` and unrolled `_BindPatchParams`, shift-aware WHERE-side parameter names in `EmitCarrierCommandBinding`, `ClauseBodyEmitter.EmitUpdateSetPatch` / `EmitUpdateSetPatchAction`, `FileEmitter` dispatch, `InterceptorRouter` Clause entries) and refactored `EmitInlineSqlBuilder` so the caller owns `int __setShift = 0;` (updated Phase 6 test assertions accordingly). Added cross-dialect `Update_SetPatch_*` tests in `CrossDialectUpdateTests.cs`; build fails with CS9144 because Phase 3 discovery emits a SetPoco-shaped interceptor at Patch call sites. Investigated three semantic fixes (containing-type check, GetTypeInfo on argument, relaxed `IsPatchType` name fallback) — none worked because the SyntaxProvider's `SemanticModel` doesn't see the generator-emitted `Entity.Patch` struct at discovery time, so Roslyn binds `Set(somePatch)` to the SetPoco DIM. Discussed three architectural fixes with user (two generators — ruled out, generators don't see each other's outputs; supplemental compilation — too heavy; syntax-only classification — small, local, cheap). Decision: **syntax-only classification** locked in `## Decisions` and Phase 3 of plan.md rewritten. Suspended to hand off the rewrite to the next session. |
