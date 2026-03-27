# Migrations

Code-first migration scaffolding via the `quarry` CLI tool. The tool reads C# schema definitions via Roslyn, diffs against the previous snapshot, and generates migration files -- no database connection required for scaffolding.

## How It Works

The migration system operates in three layers:

1. **Quarry.Tool (CLI)** -- Opens your `.csproj` via MSBuild/Roslyn, discovers all `Schema` subclasses, extracts table/column/index/FK metadata, diffs against the previous snapshot, and generates migration code.
2. **Quarry.Shared (diffing and codegen)** -- `SchemaDiffer` computes structural differences between two `SchemaSnapshot` instances. `MigrationCodeGenerator` and `SnapshotCodeGenerator` emit the C# files. `BackupGenerator` produces backup/restore SQL for destructive steps. `RenameMatcher` uses Levenshtein distance to detect renames vs. drop+add.
3. **Quarry runtime** -- `MigrationRunner.RunAsync()` executes migrations against a live `DbConnection`. `DdlRenderer` translates `MigrationBuilder` operations into dialect-specific DDL. The source generator emits a `MigrateAsync` method on each `QuarryContext` that wires everything together.

The key design decision is that scaffolding never touches a database. The previous schema state is stored as a compilable C# snapshot class. When you run `quarry migrate add`, the tool compiles that snapshot in a collectible `AssemblyLoadContext`, invokes its `Build()` method to reconstruct a `SchemaSnapshot`, and diffs the result against your current schema classes. This means you can scaffold migrations in CI, on a developer laptop with no database server, or in any environment with the .NET SDK.

## Setup

```sh
dotnet tool install --global Quarry.Tool
```

Requirements:
- .NET 10 SDK
- A project referencing `Quarry` with schema classes inheriting `Schema`
- A `[QuarryContext(Dialect = ...)]` attribute on your context class (used for dialect auto-detection)

## CLI Commands

```sh
quarry migrate add InitialCreate                # scaffold from schema changes
quarry migrate add AddUserEmail -p src/MyApp    # specify project path
quarry migrate add-empty SeedData               # empty migration for custom SQL
quarry migrate list                             # list all migrations
quarry migrate validate                         # check version integrity
quarry migrate remove                           # remove latest migration files
quarry migrate diff                             # preview schema changes (no file generation)
quarry migrate script                           # generate incremental migration SQL
quarry migrate status -c <conn>                 # show applied vs pending (requires connection)
quarry migrate squash                           # collapse all into a single baseline
quarry migrate bundle                           # build self-contained migration executable
quarry create-scripts -d postgresql -o schema.sql  # generate full DDL
```

### `migrate add <name>`

Scaffolds a new migration from schema changes.

```sh
quarry migrate add AddUserEmail
quarry migrate add AddUserEmail -p src/MyApp
quarry migrate add AddUserEmail -o Migrations/Schema
quarry migrate add AddUserEmail --ni   # non-interactive (CI mode)
```

| Flag | Long | Default | Description |
|------|------|---------|-------------|
| `-p` | `--project` | `.` | Path to `.csproj` file or directory containing one |
| `-o` | `--output` | `Migrations` | Output directory for generated files (relative to project) |
| | `--ni` | `false` | Non-interactive mode -- auto-accepts renames with score >= 0.8, skips prompts |

What it does:
1. Opens your project via MSBuild/Roslyn and compiles it.
2. Finds the latest `[MigrationSnapshot]` version in your code.
3. Extracts the current schema from all `Schema` subclasses.
4. If a previous snapshot exists, compiles it in memory and invokes `Build()` to reconstruct it.
5. Diffs old vs. new schema (tables, columns, foreign keys, indexes).
6. Prompts for rename confirmation when the differ detects a possible rename (Levenshtein distance matching).
7. Generates snapshot and migration files in the output directory.
8. Prints a summary with risk-classified steps.

Uses file locking (`.quarry-migrate.lock`) to prevent concurrent version conflicts. If no changes are detected, prints "No schema changes detected." and generates nothing.

### `migrate add-empty <name>`

Creates a migration with empty `Upgrade()`/`Downgrade()` methods.

```sh
quarry migrate add-empty SeedDefaultRoles
```

Use this for data migrations (inserting/updating rows via `builder.Sql(...)` or typed seed methods), business logic changes that need a version marker, or manual DDL that the diff engine cannot express. This does not generate a snapshot file -- the next `migrate add` will still compare against the last snapshot.

### `migrate diff`

Preview schema changes without generating migration files.

```sh
quarry migrate diff
quarry migrate diff -p src/MyApp
```

Performs the same diff logic as `migrate add` but only outputs the results to the console. No files are created.

### `migrate list`

Lists all migrations discovered in the project by scanning for classes with `[Migration(Version = N, Name = "...")]` attributes.

```sh
quarry migrate list
```

### `migrate validate`

Checks the migration chain for integrity issues:
- **Duplicate versions** -- two migrations with the same `Version` (ERROR)
- **Version gaps** -- non-sequential version numbers, e.g., 1, 2, 5 (WARNING)

### `migrate remove`

Removes the latest migration's files from disk. Supports both directory-based layout (`M0001_Name/`) and flat layout (`Migration_NNN_*` / `Snapshot_NNN_*`). This is a file-system operation -- it does not modify the database.

## Generated Migration

Each `migrate add` generates two files:

1. **A migration class** (`M0001_InitialCreate.cs`) with `Upgrade()`, `Downgrade()`, and `Backup()` methods, plus partial hooks for `BeforeUpgrade`/`AfterUpgrade`/`BeforeDowngrade`/`AfterDowngrade`.
2. **A snapshot class** that captures the full schema state at that point in time, stored as compilable C# -- not JSON, not binary.

### Upgrade and Downgrade

The `Upgrade()` method contains `MigrationBuilder` calls that correspond to the detected schema changes:

```csharp
[Migration(Version = 2, Name = "AddCreatedAt")]
internal static partial class M0002_AddCreatedAt
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);
        builder.AddColumn("users", "created_at", c => c
            .ClrType("DateTime").NotNull().DefaultExpression("CURRENT_TIMESTAMP"));
        builder.AddIndex("IX_Created", "users", ["created_at"], descending: [true]);
        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropIndex("IX_Created", "users");
        builder.DropColumn("users", "created_at");
        AfterDowngrade(builder);
    }

    public static void Backup(MigrationBuilder builder)
    {
    }

    static partial void BeforeUpgrade(MigrationBuilder builder);
    static partial void AfterUpgrade(MigrationBuilder builder);
    static partial void BeforeDowngrade(MigrationBuilder builder);
    static partial void AfterDowngrade(MigrationBuilder builder);
}
```

`Downgrade()` reverses the steps in reverse order. The partial hooks let you inject custom logic (data transforms, raw SQL) before or after the auto-generated DDL without modifying the generated file.

### Backup

The `Backup()` method is auto-generated for migrations that contain destructive steps (`DropTable`, `DropColumn`). When `MigrationOptions.RunBackups = true`, the runtime executes backup SQL before applying the migration. `BackupGenerator` creates `SELECT INTO` / `CREATE TABLE AS SELECT` statements that preserve affected data in temporary backup tables. For `DropTable`, it copies the entire table. For `DropColumn`, it copies the column data keyed by the primary key.

### Snapshot

The snapshot class captures the entire schema as `MigrationBuilder` calls that reconstruct every table, column, FK, and index. The next `migrate add` compiles this snapshot to diff against. Because the snapshot is compilable C#, it participates in normal builds and can be inspected, diffed in source control, or manually edited.

### MigrationBuilder API

The builder supports these operations:

| Method | Description |
|---|---|
| `CreateTable(name, schema, configure)` | Create a new table with columns and constraints |
| `DropTable(name)` | Drop a table |
| `RenameTable(oldName, newName)` | Rename a table |
| `AddColumn(table, column, configure)` | Add a column to an existing table |
| `DropColumn(table, column)` | Drop a column |
| `RenameColumn(table, oldName, newName)` | Rename a column |
| `AlterColumn(table, column, configure)` | Change column type, nullability, or default |
| `AddForeignKey(name, table, column, ...)` | Add a foreign key constraint |
| `DropForeignKey(name, table)` | Drop a foreign key constraint |
| `AddIndex(name, table, columns, ...)` | Add an index (with optional `unique`, `filter`, `descending`) |
| `DropIndex(name, table)` | Drop an index |
| `InsertData(table, rows)` | Seed data |
| `UpdateData(table, set, where)` | Update seed data |
| `DeleteData(table, where)` | Delete seed data |
| `Sql(rawSql)` | Execute arbitrary SQL |
| `CreateView` / `DropView` / `AlterView` | View management |
| `CreateProcedure` / `DropProcedure` / `AlterProcedure` | Stored procedure management (not SQLite) |

Modifiers that can be chained after an operation:
- `.Online()` -- marks the operation for online execution (SQL Server)
- `.Batched(batchSize)` -- hints for large-table operations
- `.ConcurrentIndex()` -- `CREATE INDEX CONCURRENTLY` (PostgreSQL)
- `.SuppressTransaction()` -- runs the operation outside the migration transaction
- `.WithSourceTable(configure)` -- provides the full table definition for SQLite table rebuild (used with `DropColumn` and `AlterColumn`)

## Risk Classification

Every migration step is classified by risk level. The classification appears in the CLI output and in the generated migration comments.

### Safe (`[+]`)

Operations that add new structures without affecting existing data.

| Operation | Condition |
|---|---|
| `CreateTable` | Always safe |
| `AddColumn` | When the column is nullable or has a default value |
| `AddIndex` | Always safe |
| `AddForeignKey` | Always safe |

Examples:
```
[+] Create table 'orders'
[+] Add column 'email' to 'users' (nullable)
[+] Add index 'IX_Email' on 'users'
```

### Cautious (`[~]`)

Operations that modify existing structures. Data is preserved but application code may need updates.

| Operation | Condition |
|---|---|
| `AlterColumn` | Any type, nullability, or default change |
| `RenameTable` | Always cautious |
| `RenameColumn` | Always cautious |

Examples:
```
[~] Alter column 'email' on 'users' (change type from VARCHAR(100) to VARCHAR(255))
[~] Rename table 'users' to 'accounts'
[~] Rename column 'name' to 'user_name' on 'users'
```

### Destructive (`[!]`)

Operations that remove data or structures. These are irreversible without backups.

| Operation | Condition |
|---|---|
| `DropTable` | Always destructive |
| `DropColumn` | Always destructive |
| `DropIndex` | Always destructive |
| `DropForeignKey` | Always destructive |
| `AddColumn` | When the column is NOT NULL and has no default (existing rows would fail) |

Examples:
```
[!] Drop table 'legacy_logs'
[!] Drop column 'old_email' from 'users'
[!] Add column 'tenant_id' to 'users' (NOT NULL, no default — existing rows will fail)
```

When a migration contains destructive steps, the generated `Backup()` method includes backup logic, and the CLI output includes a warning. The QRY054 diagnostic fires at compile time if a destructive migration has no backup.

## Migration History Table

The runtime creates a `__quarry_migrations` table to track which migrations have been applied. This table is created automatically by `MigrationRunner` (or the generated `MigrateAsync` method) the first time migrations run.

### Schema

| Column | Type | Description |
|---|---|---|
| `version` | `INT PRIMARY KEY` | Migration version number |
| `name` | `VARCHAR(256)` | Migration name |
| `applied_at` | `TIMESTAMP` | When the migration was recorded |
| `checksum` | `VARCHAR(64)` | FNV-1a 64-bit hash of the generated SQL |
| `execution_time_ms` | `INT` | How long the migration took to apply |
| `applied_by` | `VARCHAR(256)` | `MachineName/UserName` of the executor |
| `started_at` | `TIMESTAMP` | When execution began |
| `status` | `VARCHAR(20)` | `running` or `applied` |
| `squash_from` | `INT` | If this is a squash baseline, the last version it covers |

### Status Tracking

Migrations are inserted with `status = 'running'` before DDL execution begins, then updated to `'applied'` after the transaction commits. If the process crashes mid-migration, the `running` row remains. On the next run, `MigrationRunner` detects incomplete migrations and throws an error unless `MigrationOptions.IgnoreIncomplete = true`.

### Checksum Verification

Each migration's SQL is hashed (FNV-1a 64-bit) when applied and stored in the `checksum` column. On subsequent runs, the runner recomputes the hash and compares. A mismatch means the migration code was modified after it was applied. By default this produces a warning; set `MigrationOptions.StrictChecksums = true` to throw an error instead.

## Applying at Runtime

The source generator emits a `MigrateAsync` method on each context:

```csharp
await using var db = new AppDb(connection);
await db.MigrateAsync(connection);  // apply all pending

// With options
await db.MigrateAsync(connection, new MigrationOptions
{
    Direction = MigrationDirection.Downgrade,
    TargetVersion = 1,
    DryRun = true,
    RunBackups = true,
    Logger = msg => Console.WriteLine(msg)
});
```

### MigrationOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TargetVersion` | `int?` | `null` | Target version (`null` = latest for upgrade, 0 for full downgrade) |
| `Direction` | `MigrationDirection` | `Upgrade` | `Upgrade` or `Downgrade` |
| `DryRun` | `bool` | `false` | Print SQL without executing |
| `Idempotent` | `bool` | `false` | Wrap DDL with `IF NOT EXISTS` / `IF EXISTS` guards |
| `RunBackups` | `bool` | `false` | Execute backup SQL before destructive operations |
| `CommandTimeout` | `TimeSpan?` | `null` | Per-command timeout (null = ADO.NET default 30s) |
| `LockTimeout` | `TimeSpan?` | `null` | Database lock acquisition timeout (dialect-specific `SET` command) |
| `WarnOnLargeTable` | `bool` | `false` | Query catalog for estimated row counts before DDL |
| `LargeTableThreshold` | `long` | `1,000,000` | Row count threshold for large-table warnings |
| `StrictChecksums` | `bool` | `false` | Throw on checksum mismatch (vs. warning) |
| `IgnoreIncomplete` | `bool` | `false` | Proceed past migrations stuck in `running` state |
| `Logger` | `Action<string>?` | `null` | Custom log function |
| `BeforeEach` | `Func<...>?` | `null` | Hook called before each migration (version, name, connection) |
| `AfterEach` | `Func<...>?` | `null` | Hook called after each successful migration (version, name, elapsed, connection) |
| `OnError` | `Func<...>?` | `null` | Hook called when a migration fails (version, name, exception, connection) |

### Transaction Handling

Each migration runs inside a database transaction. If an operation is marked with `.SuppressTransaction()` or `.ConcurrentIndex()` (PostgreSQL), those operations run in a separate non-transactional phase. During upgrade, the transactional phase runs first, followed by non-transactional operations. During downgrade, the order is reversed.

If the transactional phase fails, it is rolled back and the history row is cleaned up. If the non-transactional phase fails, the transactional changes have already been committed -- the error message states this explicitly.

## SQLite-Specific Handling

SQLite has limited `ALTER TABLE` support. The following operations require a **table rebuild** -- a multi-step process where the table is recreated with the new structure:

| Operation | SQLite Behavior |
|---|---|
| `AlterColumn` | Table rebuild required (SQLite does not support `ALTER COLUMN`) |
| `DropColumn` | Table rebuild or `ALTER TABLE ... DROP COLUMN` (SQLite 3.35+) |
| `DropForeignKey` | Table rebuild required (SQLite does not support `DROP CONSTRAINT`) |
| `AddForeignKey` (existing table) | Table rebuild required (SQLite does not support `ADD CONSTRAINT`) |

### Table Rebuild Pattern

When the migration tool detects one of these operations targeting SQLite, and the operation includes a `.WithSourceTable()` definition, the `DdlRenderer` emits a 5-step rebuild:

```sql
-- 1. Rename original to temp
ALTER TABLE "users" RENAME TO "_quarry_tmp_users";

-- 2. Create new table with the updated schema
CREATE TABLE "users" ( ... );

-- 3. Copy data from temp to new table
INSERT INTO "users" ("user_id", "user_name", "email")
    SELECT "user_id", "user_name", "email"
    FROM "_quarry_tmp_users";

-- 4. Drop temp table
DROP TABLE "_quarry_tmp_users";
```

When no `.WithSourceTable()` is provided, the renderer emits a comment indicating that a manual table rebuild is needed.

The `MigrationNotificationAnalyzer` checks each migration for SQLite-specific concerns and prints warnings during scaffolding, such as:
- "Altering column 'email' on 'users' requires a full table rebuild."
- "Dropping foreign key from 'orders' requires a full table rebuild."

Other SQLite limitations handled by the renderer:
- Foreign keys are folded into `CREATE TABLE` statements instead of emitting separate `ALTER TABLE ADD CONSTRAINT`
- `CREATE OR REPLACE VIEW` is not supported; the renderer emits `DROP VIEW IF EXISTS` followed by `CREATE VIEW`
- Stored procedures are not supported (throws `NotSupportedException`)

## Script and Bundle Commands

### `migrate script`

Generates incremental migration SQL for a version range. This is useful for generating deployment scripts without connecting to a database.

```sh
quarry migrate script                                    # all migrations, auto-detect dialect
quarry migrate script -d postgresql -o deploy/migrate.sql  # specific dialect, write to file
quarry migrate script --from 3 --to 7                    # specific version range
```

| Flag | Long | Default | Description |
|------|------|---------|-------------|
| `-p` | `--project` | `.` | Path to project |
| `-d` | `--dialect` | *(auto)* | SQL dialect |
| `-o` | `--output` | *(stdout)* | Write SQL to file |
| | `--from` | *(first)* | Start version (inclusive) |
| | `--to` | *(latest)* | End version (inclusive) |

The output includes a header with the generation timestamp and version range. Each migration is preceded by a comment block with the version number and name. The tool compiles each migration's `Upgrade()` method and renders dialect-specific DDL via `DdlRenderer`.

### `migrate bundle`

Creates a self-contained migration executable for deployment. This is useful for environments where the .NET SDK is not available or when you want a single artifact that encapsulates all migrations.

```sh
quarry migrate bundle
quarry migrate bundle -o dist/migrator -d postgresql --self-contained -r linux-x64
```

| Flag | Long | Default | Description |
|------|------|---------|-------------|
| `-p` | `--project` | `.` | Path to project |
| `-o` | `--output` | `quarry-migrate` | Output directory for the bundle |
| `-d` | `--dialect` | *(auto)* | Default dialect to embed |
| | `--self-contained` | `false` | Publish as self-contained (no .NET runtime required on target) |
| `-r` | `--runtime` | *(none)* | Target runtime identifier (e.g., `linux-x64`, `win-x64`) |

The tool discovers all migration classes, creates a temporary project with an auto-generated entry point and the appropriate database provider packages, then runs `dotnet publish` in Release mode.

The generated bundle accepts these flags at runtime:

```sh
./quarry-migrate -c "Host=localhost;Database=myapp" -d postgresql
./quarry-migrate -c "..." --target 5                # migrate to specific version
./quarry-migrate -c "..." --direction down           # rollback
./quarry-migrate -c "..." --dry-run                  # print SQL without executing
./quarry-migrate -c "..." --idempotent               # IF NOT EXISTS guards
```

The connection string can also be set via the `QUARRY_CONNECTION` environment variable.

### `migrate squash`

Collapses all existing migrations into a single baseline migration. Requires at least 2 existing migrations. The squashed baseline uses the latest snapshot to generate a single version-1 migration containing all current tables, then removes the old migration files.

```sh
quarry migrate squash
quarry migrate squash --ni    # skip confirmation prompt
```

The squash baseline includes a `SquashedFrom` marker so that the runtime can detect when a database already has migrations from the squashed range and skip the baseline.

### `migrate status`

Compares applied migrations (in the database) vs. pending migrations (in code). Requires a database connection.

```sh
quarry migrate status -c "Host=localhost;Database=myapp"
```

```
0001  InitialCreate   [applied 2026-03-01]
0002  AddUserEmail    [applied 2026-03-10]
0003  AddCreatedAt    [pending]
```

Warns if migrations exist in the database that are missing from code.

## Migration Diagnostics (QRY050--055)

The Roslyn source generator (Pipeline 3) discovers `[Migration]` and `[MigrationSnapshot]` attributes in your code and emits compile-time diagnostics for common migration issues.

| Code | Severity | Description |
|------|----------|-------------|
| QRY050 | Warning | **Schema drift.** The current schema does not match the latest snapshot. Run `quarry migrate add` to generate a migration. |
| QRY051 | Warning | **Unknown table/column reference.** A migration references a table or column that does not exist in the current schema. |
| QRY052 | Error | **Version gap or duplicate.** Migration versions are non-sequential or two migrations share the same version number. |
| QRY053 | Warning | **Pending migrations.** Migrations exist in code that have not been applied. Informational -- typically paired with `migrate status`. |
| QRY054 | Warning | **Destructive without backup.** A migration contains destructive steps (`[!]` classification) but the `Backup()` method is empty. Consider adding backup logic or enabling `RunBackups`. |
| QRY055 | Warning | **Nullable-to-non-null.** A column changed from nullable to non-null. Existing rows with `NULL` values will cause the migration to fail unless you add a `builder.Sql()` call to handle them first. |

These diagnostics appear in the IDE error list and CI build output alongside standard compiler errors and warnings.
