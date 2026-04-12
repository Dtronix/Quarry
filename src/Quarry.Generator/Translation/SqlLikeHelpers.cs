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

    /// <summary>
    /// Formats a compile-time constant value as a SQL literal string.
    /// Returns null for types that cannot be safely inlined (DateTime, Guid, enums, byte[], etc.).
    /// Callers should fall back to parameter binding when null is returned.
    /// </summary>
    public static string? FormatConstantAsSqlLiteral(object? value)
    {
        return value switch
        {
            null => "NULL",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short s => s.ToString(System.Globalization.CultureInfo.InvariantCulture),
            byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort us => us.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool bv => bv ? "TRUE" : "FALSE",
            char c => $"'{EscapeSqlStringLiteral(c.ToString())}'",
            string str => $"'{EscapeSqlStringLiteral(str)}'",
            _ => null
        };
    }

    /// <summary>
    /// Escapes a string for use in a SQL single-quoted literal.
    /// Handles single quotes and backslashes.
    /// </summary>
    public static string EscapeSqlStringLiteral(string value)
    {
        if (value.IndexOf('\'') < 0 && value.IndexOf('\\') < 0)
            return value;
        return value.Replace("'", "''").Replace("\\", "\\\\");
    }
}
