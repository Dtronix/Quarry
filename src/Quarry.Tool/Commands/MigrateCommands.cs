using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Shared.Migration;
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
}
