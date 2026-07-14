# Workflow: 313-fix-migration-builder-drift

## Config
platform: github
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #313
pr:

## Problem Statement
Issue #313: Migration model duplication drift — diverged builders + SnapshotCompiler whitelist silently degrade `migrate add`/`diff` to an empty baseline.

The migration schema model types (`ColumnDef`, `TableDef`, `ColumnKind`, `ForeignKeyDef`, `ForeignKeyAction`, `IndexDef`, `NamingStyleKind`, `SchemaSnapshot`, and the `ColumnDefBuilder`/`TableDefBuilder`/`SchemaSnapshotBuilder` trio) exist twice: `src/Quarry/Migration` (namespace `Quarry.Migration`, runtime) and `src/Quarry.Shared/Migration` (namespace `Quarry.Shared.Migration`, generator + CLI). The copies have drifted:

- Runtime `ColumnDefBuilder` has `DefaultValue(string)` / `Nullable(bool = true)`; shared copy has `Default(string)` / `Nullable()`.
- `SnapshotCodeGenerator` emits `.DefaultValue("...")`, which the CLI's `SnapshotCompiler` recompiles against the **shared** builders — and its `AllowedMethods` whitelist omits `DefaultValue`, `Collation`, `CharacterSet` — so such snapshots are rejected, returning `null`.
- `MigrateCommands` passes the `null` previousSnapshot to `SchemaDiffer.Diff`, which treats null as an empty schema → `migrate add`/`diff` silently scaffolds CREATE TABLE for every existing table.

Work items from the issue:
1. **Immediate bug fix**: reconcile builder APIs, extend `SnapshotCompiler.AllowedMethods` (`DefaultValue`, `Collation`, `CharacterSet`), make SnapshotCompiler failure loud (abort, never fall through to empty-baseline diff).
2. **Structural fix**: fold runtime copies into the shared projitems using the existing `#if QUARRY_GENERATOR` gating pattern so a single source compiles into both assemblies.
3. **Regression tests**: round-trip test (SnapshotCodeGenerator output exercising every builder method must recompile through SnapshotCompiler and diff as no-op against itself); test that a failed snapshot compile aborts `migrate add`.
4. Audit other emit-then-recompile seams for the same whitelist-drift pattern.

### Baseline test status
Green: Quarry.Tests 3388/3388 passed, Quarry.Migration.Tests 201/201 passed. No pre-existing failures.

## Decisions
- 2026-07-13 — **Structural approach: single-source the models.** Delete the 11 runtime duplicates in `src/Quarry/Migration`; shared Models+Builders get `#if QUARRY_RUNTIME → namespace Quarry.Migration` (else `Quarry.Shared.Migration`) on top of the existing `#if QUARRY_GENERATOR internal/public` gating; `Quarry.csproj` defines `QUARRY_RUNTIME` and selectively includes those shared files. Tool keeps compiling the same source under `Quarry.Shared.Migration` (string-replace in SnapshotCompiler stays, but drift is impossible). Runtime does NOT gain MigrationStep/MigrationStepType/StepClassification (kept excluded — no new public API).
- 2026-07-13 — **Unified builder API = runtime API.** `DefaultValue(string)`, `Nullable(bool nullable = true)`. Shared-only `Default(string)` is deleted (zero callers, never emitted); `"Default"` is removed from `SnapshotCompiler.AllowedMethods`, and `DefaultValue`, `Collation`, `CharacterSet` are added. A coverage test pins whitelist ⊇ everything SnapshotCodeGenerator can emit.
- 2026-07-13 — **Loud failure = throw.** When a snapshot with the target version exists but fails validation/compilation/invocation, SnapshotCompiler throws (message includes diagnostics); Program.cs top-level catch → stderr + exit 1. Genuine not-found stays null. Applies to migrate add/diff/squash.
- 2026-07-13 — **Hash style:** single-sourced files keep the 17/31 accumulator GetHashCode (netstandard2.0 for generator). Runtime's hash values change — acceptable, never persisted (persisted schema hash is SchemaHasher, single-sourced already).

## Working Notes
- 2026-07-13 (DESIGN exploration):
  - Drift confirmed exactly as issue describes. The ONLY API drift is in `ColumnDefBuilder`: runtime has `DefaultValue(string)` + `Nullable(bool = true)`; shared has `Default(string)` + `Nullable()`. All other duplicated types (8 models + TableDefBuilder + SchemaSnapshotBuilder) are line-identical modulo namespace, `#if QUARRY_GENERATOR internal/public` gating, and GetHashCode style (runtime `HashCode.Combine`, shared 17/31 accumulator for netstandard2.0 — generator targets netstandard2.0, so single-sourced files must use the accumulator).
  - Shared `Default(string)` has ZERO callers anywhere (`ColumnBuilder<T>.Default` in runtime is a different, runtime-only type). `SnapshotCodeGenerator` always emitted `.DefaultValue(` — no user snapshot can ever contain `.Default(`.
  - `SnapshotCodeGenerator` emits these column methods: Name, ClrType, PrimaryKey, ForeignKey, Nullable, Identity, ClientGenerated, Computed, Length, Precision, DefaultValue, HasDefault, MapTo, CustomTypeMapping, Collation; table: Name, Schema, NamingStyle, CharacterSet, AddColumn, AddForeignKey, AddIndex, CompositeKey; snapshot: SetVersion/SetName/SetTimestamp/SetParentVersion, plus `DateTimeOffset.Parse`. Whitelist is missing DefaultValue, Collation, CharacterSet.
  - Consumer map: Quarry (runtime) removes shared Migration/** (has own copies). Generator (QUARRY_GENERATOR) compiles shared as internal. Tool compiles shared as public (no define). Quarry.Tests removes shared Migration except BackupGenerator.cs, and sees `Quarry.Shared.Migration` types via **Quarry.Generator IVT to Quarry.Tests** (`ReferenceOutputAssembly=true`). Tests also Compile-Include 4 Tool files (ProjectSchemaReader, DialectResolver, BundleCommand, CommandHelpers).
  - Tool error handling: command methods print to stderr and `return` — **exit code 0**. Only thrown exceptions produce exit 1 (top-level catch in Program.cs). Loud abort ⇒ throw.
  - `FindAndBuildSnapshot` call sites: MigrateAdd:50 (silent empty-baseline degrade), MigrateDiff:394 (same degrade), MigrateSquash:601 (null-checked, prints error but exits 0).
  - Second emit-then-recompile seam (audit item): `MigrationCompiler.CompileAndBuildSql` — compiles user migration `Upgrade()` against **Quarry.dll directly** (no namespace rewrite, NO whitelist, so no whitelist-drift possible). Returns null on failure — callers to audit for silent degradation.
  - 2026-07-14 Step 5 audit findings: production emit-then-recompile seams are exactly two — SnapshotCompiler and MigrationCompiler (all other `CSharpCompilation.Create` sites are tests/benchmarks). MigrationCompiler has no whitelist and compiles against Quarry.dll directly, so no whitelist/namespace drift is possible there; but it had the same silent null-return pattern, and `MigrateScript` turned a discovered-but-uncompilable migration into an `-- ERROR` comment inside the emitted SQL script with exit 0 (incomplete DDL script a user could apply). Fixed with the same throw semantics; `CreateScripts` builds typed in-process from the extracted schema (no recompile seam). Incidental discovery: the runtime migration-DSL `ColumnBuilder` (t.Column(...)) has no `PrimaryKey()`/`Identity()` methods — its API is ClrType/NotNull/Nullable/DefaultValue/Length/etc.
  - SnapshotCompiler in test context can't round-trip end-to-end: `typeof(SchemaSnapshotBuilder).Assembly` resolves to Quarry.Generator.dll where builders are internal → recompiled snapshot can't see them. Round-trip test should compile generated code against Quarry.dll (public, same single-sourced API).

## Suspend State
- **Position:** IMPLEMENT, plan steps 1–3 of 6 complete and committed (cf81505, a8aac12, 34459af). Next: Step 4 — round-trip + whitelist-coverage regression tests.
- **In progress:** nothing mid-flight; working tree clean, branch pushed to origin.
- **Immediate next step:** write `src/Quarry.Tests/Migration/SnapshotRoundTripTests.cs` exactly per plan Step 4: full-featured shared-domain SchemaSnapshot → `SnapshotCodeGenerator.GenerateSnapshotClass` → compile against Quarry.dll (`typeof(Quarry.Migration.SchemaSnapshotBuilder).Assembly`, zero diagnostics) → load/invoke Build() → map runtime types back to shared via property-by-property test helper → `SchemaDiffer.Diff` no-op + `SchemaHasher.ComputeHash` equal. Plus whitelist-coverage guard in existing `SnapshotCompilerTests.cs` (parse full-featured generated source, every invoked method name ∈ `SnapshotCompiler.AllowedMethods` — both internal, accessible since SnapshotCompiler.cs is Compile-Included in Quarry.Tests).
- **WIP commit:** none.
- **Test status:** all passing — Quarry.Tests 3393/3393 (baseline 3388 + 5 new SnapshotCompilerTests), Quarry.Migration.Tests 201/201.
- **Unrecorded context:** none beyond Working Notes. Reminder for Step 4: in Quarry.Tests, `Quarry.Shared.Migration.*` = Quarry.Generator internals via IVT; `Quarry.Migration.*` = Quarry.dll public — both resolvable side by side. Steps 5 (MigrationCompiler audit) and 6 (docs) remain after Step 4.

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | INTAKE, DESIGN | Worktree + branch created from issue #313; baseline green (3388+201). Explored duplication; user approved single-source approach, DefaultValue whitelist reconciliation, throw-on-failure. |
| 2026-07-14 | PLAN, IMPLEMENT | Plan approved (6 steps). Steps 1–3 implemented, tested green, committed, pushed. Suspended via context check (≥3 steps this session); resume at Step 4. |
| 2026-07-14 | IMPLEMENT | Resumed at Step 4 (round-trip + whitelist-coverage tests). |
