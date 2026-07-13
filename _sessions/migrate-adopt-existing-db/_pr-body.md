## Summary

Adds the full **"adopt an existing database"** bundle so a legacy DB (commonly snake_case) can be brought under Quarry migrations without the multi-step manual dance, and without silently degrading systematic renames into data-losing `DROP COLUMN + ADD COLUMN`.

Six features, delivered together:

1. **`--from-database <connstr>`** on `migrate add` / `migrate diff` — sources the comparison ("from") snapshot from a live database via introspection instead of the last project snapshot.
2. **Always-on convention-aware rename matching** — a deterministic canonical-equality pre-pass in the core differ emits `RENAME COLUMN`/`RENAME TABLE` for case/separator-only renames (snake↔Pascal↔camel↔lower), so they can never silently become drop+add.
3. **`--rename-map`** — explicit rename overrides (inline `table.col=New,bare=New` or `@file`), trusted verbatim, now **validated** against both the live DB and the project schema before anything is written.
4. **`migrate baseline <name>`** — records a snapshot (from project schemas or a live DB) as `status='applied'` in `__quarry_migrations` **without executing DDL**.
5. **Data-loss drop guard** — a `DROP` of a *populated* column/table on a DB-sourced diff aborts unless `--allow-data-loss` is passed.
6. **`migrate adopt --from-database`** — one command wrapping 1–5: baseline the live state as an applied `InitialCreate`, then generate a single pending alignment migration to match the project schemas.

Shared refactor: introspection (connection-string build + introspector factory + per-table loop + metadata→`SchemaSnapshot` adapter) extracted into `Schema/DatabaseSchemaReader.cs`, used by `scaffold`, `add`, `baseline`, and `adopt`.

Source: current discussion (no tracking issue).

## Reason for Change

Adopting an existing database was painful: `migrate add` only diffs schema-vs-last-snapshot (never schema-vs-live-DB), so onboarding required scaffolding to legacy names, snapshotting, hand-inserting a `__quarry_migrations` history row, swapping in the real schemas, and hand-authoring a rename migration. Worse, automatic rename detection used Levenshtein scoring where systematic snake_case→PascalCase renames landed in the borderline zone and could silently degrade to `DROP + ADD` (data loss). This bundle makes adoption a single command and makes convention renames deterministic and safe.

## Impact

- New CLI verbs `migrate baseline`, `migrate adopt` and flags `--from-database` / `-d` / `--rename-map` / `--allow-data-loss`.
- **Behavior change for all `migrate add` users** — see Breaking Changes.
- New tool source: `Schema/DatabaseSchemaReader.cs`, `Schema/RenameMap.cs`, `Schema/MigrationHistoryWriter.cs`, `Schema/DropGuard.cs`.
- Full suite green: **3714 → 3796** after final rebase (0 failed / 0 skipped); +~70 new tests across the feature and the review-remediation pass.

## Plan items implemented as specified

- **1a** Extract shared `DatabaseSchemaReader` from `ScaffoldCommand` (no behavior change).
- **1b** Metadata → `SchemaSnapshot` adapter (CLR types via `ReverseTypeMapper`, kind inferred from PK/FK lists, PK-backing indexes skipped, composite keys).
- **2** `NamingConventions.Canonicalize` + always-on canonical rename pre-pass (not subject to the `acceptRename` reject callback).
- **3** `--rename-map` parse (inline + `@file`) + forced-rename pre-transform for pairs below the Hungarian floor.
- **4** `MigrationHistoryWriter` (ensure-table + mark-applied); `squash` refactored to use it.
- **6** `--from-database` on `add`/`diff` via a shared `ResolveFromSnapshotAsync` (default path unchanged).
- **7** `--allow-data-loss` drop guard on DB-sourced diffs.
- **9** Docs: `README.md`, `llm.md`, `llm-migrate.md` (the manual "A4 dance" replaced by `adopt`).

## Deviations from plan implemented

- **`baseline` / `adopt` implemented as methods on `MigrateCommands`** (not separate `*Command.cs` classes) to reuse its private file-generation / dialect / connection helpers. Consequence: the command orchestration is covered by-composition (adapter, differ, guard, history writer tested in isolation) rather than by direct CLI unit tests.
- **Step 8 uncovered a snapshot field-asymmetry bug** and added `DatabaseSchemaReader.NormalizeForDiff`: `ProjectSchemaReader` leaves identity/length/default/FK/index at defaults while the introspection adapter fills them richly, so a raw DB-vs-schema diff over-reports `AlterColumn` for nearly every column. Both sides are normalized to the reliably-shared subset (name/type/nullable/kind) for the diff; files are still generated from the rich snapshots. FK/index changes are intentionally out of scope for the alignment diff (see Migration note below).
- **`baseline`/`adopt` write a checksum sentinel `"baseline"`** (mirrors squash's `"squashed"`); `StrictChecksums=true` will warn on baselined migrations (accepted, squash-consistent).

## Gaps in original plan implemented (review remediation)

A structured review (0 H / 5 M / 10 L) was run on the integrated diff; all 5 M and the actionable L findings were fixed (5A + 6B; 4 dismissed as accepted/informational):

- **Schema-aware drop guard (F5, M)** — a normalized diff strips the schema qualifier, so the guard now re-qualifies each drop with the live table's real schema (`DropGuard.BuildTableSchemaMap`/`ResolveSchema`). Previously a drop against a table in a non-default PostgreSQL/SqlServer schema could mis-count and **bypass** the guard. Verified end-to-end on real PostgreSQL (`AdoptGuardPostgresTests`).
- **Rename-map validation (F6/F7, M/L)** — `RenameMap.Validate` rejects a target absent from the project schema, a duplicate/colliding target, and warns on entries matching nothing. `adopt` now parses + validates the map **before** writing the baseline, so an invalid map can no longer leave a half-adopted database (baseline marked applied, then a crash).
- **Canonical table-rename schema transfer (F8, L)** — a canonical table rename that also moves schema now carries `oldSchemaName`, instead of silently dropping the move.
- **Non-fresh adopt warning (F2, L)** — `adopt` warns when the project already has migrations (baseline is recorded at `latest+1` and earlier versions are not reconciled).
- **Missing safety test (F9, M)** — added `AdoptGuardScenarioTests`: the introspect→normalize→diff→guard pipeline flags a populated unmapped column (adopt aborts) and the `--allow-data-loss` branch is asserted.
- **Test coverage (F10, L)** — added real-PostgreSQL multi-schema guard tests + `DropGuard` schema-qualification unit tests; history-table DDL **parity test** (F11) comparing the writer's DDL against the runtime's.
- **Dead-code cleanup (F12, L)** — removed a duplicated `AddParameter` helper left in `MigrateCommands` after the squash refactor.

## Migration Steps

- Recommended path: `quarry migrate adopt <Name> -c "<connstr>" -d <dialect> [--rename-map …]`, then `await db.MigrateAsync(connection)` — `InitialCreate` is skipped and the alignment migration renames columns in place, preserving data.
- The alignment diff focuses on columns (renames + type/nullability). It does **not** reconcile foreign keys or indexes captured only in the baseline — add genuinely new ones with a follow-up `quarry migrate add`.

## Security Considerations

- Row-count queries quote all interpolated identifiers via `SqlFormatting.QuoteIdentifier`; history writes are fully parameterized (positional order preserved for MySQL). Connection strings are never echoed to stdout/stderr. The review's Security pass returned no concerns.

## Breaking Changes

- **Internal / behavioral (all `migrate add` users):** canonical name matching is now **always on** in the core differ, not just in `adopt`. An add+drop pair whose names are equal under canonicalization (case/separator-only, e.g. `user_name`↔`UserName`) is always emitted as a `RENAME COLUMN`/`RENAME TABLE` and is **not** offered to the interactive / `acceptRename` confirmation. This strictly prevents data loss, but a user who genuinely intends to drop a column and add a canonically-equal one (discarding the old data) must split it across two migrations or use `--allow-data-loss` on a DB-sourced diff. Interactive users no longer see a "Is this a rename?" prompt for convention-only renames.
- **Consumer-facing API:** none. New verbs/flags are additive; `MigrateAdd`/`MigrateDiff` gained optional parameters with defaults (source-compatible).
