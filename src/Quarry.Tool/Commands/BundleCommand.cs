using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Tool.Schema;

namespace Quarry.Tool.Commands;

internal static class BundleCommand
{
    /// <summary>
    /// quarry migrate bundle — Produces a self-contained migration executable.
    /// </summary>
    /// <param name="project">-p, Path to .csproj</param>
    /// <param name="output">-o, Output path for the bundle executable</param>
    /// <param name="dialect">-d, Default dialect to embed (optional, overridable at runtime)</param>
    /// <param name="selfContained">--self-contained, Publish as self-contained (no runtime required)</param>
    /// <param name="runtime">-r, Target runtime identifier (e.g. linux-x64, win-x64)</param>
    public static async Task<int> RunAsync(
        string project,
        string output,
        string? dialect,
        bool selfContained,
        string? runtime)
    {
        var csprojPath = CommandHelpers.ResolveCsproj(project);
        Console.WriteLine($"Loading project: {csprojPath}");

        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to load project compilation.");
            return 1;
        }

        // Discover migrations
        var migrations = CommandHelpers.FindMigrations(compilation);
        if (migrations.Count == 0)
        {
            Console.Error.WriteLine("No migrations found. Nothing to bundle.");
            return 1;
        }

        Console.WriteLine($"Found {migrations.Count} migration(s).");

        // Discover migration source files on disk
        var migrationSourceFiles = DiscoverMigrationSourceFiles(compilation, migrations);
        if (migrationSourceFiles.Count == 0)
        {
            Console.Error.WriteLine("No migration source files found on disk.");
            return 1;
        }

        // Resolve default dialect from project (if not explicitly specified)
        var defaultDialect = DialectResolver.ResolveDialect(compilation, dialect);

        // Find Quarry reference from user's project
        var quarryRef = FindQuarryReference(csprojPath);
        if (quarryRef == null)
        {
            Console.Error.WriteLine("Could not find Quarry reference in project. Ensure the project references Quarry.");
            return 1;
        }

        // Create temp directory for the bundle project
        var tempDir = Path.Combine(Path.GetTempPath(), $"quarry-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Copy migration source files
            var migrationsDir = Path.Combine(tempDir, "Migrations");
            Directory.CreateDirectory(migrationsDir);
            CopyMigrationFiles(migrationSourceFiles, migrationsDir);

            // Generate Program.cs
            var programCs = GenerateBundleProgram(migrations, defaultDialect);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), programCs);

            // Generate .csproj
            var bundleCsproj = GenerateBundleCsproj(quarryRef, selfContained, runtime);
            var bundleCsprojPath = Path.Combine(tempDir, "QuarryBundle.csproj");
            await File.WriteAllTextAsync(bundleCsprojPath, bundleCsproj);

            // Run dotnet publish
            Console.WriteLine("Building migration bundle...");
            var publishArgs = $"publish \"{bundleCsprojPath}\" -c Release -o \"{Path.Combine(tempDir, "publish")}\"";
            if (!string.IsNullOrEmpty(runtime))
                publishArgs += $" -r {runtime}";

            var psi = new ProcessStartInfo("dotnet", publishArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start dotnet publish.");
                return 1;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine("Bundle build failed:");
                if (!string.IsNullOrWhiteSpace(stdout))
                    Console.Error.WriteLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                    Console.Error.WriteLine(stderr);
                return 1;
            }

            // Copy all published files to the output directory
            var publishDir = Path.Combine(tempDir, "publish");
            var outputDir = Path.GetFullPath(output);
            Directory.CreateDirectory(outputDir);

            if (Directory.Exists(publishDir))
            {
                foreach (var file in Directory.GetFiles(publishDir))
                {
                    var destPath = Path.Combine(outputDir, Path.GetFileName(file));
                    File.Copy(file, destPath, overwrite: true);
                }
            }

            Console.WriteLine($"Bundle created: {outputDir}");
            Console.WriteLine($"  Migrations: {migrations.Count}");
            if (defaultDialect != null)
                Console.WriteLine($"  Default dialect: {defaultDialect}");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine($"  ./{Path.GetFileName(outputDir)}/QuarryBundle --connection \"<connection-string>\" --dialect <dialect>");
            Console.WriteLine($"  ./{Path.GetFileName(outputDir)}/QuarryBundle --connection \"...\" --target 5");
            Console.WriteLine($"  ./{Path.GetFileName(outputDir)}/QuarryBundle --connection \"...\" --dry-run");

            return 0;
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Discovers all source files on disk that belong to migration classes.
    /// </summary>
    internal static List<MigrationSourceFile> DiscoverMigrationSourceFiles(
        Compilation compilation,
        List<CommandHelpers.MigrationInfo> migrations)
    {
        var migrationClassNames = new HashSet<string>(migrations.Select(m => m.ClassName));
        var files = new List<MigrationSourceFile>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var filePath = tree.FilePath;
            if (string.IsNullOrEmpty(filePath)) continue;

            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (migrationClassNames.Contains(classDecl.Identifier.Text))
                {
                    files.Add(new MigrationSourceFile(
                        classDecl.Identifier.Text,
                        filePath));
                    break; // one match per file is enough
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Copies migration source files into the bundle's Migrations directory,
    /// preserving relative structure.
    /// </summary>
    private static void CopyMigrationFiles(List<MigrationSourceFile> sourceFiles, string migrationsDir)
    {
        // Group by class name to preserve subdirectory structure
        var groups = sourceFiles.GroupBy(f => f.ClassName);
        foreach (var group in groups)
        {
            var subDir = Path.Combine(migrationsDir, group.Key);
            Directory.CreateDirectory(subDir);

            foreach (var file in group)
            {
                var destPath = Path.Combine(subDir, Path.GetFileName(file.FilePath));
                File.Copy(file.FilePath, destPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Generates the bundle's entry-point Program.cs.
    /// </summary>
    internal static string GenerateBundleProgram(
        List<CommandHelpers.MigrationInfo> migrations,
        string? defaultDialect)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Data.Common;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Quarry;");
        sb.AppendLine("using Quarry.Migration;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        sb.AppendLine("using Npgsql;");
        sb.AppendLine("using MySqlConnector;");
        sb.AppendLine("using Microsoft.Data.SqlClient;");

        // Add usings for migration namespaces
        var namespaces = new HashSet<string>();
        foreach (var m in migrations)
        {
            if (!string.IsNullOrEmpty(m.Namespace))
                namespaces.Add(m.Namespace);
        }
        foreach (var ns in namespaces.OrderBy(n => n))
        {
            sb.Append("using ").Append(ns).AppendLine(";");
        }

        sb.AppendLine();
        sb.AppendLine("return await RunAsync(args);");
        sb.AppendLine();
        sb.AppendLine("static async Task<int> RunAsync(string[] args)");
        sb.AppendLine("{");

        // CLI argument parsing
        sb.AppendLine("    var opts = ParseOptions(args);");
        sb.AppendLine();
        sb.AppendLine("    if (HasFlag(opts, \"h\", \"help\"))");
        sb.AppendLine("    {");
        sb.AppendLine("        PrintUsage();");
        sb.AppendLine("        return 0;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Connection string
        sb.AppendLine("    var connection = GetOpt(opts, \"c\", \"connection\", null)");
        sb.AppendLine("        ?? Environment.GetEnvironmentVariable(\"QUARRY_CONNECTION\");");
        sb.AppendLine();
        sb.AppendLine("    if (string.IsNullOrEmpty(connection))");
        sb.AppendLine("    {");
        sb.AppendLine("        Console.Error.WriteLine(\"Error: Connection string required. Use --connection or set QUARRY_CONNECTION.\");");
        sb.AppendLine("        return 1;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Dialect
        if (defaultDialect != null)
        {
            sb.Append("    var dialectStr = GetOpt(opts, \"d\", \"dialect\", \"").Append(defaultDialect).AppendLine("\");");
        }
        else
        {
            sb.AppendLine("    var dialectStr = GetOpt(opts, \"d\", \"dialect\", null);");
            sb.AppendLine("    if (string.IsNullOrEmpty(dialectStr))");
            sb.AppendLine("    {");
            sb.AppendLine("        Console.Error.WriteLine(\"Error: Dialect required. Use --dialect <sqlite|postgresql|mysql|sqlserver>.\");");
            sb.AppendLine("        return 1;");
            sb.AppendLine("    }");
        }
        sb.AppendLine();

        sb.AppendLine("    var dialect = ParseDialect(dialectStr!);");
        sb.AppendLine("    var targetStr = GetOpt(opts, \"t\", \"target\", null);");
        sb.AppendLine("    var dryRun = HasFlag(opts, null, \"dry-run\");");
        sb.AppendLine("    var directionStr = GetOpt(opts, null, \"direction\", \"up\");");
        sb.AppendLine("    var idempotent = HasFlag(opts, null, \"idempotent\");");
        sb.AppendLine("    var ignoreIncomplete = HasFlag(opts, null, \"ignore-incomplete\");");
        sb.AppendLine();

        // Build migrations array
        sb.AppendLine("    var migrations = new (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup)[]");
        sb.AppendLine("    {");
        for (var i = 0; i < migrations.Count; i++)
        {
            var m = migrations[i];
            sb.Append("        (").Append(m.Version);
            sb.Append(", \"").Append(EscapeCSharpString(m.Name)).Append("\"");
            sb.Append(", ").Append(m.ClassName).Append(".Upgrade");
            sb.Append(", ").Append(m.ClassName).Append(".Downgrade");
            sb.Append(", ").Append(m.ClassName).Append(".Backup");
            sb.Append(")");
            if (i < migrations.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // Options
        sb.AppendLine("    var options = new MigrationOptions");
        sb.AppendLine("    {");
        sb.AppendLine("        DryRun = dryRun,");
        sb.AppendLine("        Idempotent = idempotent,");
        sb.AppendLine("        IgnoreIncomplete = ignoreIncomplete,");
        sb.AppendLine("        Direction = directionStr?.ToLowerInvariant() == \"down\" ? MigrationDirection.Downgrade : MigrationDirection.Upgrade,");
        sb.AppendLine("        Logger = msg => Console.WriteLine(msg)");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    if (targetStr != null && int.TryParse(targetStr, out var target))");
        sb.AppendLine("        options.TargetVersion = target;");
        sb.AppendLine();

        // Create connection and run
        sb.AppendLine("    DbConnection dbConnection = dialect switch");
        sb.AppendLine("    {");
        sb.AppendLine("        SqlDialect.SQLite => new SqliteConnection(connection),");
        sb.AppendLine("        SqlDialect.PostgreSQL => new NpgsqlConnection(connection),");
        sb.AppendLine("        SqlDialect.MySQL => new MySqlConnection(connection),");
        sb.AppendLine("        SqlDialect.SqlServer => new SqlConnection(connection),");
        sb.AppendLine("        _ => throw new NotSupportedException($\"Unsupported dialect: {dialect}\")");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    await using (dbConnection)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await MigrationRunner.RunAsync(dbConnection, dialect, migrations, options);");
        sb.AppendLine("            Console.WriteLine(\"Migrations completed successfully.\");");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            Console.Error.WriteLine($\"Migration failed: {ex.Message}\");");
        sb.AppendLine("            return 1;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Helper methods
        sb.AppendLine("static SqlDialect ParseDialect(string dialect) => dialect.ToLowerInvariant() switch");
        sb.AppendLine("{");
        sb.AppendLine("    \"sqlite\" => SqlDialect.SQLite,");
        sb.AppendLine("    \"postgresql\" or \"postgres\" or \"pg\" => SqlDialect.PostgreSQL,");
        sb.AppendLine("    \"mysql\" => SqlDialect.MySQL,");
        sb.AppendLine("    \"sqlserver\" or \"mssql\" => SqlDialect.SqlServer,");
        sb.AppendLine("    _ => throw new ArgumentException($\"Unknown dialect: {dialect}\")");
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine("static Dictionary<string, string> ParseOptions(string[] args)");
        sb.AppendLine("{");
        sb.AppendLine("    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    for (var i = 0; i < args.Length; i++)");
        sb.AppendLine("    {");
        sb.AppendLine("        var arg = args[i];");
        sb.AppendLine("        if (arg.StartsWith(\"--\"))");
        sb.AppendLine("        {");
        sb.AppendLine("            var key = arg[2..];");
        sb.AppendLine("            if (i + 1 < args.Length && !args[i + 1].StartsWith(\"-\"))");
        sb.AppendLine("                opts[key] = args[++i];");
        sb.AppendLine("            else");
        sb.AppendLine("                opts[key] = \"true\";");
        sb.AppendLine("        }");
        sb.AppendLine("        else if (arg.StartsWith(\"-\") && arg.Length == 2)");
        sb.AppendLine("        {");
        sb.AppendLine("            var key = arg[1..];");
        sb.AppendLine("            if (i + 1 < args.Length && !args[i + 1].StartsWith(\"-\"))");
        sb.AppendLine("                opts[key] = args[++i];");
        sb.AppendLine("            else");
        sb.AppendLine("                opts[key] = \"true\";");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    return opts;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("static string? GetOpt(Dictionary<string, string> opts, string? shortKey, string longKey, string? defaultValue)");
        sb.AppendLine("{");
        sb.AppendLine("    if (shortKey != null && opts.TryGetValue(shortKey, out var v1)) return v1;");
        sb.AppendLine("    if (opts.TryGetValue(longKey, out var v2)) return v2;");
        sb.AppendLine("    return defaultValue;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("static bool HasFlag(Dictionary<string, string> opts, string? shortKey, string longKey)");
        sb.AppendLine("{");
        sb.AppendLine("    return GetOpt(opts, shortKey, longKey, \"false\") == \"true\";");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("static void PrintUsage()");
        sb.AppendLine("{");
        sb.AppendLine("    Console.WriteLine(\"Quarry Migration Bundle\");");
        sb.AppendLine("    Console.WriteLine();");
        sb.AppendLine("    Console.WriteLine(\"Usage: <bundle> [options]\");");
        sb.AppendLine("    Console.WriteLine();");
        sb.AppendLine("    Console.WriteLine(\"Options:\");");
        sb.AppendLine("    Console.WriteLine(\"  -c, --connection <string>   Connection string (or set QUARRY_CONNECTION env var)\");");
        sb.AppendLine("    Console.WriteLine(\"  -d, --dialect <dialect>     SQL dialect: sqlite, postgresql, mysql, sqlserver\");");
        sb.AppendLine("    Console.WriteLine(\"  -t, --target <version>      Target migration version\");");
        sb.AppendLine("    Console.WriteLine(\"      --direction <up|down>   Migration direction (default: up)\");");
        sb.AppendLine("    Console.WriteLine(\"      --dry-run               Print SQL without executing\");");
        sb.AppendLine("    Console.WriteLine(\"      --idempotent            Wrap DDL with IF NOT EXISTS guards\");");
        sb.AppendLine("    Console.WriteLine(\"      --ignore-incomplete     Skip incomplete migration checks\");");
        sb.AppendLine("    Console.WriteLine(\"  -h, --help                  Show this help message\");");
        sb.AppendLine("    Console.WriteLine();");
        sb.AppendLine("    Console.WriteLine(\"Exit codes: 0 = success, 1 = failure\");");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the bundle's .csproj file.
    /// </summary>
    internal static string GenerateBundleCsproj(QuarryReference quarryRef, bool selfContained, string? runtime)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <PublishSingleFile>true</PublishSingleFile>");

        if (selfContained)
        {
            sb.AppendLine("    <SelfContained>true</SelfContained>");
            sb.AppendLine("    <PublishTrimmed>true</PublishTrimmed>");
        }
        else
        {
            sb.AppendLine("    <SelfContained>false</SelfContained>");
        }

        if (!string.IsNullOrEmpty(runtime))
            sb.Append("    <RuntimeIdentifier>").Append(runtime).AppendLine("</RuntimeIdentifier>");

        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");

        // Quarry reference
        if (quarryRef.IsProjectReference)
        {
            sb.Append("    <ProjectReference Include=\"").Append(quarryRef.Path).AppendLine("\" />");
        }
        else if (quarryRef.IsPackageReference)
        {
            sb.Append("    <PackageReference Include=\"").Append(quarryRef.PackageName).Append("\" Version=\"").Append(quarryRef.Version).AppendLine("\" />");
        }
        else
        {
            // Direct DLL reference
            sb.Append("    <Reference Include=\"Quarry\"><HintPath>").Append(quarryRef.Path).AppendLine("</HintPath></Reference>");
        }

        // Database provider packages
        sb.AppendLine("    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"9.*\" />");
        sb.AppendLine("    <PackageReference Include=\"Npgsql\" Version=\"9.*\" />");
        sb.AppendLine("    <PackageReference Include=\"MySqlConnector\" Version=\"2.*\" />");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"6.*\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    /// <summary>
    /// Parses the user's .csproj to find how they reference Quarry.
    /// </summary>
    internal static QuarryReference? FindQuarryReference(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Check for ProjectReference to Quarry
        foreach (var projRef in doc.Descendants(ns + "ProjectReference"))
        {
            var include = projRef.Attribute("Include")?.Value;
            if (include != null && include.Contains("Quarry") && !include.Contains("Generator") && !include.Contains("Tool"))
            {
                // Resolve the path relative to the .csproj
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csprojPath)!, include));
                return new QuarryReference { IsProjectReference = true, Path = fullPath };
            }
        }

        // Check for PackageReference to Quarry
        foreach (var pkgRef in doc.Descendants(ns + "PackageReference"))
        {
            var include = pkgRef.Attribute("Include")?.Value;
            if (include != null && string.Equals(include, "Quarry", StringComparison.OrdinalIgnoreCase))
            {
                return new QuarryReference
                {
                    IsPackageReference = true,
                    PackageName = include,
                    Version = pkgRef.Attribute("Version")?.Value ?? "*"
                };
            }
        }

        // Fallback: try to find Quarry.dll from the tool's own assembly
        var quarryAssembly = typeof(Quarry.Migration.MigrationRunner).Assembly;
        if (!string.IsNullOrEmpty(quarryAssembly.Location))
        {
            return new QuarryReference { Path = quarryAssembly.Location };
        }

        return null;
    }

    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    internal sealed class MigrationSourceFile(string className, string filePath)
    {
        public string ClassName { get; } = className;
        public string FilePath { get; } = filePath;
    }

    internal sealed class QuarryReference
    {
        public bool IsProjectReference { get; init; }
        public bool IsPackageReference { get; init; }
        public string? Path { get; init; }
        public string? PackageName { get; init; }
        public string? Version { get; init; }
    }
}
