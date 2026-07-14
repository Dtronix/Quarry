# Plan: 313-fix-migration-builder-drift

## Overview

Issue #313 has two halves. The **live bug**: the CLI's `SnapshotCompiler` recompiles generated snapshot code against the shared builders, whose API drifted from the runtime builders the code was generated for (`DefaultValue` vs `Default`, `Nullable(bool)` vs `Nullable()`), and whose `AllowedMethods` whitelist omits `DefaultValue`/`Collation`/`CharacterSet` — so any snapshot using those silently returns `null` and `migrate add`/`diff` diffs against an empty baseline, scaffolding CREATE TABLE for everything. The **structural cause**: 11 types exist as two hand-synced copies (`src/Quarry/Migration` runtime vs `src/Quarry.Shared/Migration` shared).

The fix, per approved design: unify the builder API on the runtime shape, fix the whitelist, make recompile failure throw (tool exits 1 via Program.cs's top-level catch), then single-source the 11 types so the runtime copies are deleted and one file compiles into Quarry.dll (public, `namespace Quarry.Migration`), Quarry.Generator (internal, `Quarry.Shared.Migration`), and Quarry.Tool (public, `Quarry.Shared.Migration`).

## Key concepts

- **Namespace/visibility gating** (existing pattern, cf. `SqlDialect.cs`): the shared files already carry `#if QUARRY_GENERATOR internal #else public #endif`. We add a second axis: `#if QUARRY_RUNTIME` → `namespace Quarry.Migration`, else `namespace Quarry.Shared.Migration`. Only `Quarry.csproj` defines `QUARRY_RUNTIME`.
- **Selective include**: `Quarry.csproj` keeps `<Compile Remove="..\Quarry.Shared\Migration\**\*.cs" />` and re-includes exactly the 8 model files + 3 builder files (NOT `MigrationStep`/`MigrationStepType`/`StepClassification` — no new public runtime API).
- **netstandard2.0 constraint**: the generator targets netstandard2.0, so single-sourced files keep the 17/31 accumulator `GetHashCode` (runtime's in-memory hash values change; never persisted — persisted hash is the already-single-sourced `SchemaHasher`).
- **Type domains in Quarry.Tests**: `Quarry.Shared.Migration.*` resolves to Quarry.Generator's internals via IVT; `Quarry.Migration.*` resolves to Quarry.dll (public). Both coexist. The round-trip test crosses domains via a small property-mapping helper.
- **Loud failure**: `SnapshotCompiler.CompileAndBuild` returns `null` only for "no snapshot class with that version exists"; any failure after discovery (whitelist violation, compile error, load/invoke failure) throws with diagnostics in the message. `FindAndBuildSnapshot` throws if it gets `null` for a version that `FindLatestSnapshotVersion` discovered (inconsistency guard). Program.cs's catch → stderr + exit 1.

## Steps

### Step 1: Unify the builder API and fix the whitelist
- [x] In `src/Quarry.Shared/Migration/Builders/ColumnDefBuilder.cs`: rename `Default(string)` → `DefaultValue(string)`, change `Nullable()` → `Nullable(bool nullable = true)` (matches runtime copy exactly).
- [x] In `src/Quarry.Tool/Schema/SnapshotCompiler.cs` `AllowedMethods`: add `DefaultValue`, `Collation`, `CharacterSet`; remove `Default`.
- [x] Verify no callers of the old shared `Default(` break (none exist per exploration).
- Tests: full suite must stay green (shared builder is exercised by generator/tool paths in existing tests). No new tests yet — coverage tests land in Step 4 where the plumbing exists.
- Commit: `fix: unify shared ColumnDefBuilder API with runtime; whitelist DefaultValue/Collation/CharacterSet`

### Step 2: Make snapshot recompile failure loud
- [x] `SnapshotCompiler.CompileAndBuild`: after the snapshot class is discovered, every failure path throws `InvalidOperationException` with context (validation: the disallowed method name; emit: joined error diagnostics; load/invoke: what was missing) instead of `Console.Error` + `return null`. `null` remains only for "not found".
- [x] `MigrateCommands.FindAndBuildSnapshot`: throw if `CompileAndBuild` returns `null` (callers only invoke it for discovered versions — null is an internal inconsistency, never a valid empty baseline).
- [x] Remove the now-dead null-check in `MigrateSquash` (line ~602).
- Tests (new, in `Quarry.Tests/Migration/SnapshotCompilerTests.cs`; `SnapshotCompiler.cs` gets Compile-Included into Quarry.Tests like `ProjectSchemaReader.cs` already is; `ValidateBuildMethod` becomes `internal` for direct testing):
  - Snapshot containing a disallowed call (e.g. `File.Delete`) → `CompileAndBuild` throws, message names the method.
  - Snapshot whose Build() fails to compile (whitelisted name, bad arg type) → throws with compile diagnostics.
  - No snapshot with target version in compilation → returns null (no throw).
- Commit: `fix: snapshot recompile failure aborts migrate add/diff/squash instead of degrading to empty baseline`

### Step 3: Single-source the migration model (structural fix)
- [x] Add namespace gating (`#if QUARRY_RUNTIME → namespace Quarry.Migration`) to the 11 shared files: `Models/{ColumnDef,ColumnKind,ForeignKeyAction,ForeignKeyDef,IndexDef,NamingStyleKind,SchemaSnapshot,TableDef}.cs` and `Builders/{ColumnDefBuilder,TableDefBuilder,SchemaSnapshotBuilder}.cs`.
- [x] `Quarry.csproj`: define `QUARRY_RUNTIME`; after the existing `Compile Remove`, re-include those 11 files with `LinkBase="Shared"`.
- [x] Delete the 11 runtime duplicates from `src/Quarry/Migration`: `ColumnDef.cs`, `ColumnDefBuilder.cs`, `ColumnKind.cs`, `ForeignKeyAction.cs`, `ForeignKeyDef.cs`, `IndexDef.cs`, `NamingStyleKind.cs`, `SchemaSnapshot.cs`, `SchemaSnapshotBuilder.cs`, `TableDef.cs`, `TableDefBuilder.cs`.
- [x] Build every project in the solution (runtime, generator, tool, both test projects, samples) — catches namespace/visibility mismatches at all consumption sites.
- Tests: full suite green. Existing `Quarry.Tests/Migration/*` (MigrationBuilder, DdlRenderer, MigrationRunner, EdgeCase tests incl. `.Nullable(false)`/`.DefaultValue(...)` call sites) now exercise the single-sourced types in the runtime domain; `SnapshotCodeGeneratorTests` exercise them in the generator domain.
- Commit: `refactor: single-source migration model types via QUARRY_RUNTIME namespace gating (#313)`

### Step 4: Round-trip and whitelist-coverage regression tests
- [ ] New `Quarry.Tests/Migration/SnapshotRoundTripTests.cs`:
  - Build a `Quarry.Shared.Migration.SchemaSnapshot` exercising **every** builder feature: PK, FK column + `AddForeignKey` with non-default actions, identity, client-generated, computed (with and without expression), nullable, length, precision/scale, `DefaultValue`, `HasDefault`, `MapTo`, `CustomTypeMapping`, `Collation`, table `Schema`, `NamingStyle`, `CharacterSet`, `AddIndex` (unique, filter, method, descendingColumns), `CompositeKey`.
  - Generate source via `SnapshotCodeGenerator.GenerateSnapshotClass`.
  - Compile it against Quarry.dll (`typeof(Quarry.Migration.SchemaSnapshotBuilder).Assembly`) with `using Quarry.Migration;` as generated — **zero diagnostics** (this alone catches the `DefaultValue`-class drift: generated code must compile against the builders user projects and the tool consume).
  - Load + invoke `Build()` → typed `Quarry.Migration.SchemaSnapshot`; map back to `Quarry.Shared.Migration.SchemaSnapshot` via test helper (property-by-property); assert `SchemaDiffer.Diff(roundTripped, original)` is empty and `SchemaHasher.ComputeHash` values match.
- [ ] Whitelist-coverage guard (in `SnapshotCompilerTests`): parse the full-featured generated source, collect every invoked method name, assert all ∈ `SnapshotCompiler.AllowedMethods` — pins generator emissions to the whitelist so this drift class can never silently return.
- Commit: `test: snapshot round-trip and whitelist-coverage regression tests (#313)`

### Step 5: Audit the other emit-then-recompile seam
- [ ] `MigrationCompiler.CompileAndBuildSql` (no whitelist → no whitelist drift, compiles against Quarry.dll directly → no namespace drift): audit its null-return paths and callers (`MigrateScript`, `CreateScripts`, bundle paths) for the same silent-degradation pattern; where a discovered migration fails to compile/invoke, apply the same throw semantics. Record findings in workflow.md Working Notes.
- [ ] Grep the tool/generator for any other Roslyn `CSharpCompilation.Create` recompile seams (exploration found exactly two: SnapshotCompiler, MigrationCompiler) — confirm and note.
- Tests: as dictated by findings (at minimum, existing suite green).
- Commit: `fix: MigrationCompiler failures abort loudly; audit recompile seams (#313)`

### Step 6: Documentation touch-up
- [ ] Check `llm-migrate.md`, root `llm.md`, and `src/Quarry.Generator/llm.md` for descriptions of the duplicated model or the two-copy discipline; update to describe the single-source gating. Keep root llm.md usage-only per repo doc-split convention.
- Commit: `docs: describe single-sourced migration model` (fold into Step 5's commit if trivial).

## Dependencies
- Step 2 is independent of Step 1 (both touch SnapshotCompiler — do sequentially to avoid conflicts).
- Step 3 depends on Step 1 (single source must carry the unified API).
- Step 4 depends on Steps 1–3.
- Steps 5–6 are independent, after Step 2.

## Test summary
| Step | New tests | Existing coverage relied on |
|------|-----------|------------------------------|
| 1 | — | Full suite (3388+201 baseline green) |
| 2 | SnapshotCompilerTests: throw-on-disallowed, throw-on-compile-error, null-on-not-found | — |
| 3 | — | All Migration/* suites both domains |
| 4 | SnapshotRoundTripTests + whitelist-coverage guard | — |
| 5 | Per audit findings | Full suite |
