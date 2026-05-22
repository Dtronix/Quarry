# Implementation Plan: add-patch-partial-update

## Overview

Add two new `.Set(...)` overloads for `Update()` chains, backed by a per-entity generated `Patch` mutable struct with write-tracking property setters and a `ulong __mask` field. SET clause SQL is assembled at execute time from a per-chain fragment table; downstream `WHERE` parameter placeholders are renumbered via a new `__setShift` term that joins the existing `__colShift` machinery for collection expansion.

Existing `.Set(new User { … })` (`UpdateSetPoco`) and `.Set(u => u.X = v)` (`UpdateSetAction`) paths are untouched.

## Key Concepts

### Patch struct

For each entity `User`, the generator emits a nested mutable struct:

```csharp
public partial class User
{
    public struct Patch
    {
        internal ulong __mask;

        private string __UserName;
        public string UserName
        {
            get => __UserName;
            set { __UserName = value; __mask |= 0x1UL; }
        }
        // ... one tracked property per updatable column ...

        internal const ulong _Mask_UserName = 0x1UL;
        internal const ulong _Mask_Email    = 0x2UL;
        // ... matching mask constants ...
    }
}
```

- Includes all columns except Identity and Computed (reuses `InsertInfo.FromEntityInfo` filtering).
- Bit positions match column declaration order.
- Hard cap: 64 updatable columns per entity. >64 → QRY045 at generation time.
- Property setters OR into `__mask`; reads don't touch the mask.
- Internal field/mask visibility — interceptor code lives in the same assembly so direct field access is permitted.

### PatchAction delegate

Single runtime definition in `Quarry`:

```csharp
public delegate void PatchAction<T>(ref T patch);
```

The `T` is the Patch type. Used in the lambda overload:

```csharp
.Set((ref User.Patch p) => { if (name is not null) p.UserName = name; })
```

C# infers the generic from the `Set` overload's parameter type — no explicit annotation needed.

### Runtime SET assembly via `{__PATCH_SET__}` token

The SqlAssembler emits a literal `{__PATCH_SET__}` placeholder where the SET clause would normally be rendered. At execute time, `EmitInlineSqlBuilder` recognizes this segment and emits a runtime loop:

```csharp
if (__c.PatchMask == 0UL)
    throw new InvalidOperationException("Set received a Patch with no fields assigned.");

int __setShift = 0;
\ Walk the per-chain fragment table; append "col" = @pN (or $N / ?) for each set bit.
\ Bind the DbParameter for each active column.
\ __setShift accumulates the count of active SET parameters.
```

`__setShift` feeds `ComputeShiftExprForIndex` for every downstream WHERE / collection placeholder: every scalar param at original index N becomes `N + __setShift + __colShift` at runtime.

For PostgreSQL (`$1`-based) and SQL Server / SQLite (`@p0`-based) the placeholder text is shift-aware. For MySQL (positional `?`), only the bind order matters — SET binds run before WHERE binds inside the same `DbCommand`.

### Dialect handling

Reuses `Quarry.Internal.ParameterNames.AtP` / `Dollar` for runtime parameter-name lookup. Reuses `SqlExprRenderer.AppendParameterPlaceholder` shape (three dialect cases) for the runtime placeholder rendering inside the SET assembler. No new dialect branching is introduced — each chain emits exactly one dialect's code.

## Phases

### Phase 1 — IR foundations: `PatchInfo`, `InterceptorKind`, runtime delegate

**Goal:** Set up the data structures and runtime delegate without any behavior change.

**Changes:**
- Add `internal sealed class PatchInfo : IEquatable<PatchInfo>` in `src/Quarry.Generator/Models/PatchInfo.cs`. Fields: `EntityTypeName`, `IsLambdaForm` (bool), `Columns` (list of `WriteColumnInfo` — the shared write-side column model, renamed from `InsertColumnInfo` once it was no longer insert-specific).
- Add `InterceptorKind.UpdateSetPatch` and `InterceptorKind.UpdateSetPatchAction` enum values to `src/Quarry.Generator/Models/InterceptorKind.cs`.
- Add `BoundCallSite.PatchInfo` property (nullable) alongside existing `UpdateInfo` / `InsertInfo`.
- Add `public delegate void PatchAction<T>(ref T patch);` in `src/Quarry/PatchAction.cs` (new file in runtime).
- Add QRY045 diagnostic descriptor for >64 updatable columns: severity Error, message `Entity '{0}' has {1} updatable columns, exceeding the 64-column limit for Patch generation`.

**Tests:**
- New: unit test that asserts `PatchInfo.Equals` round-trip and `GetHashCode` stability.
- Build still passes — no behavior change, just additions.

**Commit:** `feat(generator): add PatchInfo IR + UpdateSetPatch InterceptorKinds`

### Phase 2 — Patch struct emission in `EntityCodeGenerator`

**Goal:** Generate the `Patch` nested struct inline in each entity class, with tracking setters.

**Changes:**
- Extend `EntityCodeGenerator.GenerateEntityClass`: after generating navigation properties, append a `GeneratePatchStruct(sb, entity)` call before `sb.AppendLine("}")`.
- `GeneratePatchStruct` emits:
  - `public struct Patch { ... }` block
  - `internal ulong __mask;` field
  - For each updatable column (using `InsertInfo.FromEntityInfo`-equivalent filtering — Identity + Computed excluded):
    - `private {type} __{PropertyName};` backing field
    - Public property with custom set accessor that flips the mask bit
    - `internal const ulong _Mask_{PropertyName} = 0x{n}UL;` constant
  - If updatable column count > 64, emit QRY045 diagnostic via `SourceProductionContext` and skip Patch struct emission for that entity.
- For FK columns (`EntityRef<TEntity, TKey>`), the Patch field uses the same type — `EntityRef<User, int>` etc.

**Tests:**
- New: `EntityCodeGenerationTests` (or similar — confirm location of generator output tests) that:
  - Compiles a schema with a known column count and asserts the `Patch` struct is present with correct properties.
  - Asserts mask bit constants increment correctly.
  - Asserts Identity and Computed columns are excluded.
  - Asserts an entity with >64 updatable columns produces QRY045.

**Commit:** `feat(generator): emit User.Patch nested struct with write tracking`

### Phase 3 — Discovery: detect `Set(*.Patch)` and `Set(PatchAction<*.Patch>)`

**Goal:** Classify Set call sites as the right InterceptorKind based on argument type.

**Changes:**
- In `UsageSiteDiscovery.cs` ~line 473–486, extend the existing UpdateBuilder `Set` classification:
  - If single arg is a lambda with `ref Patch` parameter → `UpdateSetPatchAction`.
  - If single arg is a value with type ending in `.Patch` and the containing type is a known entity → `UpdateSetPatch`.
  - Else fall through to existing `UpdateSetAction` (other lambdas) / `UpdateSetPoco` (entity).
- Helper `IsPatchType(ITypeSymbol)` checks: type is a struct, containing type is in `EntityRegistry` (or is a known entity by name — same check the entity-form path uses).
- For `UpdateSetPatch`: leave `InitializedPropertyNames` null (we want all updatable columns).
- For `UpdateSetPatchAction`: same — no initializer to inspect.

**Tests:**
- New: tests in `UsageSiteDiscoveryTests` (or similar) that classify each form correctly:
  - `Set(new User { X = v })` → UpdateSetPoco (unchanged)
  - `Set(u => u.X = v)` → UpdateSetAction (unchanged)
  - `Set(somePatchVariable)` → UpdateSetPatch
  - `Set((ref User.Patch p) => p.X = v)` → UpdateSetPatchAction
  - `Set(p => p.X = v)` where p is inferred as `User.Patch` → also UpdateSetPatchAction (lambda discrimination by ref parameter)

**Commit:** `feat(generator): discover UpdateSetPatch + UpdateSetPatchAction call sites`

### Phase 4 — Binder: build `PatchInfo` from `EntityInfo`

**Goal:** Wire the discovery to a populated `PatchInfo` on `BoundCallSite`.

**Changes:**
- In `CallSiteBinder.cs` ~line 184–196 (where `UpdateInfo` is built today), add a parallel branch for the new kinds:
  - If `raw.Kind` is `UpdateSetPatch` or `UpdateSetPatchAction`, build a `PatchInfo` from `entry.Entity` listing all updatable columns. Pass `IsLambdaForm = (raw.Kind == UpdateSetPatchAction)`.
- `PatchInfo.Columns` is already `IReadOnlyList<WriteColumnInfo>` (shape decided in Phase 1). Phase 4 just populates it from `EntityInfo` via `PatchInfo.FromEntityInfo`.

**Tests:**
- New: binder tests that, given a sample schema, assert `PatchInfo.Columns.Count` matches the expected updatable count and that Identity/Computed are excluded.

**Commit:** `feat(generator): bind PatchInfo for UpdateSetPatch sites`

### Phase 5 — ChainAnalyzer + SqlAssembler + segment parser: `{__PATCH_SET__}` placeholder end-to-end

**Goal:** Make the chain analyzer route Patch sites to the new SET token, ensure SqlAssembler emits the token verbatim, and ensure the runtime segment parser recognizes it. All three layers touch the placeholder — cleaner cohesion as one commit.

**Changes:**
- In `ChainAnalyzer.cs` ~line 1150 (`UpdateSetPoco` branch), add new branches:
  - If `kind == UpdateSetPatch || kind == UpdateSetPatchAction` and `site.Bound.PatchInfo != null`:
    - Do NOT iterate columns to emit SetTerms — emit a single sentinel `SetTerm` carrying the patch placeholder.
    - The sentinel's column is the empty string; its value is a new `PatchSetPlaceholderExpr` SqlExpr node (or reuse `LiteralExpr` with content `{__PATCH_SET__}` — see below).
    - DO NOT add per-column `QueryParameter` entries — the runtime assembler handles parameters at execute time.
    - Record on the `AssembledPlan` that this chain has a Patch SET clause (e.g., `HasPatchSet: true`).
- Introduce a dedicated `PatchSetPlaceholderExpr` SqlExpr node that renders as `{__PATCH_SET__}` regardless of dialect; renderer just emits the literal token.
- `SqlAssembler.RenderUpdateSql`: when SET clause is a Patch placeholder, emit literally:
  - SQL Server: `UPDATE [users] {__PATCH_SET__} WHERE ...`
  - PostgreSQL: `UPDATE "users" {__PATCH_SET__} WHERE ...`
  - SQLite: `UPDATE "users" {__PATCH_SET__} WHERE ...`
  - MySQL: `UPDATE \`users\` {__PATCH_SET__} WHERE ...`
- (No `SET` keyword in the prefix — the runtime emitter writes ` SET ` followed by the field list, or throws if mask=0.)
- `TerminalEmitHelpers.ParseSqlSegments`: add detection of `{__PATCH_SET__}` token alongside `{__COL_PN__}`. Emit new `SqlSegment.PatchSet(0)` (the int param unused for this kind).
- Add `SqlSegmentKind.PatchSet` to the enum.

**Tests:**
- New: chain analyzer tests asserting that Patch sites produce one placeholder SetTerm and zero per-column parameters.
- New: SQL assembler tests for the four dialects asserting the placeholder is emitted exactly once and `WHERE` parameters use their normal indices (no shift baked at compile time — runtime applies shift).
- New: segment parser test asserting `{__PATCH_SET__}` produces a `PatchSet` segment.

**Commit:** `feat(generator): plumb {__PATCH_SET__} placeholder through analyzer, assembler, parser`

### Phase 6 — TerminalEmitHelpers: runtime SET assembly + `__setShift`

**Goal:** Emit the runtime code that walks the fragment table, builds the SET list, and applies the shift to downstream parameters.

**Changes:**
- `EmitInlineSqlBuilder`: handle `SqlSegmentKind.PatchSet`:
  - Emit empty-mask guard: `if ((__c.PatchMask) == 0UL) throw new InvalidOperationException(...);`
  - Emit `__sb.Append(" SET ");`
  - Emit a `foreach`-equivalent over the per-chain fragment table (generated per chain — see Phase 8 for how that table is structured).
  - Inside the loop: bit check, comma separator, prefix append, dialect-specific placeholder append using `__setShift` as the running index, parameter binder call, `__setShift++`.
- `ComputeShiftExprForIndex`: prepend `__setShift` as the first term if the chain has a Patch SET. Document ordering: SET comes before WHERE so its shift dominates all subsequent indices.
- The `__setShift` variable is declared before the segment loop; only initialized to 0 when a Patch segment is present.

**Tests:**
- New: integration-style test asserting the generated terminal body for a Patch update contains the empty-mask guard, the runtime loop, and the right shift expression on the WHERE bind.

**Commit:** `feat(generator): emit runtime SET assembly + __setShift for Patch`

### Phase 7 — Carrier + per-chain fragment table; interceptor body for `Set(Patch)`

**Goal:** End-to-end working value-overload path.

**Changes:**
- `CarrierAnalyzer`: when chain has a Patch SET, add carrier fields:
  - `public {EntityType}.Patch Patch;`
  - `public ulong PatchMask;` (mirrors `Patch.__mask` for cheap reads; populated by Set interceptor)
- `CarrierEmitter`: emit a static fragment table per chain. One entry per Patch column:
  - `(ulong Bit, string Prefix, Action<DbCommand, {EntityType}.Patch, int> Bind)`
  - Each binder is a generated static method `_BindPatch_{ColumnName}(DbCommand, in Patch, int paramIdx)` that creates a parameter with the right name and binds the typed value (handling FK `.Id` extraction, enum underlying-type cast, sensitive redaction).
- `ClauseBodyEmitter.EmitUpdateSetPatch`: emit interceptor body:
  ```csharp
  public static IUpdateBuilder<User> Set(this IUpdateBuilder<User> b, User.Patch patch)
  {
      var __c = Unsafe.As<Chain_5>(b);
      __c.Patch = patch;
      __c.PatchMask = patch.__mask;
      return Unsafe.As<IUpdateBuilder<User>>(b);
  }
  ```
- `InterceptorRouter`: route `UpdateSetPatch` to ClauseBodyEmitter.

**Tests:**
- New: cross-dialect SQL output tests in `Quarry.Tests/SqlOutput/CrossDialectUpdateTests.cs` for `Set(User.Patch)` value form:
  - 1 column set
  - 2 columns set
  - 3 columns set
  - All four dialects
- New: end-to-end SQLite test (in `EndToEndSqlTests.cs` or similar) that builds a Patch, executes the update, asserts row state.

**Commit:** `feat(generator): wire end-to-end Set(User.Patch) value overload`

### Phase 8 — Lambda overload: `Set(PatchAction<User.Patch>)`

**Goal:** Same carrier/fragment-table backend, but with a delegate that mutates the patch first.

**Changes:**
- `ClauseBodyEmitter.EmitUpdateSetPatchAction`: emit interceptor body:
  ```csharp
  public static IUpdateBuilder<User> Set(this IUpdateBuilder<User> b, PatchAction<User.Patch> action)
  {
      var __c = Unsafe.As<Chain_5>(b);
      action(ref __c.Patch);
      __c.PatchMask = __c.Patch.__mask;
      return Unsafe.As<IUpdateBuilder<User>>(b);
  }
  ```
- `InterceptorRouter`: route `UpdateSetPatchAction` to the same emitter (or a sibling).

**Tests:**
- New: cross-dialect SQL output tests for `Set((ref User.Patch p) => { ... })` lambda form with conditional branches.
- New: end-to-end SQLite test executing a lambda-form update with a conditional that toggles columns at runtime.

**Commit:** `feat(generator): wire Set(PatchAction<T>) lambda overload`

### Phase 9 — Integration edge cases

**Goal:** Confirm composition with other Quarry features.

**Changes:**
- Verify Patch + `Where(captured-var)` parameter shift composes correctly (`__setShift + __colShift` order in `ComputeShiftExprForIndex`).
- Verify Patch + `Where(ids.Contains(x))` collection-expansion composes correctly.
- Verify Patch with FK column update binds `.Id` correctly.
- Verify Patch with enum column update casts underlying type correctly.
- Verify Patch with custom type mapping invokes `mapper.ToDb(value)` in the binder.
- Verify Patch with sensitive column redacts in diagnostic output.
- Verify Patch executed on `IExecutableUpdateBuilder<T>` path (chained after `Where`) still works — the existing `UpdateBuilder` → `ExecutableUpdateBuilder` transition.
- Fix any issues uncovered.

**Tests:** Cross-dialect coverage matrix for each composition.

**Commit:** `test: cross-dialect Patch composition coverage` (one commit; may split into multiple if fixes are non-trivial)

### Phase 10 — Documentation

**Goal:** User-facing docs and generator reference updated.

**Changes:**
- `docs/articles/modifications.md`: Add a third "Patch" subsection alongside Assignment syntax and Entity form. Cover both value and lambda overloads. Note the cross-method-boundary use case as the motivating example.
- `src/Quarry.Generator/llm.md`: Update the InterceptorKind Categories table (add UpdateSetPatch, UpdateSetPatchAction under Clause). Add a "Partial Updates via Patch" section describing the runtime-assembly path. Add QRY045 to the diagnostics table.
- `llm.md` (root): If the Update API section calls out specific `Set` overloads, add the new ones.

**Tests:** N/A (docs).

**Commit:** `docs: document Patch partial-update API`

## Dependencies between phases

- Phase 1 (IR) → Phase 2 (entity codegen): emission needs the column-count check for QRY045.
- Phase 1 → Phase 3 (discovery): discovery needs the new InterceptorKind enum values.
- Phase 3 → Phase 4 (binder): binder reads the discovered Kind.
- Phase 4 → Phase 5 (analyzer + assembler + parser): analyzer reads PatchInfo from BoundCallSite; assembler emits the placeholder; parser reads it.
- Phase 5 → Phase 6 (terminal helpers): runtime SET assembly needs segment recognition.
- Phase 6 → Phase 7 (carrier + interceptor): interceptor body relies on `__setShift` mechanics.
- Phase 7 → Phase 8 (lambda overload): lambda emitter shares everything with value emitter except the body.
- Phase 8 → Phase 9 (integration): edge cases run against the complete pipeline.
- Phase 9 → Phase 10 (docs).

## Out of Scope (deferred)

- `User.Patch.From(entity)` seeded patches (see Decisions log 2026-05-22).
- BatchUpdate / batched Patch — out of scope; this only adds to single-row `Update().Set(...)`.
- Conditional-aware `Set(Action<T>)` lambda (the original Option C) — collapsed into A′ lambda form.
- Prebuilt-variant SQL for Patch — always runtime-assembled (see Decisions log).
- Patch column count > 64 (currently rejected at QRY045 → diagnostic upgrade to multi-word mask is a future enhancement).
