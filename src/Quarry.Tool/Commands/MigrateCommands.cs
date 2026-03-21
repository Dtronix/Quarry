using System.Data.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Shared.Migration;
using Quarry.Shared.Sql;
using Quarry.Tool.Interactive;
using Quarry.Tool.Schema;

namespace Quarry.Tool.Commands;

internal static class MigrateCommands
{
    /// <summary>
    /// quarry migrate add — Scaffolds a new migration from schema changes.
    /// </summary>
    /// <param name="name">Migration name</param>
    /// <param name="project">-p, Path to .csproj</param>
    /// <param name="output">-o, Output directory for migration files</param>
    /// <param name="nonInteractive">--ni, Non-interactive mode for CI</param>
    public static async Task MigrateAdd(string name, string project = ".", string output = "Migrations", bool nonInteractive = false)
    {
        var csprojPath = ResolveCsproj(project);
        Console.WriteLine($"Loading project: {csprojPath}");

        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        // Find latest snapshot version, using a lock file to prevent concurrent version conflicts
        var baseOutputDir = Path.Combine(Path.GetDirectoryName(csprojPath)!, output);
        Directory.CreateDirectory(baseOutputDir);
        var lockFilePath = Path.Combine(baseOutputDir, ".quarry-migrate.lock");
        using (var lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var latestVersion = FindLatestSnapshotVersion(compilation);
            var newVersion = latestVersion + 1;

            // Build current schema
            var currentSnapshot = ProjectSchemaReader.ExtractSchemaSnapshot(compilation, newVersion, name, latestVersion > 0 ? latestVersion : null);

            // Build previous snapshot (if any)
            SchemaSnapshot? previousSnapshot = null;
            if (latestVersion > 0)
            {
                // Try to find and invoke the previous snapshot's Build() method
                previousSnapshot = FindAndBuildSnapshot(compilation, latestVersion);
            }

            // Diff
            Func<RenameMatcher.RenameCandidate, bool>? acceptRename = null;
            if (InteractivePrompt.IsInteractive(nonInteractive))
            {
                acceptRename = candidate =>
                {
                    return InteractivePrompt.Confirm(
                        $"Detected possible rename: '{candidate.OldName}' → '{candidate.NewName}' (confidence: {candidate.Score:F2})\n  Is this a rename?");
                };
            }

            var steps = SchemaDiffer.Diff(previousSnapshot, currentSnapshot, acceptRename);

            if (steps.Count == 0)
            {
                Console.WriteLine("No schema changes detected.");
                return;
            }

            // Generate files
            var names = ComputeMigrationNames(newVersion, name);
            var migrationDir = Path.Combine(baseOutputDir, names.SubdirName);
            Directory.CreateDirectory(migrationDir);

            var ns = GuessNamespace(csprojPath, output);

            // Generated file: migration class with snapshot (always overwrite)
            var combinedCode = GenerateCombinedMigrationFile(
                newVersion, name, steps, previousSnapshot, currentSnapshot, ns);
            await File.WriteAllTextAsync(Path.Combine(migrationDir, names.GeneratedFileName), combinedCode);
            Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.GeneratedFileName)}");

            // User partial file (only create if it doesn't exist)
            var userFilePath = Path.Combine(migrationDir, names.UserFileName);
            if (!File.Exists(userFilePath))
            {
                var userCode = GenerateUserPartialFile(newVersion, name, ns);
                await File.WriteAllTextAsync(userFilePath, userCode);
                Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.UserFileName)}");
            }

            // Summary
            Console.WriteLine();
            Console.WriteLine($"Migration {newVersion}: {name}");
            foreach (var step in steps)
            {
                var icon = step.Classification switch
                {
                    StepClassification.Safe => "+",
                    StepClassification.Cautious => "~",
                    StepClassification.Destructive => "!",
                    _ => " "
                };
                Console.WriteLine($"  [{icon}] {step.Description}");
            }

            // Dialect-specific notifications
            var resolvedDialect = DialectResolver.ResolveDialect(compilation, null);
            var notifications = MigrationNotificationAnalyzer.Analyze(steps, resolvedDialect);
            if (notifications.Count > 0)
            {
                Console.WriteLine();
                foreach (var n in notifications)
                {
                    var prefix = n.Level == NotificationLevel.Warning ? "WARNING" : "NOTE";
                    Console.Error.WriteLine($"  {prefix}: {n.Message}");
                }
            }
        } // lock file released here
    }

    /// <summary>
    /// quarry migrate add-empty — Creates an empty migration for manual data operations.
    /// </summary>
    public static async Task MigrateAddEmpty(string name, string project = ".", string output = "Migrations")
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var latestVersion = FindLatestSnapshotVersion(compilation);
        var newVersion = latestVersion + 1;
        var baseOutputDir = Path.Combine(Path.GetDirectoryName(csprojPath)!, output);
        var names = ComputeMigrationNames(newVersion, name);
        var migrationDir = Path.Combine(baseOutputDir, names.SubdirName);
        Directory.CreateDirectory(migrationDir);
        var ns = GuessNamespace(csprojPath, output);

        // Empty migration (always overwrite — no snapshot for empty migrations)
        var migrationCode = MigrationCodeGenerator.GenerateMigrationClass(
            newVersion, name, Array.Empty<MigrationStep>(), null,
            new SchemaSnapshot(newVersion, name, DateTimeOffset.UtcNow, latestVersion > 0 ? latestVersion : null, Array.Empty<TableDef>()),
            ns);
        await File.WriteAllTextAsync(Path.Combine(migrationDir, names.GeneratedFileName), migrationCode);
        Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.GeneratedFileName)}");

        // User partial file (only create if it doesn't exist)
        var userFilePath = Path.Combine(migrationDir, names.UserFileName);
        if (!File.Exists(userFilePath))
        {
            var userCode = GenerateUserPartialFile(newVersion, name, ns);
            await File.WriteAllTextAsync(userFilePath, userCode);
            Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.UserFileName)}");
        }
    }

    /// <summary>
    /// quarry migrate list — Lists all migrations and their status.
    /// </summary>
    public static async Task MigrateList(string project = ".")
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var migrations = FindMigrations(compilation);
        if (migrations.Count == 0)
        {
            Console.WriteLine("No migrations found.");
            return;
        }

        Console.WriteLine("Migrations:");
        foreach (var (version, migName) in migrations)
        {
            Console.WriteLine($"  {version:D4}  {migName}");
        }
    }

    /// <summary>
    /// quarry migrate validate — Validates migration integrity.
    /// </summary>
    public static async Task MigrateValidate(string project = ".")
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var migrations = FindMigrations(compilation);
        var errors = 0;

        // Check for version gaps and duplicates
        var seen = new HashSet<int>();
        foreach (var (version, _) in migrations)
        {
            if (!seen.Add(version))
            {
                Console.Error.WriteLine($"ERROR: Duplicate migration version {version}");
                errors++;
            }
        }

        if (migrations.Count > 0)
        {
            var sorted = migrations.OrderBy(m => m.Version).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Version != sorted[i - 1].Version + 1)
                {
                    Console.Error.WriteLine($"WARNING: Version gap between {sorted[i - 1].Version} and {sorted[i].Version}");
                }
            }
        }

        if (errors == 0)
            Console.WriteLine("Validation passed.");
        else
            Console.WriteLine($"Validation completed with {errors} error(s).");
    }

    /// <summary>
    /// quarry migrate remove — Removes the latest unapplied migration.
    /// </summary>
    public static async Task MigrateRemove(string project = ".", string output = "Migrations")
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var migrations = FindMigrations(compilation);
        if (migrations.Count == 0)
        {
            Console.WriteLine("No migrations to remove.");
            return;
        }

        var latest = migrations.Last();
        var outputDir = Path.Combine(Path.GetDirectoryName(csprojPath)!, output);
        var deleted = 0;

        if (Directory.Exists(outputDir))
        {
            // New layout: subdirectory per migration (M0001_Name/)
            var subdirPrefix = $"M{latest.Version:D4}_";
            foreach (var dir in Directory.GetDirectories(outputDir))
            {
                if (Path.GetFileName(dir).StartsWith(subdirPrefix))
                {
                    var dirName = Path.GetFileName(dir);
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"Deleted: {Path.Combine(output, dirName)}/");
                    deleted++;
                }
            }

            // Fallback: old flat-file layout (Migration_NNN_ / Snapshot_NNN_)
            if (deleted == 0)
            {
                var prefix = $"Migration_{latest.Version:D3}_";
                var snapshotPrefix = $"Snapshot_{latest.Version:D3}_";
                foreach (var file in Directory.GetFiles(outputDir, "*.cs"))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(prefix) || fileName.StartsWith(snapshotPrefix))
                    {
                        File.Delete(file);
                        Console.WriteLine($"Deleted: {Path.Combine(output, fileName)}");
                        deleted++;
                    }
                }
            }
        }

        if (deleted == 0)
            Console.WriteLine($"No files found for migration version {latest.Version}.");
        else
            Console.WriteLine($"Removed migration {latest.Version}: {latest.Name}");
    }

    /// <summary>
    /// quarry create-scripts — Generates full CREATE TABLE DDL from current schema.
    /// </summary>
    public static async Task CreateScripts(string project = ".", string? dialect = null, string? output = null)
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var resolvedDialect = DialectResolver.ResolveDialect(compilation, dialect);
        if (resolvedDialect == null)
        {
            Console.Error.WriteLine("Could not determine SQL dialect. Use --dialect / -d to specify.");
            return;
        }

        var sqlDialect = ParseDialect(resolvedDialect);
        var dialectInstance = SqlDialectFactory.GetDialect(sqlDialect);

        // Extract current schema and generate CREATE TABLE for each table
        var snapshot = ProjectSchemaReader.ExtractSchemaSnapshot(compilation, 0, "current", null);

        var builder = new Quarry.Migration.MigrationBuilder();
        foreach (var table in snapshot.Tables)
        {
            builder.CreateTable(table.TableName, table.SchemaName, t =>
            {
                foreach (var col in table.Columns)
                {
                    t.Column(col.Name, c =>
                    {
                        c.ClrType(col.ClrType);
                        if (col.IsNullable) c.Nullable();
                        else c.NotNull();
                        if (col.IsIdentity) c.Identity();
                        if (col.MaxLength.HasValue) c.Length(col.MaxLength.Value);
                        if (col.Precision.HasValue && col.Scale.HasValue) c.Precision(col.Precision.Value, col.Scale.Value);
                        if (col.HasDefault && col.DefaultExpression != null) c.DefaultExpression(col.DefaultExpression);
                    });
                }
                // Add PK constraint
                var pkCols = table.Columns.Where(c => c.Kind == Quarry.Shared.Migration.ColumnKind.PrimaryKey).Select(c => c.Name).ToArray();
                if (pkCols.Length > 0)
                    t.PrimaryKey($"PK_{table.TableName}", pkCols);
                // Add FK constraints
                foreach (var fk in table.ForeignKeys)
                    t.ForeignKey(fk.ConstraintName, fk.ColumnName, fk.ReferencedTable, fk.ReferencedColumn,
                        (Quarry.Migration.ForeignKeyAction)fk.OnDelete, (Quarry.Migration.ForeignKeyAction)fk.OnUpdate);
            });
        }

        var sql = builder.BuildSql(dialectInstance);

        if (output != null)
        {
            await File.WriteAllTextAsync(output, sql);
            Console.WriteLine($"Scripts written to: {output}");
        }
        else
        {
            Console.Write(sql);
        }
    }

    /// <summary>
    /// quarry migrate diff — Preview schema changes without generating migration files.
    /// </summary>
    public static async Task MigrateDiff(string project = ".", bool nonInteractive = false)
    {
        var csprojPath = ResolveCsproj(project);
        Console.WriteLine($"Loading project: {csprojPath}");

        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var latestVersion = FindLatestSnapshotVersion(compilation);
        var previewVersion = latestVersion + 1;

        // Build current schema
        var currentSnapshot = ProjectSchemaReader.ExtractSchemaSnapshot(
            compilation, previewVersion, "preview", latestVersion > 0 ? latestVersion : null);

        // Build previous snapshot (if any)
        SchemaSnapshot? previousSnapshot = null;
        if (latestVersion > 0)
            previousSnapshot = FindAndBuildSnapshot(compilation, latestVersion);

        // Diff
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename = null;
        if (InteractivePrompt.IsInteractive(nonInteractive))
        {
            acceptRename = candidate =>
            {
                return InteractivePrompt.Confirm(
                    $"Detected possible rename: '{candidate.OldName}' → '{candidate.NewName}' (confidence: {candidate.Score:F2})\n  Is this a rename?");
            };
        }

        var steps = SchemaDiffer.Diff(previousSnapshot, currentSnapshot, acceptRename);

        if (steps.Count == 0)
        {
            Console.WriteLine("No schema changes detected.");
            return;
        }

        Console.WriteLine("Schema changes detected:");
        foreach (var step in steps)
        {
            var icon = step.Classification switch
            {
                StepClassification.Safe => "+",
                StepClassification.Cautious => "~",
                StepClassification.Destructive => "!",
                _ => " "
            };
            Console.WriteLine($"  [{icon}] {step.Description}");
        }
    }

    /// <summary>
    /// quarry migrate script — Generate incremental migration SQL for a version range.
    /// </summary>
    public static async Task MigrateScript(
        string project = ".", string? dialect = null, string? output = null,
        int? from = null, int? to = null)
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var resolvedDialect = DialectResolver.ResolveDialect(compilation, dialect);
        if (resolvedDialect == null)
        {
            Console.Error.WriteLine("Could not determine SQL dialect. Use --dialect / -d to specify.");
            return;
        }

        var sqlDialect = ParseDialect(resolvedDialect);
        var migrations = FindMigrations(compilation);
        if (migrations.Count == 0)
        {
            Console.WriteLine("No migrations found.");
            return;
        }

        // Filter by range
        var fromVersion = from ?? migrations[0].Version;
        var toVersion = to ?? migrations[^1].Version;

        var filtered = migrations
            .Where(m => m.Version >= fromVersion && m.Version <= toVersion)
            .ToList();

        if (filtered.Count == 0)
        {
            Console.WriteLine($"No migrations found in range {fromVersion}–{toVersion}.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- Quarry migration script ({resolvedDialect})");
        sb.AppendLine($"-- Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Range: {filtered[0].Version} to {filtered[^1].Version}");
        sb.AppendLine();

        foreach (var (version, migName) in filtered)
        {
            sb.AppendLine($"-- Migration {version}: {migName}");

            var sql = MigrationCompiler.CompileAndBuildSql(compilation, version, sqlDialect);
            if (sql == null)
            {
                sb.AppendLine($"-- ERROR: Could not compile migration {version}");
                Console.Error.WriteLine($"WARNING: Could not compile migration {version}: {migName}");
            }
            else if (!string.IsNullOrWhiteSpace(sql))
            {
                sb.AppendLine(sql);
            }
            else
            {
                sb.AppendLine("-- (no SQL operations)");
            }

            sb.AppendLine();
        }

        var result = sb.ToString();

        if (output != null)
        {
            await File.WriteAllTextAsync(output, result);
            Console.WriteLine($"Script written to: {output}");
        }
        else
        {
            Console.Write(result);
        }
    }

    /// <summary>
    /// quarry migrate status — Compare applied migrations (DB) vs pending (code).
    /// </summary>
    public static async Task MigrateStatus(
        string project, string? dialect, string connectionString)
    {
        var csprojPath = ResolveCsproj(project);
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var resolvedDialect = DialectResolver.ResolveDialect(compilation, dialect);
        if (resolvedDialect == null)
        {
            Console.Error.WriteLine("Could not determine SQL dialect. Use --dialect / -d to specify.");
            return;
        }

        var sqlDialect = ParseDialect(resolvedDialect);
        var migrations = FindMigrations(compilation);

        // Connect to database
        using var connection = CreateConnection(resolvedDialect, connectionString);
        await connection.OpenAsync();

        // Query applied migrations
        var applied = await GetAppliedMigrationsAsync(connection, sqlDialect);

        if (migrations.Count == 0 && applied.Count == 0)
        {
            Console.WriteLine("No migrations found.");
            return;
        }

        Console.WriteLine("Migrations:");
        foreach (var (version, migName) in migrations)
        {
            if (applied.TryGetValue(version, out var appliedAt))
                Console.WriteLine($"  {version:D3}  {migName,-30} [applied {appliedAt:yyyy-MM-dd}]");
            else
                Console.WriteLine($"  {version:D3}  {migName,-30} [pending]");
        }

        // Warn about applied migrations not found in code
        foreach (var (version, _) in applied)
        {
            if (!migrations.Any(m => m.Version == version))
                Console.Error.WriteLine($"  WARNING: Migration {version} is applied in the database but not found in code.");
        }
    }

    /// <summary>
    /// quarry migrate squash — Collapse all migrations into a single baseline.
    /// </summary>
    public static async Task MigrateSquash(
        string project = ".", string output = "Migrations",
        bool nonInteractive = false, string? dialect = null,
        string? connectionString = null)
    {
        var csprojPath = ResolveCsproj(project);
        Console.WriteLine($"Loading project: {csprojPath}");

        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return;
        }

        var migrations = FindMigrations(compilation);
        if (migrations.Count <= 1)
        {
            Console.WriteLine("Nothing to squash — only 0 or 1 migration exists.");
            return;
        }

        var latestVersion = FindLatestSnapshotVersion(compilation);
        if (latestVersion == 0)
        {
            Console.Error.WriteLine("No snapshots found. Cannot squash without a schema snapshot.");
            return;
        }

        // Build the latest snapshot — this represents the full schema state
        var latestSnapshot = FindAndBuildSnapshot(compilation, latestVersion);
        if (latestSnapshot == null)
        {
            Console.Error.WriteLine($"Failed to build snapshot for version {latestVersion}.");
            return;
        }

        var squashedFromVersion = migrations[^1].Version;

        // Confirm with user
        if (InteractivePrompt.IsInteractive(nonInteractive))
        {
            var confirm = InteractivePrompt.Confirm(
                $"This will squash {migrations.Count} migrations (versions {migrations[0].Version}–{squashedFromVersion}) " +
                "into a single baseline. Old migration files will be deleted.\n  Continue?");
            if (!confirm)
            {
                Console.WriteLine("Squash cancelled.");
                return;
            }
        }

        var baseOutputDir = Path.Combine(Path.GetDirectoryName(csprojPath)!, output);
        var ns = GuessNamespace(csprojPath, output);

        // Create baseline snapshot at version 1
        var baselineSnapshot = new SchemaSnapshot(
            1, "Baseline", DateTimeOffset.UtcNow, null, latestSnapshot.Tables);

        // Generate baseline migration — creates all tables from the snapshot
        var baselineSteps = new List<MigrationStep>();
        foreach (var table in latestSnapshot.Tables)
        {
            baselineSteps.Add(new MigrationStep(
                MigrationStepType.CreateTable, StepClassification.Safe,
                table.TableName, table.SchemaName,
                null, null, table, $"Create table '{table.TableName}'"));
        }

        var names = ComputeMigrationNames(1, "Baseline");
        var migrationDir = Path.Combine(baseOutputDir, names.SubdirName);

        // Delete old migration files first
        var deletedCount = 0;
        if (Directory.Exists(baseOutputDir))
        {
            foreach (var dir in Directory.GetDirectories(baseOutputDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("M") && dirName != names.SubdirName)
                {
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"Deleted: {Path.Combine(output, dirName)}/");
                    deletedCount++;
                }
            }

            // Also clean old flat-file layout
            foreach (var file in Directory.GetFiles(baseOutputDir, "*.cs"))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("Migration_") || fileName.StartsWith("Snapshot_"))
                {
                    File.Delete(file);
                    Console.WriteLine($"Deleted: {Path.Combine(output, fileName)}");
                    deletedCount++;
                }
            }
        }

        // Generate new baseline files
        Directory.CreateDirectory(migrationDir);

        var combinedCode = GenerateCombinedMigrationFile(
            1, "Baseline", baselineSteps, null, baselineSnapshot, ns);

        // Add squash marker attribute to the generated code
        combinedCode = combinedCode.Replace(
            "[Migration(Version = 1, Name = \"Baseline\")]",
            $"[Migration(Version = 1, Name = \"Baseline\", SquashedFrom = {squashedFromVersion})]");

        await File.WriteAllTextAsync(Path.Combine(migrationDir, names.GeneratedFileName), combinedCode);
        Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.GeneratedFileName)}");

        var userFilePath = Path.Combine(migrationDir, names.UserFileName);
        if (!File.Exists(userFilePath))
        {
            var userCode = GenerateUserPartialFile(1, "Baseline", ns);
            await File.WriteAllTextAsync(userFilePath, userCode);
            Console.WriteLine($"Created: {Path.Combine(output, names.SubdirName, names.UserFileName)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Squashed {migrations.Count} migrations into baseline (squashed from version {squashedFromVersion}).");
        Console.WriteLine($"Deleted {deletedCount} old migration entries.");

        // Optionally update database
        if (connectionString != null)
        {
            var resolvedDialect = DialectResolver.ResolveDialect(compilation, dialect);
            if (resolvedDialect == null)
            {
                Console.Error.WriteLine("Could not determine SQL dialect for DB update. Use --dialect / -d to specify.");
                return;
            }

            var sqlDialect = ParseDialect(resolvedDialect);
            using var connection = CreateConnection(resolvedDialect, connectionString);
            await connection.OpenAsync();

            using var tx = await connection.BeginTransactionAsync();
            try
            {
                // Ensure squash_from column exists
                await EnsureSquashFromColumnAsync(connection, tx, sqlDialect);

                // Delete all existing migration history rows
                using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.Transaction = tx;
                    deleteCmd.CommandText = "DELETE FROM __quarry_migrations;";
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // Insert baseline row with squash_from marker
                using (var insertCmd = connection.CreateCommand())
                {
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = $@"INSERT INTO __quarry_migrations (version, name, applied_at, checksum, execution_time_ms, applied_by, status, squash_from)
                        VALUES ({SqlFormatting.FormatParameter(sqlDialect, 0)}, {SqlFormatting.FormatParameter(sqlDialect, 1)}, {SqlFormatting.FormatParameter(sqlDialect, 2)}, {SqlFormatting.FormatParameter(sqlDialect, 3)}, {SqlFormatting.FormatParameter(sqlDialect, 4)}, {SqlFormatting.FormatParameter(sqlDialect, 5)}, {SqlFormatting.FormatParameter(sqlDialect, 6)}, {SqlFormatting.FormatParameter(sqlDialect, 7)});";
                    AddParameter(insertCmd, sqlDialect, 0, 1);
                    AddParameter(insertCmd, sqlDialect, 1, "Baseline");
                    AddParameter(insertCmd, sqlDialect, 2, DateTime.UtcNow.ToString("o"));
                    AddParameter(insertCmd, sqlDialect, 3, "squashed");
                    AddParameter(insertCmd, sqlDialect, 4, 0);
                    AddParameter(insertCmd, sqlDialect, 5, $"{Environment.MachineName}/{Environment.UserName}");
                    AddParameter(insertCmd, sqlDialect, 6, "applied");
                    AddParameter(insertCmd, sqlDialect, 7, squashedFromVersion);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                Console.WriteLine("Database migration history updated with squashed baseline.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Console.Error.WriteLine($"Failed to update database: {ex.Message}");
            }
        }
    }

    // --- Helpers ---

    private static string ResolveCsproj(string project)
    {
        if (project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(project);

        var csprojs = Directory.GetFiles(project, "*.csproj");
        if (csprojs.Length == 1) return Path.GetFullPath(csprojs[0]);
        if (csprojs.Length == 0) throw new InvalidOperationException($"No .csproj found in '{project}'");
        throw new InvalidOperationException($"Multiple .csproj files found in '{project}'. Specify one with -p.");
    }

    private static int FindLatestSnapshotVersion(Microsoft.CodeAnalysis.Compilation compilation)
    {
        var maxVersion = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl);
                if (symbol == null) continue;
                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "MigrationSnapshotAttribute")
                    {
                        foreach (var arg in attr.NamedArguments)
                        {
                            if (arg.Key == "Version" && arg.Value.Value is int v && v > maxVersion)
                                maxVersion = v;
                        }
                    }
                }
            }
        }
        return maxVersion;
    }

    private static SchemaSnapshot? FindAndBuildSnapshot(Microsoft.CodeAnalysis.Compilation compilation, int version)
    {
        return SnapshotCompiler.CompileAndBuild(compilation, version);
    }

    private static List<(int Version, string Name)> FindMigrations(Microsoft.CodeAnalysis.Compilation compilation)
    {
        var migrations = new List<(int Version, string Name)>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl);
                if (symbol == null) continue;
                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "MigrationAttribute")
                    {
                        int? ver = null;
                        string? migName = null;
                        foreach (var arg in attr.NamedArguments)
                        {
                            if (arg.Key == "Version" && arg.Value.Value is int v) ver = v;
                            if (arg.Key == "Name" && arg.Value.Value is string n) migName = n;
                        }
                        if (ver.HasValue)
                            migrations.Add((ver.Value, migName ?? ""));
                    }
                }
            }
        }
        return migrations.OrderBy(m => m.Version).ToList();
    }

    private static string GuessNamespace(string csprojPath, string outputDir)
    {
        var projectName = Path.GetFileNameWithoutExtension(csprojPath);
        return $"{projectName}.{outputDir.Replace('/', '.').Replace('\\', '.')}";
    }

    private static string SanitizeName(string name) => CodeGenHelpers.SanitizeCSharpName(name);

    private static (string SubdirName, string MigrationClassName,
        string GeneratedFileName, string UserFileName)
        ComputeMigrationNames(int version, string name)
    {
        var sanitized = SanitizeName(name);
        var migrationClass = $"M{version:D4}_{sanitized}";
        return (
            SubdirName: migrationClass,
            MigrationClassName: migrationClass,
            GeneratedFileName: $"{migrationClass}.g.cs",
            UserFileName: $"{migrationClass}.cs"
        );
    }

    private static string GenerateUserPartialFile(int version, string name, string namespaceName)
    {
        var className = $"M{version:D4}_{SanitizeName(name)}";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using Quarry.Migration;");
        sb.AppendLine();
        sb.Append("namespace ").Append(namespaceName).AppendLine(";");
        sb.AppendLine();
        sb.Append("internal static partial class ").AppendLine(className);
        sb.AppendLine("{");
        sb.AppendLine("    // Uncomment and implement any hooks you need:");
        sb.AppendLine("    // static partial void BeforeUpgrade(MigrationBuilder builder) { }");
        sb.AppendLine("    // static partial void AfterUpgrade(MigrationBuilder builder) { }");
        sb.AppendLine("    // static partial void BeforeDowngrade(MigrationBuilder builder) { }");
        sb.AppendLine("    // static partial void AfterDowngrade(MigrationBuilder builder) { }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a single migration class file containing both the migration methods
    /// (Upgrade/Downgrade/Backup) and the snapshot Build() method.
    /// </summary>
    private static string GenerateCombinedMigrationFile(
        int version, string name,
        IReadOnlyList<MigrationStep> steps,
        SchemaSnapshot? previousSnapshot,
        SchemaSnapshot currentSnapshot,
        string namespaceName)
    {
        // Generate migration code (full file with class)
        var migrationCode = MigrationCodeGenerator.GenerateMigrationClass(
            version, name, steps, previousSnapshot, currentSnapshot, namespaceName);

        // Generate snapshot attribute + Build() method to insert into the same class
        var snapshotAttr = SnapshotCodeGenerator.GenerateSnapshotAttribute(currentSnapshot);
        var buildMethod = SnapshotCodeGenerator.GenerateBuildMethod(currentSnapshot);

        // Insert [MigrationSnapshot] before [Migration], and Build() before the final '}'.
        var lastBrace = migrationCode.LastIndexOf('}');
        var migrationAttr = "[Migration(";
        var attrPos = migrationCode.IndexOf(migrationAttr);

        var sb = new System.Text.StringBuilder();
        if (attrPos >= 0)
        {
            sb.Append(migrationCode, 0, attrPos);
            sb.Append(snapshotAttr);
            sb.Append(migrationCode, attrPos, lastBrace - attrPos);
        }
        else
        {
            sb.Append(migrationCode, 0, lastBrace);
        }

        sb.AppendLine();
        sb.Append(buildMethod);
        sb.Append(migrationCode, lastBrace, migrationCode.Length - lastBrace);

        return sb.ToString();
    }

    private static SqlDialect ParseDialect(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => SqlDialect.SQLite,
            "postgresql" or "postgres" or "pg" => SqlDialect.PostgreSQL,
            "mysql" => SqlDialect.MySQL,
            "sqlserver" or "mssql" => SqlDialect.SqlServer,
            _ => throw new InvalidOperationException($"Unknown dialect: {dialect}")
        };
    }

    private static DbConnection CreateConnection(string dialect, string connectionString)
    {
        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
            "postgresql" or "postgres" or "pg" => new Npgsql.NpgsqlConnection(connectionString),
            "mysql" => new MySqlConnector.MySqlConnection(connectionString),
            "sqlserver" or "mssql" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            _ => throw new InvalidOperationException($"Unknown dialect for connection: {dialect}")
        };
    }

    private static async Task<Dictionary<int, DateTime>> GetAppliedMigrationsAsync(
        DbConnection connection, SqlDialect dialect)
    {
        var map = new Dictionary<int, DateTime>();

        // Check if history table exists
        var tableExists = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = dialect switch
            {
                SqlDialect.SQLite => "SELECT name FROM sqlite_master WHERE type='table' AND name='__quarry_migrations';",
                SqlDialect.SqlServer => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__quarry_migrations';",
                _ => "SELECT table_name FROM information_schema.tables WHERE table_name = '__quarry_migrations';"
            };
            var result = await checkCmd.ExecuteScalarAsync();
            tableExists = result != null;
        }

        if (!tableExists)
            return map;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version, applied_at FROM __quarry_migrations WHERE status = 'applied' ORDER BY version;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var version = reader.GetInt32(0);
            DateTime appliedAt;
            try
            {
                appliedAt = reader.GetDateTime(1);
            }
            catch
            {
                // Fallback for string-stored dates (SQLite)
                appliedAt = DateTime.Parse(reader.GetString(1));
            }
            map[version] = appliedAt;
        }
        return map;
    }

    private static async Task EnsureSquashFromColumnAsync(DbConnection connection, DbTransaction tx, SqlDialect dialect)
    {
        var alterSql = dialect switch
        {
            SqlDialect.SQLite => "ALTER TABLE __quarry_migrations ADD COLUMN squash_from INTEGER;",
            SqlDialect.PostgreSQL => "ALTER TABLE __quarry_migrations ADD COLUMN IF NOT EXISTS squash_from INT;",
            SqlDialect.MySQL => "ALTER TABLE __quarry_migrations ADD COLUMN squash_from INT;",
            _ => @"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '__quarry_migrations' AND COLUMN_NAME = 'squash_from')
                   ALTER TABLE __quarry_migrations ADD squash_from INT;"
        };

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = alterSql;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists — safe to ignore
        }
    }

    private static void AddParameter(DbCommand cmd, SqlDialect dialect, int index, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = SqlFormatting.GetParameterName(dialect, index);
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
