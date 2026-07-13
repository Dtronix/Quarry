# Workflow: migrate-adopt-existing-db
## Config
platform: github
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
## Problem Statement
Adopting an existing database into Quarry is painful. `migrate add` diffs schema-vs-last-snapshot (never schema-vs-live-DB), so onboarding a legacy DB requires the multi-step "A4" dance: scaffold to legacy names, snapshot, hand-insert a `__quarry_migrations` history row to mark it applied, swap in the real schemas, then hand-author a rename migration. The automatic rename detection uses Levenshtein scoring (`RenameMatcher`: 0.6 accept / 0.8 auto-accept), and systematic renames like snake_case->PascalCase land in that danger zone, so some renames silently degrade to `DROP COLUMN + ADD COLUMN` (data loss).

Goal (this workflow): the full "adopt bundle" of six brainstormed features, delivered in a single PR (per user directive):
1. `--from-database` live-DB diff (adapter: introspection metadata -> SchemaSnapshot).
2. Convention-aware deterministic rename matching (normalize live name through declared NamingStyle, exact-match before Levenshtein fallback).
3. `--rename-map` explicit override file (trusted verbatim, bypasses scoring).
4. `quarry migrate baseline` mark-applied command (reuse squash's history-write mechanics).
5. Non-empty-column drop guard (refuse silent destructive drop against populated column without --allow-data-loss).
6. Bundled `quarry migrate adopt --from-database` wrapping 1-5.

### Baseline (INTAKE)
- Release build: clean (0 errors, 32 pre-existing CS0219 `__colShift` warnings in generated files).
- Test baseline: GREEN — 3628 passed, 0 failed, 0 skipped (Analyzers 146, Migration 201, Quarry 3281). No pre-existing failures.

## Decisions
| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-07-13 | Source = current discussion (not an existing issue). | Feature set emerged from this session's brainstorm on reducing Quarry DB-adoption pain. |
| 2026-07-13 | Scope = full adopt bundle (all 6 features) in ONE PR. User chose this over the recommended "safety core only" first slice. | User directive; recorded despite size/review risk (see Working Notes). |
| 2026-07-13 | Convention-aware canonical-match is ALWAYS-ON in the core differ (not gated to adopt). | User approved. Strictly safer for all `migrate add` users; accepted the behavior change (canonical-equal renames now emit RenameColumn instead of drop+add). |
| 2026-07-13 | Canonical-equal renames are a DETERMINISTIC pre-pass: emitted as RenameColumn before Hungarian/callback, NOT subject to the acceptRename reject callback. | Guarantees no silent data loss regardless of callback/interactivity. Verified existing SchemaDifferRenameTests stay green (their reject/drop+add cases use non-canonical pairs). |
| 2026-07-13 | Drop guard BLOCKS destructive DropColumn/DropTable against populated objects unless --allow-data-loss; only on DB-sourced diffs (adopt / --from-database) where row counts are knowable. | User approved. This is the core safety guarantee of the bundle. |
| 2026-07-13 | `--rename-map` supports both inline (`table.col=new,...`) and `@file`; bare `col=new` applies to all tables, `table.col=new` scopes to one. | User approved. Cross-table column-name collisions require table qualification. |

## Working Notes
- 2026-07-13: SIZE/REVIEW RISK — six features (one new adapter, a matcher-algorithm change, three new/extended CLI verbs, a runtime guard) in a single PR is large and cross-cutting. Recommended a smaller safety-core-first slice; user opted for the full bundle in one PR. Mitigate with small atomic plan.md steps, each independently committable + tested.
- 2026-07-13: Existing machinery to reuse (verified during brainstorm): per-dialect `IDatabaseIntrospector` (`GetTablesAsync/GetColumnsAsync/GetForeignKeysAsync/GetIndexesAsync`) already reads live DBs -> `TableMetadata`/`ColumnMetadata`. `SchemaDiffer` consumes `SchemaSnapshot` (`TableDef`/`ColumnDef`). So `--from-database` is an ADAPTER (metadata->snapshot), not new introspection. `RenameMatcher.MatchColumn`/`MatchTable` gate rename at score>=0.6; `ShouldAutoAccept` at >=0.8. `squash` already DELETE+INSERTs `__quarry_migrations` rows (reuse for baseline/mark-applied).
- 2026-07-13: Levenshtein `Similarity` is ordinal/case-sensitive, so snake->Pascal scores ~0.8 (borderline). This is the data-loss root cause the convention-aware matching must fix.
- 2026-07-13: cwd resets between Bash calls in this session — prefix every command with `cd Z:/Projects/Quarry/migrate-adopt-existing-db &&`.
- 2026-07-13: CI = `dotnet build Quarry.sln -c Release` then `dotnet test Quarry.sln -c Release --no-build` (ci.yml). Integration tests use Testcontainers (Docker required; available locally).
- 2026-07-13: SchemaDiffer design findings (read SchemaDiffer.cs, RenameMatcher.cs, NamingConventions.cs):
  - `SchemaDiffer.Diff(from, to, acceptRename?)` is PURE (no DB). `acceptRename` callback: null => auto-accept at score>=0.8 else drop+add. Column rename detection = `DetectColumnRenames` -> Hungarian solve with 0.6 floor -> `acceptRename`/`ShouldAutoAccept`.
  - FEATURE 2 (convention-aware) design = canonical-equality pre-match: if `Canonicalize(oldName) == Canonicalize(newName)` (lowercase + strip `_`/`-`/space separators) treat as DETERMINISTIC rename (score 1.0, always accept), bypassing Levenshtein. Covers snake/camel/pascal/lower in one shot. Reuse/extend `Quarry.Shared.Migration.NamingConventions` (has `ToColumnName`/`ToSnakeCase`/`ToCamelCase`; both tool+generator share it via QUARRY_GENERATOR guard). GUARD: if canonical form is not unique on either side (collision, e.g. both `user_name` and `UserName` present), fall back to scoring — don't force an ambiguous match.
  - FEATURE 5 (drop guard) must live at the COMMAND layer, not SchemaDiffer (differ has no DB/row-counts). After computing steps in the `--from-database`/`adopt` flow, for any `DropColumn`/`DropTable` step, query row count; if >0 refuse unless `--allow-data-loss`.
  - FEATURE 3 (`--rename-map`) must PRE-TRANSFORM the `from` snapshot (rename old cols to new names + emit explicit RenameColumn steps), because forced pairs can score below the 0.6 Hungarian floor and would never be proposed. Alternatively inject via `acceptRename` only works when scoring already pairs them — insufficient. Pre-transform is the robust route.
  - NamingStyleKind enum {Exact,SnakeCase,CamelCase,LowerCase} in Quarry.Migration; NamingStyle enum (same names) in Quarry. Canonicalize is style-agnostic so it doesn't need the declared style, but declared style can refine confidence later.
- 2026-07-13: FEATURE 4 (baseline / mark-applied) mechanics (agent trace of squash + MigrationRunner):
  - squash's DB-write (MigrateCommands.cs ~692-745) is the template: `CreateConnection(dialect, connstr)` + `ParseDialect` (~866-888), open, txn, DELETE/INSERT into `__quarry_migrations`, params via `SqlFormatting.FormatParameter`+`AddParameter` (cross-dialect for free; PG `$1`, MySQL `?`, else `@pN`).
  - History table 9 cols; NOT-NULL required: version, name, applied_at, checksum, execution_time_ms, applied_by, status. (`started_at`, `squash_from` nullable/omittable.)
  - MigrateAsync skip = `appliedMap.ContainsKey(version)`; appliedMap loaded `WHERE status='applied'`. So baseline row MUST have status='applied'.
  - Checksum = `MigrationRunner.ComputeChecksum(builder.BuildSql(dialect))` (FNV-1a 64-bit hex16 of Upgrade non-idempotent SQL, DIALECT-DEPENDENT). Needed so StrictChecksums validation passes. `ComputeChecksum` is `internal` -> need InternalsVisibleTo(Quarry.Tool) or replicate the 8-line hash.
  - GOTCHA: the TOOL never creates `__quarry_migrations`; only runtime `EnsureHistoryTableAsync` (private) does, via `CREATE TABLE IF NOT EXISTS`. squash assumes table exists. So `baseline` on a fresh adopt DB MUST create the table first (inline the CREATE TABLE IF NOT EXISTS DDL, or expose EnsureHistoryTableAsync). This is a real must-handle for the adopt flow.
  - DryRun returns before any history write (MigrationRunner.cs 246-251) -> cannot be used to baseline. Confirmed.
- 2026-07-13: CLI architecture (agent): hand-rolled parser in Program.cs top-level statements (NO System.CommandLine). Add verbs = new `case` in `DispatchAsync` switch + option helpers (GetOpt/GetOptOrNull/HasFlag/GetPositional) + a PrintUsage line. Command keys are space-joined ("migrate add"). Parser caveat: option values starting with `-` can't be consumed (fine for normal connstrings). `bundle` lives in its own `BundleCommand` class -> precedent to put `adopt`/`baseline` in own Commands/*.cs. `MigrateAdd` currently takes NO conn/dialect. `MigrateSquash` DB-write block (692-745) = history-manip template. GOTCHA: `ScaffoldCommand.CreateIntrospectorAsync` + `BuildConnectionString` are PRIVATE -> must extract to a shared helper (e.g. Schema/DatabaseSchemaReader.cs) so scaffold + migrate both use them. No adopt/baseline/--from-database/--rename-map/--allow-data-loss exists yet — all greenfield.
- 2026-07-13: Adapter (introspection metadata -> SchemaSnapshot), agent gap-analysis: target `Quarry.Shared.Migration` models (same assembly as Scaffold metadata + what SchemaDiffer/ProjectSchemaReader use). Critical conversions: (a) ColumnMetadata has NO CLR type -> call `ReverseTypeMapper.MapSqlType(DataType, dialect, name, isNullable, isIdentity, isPk)` -> ClrType + len/prec/scale (SQLite carries len/prec/scale only inside DataType string; MAX = -1). (b) Column `Kind` inferred by cross-ref of PK column list + FK column list. (c) FK OnDelete/OnUpdate string -> ForeignKeyAction enum. (d) FK ConstraintName null -> synth `FK_{table}_{col}`. (e) skip PK-backing indexes (IndexMetadata.IsPrimaryKey). (f) CompositeKeyColumns from PrimaryKeyMetadata when Count>=2. (g) NamingStyle default Exact (DB names verbatim). Mirror builder = ProjectSchemaReader.ExtractSchemaSnapshot; fluent SchemaSnapshotBuilder/TableDefBuilder/ColumnDefBuilder also available.
- 2026-07-13: Cross-dialect RENAME COLUMN confirmed supported (DdlRenderer.RenderRenameColumn): SqlServer sp_rename, MySQL RENAME COLUMN (8.0+), PG/SQLite ALTER TABLE ... RENAME COLUMN (SQLite 3.25+, no table rebuild). So the rename path works on all four dialects — no SQLite-rebuild caveat.
- 2026-07-13: Existing rename tests (SchemaDifferRenameTests) FORCE detection with `_ => true` accept-all callback; NUnit ([Test]/Assert.That); helpers BuildSnapshot/BuildTable/BuildColumn. Feature 2 must make convention renames deterministic under the DEFAULT (null) callback -> new tests assert rename WITHOUT accept-all.

## Implementation discoveries
- 2026-07-13 (step 1a): Quarry.Tests does NOT project-reference Quarry.Tool. It selectively COMPILES specific tool .cs files into the test assembly via `<Compile Include="../Quarry.Tool/...">` (csproj ~76-80) + imports Quarry.Shared.projitems (Scaffold in, Migration/Sql out — those come from Quarry.dll as public). So every new tool file that needs a unit test must be added to that `<Compile Include>` list (did so for DatabaseSchemaReader.cs). Model types (SchemaSnapshot etc.) resolve from Quarry.dll.
- 2026-07-13 (step 1b): ADAPTER FK diff-noise — introspection can't know the CLR entity name, so `ColumnDef.ReferencedEntityName` is left null on the DB side, but ProjectSchemaReader SETS it (e.g. "Order") on the schema side. `ColumnDef.Equals` compares it, so FK columns will show a spurious AlterColumn in the adopt diff even when only renamed. NOT data loss (rename still detected via canonical name match), but noisy. HANDLE IN STEP 8 (adopt): normalize/ignore ReferencedEntityName when diffing DB-sourced snapshots (e.g. null it on the project side too before diff, or add a diff option). Same likely applies to MappedName. Revisit at step 8.
- 2026-07-13 (step 1b): exact ctors confirmed — ColumnDef(name, clrType, isNullable, kind, isIdentity=…, isClientGenerated, isComputed, maxLength, precision, scale, hasDefault, defaultExpression, mappedName, referencedEntityName, customTypeMapping, computedExpression, collation); TableDef(tableName, schemaName, namingStyle, columns, foreignKeys, indexes, compositeKeyColumns?, characterSet?); ForeignKeyDef(constraintName, columnName, referencedTable, referencedColumn, onDelete=NoAction, onUpdate=NoAction); IndexDef(name, columns, isUnique, filter, method, descendingColumns); ReverseTypeMapper.MapSqlType(sqlType, dialect, columnName, isNullable, isIdentity, isPrimaryKey)->ReverseTypeResult. ColumnKind{Standard,PrimaryKey,ForeignKey}; ForeignKeyAction{NoAction,Cascade,SetNull,SetDefault,Restrict}.

## Step deviations from plan
- Step 4: no IVT change needed — Quarry.csproj already has InternalsVisibleTo for Quarry.Tool AND Quarry.Tests.
- Step 5: `MigrateBaseline` implemented as a METHOD IN MigrateCommands (not a separate BaselineCommand.cs class), to reuse its private file-gen/dialect/connection helpers (FindLatestSnapshotVersion, FindAndBuildSnapshot, ComputeMigrationNames, GenerateCombinedMigrationFile, GenerateUserPartialFile, GuessNamespace, ParseDialect, CreateConnection). Consequence: MigrateCommands.cs is NOT compiled into the test project, so the command method itself isn't directly unit-tested. Its constituents ARE tested (adapter=1b, differ=2, history writer=4, file-gen via existing MigrateAdd/MigrationCodeGenerator tests) and the core baseline guarantee (mark-applied -> runner skips, no DDL) is proven in MigrationHistoryWriterTests.MarkApplied_ThenRun_SkipsBaselinedMigration_NoDdlExecuted.
- Step 5: baseline writes checksum sentinel "baseline" (mirrors squash's "squashed"). MigrateAsync skips by version; non-strict checksum validation ignores it. KNOWN LIMITATION (same as squash): StrictChecksums=true will warn on baselined migrations. Computing the real checksum would need steps->MigrationBuilder->BuildSql plumbing; deferred as not worth it given squash precedent.
- Step 8 (adopt) will reuse MigrateBaseline + the from-database diff; keep it as a MigrateCommands method too for the same private-helper reason.

## Proposed Design (approved)
Coherent command composition (avoids overlap between features 1/4/6):
- Feature 1 `--from-database <connstr>` (+`-d`): on `migrate add` and `migrate diff`, sources the COMPARISON ("from") snapshot from a live DB instead of the last project snapshot. Building block + standalone live-DB preview.
- Feature 4 `migrate baseline <name> [--from-database]`: persist a snapshot (from current project schemas, or from live DB) as version N AND write a `status='applied'` history row WITHOUT executing DDL. Own Commands/BaselineCommand.cs. Must create `__quarry_migrations` if absent; checksum via ComputeChecksum.
- Feature 6 `migrate adopt --from-database -d`: orchestration = baseline(DB-state as v1, marked applied) THEN add(diff v1 vs project schemas -> rename migration v2, NOT applied). Own Commands/AdoptCommand.cs. KEY INSIGHT: once baseline persists the DB-state snapshot as a real project snapshot, ordinary `migrate add` already produces the renames (diffs v1 vs schemas) — adopt just wires the two together + applies guards.
- Feature 2 convention-aware matching: canonical-equality pre-match inside RenameMatcher/SchemaDiffer (deterministic, style-agnostic).
- Feature 3 `--rename-map`: parsed into an acceptRename-forcing map; forced pairs pre-transform the `from` snapshot (can score < 0.6 Hungarian floor).
- Feature 5 `--allow-data-loss` drop guard: command layer, only on DB-sourced diffs (add --from-database / adopt), blocks DropColumn/DropTable against populated objects unless flag set.
Shared refactor: extract introspection (connstring build + introspector factory + per-table loop + metadata->snapshot adapter) into Schema/DatabaseSchemaReader.cs used by scaffold, add, baseline, adopt.

### Open decisions for user (DESIGN gate)
1. Convention-aware matching: always-on in core differ (safer, but changes existing `migrate add` diff output for ALL users) vs gated to DB-sourced/adopt path only.
2. Drop guard default: BLOCK destructive drops unless --allow-data-loss (safe default) vs only WARN.
3. `--rename-map` format: inline `table.col=new,...` and/or `@file`.

## Suspend State
- **Phase/position:** IMPLEMENT — steps 1a, 1b, 2, 3, 4, 5 complete and committed+pushed (6 of 10 plan checkboxes). Next up: **step 6**.
- **In progress:** nothing — working tree clean, all committed. No WIP commit.
- **Immediate next step:** Step 6 — add `--from-database <connstr>` + `-d/--dialect` + `-c/--connection` params to `MigrateAdd` and `MigrateDiff` (MigrateCommands): when `--from-database` set, the "from"/comparison snapshot = `DatabaseSchemaReader.ReadTablesAsync` + `ToSnapshot` instead of `FindAndBuildSnapshot`. Wire options in Program.cs (mirror the `migrate baseline` case just added). Keep the opened `DbConnection` available for the step-7 drop guard. Test intent: `add --from-database` against a seeded SQLite legacy snake_case DB vs PascalCase project schemas emits RenameColumn (via the always-on canonical pass), no drop+add. NOTE: MigrateCommands is NOT in the test project, so prefer testing the seam via DatabaseSchemaReader.ToSnapshot + SchemaDiffer.Diff composition (already unit-testable) rather than the CLI method; or add a focused runner-style integration test.
- **Remaining steps:** 6 (--from-database), 7 (drop guard), 8 (adopt orchestration — reuse MigrateBaseline + from-database diff + RenameMap.ApplyForcedRenames + drop guard; keep as MigrateCommands method), 9 (docs: README + llm.md + llm-migrate.md, replace A4 dance). See plan.md.
- **Test status:** ALL GREEN — full suite 3662 passed / 0 failed / 0 skipped (Analyzers 146, Migration 201, Quarry 3315). Baseline was 3628; +34 new tests so far.
- **HEAD:** b8c0962 (step 5). Branch pushed to origin/migrate-adopt-existing-db (no PR yet; CI runs only on PR / master, not branch push).
- **Carry-forward gotchas (also in Working Notes / Step deviations):** FK `ReferencedEntityName` is null on DB-side snapshots but set on schema-side -> spurious AlterColumn noise on FK columns in the adopt diff; NEUTRALIZE IN STEP 8 (e.g. null-out ReferencedEntityName/MappedName on the project side before diffing, or ignore in comparison). Every new tool .cs file needing tests must be added to Quarry.Tests csproj `<Compile Include>`. `CreateConnection`/`ParseDialect`/file-gen helpers are private in MigrateCommands -> keep from-database/adopt logic as MigrateCommands methods. Drop guard (step 7) needs an open DbConnection + `SELECT COUNT(*)` per DropColumn/DropTable; blocks unless `--allow-data-loss`.

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | Bootstrap, INTAKE | Chose new workflow from discussion over 3 existing worktree workflows (305 FINALIZE/suspended, 307 DESIGN, 308 IMPLEMENT). Detected github/master. Created worktree+branch. Baseline build clean; tests running. Transition to DESIGN. |
| 2026-07-13 | DESIGN | Explored via 3 agents (CLI arch, metadata/snapshot models, history-write) + self (SchemaDiffer/RenameMatcher/NamingConventions/DdlRenderer). Locked design: 3 CLI verbs (add --from-database, baseline, adopt) + convention-aware match + rename-map + drop guard + shared DatabaseSchemaReader refactor. 3 design decisions approved (always-on match, block-drops-default, dual rename-map format). Transition to PLAN. |
| 2026-07-13 | PLAN, IMPLEMENT | Plan approved (10 steps). Implemented + committed steps 1a (extract DatabaseSchemaReader), 1b (metadata->snapshot adapter), 2 (always-on canonical rename pre-pass). Full suite green (3648). Suspended at 3-step context checkpoint; branch pushed. Resume at step 3. |
| 2026-07-13 | IMPLEMENT (resume) | Implemented + committed steps 3 (--rename-map + forced-rename pre-transform), 4 (MigrationHistoryWriter + squash refactor), 5 (migrate baseline command). Full suite green (3662). Suspended at 3-step checkpoint; branch pushed (HEAD b8c0962). Resume at step 6. |
