using System;
using System.Runtime.CompilerServices;
using System.Text;
#if !QUARRY_GENERATOR
using Quarry;
#endif

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql;
#else
namespace Quarry.Shared.Sql;
#endif

/// <summary>
/// Shared static methods for dialect-specific SQL formatting.
/// Used by both the runtime and the compile-time SQL builder.
/// </summary>
internal static partial class SqlFormatting
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (char Start, char End) GetIdentifierQuoteChars(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => ('`', '`'),
            SqlDialect.SqlServer => ('[', ']'),
            _ => ('"', '"') // SQLite, PostgreSQL
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string QuoteIdentifier(SqlDialect dialect, string identifier)
    {
        if (identifier is null) throw new ArgumentNullException(nameof(identifier));
        return dialect switch
        {
            SqlDialect.MySQL => $"`{EscapeIdentifier(identifier, '`')}`",
            SqlDialect.SqlServer => $"[{EscapeIdentifier(identifier, ']')}]",
            _ => $"\"{EscapeIdentifier(identifier, '"')}\"" // SQLite, PostgreSQL
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string EscapeIdentifier(string identifier, char quoteChar)
    {
        if (identifier.IndexOf(quoteChar) < 0)
            return identifier;
        return identifier.Replace(quoteChar.ToString(), $"{quoteChar}{quoteChar}");
    }

    public static string FormatTableName(SqlDialect dialect, string tableName, string? schemaName)
    {
        var quotedTable = QuoteIdentifier(dialect, tableName);
        if (string.IsNullOrEmpty(schemaName))
        {
            return quotedTable;
        }
        return $"{QuoteIdentifier(dialect, schemaName!)}.{quotedTable}";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatParameter(SqlDialect dialect, int index)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => $"${index + 1}",    // 1-based: $1, $2, ...
            SqlDialect.MySQL => "?",                      // positional placeholder
            _ => $"@p{index}"                                 // SQLite, SqlServer: @p0, @p1, ...
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetParameterName(SqlDialect dialect, int index)
    {
        // All dialects use @p0, @p1 for DbParameter.ParameterName,
        // even MySQL which uses ? placeholders in SQL
        return $"@p{index}";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatBoolean(SqlDialect dialect, bool value)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => value ? "TRUE" : "FALSE",
            _ => value ? "1" : "0"
        };
    }

    public static string? FormatReturningClause(SqlDialect dialect, string identityColumn)
    {
        return dialect switch
        {
            SqlDialect.SQLite => $"RETURNING {QuoteIdentifier(dialect, identityColumn)}",
            SqlDialect.PostgreSQL => $"RETURNING {QuoteIdentifier(dialect, identityColumn)}",
            SqlDialect.SqlServer => $"OUTPUT INSERTED.{QuoteIdentifier(dialect, identityColumn)}",
            SqlDialect.MySQL => null,
            _ => null
        };
    }

    public static string? GetLastInsertIdQuery(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => "SELECT LAST_INSERT_ID()",
            _ => null
        };
    }

    public static string GetIdentitySyntax(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.SQLite => "AUTOINCREMENT",
            SqlDialect.PostgreSQL => "GENERATED ALWAYS AS IDENTITY",
            SqlDialect.MySQL => "AUTO_INCREMENT",
            SqlDialect.SqlServer => "IDENTITY(1,1)",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect))
        };
    }

    public static string FormatStringConcat(SqlDialect dialect, params string[] operands)
    {
        if (operands is null || operands.Length == 0)
            return string.Empty;

        if (operands.Length == 1)
            return operands[0];

        return dialect switch
        {
            SqlDialect.MySQL => $"CONCAT({string.Join(", ", operands)})",
            SqlDialect.SqlServer => string.Join(" + ", operands),
            _ => string.Join(" || ", operands) // SQLite, PostgreSQL
        };
    }

    /// <summary>
    /// Formats pagination with literal integer values (runtime path).
    /// </summary>
    public static string FormatPagination(SqlDialect dialect, int? limit, int? offset)
    {
        if (limit is null && offset is null)
            return string.Empty;

        if (dialect == SqlDialect.SqlServer)
            return FormatSqlServerPagination(limit, offset);

        return FormatLimitOffset(limit, offset);
    }

    private static string FormatLimitOffset(int? limit, int? offset)
    {
        var sb = new StringBuilder();

        if (limit.HasValue)
        {
            sb.Append($"LIMIT {limit.Value}");
        }

        if (offset.HasValue && offset.Value > 0)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"OFFSET {offset.Value}");
        }

        return sb.ToString();
    }

    private static string FormatSqlServerPagination(int? limit, int? offset)
    {
        var sb = new StringBuilder();

        // OFFSET is required even if 0
        var offsetValue = offset ?? 0;
        sb.Append($"OFFSET {offsetValue} ROWS");

        if (limit.HasValue)
        {
            sb.Append($" FETCH NEXT {limit.Value} ROWS ONLY");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats pagination when limit/offset values may be any combination of literals and parameters.
    /// Each of limit/offset is resolved from either a literal value or a parameter index.
    /// </summary>
    internal static string FormatMixedPagination(
        SqlDialect dialect,
        int? literalLimit, int? limitParamIndex,
        int? literalOffset, int? offsetParamIndex)
    {
        // Resolve each to its string representation
        string? limitStr = literalLimit.HasValue
            ? literalLimit.Value.ToString()
            : limitParamIndex.HasValue
                ? FormatParameter(dialect, limitParamIndex.Value)
                : null;

        string? offsetStr = literalOffset.HasValue
            ? literalOffset.Value.ToString()
            : offsetParamIndex.HasValue
                ? FormatParameter(dialect, offsetParamIndex.Value)
                : null;

        if (limitStr == null && offsetStr == null)
            return string.Empty;

        if (dialect == SqlDialect.SqlServer)
        {
            var sb = new StringBuilder();
            sb.Append("OFFSET ");
            sb.Append(offsetStr ?? "0");
            sb.Append(" ROWS");
            if (limitStr != null)
            {
                sb.Append(" FETCH NEXT ");
                sb.Append(limitStr);
                sb.Append(" ROWS ONLY");
            }
            return sb.ToString();
        }

        {
            var sb = new StringBuilder();
            if (limitStr != null)
            {
                sb.Append("LIMIT ");
                sb.Append(limitStr);
            }
            if (offsetStr != null)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("OFFSET ");
                sb.Append(offsetStr);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Formats a CLR value as a dialect-specific SQL literal for embedding in migration SQL.
    /// </summary>
    public static string FormatLiteral(SqlDialect dialect, object? value)
    {
        if (value is null || value is DBNull)
            return "NULL";

        return value switch
        {
            bool b => FormatBoolean(dialect, b),
            byte n => n.ToString(),
            short n => n.ToString(),
            int n => n.ToString(),
            long n => n.ToString(),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => $"'{EscapeStringLiteral(s)}'",
            DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}'",
            DateTimeOffset dto => $"'{dto.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture)}'",
            Guid g => $"'{g}'",
            byte[] bytes => FormatBinaryLiteral(dialect, bytes),
            Enum e => Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture).ToString(),
            _ => $"'{EscapeStringLiteral(value.ToString()!)}'"
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string EscapeStringLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Resolves <c>{identifier}</c> placeholders in a canonical SQL expression to
    /// dialect-quoted identifiers.  Placeholders are produced by the projection
    /// analyzer during discovery; quoting is deferred to render time so that the
    /// expression is dialect-agnostic until final SQL assembly.
    /// </summary>
    public static string? QuoteSqlExpression(string? sqlExpression, SqlDialect dialect)
    {
        if (sqlExpression == null)
            return null;

        // Fast path: no placeholders to resolve.
        if (sqlExpression.IndexOf('{') < 0)
            return sqlExpression;

        var sb = new StringBuilder(sqlExpression.Length + 8);
        int i = 0;
        while (i < sqlExpression.Length)
        {
            if (sqlExpression[i] == '{')
            {
                int close = sqlExpression.IndexOf('}', i + 1);
                if (close > i + 1)
                {
                    var identifier = sqlExpression.Substring(i + 1, close - i - 1);
                    if (identifier.Length > 1 && identifier[0] == '@'
                        && int.TryParse(identifier.Substring(1), out var paramIdx))
                    {
                        // {@N} → dialect-specific parameter placeholder
                        sb.Append(FormatParameter(dialect, paramIdx));
                    }
                    else
                    {
                        sb.Append(QuoteIdentifier(dialect, identifier));
                    }
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(sqlExpression[i]);
            i++;
        }

        return sb.ToString();
    }

    private static string FormatBinaryLiteral(SqlDialect dialect, byte[] bytes)
    {
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            hex.Append(b.ToString("X2"));

        return dialect switch
        {
            SqlDialect.PostgreSQL => $"'\\x{hex}'",
            _ => $"X'{hex}'" // SQLite, MySQL, SqlServer
        };
    }
}
