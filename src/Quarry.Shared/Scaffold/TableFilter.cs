using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Quarry.Shared.Scaffold;

internal static class TableFilter
{
    private static readonly HashSet<string> AutoExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "__EFMigrationsHistory",
        "__quarry_migrations",
        "schema_migrations",
        "flyway_schema_history",
        "_prisma_migrations"
    };

    private static readonly string[] AutoExcludedPrefixes =
    {
        "pg_", "sqlite_", "sys.", "information_schema."
    };

    public static List<TableMetadata> Apply(List<TableMetadata> tables, string? tablePattern)
    {
        var result = new List<TableMetadata>();

        // Parse patterns
        var (includes, excludes) = ParsePatterns(tablePattern);

        foreach (var table in tables)
        {
            // Auto-exclude system tables
            if (IsAutoExcluded(table.Name))
                continue;

            // Apply user patterns
            if (includes.Count > 0 && !includes.Any(p => GlobMatch(table.Name, p)))
                continue;

            if (excludes.Any(p => GlobMatch(table.Name, p)))
                continue;

            result.Add(table);
        }

        return result;
    }

    private static bool IsAutoExcluded(string tableName)
    {
        if (AutoExcluded.Contains(tableName))
            return true;

        foreach (var prefix in AutoExcludedPrefixes)
        {
            if (tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static (List<string> Includes, List<string> Excludes) ParsePatterns(string? pattern)
    {
        var includes = new List<string>();
        var excludes = new List<string>();

        if (string.IsNullOrWhiteSpace(pattern))
        {
            includes.Add("*");
            return (includes, excludes);
        }

        foreach (var part in pattern.Split(','))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.StartsWith("!"))
                excludes.Add(trimmed.Substring(1));
            else
                includes.Add(trimmed);
        }

        if (includes.Count == 0)
            includes.Add("*");

        return (includes, excludes);
    }

    private static readonly Dictionary<string, Regex> GlobCache = new();

    private static bool GlobMatch(string input, string pattern)
    {
        if (!GlobCache.TryGetValue(pattern, out var regex))
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            GlobCache[pattern] = regex;
        }
        return regex.IsMatch(input);
    }
}
