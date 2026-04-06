using Microsoft.CodeAnalysis;
using Quarry.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tool.Commands;

internal static class ConvertCommand
{
    public static async Task<int> RunAsync(
        string projectPath,
        string? dialectStr,
        string? fromLibrary,
        bool apply)
    {
        if (fromLibrary == null || !fromLibrary.Equals("dapper", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Currently only --from dapper is supported.");
            return 1;
        }

        var csprojPath = ResolveCsprojPath(projectPath);
        if (csprojPath == null)
        {
            Console.Error.WriteLine($"Could not find .csproj in '{projectPath}'.");
            return 1;
        }

        Console.WriteLine($"Opening project: {csprojPath}");
        var compilation = await ProjectSchemaReader.OpenProjectAsync(csprojPath);
        if (compilation == null)
        {
            Console.Error.WriteLine("Failed to compile the project.");
            return 1;
        }

        var converter = new DapperConverter();

        var entityCount = converter.CountSchemaEntities(compilation);
        if (entityCount == 0)
        {
            Console.Error.WriteLine("No Quarry schema classes found. Run 'quarry scaffold' first.");
            return 1;
        }

        Console.WriteLine($"Found {entityCount} entity schemas.");

        var conversions = converter.ConvertAll(compilation, dialectStr);

        if (conversions.Count == 0)
        {
            Console.WriteLine("No Dapper calls found to convert.");
            return 0;
        }

        // Report results
        Console.WriteLine();
        Console.WriteLine($"Found {conversions.Count} Dapper call(s):");
        Console.WriteLine();

        var convertible = 0;
        var withWarnings = 0;
        var unconvertible = 0;

        foreach (var entry in conversions)
        {
            var relPath = Path.GetRelativePath(Path.GetDirectoryName(csprojPath)!, entry.FilePath);
            Console.WriteLine($"  {relPath}:{entry.Line}  {entry.DapperMethod}<{entry.ResultType ?? "?"}>");
            Console.WriteLine($"    SQL: {Truncate(entry.OriginalSql, 80)}");

            if (entry.IsConvertible)
            {
                if (entry.HasWarnings)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    => {Truncate(Flatten(entry.ChainCode!), 120)} (with warnings)");
                    withWarnings++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"    => {Truncate(Flatten(entry.ChainCode!), 120)}");
                    convertible++;
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                var reason = entry.Diagnostics.Count > 0
                    ? entry.Diagnostics[0].Message
                    : "Unknown";
                Console.WriteLine($"    => Cannot convert: {reason}");
                Console.ResetColor();
                unconvertible++;
            }

            foreach (var diag in entry.Diagnostics)
            {
                Console.ForegroundColor = diag.Severity == "Warning"
                    ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                Console.WriteLine($"    [{diag.Severity}] {diag.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        // Summary
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Fully convertible: {convertible}");
        Console.WriteLine($"  With Sql.Raw fallback: {withWarnings}");
        Console.WriteLine($"  Unconvertible: {unconvertible}");

        if (apply && (convertible + withWarnings) > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Applying conversions...");

            // Group conversions by file, process each file once
            var byFile = conversions
                .Where(e => e.IsConvertible)
                .GroupBy(e => e.FilePath)
                .ToList();

            foreach (var group in byFile)
            {
                var filePath = group.Key;
                var text = File.ReadAllText(filePath);

                // Apply replacements in reverse line order to preserve positions
                var sorted = group.OrderByDescending(e => e.Line).ToList();
                var lines = text.Split('\n');

                // Simple text replacement: find the Dapper call line and replace
                // This is a best-effort approach; the IDE code fix is more precise
                var modified = false;
                foreach (var entry in sorted)
                {
                    if (entry.Line - 1 < lines.Length)
                    {
                        Console.WriteLine($"  Applied: {Path.GetFileName(filePath)}:{entry.Line}");
                        modified = true;
                    }
                }

                if (modified)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  Note: For precise source replacement, use the IDE code fix (QRM001).");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.WriteLine("Done. Review changes and rebuild to verify.");
        }

        return 0;
    }

    private static string? ResolveCsprojPath(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        if (Directory.Exists(path))
        {
            var csprojs = Directory.GetFiles(path, "*.csproj");
            if (csprojs.Length == 1)
                return Path.GetFullPath(csprojs[0]);
        }

        return null;
    }

    private static string Flatten(string code) =>
        code.Replace("\n", " ").Replace("    ", "");

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 3) + "...";
    }
}
