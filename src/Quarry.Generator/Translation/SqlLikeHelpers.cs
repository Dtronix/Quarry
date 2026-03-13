using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Translation;

/// <summary>
/// Shared helpers for LIKE expression generation used by both translators.
/// </summary>
internal static class SqlLikeHelpers
{
    /// <summary>
    /// Escapes LIKE metacharacters using backslash escaping (cross-dialect with ESCAPE '\').
    /// </summary>
    public static string EscapeLikeMetaChars(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    /// <summary>
    /// Formats a LIKE expression with a parameter value using dialect-appropriate concatenation.
    /// </summary>
    public static string FormatLikeWithParameter(
        SqlDialect dialect,
        string column,
        string param,
        string prefix,
        string suffix)
    {
        var prefixLiteral = string.IsNullOrEmpty(prefix) ? "" : $"'{prefix}'";
        var suffixLiteral = string.IsNullOrEmpty(suffix) ? "" : $"'{suffix}'";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(prefixLiteral))
            parts.Add(prefixLiteral);
        parts.Add(param);
        if (!string.IsNullOrEmpty(suffixLiteral))
            parts.Add(suffixLiteral);

        string pattern;
        if (parts.Count == 1)
        {
            pattern = parts[0];
        }
        else
        {
            pattern = dialect switch
            {
                SqlDialect.MySQL => $"CONCAT({string.Join(", ", parts)})",
                SqlDialect.SqlServer => string.Join(" + ", parts),
                _ => string.Join(" || ", parts) // SQLite, PostgreSQL
            };
        }

        return $"{column} LIKE {pattern}";
    }
}
