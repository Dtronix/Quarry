# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry Migration Tool

Code-first migration scaffolding for Quarry. Reads your C# schema definitions via Roslyn, diffs them against the previous snapshot, and generates migration files — no database connection required.

## Packages

| Name | NuGet | Description |
|------|-------|-------------|
| [`Quarry`](https://www.nuget.org/packages/Quarry) | [![Quarry](https://img.shields.io/nuget/v/Quarry.svg?maxAge=60)](https://www.nuget.org/packages/Quarry) | Runtime types: builders, schema DSL, dialects, executors. |
| [`Quarry.Generator`](https://www.nuget.org/packages/Quarry.Generator) | [![Quarry.Generator](https://img.shields.io/nuget/v/Quarry.Generator.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Generator) | Roslyn incremental source generator + interceptor emitter. |
| [`Quarry.Analyzers`](https://www.nuget.org/packages/Quarry.Analyzers) | [![Quarry.Analyzers](https://img.shields.io/nuget/v/Quarry.Analyzers.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers) | Compile-time SQL query analysis rules (QRA series) with code fixes. |
| [`Quarry.Analyzers.CodeFixes`](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | [![Quarry.Analyzers.CodeFixes](https://img.shields.io/nuget/v/Quarry.Analyzers.CodeFixes.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Analyzers.CodeFixes) | Code fix providers for QRA diagnostics. |
| [`Quarry.Tool`](https://www.nuget.org/packages/Quarry.Tool) | [![Quarry.Tool](https://img.shields.io/nuget/v/Quarry.Tool.svg?maxAge=60)](https://www.nuget.org/packages/Quarry.Tool) | CLI tool for migrations and database scaffolding (`quarry` command). |

---

## Overview

The `quarry` CLI analyzes your project at the source level using the Roslyn compiler APIs. It discovers schema classes (`Schema`), extracts table/column metadata from properties like `Col<T>`, `Key<T>`, `Ref<TEntity,TKey>`, and `Index`, then compares the current schema against the last known snapshot to produce a set of migration steps.

Every migration generates two files:
- A **migration class** with `Upgrade()`, `Downgrade()`, and `Backup()` methods
- A **snapshot class** that captures the full schema state at that point in time

The snapshot is stored as compilable C# — not JSON, not binary. When generating the next migration, the tool compiles the previous snapshot in memory, invokes its `Build()` method, and diffs the result against your current code.

## Installation

```sh
# Install as a global .NET tool
dotnet tool install --global Quarry.Tool

# Or as a local tool
dotnet new tool-manifest
dotnet tool install Quarry.Tool
```

The tool is packaged via `<PackAsTool>true</PackAsTool>` with command name `quarry`.

### Requirements

- .NET 10 SDK (the tool targets `net10.0`)
- A project that references `Quarry` and defines schemas inheriting from `Schema`
- A `[QuarryContext]` attribute on your context class (used for dialect auto-detection)

## Quick Start

### 1. Define your schema

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);

    public Index IX_Email => Index(Email);
}
```

### 2. Define your context

```csharp
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class AppDb : QuarryContext
{
    public partial QueryBuilder<User> Users { get; }
}
```

### 3. Scaffold the initial migration

```sh
quarry migrate add InitialCreate
```

Output:
```
Loading project: /path/to/MyApp.csproj
Created: Migrations/Snapshot_001_InitialCreate.cs
Created: Migrations/Migration_001_InitialCreate.cs

Migration 1: InitialCreate
  [+] Create table 'users'
  [+] Add index 'IX_Email' on 'users'
```

### 4. Modify the schema and scaffold again

Add a column and an index to `UserSchema`:

```csharp
public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
public Index IX_Created => Index(CreatedAt.Desc());
```

```sh
quarry migrate add AddCreatedAt
```

Output:
```
Migration 2: AddCreatedAt
  [+] Add column 'created_at' to 'users'
  [+] Add index 'IX_Created' on 'users'
```

### 5. Check migration integrity

```sh
quarry migrate validate
```

```
Validation passed.
```

## Commands

### `quarry migrate add <name>`

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
| | `--ni` | `false` | Non-interactive mode — auto-accepts renames with score >= 0.8, skips prompts |

**What it does:**
1. Opens your project via MSBuild/Roslyn and compiles it
2. Finds the latest `[MigrationSnapshot]` version in your code
3. Extracts the current schema from all `Schema` subclasses
4. If a previous snapshot exists, compiles it in memory and invokes `Build()` to reconstruct it
5. Diffs old vs. new schema (tables, columns, foreign keys, indexes)
6. Prompts for rename confirmation if applicable (see [Rename Detection](#rename-detection))
7. Generates `Snapshot_NNN_Name.cs` and `Migration_NNN_Name.cs` in the output directory
8. Prints a summary with risk-classified steps

If no changes are detected, prints `No schema changes detected.` and generates nothing.

---

### `quarry migrate add-empty <name>`

Creates a migration with empty `Upgrade()`/`Downgrade()` methods.

```sh
quarry migrate add-empty SeedDefaultRoles
```

Use this for:
- Data migrations (inserting/updating rows via `builder.Sql(...)`)
- Business logic changes that need a version marker
- Manual DDL that the diff engine can't express

**Note:** This does *not* generate a snapshot file — only the migration class. The next `migrate add` will still compare against the last snapshot.

---

### `quarry migrate list`

Lists all migrations discovered in the project.

```sh
quarry migrate list
quarry migrate list -p src/MyApp
```

Output:
```
Migrations:
  001  InitialCreate
  002  AddUserEmail
  003  SeedDefaultRoles
```

Discovers migrations by scanning for classes with `[Migration(Version = N, Name = "...")]` attributes.

---

### `quarry migrate validate`

Checks the migration chain for integrity issues.

```sh
quarry migrate validate
```

**Checks:**
- **Duplicate versions** — two migrations with the same `Version` (ERROR)
- **Version gaps** — non-sequential version numbers, e.g., 1, 2, 5 (WARNING)

```
ERROR: Duplicate migration version 3
WARNING: Version gap between 2 and 5
Validation completed with 1 error(s).
```

---

### `quarry migrate remove`

Removes the latest migration's files from disk.

```sh
quarry migrate remove
quarry migrate remove -p src/MyApp
```

Deletes all `.cs` files matching `Migration_NNN_*` and `Snapshot_NNN_*` for the highest version number. This is a file-system operation — it does not modify the database.

---

### `quarry create-scripts`

Generates full `CREATE TABLE` DDL from the current schema (no diffing, no versioning).

```sh
quarry create-scripts                          # auto-detect dialect, print to stdout
quarry create-scripts -d postgresql            # explicit dialect
quarry create-scripts -o schema.sql            # write to file
quarry create-scripts -d sqlite -o init.sql    # both
```

| Flag | Long | Default | Description |
|------|------|---------|-------------|
| `-p` | `--project` | `.` | Path to project |
| `-d` | `--dialect` | *(auto)* | SQL dialect override |
| `-o` | `--output` | *(stdout)* | Output file path |

**Dialect values:** `sqlite`, `postgresql` (or `postgres`, `pg`), `mysql`, `sqlserver` (or `mssql`).

If `-d` is not provided, the tool scans for `[QuarryContext(Dialect = ...)]` in your project. Fails with an error if no dialect can be determined.

## How It Works

### The Pipeline

```
Your C# Project (.csproj)
        |
        v
  [MSBuild + Roslyn]  ──  Open project, compile to semantic model
        |
        v
  [Schema Extraction]  ──  Find Schema subclasses, parse Col/Key/Ref/Index properties
        |
        v
  [Current SchemaSnapshot]
        |                              [Previous Snapshot .cs file]
        |                                        |
        |                                        v
        |                              [Snapshot Compiler]  ──  Compile in memory,
        |                                        |              invoke Build()
        |                                        v
        |                              [Previous SchemaSnapshot]
        |                                        |
        +────────────────┬───────────────────────+
                         |
                         v
                  [SchemaDiffer]  ──  Compare tables, columns, FKs, indexes
                         |
                         v
                  [MigrationStep[]]  ──  Classified as Safe / Cautious / Destructive
                         |
              ┌──────────┴──────────┐
              v                     v
  [MigrationCodeGenerator]  [SnapshotCodeGenerator]
              |                     |
              v                     v
  Migration_NNN_Name.cs    Snapshot_NNN_Name.cs
```

### Schema Extraction

The tool opens your `.csproj` via `MSBuildWorkspace`, compiles it, then walks all syntax trees looking for classes that inherit from `Schema`. For each schema class it extracts:

- **Table name** from the `Table` static property
- **Naming convention** from the `Naming` property (`SnakeCase`, `CamelCase`, `LowerCase`, or `Exact`)
- **Columns** from properties typed as `Col<T>`, `Key<T>`, or `Ref<TEntity, TKey>`
  - CLR type and nullable annotations
  - Column kind (Standard, PrimaryKey, ForeignKey)
  - Modifiers: `Identity()`, `Length()`, `Precision()`, `Default()`, `Computed()`, `MapTo()`, `ClientGenerated()`
- **Indexes** from properties typed as `Index`
  - Column names (with naming convention applied)
  - Fluent modifiers: `Unique()`, `Where()` (bool column or raw SQL filter), `Using()` (index type)
  - Sort directions via `.Asc()` / `.Desc()` on column arguments

`Many<T>` navigation properties are not extracted as columns (they represent relationships, not physical columns). `Include()` columns (covering indexes) are parsed at the generator level but not tracked in migration snapshots.

### Snapshot Compilation

Each snapshot is a standalone C# class that can reconstruct a `SchemaSnapshot` object:

```csharp
using Quarry;
using Quarry.Migration;

[MigrationSnapshot(Version = 1, Name = "InitialCreate", Timestamp = "2026-02-27T...",
    ParentVersion = 0, SchemaHash = "a1b2c3d4e5f6g7h8")]
public static partial class Snapshot_001_InitialCreate
{
    public static SchemaSnapshot Build()
    {
        return new SchemaSnapshotBuilder()
            .SetVersion(1)
            .SetName("InitialCreate")
            .SetTimestamp(DateTimeOffset.Parse("2026-02-27T..."))
            .AddTable(t => t
                .Name("users")
                .NamingStyle(NamingStyleKind.SnakeCase)
                .AddColumn(c => c.Name("user_id").ClrType("int").PrimaryKey().Identity())
                .AddColumn(c => c.Name("user_name").ClrType("string").Length(100))
                .AddColumn(c => c.Name("email").ClrType("string").Nullable())
            )
            .Build();
    }
}
```

When the tool needs the previous schema, it:
1. Finds the snapshot class by `[MigrationSnapshot(Version = N)]` attribute
2. Creates an in-memory Roslyn compilation with the snapshot source
3. Emits to a `MemoryStream` and loads via a collectible `AssemblyLoadContext`
4. Invokes the static `Build()` method via reflection
5. Unloads the assembly context to free resources

This means snapshots are fully self-contained — no separate data files, no database state.

### Schema Hashing

Each snapshot includes a `SchemaHash` attribute — an FNV-1a hash of sorted table names, column names, types, and kinds. This provides a lightweight fingerprint for drift detection without needing to compile and compare full snapshots.

## Generated Files

### Migration File

`Migration_001_InitialCreate.cs`:

```csharp
using Quarry;
using Quarry.Migration;

namespace MyApp.Migrations;

[Migration(Version = 1, Name = "InitialCreate")]
public static partial class Migration_001_InitialCreate
{
    public static void Upgrade(MigrationBuilder builder)
    {
        builder.CreateTable("users", null, t =>
        {
            t.Column("user_id", c => c.ClrType("int").NotNull().Identity());
            t.Column("user_name", c => c.ClrType("string").NotNull().Length(100));
            t.Column("email", c => c.ClrType("string").Nullable());
            t.PrimaryKey("PK_users", new[] { "user_id" });
        });
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        builder.DropTable("users");
    }

    public static void Backup(MigrationBuilder builder)
    {
        // Backup SQL is generated at apply-time by BackupGenerator
    }
}
```

### Snapshot File

See the [Snapshot Compilation](#snapshot-compilation) section above for the full structure.

### File Naming

| Pattern | Example |
|---------|---------|
| `Migration_{NNN}_{SanitizedName}.cs` | `Migration_001_InitialCreate.cs` |
| `Snapshot_{NNN}_{SanitizedName}.cs` | `Snapshot_001_InitialCreate.cs` |

- Version is zero-padded to 3 digits
- Name is sanitized: only alphanumerics and underscores are kept

### Namespace

Inferred from `{ProjectName}.{OutputDir}`:
- Project: `MyApp.csproj`, Output: `Migrations` -> `MyApp.Migrations`
- Project: `MyApp.csproj`, Output: `Data/Migrations` -> `MyApp.Data.Migrations`

## Rename Detection

When exactly one table is added and one is dropped (or one column added and one dropped within a table), the tool attempts to detect whether this is a rename rather than a drop+add.

### Scoring

**Table rename score:**
| Factor | Weight |
|--------|--------|
| Column count matches | +0.3 |
| Single add/drop pair | +0.2 |
| Name similarity (Levenshtein) | +0.3 * similarity |

**Column rename score:**
| Factor | Weight |
|--------|--------|
| CLR type matches | +0.3 |
| Single add/drop pair | +0.2 |
| Name similarity (Levenshtein) | +0.3 * similarity |
| MapTo change detected | +0.2 |

A candidate is returned only if the score reaches **0.6 or higher**.

### Interactive Mode (default)

When running interactively, the tool prompts:

```
Detected possible rename: 'user_name' -> 'username' (confidence: 0.75)
  Is this a rename? [Y/n]:
```

If you reject the rename, it generates separate `DropColumn` + `AddColumn` steps instead.

### Non-Interactive Mode (`--ni`)

Renames with score **>= 0.8** are automatically accepted. Below 0.8, they are treated as separate drop+add operations.

This threshold is deliberately strict to avoid false positives in CI.

### When Detection Doesn't Apply

- Multiple tables or columns added/dropped simultaneously — no rename detection
- The tool only detects single renames per scope (one table rename per migration, one column rename per table)

## Risk Classification

Every migration step is classified by risk level:

| Icon | Level | Operations |
|------|-------|------------|
| `[+]` | **Safe** | `CreateTable`, `AddIndex`, `AddForeignKey`, `AddColumn` (if nullable or has default) |
| `[~]` | **Cautious** | `AlterColumn`, `RenameTable`, `RenameColumn` |
| `[!]` | **Destructive** | `DropTable`, `DropColumn`, `DropIndex`, `DropForeignKey`, `AddColumn` (if NOT NULL without default) |

Adding a non-nullable column without a default is classified as **destructive** because it will fail on tables with existing data.

### Warnings in Generated Code

The migration code generator inserts warning comments for risky operations:

```csharp
// WARNING: Column 'status' changed from nullable to non-nullable.
// Existing NULL values will cause this migration to fail.
// Consider adding a default value or running: builder.Sql("UPDATE users SET status = 'active' WHERE status IS NULL");
builder.AlterColumn("users", "status", c => c.ClrType("string").NotNull());
```

```csharp
// WARNING: Column 'age' type changed from 'string' to 'int'.
// This may cause data loss. Consider a data migration step.
builder.AlterColumn("users", "age", c => c.ClrType("int").NotNull());
```

## Dialect Notifications

After generating a migration, the tool analyzes the steps for dialect-specific limitations and prints warnings to stderr. These are driven by a rule-based `MigrationNotificationAnalyzer` — adding a new notification requires only adding a line to the rule table.

### SQLite Notifications

| Operation | Level | Reason |
|-----------|-------|--------|
| `AlterColumn` | WARNING | Requires a full table rebuild (rename → recreate → copy → drop) |
| `DropColumn` | WARNING | Requires a full table rebuild |
| `AddForeignKey` (existing table) | WARNING | SQLite only supports FK constraints inline in `CREATE TABLE` |
| `DropForeignKey` | WARNING | Requires a full table rebuild |
| `DropIndex` + `AddIndex` (same table) | NOTE | Index will be dropped and recreated |

`AddForeignKey` on a table created in the **same** migration is handled automatically — the FK is folded into the `CREATE TABLE` statement at runtime. No warning is emitted in this case.

Example output:

```
Migration 2: AddUserEmail
  [+] Add column 'Email' to 'users'
  [~] Alter column 'Name' on 'users'

  WARNING: Altering column 'Name' on 'users' requires a full table rebuild.
```

When the dialect is unknown (no `[QuarryContext]` attribute found), SQLite warnings are shown with a `SQLite: ` prefix since it has the most constraints.

## Backup Generation

For destructive steps (`DropTable`, `DropColumn`), the `BackupGenerator` can produce SQL to preserve data before the operation:

| Step | Backup SQL |
|------|-----------|
| `DropTable` | `CREATE TABLE __quarry_backup_users AS SELECT * FROM users;` |
| `DropColumn` | `CREATE TABLE __quarry_backup_users_email AS SELECT pk, email FROM users;` |

Restore SQL re-inserts from the backup table and drops it. SQL Server uses `SELECT INTO` syntax; all other dialects use `CREATE TABLE ... AS SELECT`.

Backup SQL is generated at migration apply-time, not scaffolding-time.

## Customizing Migrations

Generated migrations are regular C# code. You can edit them freely after generation.

### Adding Raw SQL

Use `builder.Sql()` for operations the diff engine doesn't handle:

```csharp
public static void Upgrade(MigrationBuilder builder)
{
    // Auto-generated steps
    builder.AddColumn("users", "status", c => c.ClrType("string").NotNull().Default("'active'"));

    // Custom data migration
    builder.Sql("UPDATE users SET status = 'legacy' WHERE created_at < '2025-01-01'");
}
```

### MigrationBuilder API

| Method | Description |
|--------|-------------|
| `CreateTable(name, schema, configure)` | Create a new table |
| `DropTable(name, schema?)` | Drop a table |
| `RenameTable(oldName, newName, schema?)` | Rename a table |
| `AddColumn(table, column, configure)` | Add a column |
| `DropColumn(table, column)` | Drop a column |
| `RenameColumn(table, oldName, newName)` | Rename a column |
| `AlterColumn(table, column, configure)` | Modify a column |
| `AddForeignKey(name, table, col, refTable, refCol, onDelete?, onUpdate?)` | Add a FK constraint |
| `DropForeignKey(name, table)` | Drop a FK constraint |
| `AddIndex(name, table, columns, unique?, filter?)` | Add an index |
| `DropIndex(name, table)` | Drop an index |
| `Sql(rawSql)` | Execute arbitrary SQL |
| `Online()` | Mark the previous operation as online (no lock) |
| `Batched(batchSize)` | Mark the previous operation as batched |
| `ConcurrentIndex()` | Mark the previous index operation as concurrent |
| `BuildSql(dialect)` | Render all operations to SQL string |

### Downgrade Method

`Downgrade()` is the auto-generated reverse of `Upgrade()`. Some reversals have limitations:

- `DropTable` reversal emits a `// TODO` comment because the full table structure isn't known from the drop step alone
- `DropColumn` reversal restores the column definition from the old snapshot
- `AlterColumn` reversal restores the original type and nullability

Always review the generated `Downgrade()` method — it's a best-effort reverse, not a guarantee.

## CI/CD Integration

### Non-Interactive Mode

Always use `--ni` in automated environments:

```sh
quarry migrate add AutoMigration --ni
```

The tool also auto-detects redirected stdin (pipes) and disables prompts, but `--ni` makes the intent explicit.

### Validation in Pipelines

Add a validation step to catch migration issues early:

```yaml
# Example CI step
- name: Validate migrations
  run: quarry migrate validate -p src/MyApp
```

### Generating Scripts for Deployment

Use `create-scripts` to produce deployment-ready DDL:

```sh
quarry create-scripts -d postgresql -o deploy/schema.sql
```

## Project Structure

```
src/Quarry.Tool/
  Program.cs                    # CLI entry point, command dispatch, option parsing
  Quarry.Tool.csproj            # PackAsTool, Roslyn/MSBuild dependencies
  Commands/
    MigrateCommands.cs          # All command implementations
  Interactive/
    InteractivePrompt.cs        # Y/n confirmation, menu selection, stdin detection
  Schema/
    ProjectSchemaReader.cs      # Roslyn-based schema extraction from .csproj
    SnapshotCompiler.cs         # In-memory compilation + reflection invocation
    DialectResolver.cs          # Extracts dialect from [QuarryContext] attribute

src/Quarry.Shared/Migration/    # Shared code (linked into Tool project)
  Models/
    SchemaSnapshot.cs           # Immutable point-in-time schema capture
    TableDef.cs                 # Table definition (columns, FKs, indexes)
    ColumnDef.cs                # Column definition (~13 properties)
    ForeignKeyDef.cs            # FK constraint definition
    IndexDef.cs                 # Index definition
    MigrationStep.cs            # Diff result with classification
    MigrationStepType.cs        # 11 operation types
    StepClassification.cs       # Safe / Cautious / Destructive
    ColumnKind.cs               # Standard / PrimaryKey / ForeignKey
    ForeignKeyAction.cs         # NoAction / Cascade / SetNull / SetDefault / Restrict
    NamingStyleKind.cs          # Exact / SnakeCase / CamelCase / LowerCase
  Diff/
    SchemaDiffer.cs             # Core diff algorithm
    RenameMatcher.cs            # Levenshtein-based rename detection + scoring
    LevenshteinDistance.cs       # Edit distance with two-row optimization
  CodeGen/
    MigrationCodeGenerator.cs   # Generates Migration_NNN classes
    SnapshotCodeGenerator.cs    # Generates Snapshot_NNN classes
  MigrationNotificationAnalyzer.cs # Rule-based dialect notification system
  BackupGenerator.cs            # Backup/restore SQL for destructive ops
  SchemaHasher.cs               # FNV-1a hash for drift detection
  NamingConventions.cs          # Property name -> column name conversion
  Builders/
    SchemaSnapshotBuilder.cs    # Fluent builder for SchemaSnapshot
    TableDefBuilder.cs          # Fluent builder for TableDef
    ColumnDefBuilder.cs         # Fluent builder for ColumnDef

src/Quarry/Migration/           # Runtime types (referenced by generated code)
  MigrationBuilder.cs           # Fluent DDL operation builder
  MigrationAttribute.cs         # [Migration(Version, Name)]
  MigrationSnapshotAttribute.cs # [MigrationSnapshot(Version, Name, Timestamp, ...)]
  DdlRenderer.cs                # Renders operations to dialect-specific SQL
  TableBuilder.cs               # Table definition within CreateTable
  ColumnBuilder.cs              # Column configuration within operations
  SchemaSnapshot.cs             # Public snapshot type for generated code
  SchemaSnapshotBuilder.cs      # Public builder for generated snapshot code
  TableDef.cs                   # Public table definition for generated code
  TableDefBuilder.cs            # Public builder for generated snapshot code
  ColumnDef.cs                  # Public column definition for generated code
  ColumnDefBuilder.cs           # Public builder for generated snapshot code
  ForeignKeyDef.cs              # Public FK definition for generated code
  IndexDef.cs                   # Public index definition for generated code
  ColumnKind.cs                 # Public enum for generated code
  NamingStyleKind.cs            # Public enum for generated code
```

## Limitations

- **Single rename detection only.** If two tables are added and two dropped in the same migration, no rename matching occurs. Split into separate migrations if needed.
- **No composite primary key support** for backup generation. Tables with composite PKs won't get automated column-level backup SQL.
- **No data migration intelligence.** The diff engine handles structural changes only. Use `add-empty` and `builder.Sql()` for data operations.
- **Downgrade is best-effort.** `DropTable` reversals can't reconstruct the full table definition. Always review generated downgrade code.
- **Foreign key references use entity names as placeholders.** During schema extraction, the referenced column defaults to `"id"`. Actual table resolution happens at apply-time.
- **SQLite FK constraints.** `AddForeignKey` on a table created in the same migration is folded inline automatically. Adding a FK to an existing table requires a manual table rebuild — the tool emits a warning when this occurs.
- **SQLite ALTER TABLE limitations.** `AlterColumn`, `DropColumn`, and `DropForeignKey` all require the table rebuild pattern on SQLite. The tool emits warnings for these operations (see [Dialect Notifications](#dialect-notifications)).
- **Schema extraction is syntax-based.** Complex patterns like dynamically-computed table names or schemas defined via inheritance chains may not be extracted correctly.

## Troubleshooting

**"No .csproj found"** — Point `-p` to a directory with exactly one `.csproj`, or to the `.csproj` file directly.

**"Multiple .csproj files found"** — Specify the exact `.csproj` path with `-p src/MyApp/MyApp.csproj`.

**"Failed to load project compilation"** — Ensure the project builds successfully with `dotnet build` first. The tool uses the same MSBuild infrastructure.

**"Could not determine SQL dialect"** — Add `-d postgresql` (or your dialect) explicitly, or ensure your context has `[QuarryContext(Dialect = SqlDialect.PostgreSQL)]`.

**"No schema changes detected"** — The diff found no differences between the current schema and the last snapshot. Verify your schema changes are in files included in the project compilation.

**Snapshot compilation fails silently** — Check stderr output. Common causes: missing assembly references, syntax errors in snapshot files, or `Build()` method signature changes.
