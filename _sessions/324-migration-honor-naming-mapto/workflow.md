# Workflow: 324-migration-honor-naming-mapto
## Config
platform: github
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #324
pr:
## Problem Statement
Migration tooling (`quarry migrate add`/`diff` via `ProjectSchemaReader`) does not honor two
column-naming mechanisms the runtime source generator honors:
- `NamingStyle` override (e.g. `NamingStyle.SnakeCase`)
- per-column `MapTo("physical_name")`

Result: snapshots/DDL use C# property names while runtime uses physical names → migrations create
columns the runtime never queries; adding/removing a mapping produces zero diff steps (silent no-op).

Root causes (per issue #324):
- `ProjectSchemaReader.ExtractTableDef` reads a property literally named `"Naming"` (~line 113); real
  API is `NamingStyle`. Override never detected → stays `NamingStyleKind.Exact`.
- `ProjectSchemaReader.ExtractColumnDef` (~231–319) recognizes only `Computed`/`Collation`; no `MapTo`
  case, so `ColumnDef.MappedName` is always null.
- `DatabaseSchemaReader.NormalizeForDiff` (~218) rebuilds columns dropping `MappedName`; diff keys off
  `Name` only.
- Masking tests: `ProjectSchemaReaderIndexTests.cs` naming tests declare a fictional `NamingStyle Naming`
  property matching the buggy check, so they pass without exercising the real API.

### Baseline (2026-07-14, INTAKE)
Full suite green: Quarry.Tests 3388, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 146. No pre-existing failures.

## Decisions
| Date | Decision |
|------|----------|
| 2026-07-14 | **Representation = Option A** (issue step 3 "simplest"): `ColumnDef.Name` = physical name (`MapTo-arg ?? ToColumnName(prop.Name, style)`); `MappedName` = the `MapTo` argument (or null). Rationale: `MigrationCodeGenerator` (`t.Column(col.Name,…)`) and `DdlRenderer` (`col.Name`) render `Name` and **ignore `MappedName`**, so the physical DDL name must live in `Name`. Populating `MappedName` too keeps `SnapshotCodeGenerator` re-emitting `.MapTo(...)`, and keeps `SchemaHasher`/`RenameMatcher` (both consume `MappedName`) aligned. Option B (Name=logical, MappedName=physical) would additionally require changing MigrationCodeGenerator + builder→operation path — larger blast radius, rejected. |
| 2026-07-14 | **NamingStyle detection**: match the real property name `"NamingStyle"` (was `"Naming"`), parsing expression-body (and getter-arrow) member access → `NamingStyleKind`, keeping `Exact` default. Mirrors runtime `SchemaParser.ExtractNamingStyle`. |
| 2026-07-14 | **MapTo forms**: handle both chained `.MapTo("x")` and standalone generic `MapTo<T>("x")`. Extend `ProjectSchemaReader`'s inline method-name extraction to also handle `GenericNameSyntax` (currently only MemberAccess/Identifier), mirroring runtime `GetMethodName`. |
| 2026-07-14 | **Ref FK column consistency**: in `ExtractTableDef`'s `Ref` branch, use the resolved column name (`colDef.Name`) for the FK constraint column instead of recomputing `ToColumnName(prop.Name,…)` (which ignores MapTo). Keeps FK column == actual column when a Ref uses MapTo. Fallback to recompute if colDef is null. |
| 2026-07-14 | **Issue bullet 3 is stale**: `DatabaseSchemaReader.cs` / `NormalizeForDiff` do not exist. The diff keys columns off `ColumnDef.Name` in `SchemaDiffer.DiffColumns`; no change needed there — once `Name` carries the physical name, add/remove-MapTo diffs stop being no-ops. |
| 2026-07-14 | **No changelog file exists** — behavioral-correction note goes in the PR body instead of a CHANGELOG. |
## Working Notes
- **Migration snapshot pipeline** (the buggy path): `MigrateCommands` → `ProjectSchemaReader.ExtractSchemaSnapshot` → `SchemaSnapshot` → `SchemaDiffer.Diff` (keys columns by `ColumnDef.Name`, threads `MappedName` through `Equals`/`RenameMatcher`) → `MigrationStep` → `MigrationCodeGenerator` (`t.Column(col.Name,…)`, **no MapTo**) → runtime `MigrationBuilder` → `MigrationOperation`(`ColumnDefinition`) → `DdlRenderer` (`col.Name`). Snapshot artifact round-trips via `SnapshotCodeGenerator` (`.Name()`+`.MapTo()`) ↔ `SnapshotCompiler` (Shared `ColumnDefBuilder.Name`/`.MapTo`).
- **Three schema readers exist** — only ProjectSchemaReader (tool) is buggy. `Quarry.Generator/SchemaParser` (runtime SQL) and `Quarry.Migration/SchemaResolver` (SchemaMap for result materialization) both already honor NamingStyle+MapTo correctly. SchemaResolver output type (`SchemaMap`) is unrelated to the migration snapshot path.
- **`MigrationCodeGenerator` ignores `MappedName` entirely** (lines 126–135, 186–189) → this is *why* Option A (Name=physical) is required, not just convenient.
- **3 masking tests**, not 2: `ProjectSchemaReaderIndexTests.cs` lines 90, 377, 397 declare fictional `public NamingStyle Naming => …`. `SchemaTests.cs:30` already uses the real `protected override NamingStyle NamingStyle =>` (tests runtime `Schema` property — unaffected).
- **MapTo API forms** (verified): `Schema.MapTo<T>(string)` standalone (`Col<string> X => MapTo<string>("x")`), and `.MapTo(string)` on `ColumnBuilder<T>`/`RefBuilder` chained (`… .Mapped<…>().MapTo("x")`). AccountSchema.CreditLimit uses the chained form.
- **Round-trip is consistent** either way (with/without MappedName), but MappedName is populated per Decision above.
- **Step 4 sample-compilation decision (F1)**: the real `UserSchema.cs` drags in the whole sample graph (OrderSchema, UserAddressSchema, AddressSchema, `HasMany`/`HasManyThrough`), so the E2E guard compiles the **real** `AccountSchema.cs` + real `Money.cs` (loaded via `[CallerFilePath]` from `../Samples`) with a **minimal `UserSchema` stub** (just `Table` + `Key<int> UserId`). AccountSchema itself — the drift-guard target — stays verbatim.
- **REVIEW remediation (F2/F7)**: the first NamingStyle fix was *more* permissive than the runtime (read initializer + getter-arrow, no override guard). That is itself a divergence bug (migration would style names the runtime leaves Exact). Tightened to mirror `SchemaParser.ExtractNamingStyle` exactly: `!IsStatic && IsOverride` + expression-body-with-member-access only. F5 tests lock this parity in.
## Suspend State
## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-14 | INTAKE→DESIGN | Loaded issue #324, created worktree/branch, clean test baseline. |
| 2026-07-14 | DESIGN→PLAN→IMPLEMENT | Verified full migration pipeline + 3 schema readers; fast-path design+plan approved (all 4 steps). |
| 2026-07-14 | IMPLEMENT→REVIEW | All 4 steps committed (f594cc1, 41c6659, 72d6865, 6ac9148); full suite green (3397/201/146). |
