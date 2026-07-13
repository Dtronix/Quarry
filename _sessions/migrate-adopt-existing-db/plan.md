# Plan: migrate-adopt-existing-db

Full "adopt existing database" bundle (6 features) in one PR. Steps are ordered by dependency and each is independently committable + testable. Test framework is **NUnit** (`[Test]`, `Assert.That`). Build/test mirror CI: `dotnet build Quarry.sln -c Release` then `dotnet test Quarry.sln -c Release --no-build`. Baseline is 3628 green tests.

## Key concepts

**Coherent composition.** `adopt` is not a monolith — it orchestrates two smaller commands. `baseline` persists the live-DB state as a real project snapshot (v1) *and* writes a `status='applied'` history row without executing DDL. Once that snapshot exists, an ordinary `migrate add` diffs v1 (the DB state) against the PascalCase schemas and produces the rename migration (v2). `adopt` = `baseline --from-database` then `add` with the safety guards wired on.

**Canonical rename match (always-on, deterministic).** Before Hungarian/Levenshtein scoring, a pre-pass matches added↔dropped columns (and tables) whose names are equal under `Canonicalize` (lowercase + strip `_`/`-`/space). These are emitted as `RenameColumn`/`RenameTable` directly, removed from the add/drop working lists, and are **NOT** subject to the `acceptRename` reject callback — guaranteeing snake↔pascal↔camel↔lower renames never become silent drop+add. Ambiguous canonical collisions (same canonical form appears >1× on either side) fall back to the existing scoring path.

**Drop guard.** Only meaningful when a live connection exists (DB-sourced diffs). After computing steps, any `DropColumn`/`DropTable` is checked against `SELECT COUNT(*)`; if >0 the command aborts unless `--allow-data-loss`.

**Adapter target.** Build `Quarry.Shared.Migration.SchemaSnapshot`/`TableDef`/`ColumnDef`/`ForeignKeyDef`/`IndexDef` — same assembly as `Quarry.Shared.Scaffold` metadata and what `SchemaDiffer` consumes. CLR type comes from `ReverseTypeMapper.MapSqlType(...)` (metadata has no CLR type). Column `Kind` inferred from PK/FK column lists. FK `OnDelete/OnUpdate` string→`ForeignKeyAction` enum; null constraint name→`FK_{table}_{col}`. Skip PK-backing indexes; composite PK when `PrimaryKeyMetadata.Columns.Count >= 2`; `NamingStyle` defaults to `Exact`.

**CLI shape.** Hand-rolled parser in `Quarry.Tool/Program.cs` (no System.CommandLine). New verbs = new `case` in the `DispatchAsync` switch + `PrintUsage()` line; options via `GetOpt`/`GetOptOrNull`/`HasFlag`/`GetPositional`. Large verbs get their own `Commands/*Command.cs` (bundle precedent).

## Dependencies
- Steps 2–3 (engine) are independent of steps 1/4 (infra) — can proceed in parallel conceptually but committed sequentially.
- Step 6 (`--from-database`) depends on 1 (adapter). Step 5 (`baseline`) depends on 1 + 4. Step 7 (drop guard) depends on 6. Step 8 (`adopt`) depends on 5 + 6 + 7 (+2,3). Step 9 (docs) last.

---

## Steps

- [x] **1a. Extract shared DB-introspection helper (no behavior change).**
  Move `CreateIntrospectorAsync` and `BuildConnectionString` out of `ScaffoldCommand` (currently `private static`) into a new `src/Quarry.Tool/Schema/DatabaseSchemaReader.cs` (`internal static`). Also lift the per-table introspection loop (`ScaffoldCommand.cs:63-71`, building `TableIntrospectionData`) into a reusable method `ReadTablesAsync(dialect, connStr, schemaFilter, tableFilter)`. Refactor `ScaffoldCommand` to call the shared helper.
  *Tests:* no new behavior — existing `src/Quarry.Tests/Scaffold/*` must stay green. Add one guard test that `DatabaseSchemaReader.BuildConnectionString` returns the same string ScaffoldCommand used to (parity).

- [x] **1b. Metadata → SchemaSnapshot adapter.**
  In `DatabaseSchemaReader`, add `SchemaSnapshot ToSnapshot(IReadOnlyList<TableIntrospectionData> tables, string dialect, int version, string name, int? parentVersion)`. Per table build `TableDef(name, schema, NamingStyleKind.Exact, columns, fks, indexes, compositeKeyColumns, characterSet:null)`. Per column: `ReverseTypeMapper.MapSqlType(meta.DataType, dialect, meta.Name, meta.IsNullable, meta.IsIdentity, isPk)` → `ClrType`+len/prec/scale (prefer explicit `ColumnMetadata.MaxLength/Precision/Scale`, fall back to parsed); `Kind` = PrimaryKey if in PK cols, ForeignKey if in any FK col, else Standard; `HasDefault = DefaultExpression != null`. FK: coalesce constraint name, convert action strings→`ForeignKeyAction`. Indexes: skip `IsPrimaryKey`. Composite PK when `>=2`.
  *Tests:* new `src/Quarry.Tests/Scaffold/DatabaseSchemaReaderAdapterTests.cs` — spin a SQLite `:memory:` (or Testcontainers PG) with a known DDL (identity PK, nullable col, FK, unique index, composite PK table), introspect, assert the resulting `TableDef`/`ColumnDef` fields (ClrType, Kind, IsIdentity, MaxLength, FK action, PK-index skipped, composite PK). At least SQLite; add PG if cheap.

- [x] **2. Canonicalize helper + convention-aware deterministic rename pre-pass (always-on).**
  Add `NamingConventions.Canonicalize(string)` (lowercase + remove `_`,`-`,space) in `src/Quarry.Shared/Migration/NamingConventions.cs`. In `SchemaDiffer`: before `DetectColumnRenames`/`DetectTableRenames`, run a pre-pass that pairs added↔dropped by canonical equality **only when the canonical form is unique on both sides**; emit `RenameColumn`/`RenameTable` (+ trailing `AlterColumn` if other props differ, mirroring existing logic) and remove matched entries from the add/drop lists before scoring. Do not consult `acceptRename` for these.
  *Tests:* extend `SchemaDifferRenameTests` — (a) `user_name`→`UserName` emits `RenameColumn` under the **default null callback** (no accept-all) and under `_ => false` (reject ignored for canonical); (b) table `order_items`→`OrderItems` deterministic; (c) canonical collision (`user_name` + `UserName` both added vs one dropped) falls back to scoring (no crash, no wrong pairing); (d) genuinely different names (`first_name`→`full_name`) still NOT force-matched. Confirm all pre-existing rename tests stay green.

- [ ] **3. `--rename-map` parsing + forced-rename pre-transform.**
  Add `src/Quarry.Tool/Schema/RenameMap.cs`: `Parse(string spec)` handling inline `table.col=new,bare=new` and `@file` (CSV `table,from,to` or `from,to`). Add a pure `ApplyForcedRenames(SchemaSnapshot from, RenameMap map)` util that renames matching columns in the `from` snapshot and returns the forced `(table, old, new)` list so the command can emit explicit `RenameColumn` steps for pairs that would score below the 0.6 floor.
  *Tests:* `src/Quarry.Tests/Migration/RenameMapTests.cs` — inline parse (qualified + bare), `@file` parse, precedence (qualified over bare), and a differ test proving a sub-0.6 forced pair (e.g. `qty`→`Quantity` with different type) still yields `RenameColumn` not drop+add.

- [ ] **4. Migration-history writer + checksum access.**
  Add `[assembly: InternalsVisibleTo("Quarry.Tool")]` to `Quarry` (or make `MigrationRunner.ComputeChecksum` public — prefer InternalsVisibleTo to avoid widening API). Add `src/Quarry.Tool/Schema/MigrationHistoryWriter.cs` extracting the squash DB-write block: `EnsureHistoryTableAsync` (inline `CREATE TABLE IF NOT EXISTS __quarry_migrations` per dialect — copy runtime DDL), and `MarkAppliedAsync(conn, dialect, version, name, checksum)` (parameterized INSERT via `SqlFormatting.FormatParameter`/`AddParameter`, `status='applied'`). Refactor `MigrateSquash` to use the writer (parity, no behavior change).
  *Tests:* `src/Quarry.Tests/Migration/MigrationHistoryWriterTests.cs` (SQLite) — ensure table created on fresh DB; `MarkAppliedAsync` inserts a row that `MigrationRunner.GetAppliedVersionsWithChecksumsAsync` reports as applied; checksum equals `ComputeChecksum(builder.BuildSql(SQLite))`. Existing squash tests stay green.

- [ ] **5. `migrate baseline <name>` command.**
  New `src/Quarry.Tool/Commands/BaselineCommand.cs`. Builds the snapshot from project schemas (`ProjectSchemaReader.ExtractSchemaSnapshot`) or, with `--from-database`/`-d`, from `DatabaseSchemaReader.ToSnapshot`. Writes the `M{v:D4}_{name}.g.cs` migration + `[MigrationSnapshot]` + user partial (reuse `GenerateCombinedMigrationFile`/`GenerateUserPartialFile`). If a connection is provided, marks it applied via `MigrationHistoryWriter` (compute checksum from the generated migration's `Upgrade` SQL via `MigrationCompiler.CompileAndBuildSql`). Wire `case "migrate baseline"` in `Program.cs` + `PrintUsage` line. Options: `-p/--project`, `-o/--output`, `--ni`, `--from-database`, `-d/--dialect`, `-c/--connection`.
  *Tests:* `src/Quarry.Tests/Migration/BaselineCommandTests.cs` — baseline from schemas writes files; `--from-database` (SQLite seeded) writes DB-state snapshot + applied row; a subsequent `MigrateAsync` skips the baselined version (integration, owned-schema mode).

- [ ] **6. `--from-database` on `migrate add` and `migrate diff`.**
  Add `string? fromDatabase`, `string? dialect`, `string? connection` params to `MigrateAdd`/`MigrateDiff`; when `--from-database` set, the "from" snapshot = `DatabaseSchemaReader.ToSnapshot(...)` instead of `FindAndBuildSnapshot`. Wire options in `Program.cs`. Keep the open `DbConnection` around (needed by step 7).
  *Tests:* `MigrateAddFromDatabaseTests.cs` — seed SQLite with legacy snake_case table, point project schemas at PascalCase, run `add --from-database` → generated migration contains `RenameColumn` steps (assert via generated file text or by diffing snapshots), no drop+add.

- [ ] **7. `--allow-data-loss` drop guard on DB-sourced diffs.**
  Add `HasFlag(opts, null, "allow-data-loss")` to the `add`/`diff`/`adopt` cases. After the diff, when a live connection exists, for each `DropColumn`/`DropTable` step run `SELECT COUNT(*)` on the target; if >0 and flag unset, abort with an explicit message listing the offending objects. Put the check in a shared `DropGuard.CheckAsync(conn, dialect, steps, allowDataLoss)`.
  *Tests:* `DropGuardTests.cs` (SQLite) — populated column drop aborts; empty table proceeds; `--allow-data-loss` overrides; non-destructive steps unaffected.

- [ ] **8. `migrate adopt` command (orchestration).**
  New `src/Quarry.Tool/Commands/AdoptCommand.cs`. Flow: (1) introspect DB → snapshot v1; write baseline files + mark applied (step 5 path). (2) diff v1 vs project schemas with convention-match (step 2), `--rename-map` forced renames (step 3), producing migration v2; (3) run drop guard (step 7). Requires `-c/--connection` + `-d/--dialect` (guard like `migrate status`). Options also: `-p`, `-o`, `--ni`, `--rename-map`, `--allow-data-loss`. `Program.cs` case + `PrintUsage`.
  *Tests:* `AdoptCommandTests.cs` — end-to-end against seeded SQLite (legacy snake_case + data): after adopt, v1 is applied, v2 rename migration exists, applying v2 via `MigrateAsync` renames columns and **preserves row data** (assert row counts + values before/after). Add a case where an unmapped column would drop → aborts without `--allow-data-loss`.

- [ ] **9. Documentation.**
  Update `src/Quarry.Tool/README.md`, `llm.md` (Migrations & Scaffolding section), and `llm-migrate.md` (Phase 6 — replace the manual A4 dance with the `adopt` workflow). Document `migrate adopt`, `migrate baseline`, `--from-database`, `--rename-map`, `--allow-data-loss`, and the always-on convention-aware rename behavior.
  *Tests:* none (docs); verify no code references broken.

---

## Risks / watch-items
- Convention-match is a shared-engine behavior change — step 2 must prove all pre-existing rename tests stay green (their reject/drop+add cases use non-canonical pairs, verified during DESIGN).
- `InternalsVisibleTo` on `Quarry` must not leak into the public NuGet surface unexpectedly — confirm pack still clean.
- Integration tests need Docker/Testcontainers for PG/MySQL/SqlServer; prefer SQLite `:memory:`/owned-schema for fast unit coverage, add one cross-dialect adopt smoke test.
- Keep each command's option wiring in sync with `PrintUsage` (discoverability).
