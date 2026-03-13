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
    /// Formats pagination with parameterized limit/offset values (compile-time path).
    /// </summary>
    public static string FormatParameterizedPagination(
        SqlDialect dialect,
        int? limitParamIndex,
        int? offsetParamIndex)
    {
        if (limitParamIndex == null && offsetParamIndex == null)
            return string.Empty;

        if (dialect == SqlDialect.SqlServer)
            return FormatSqlServerParameterizedPagination(dialect, limitParamIndex, offsetParamIndex);

        return FormatLimitOffsetParameterized(dialect, limitParamIndex, offsetParamIndex);
    }

    internal static string FormatLimitOffsetParameterized(
        SqlDialect dialect,
        int? limitParamIndex,
        int? offsetParamIndex)
    {
        var sb = new StringBuilder();

        if (limitParamIndex.HasValue)
        {
            sb.Append("LIMIT ");
            sb.Append(FormatParameter(dialect, limitParamIndex.Value));
        }

        if (offsetParamIndex.HasValue)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("OFFSET ");
            sb.Append(FormatParameter(dialect, offsetParamIndex.Value));
        }

        return sb.ToString();
    }

    internal static string FormatSqlServerParameterizedPagination(
        SqlDialect dialect,
        int? limitParamIndex,
        int? offsetParamIndex)
    {
        var sb = new StringBuilder();

        // OFFSET is required for SQL Server even if 0
        sb.Append("OFFSET ");
        if (offsetParamIndex.HasValue)
        {
            sb.Append(FormatParameter(dialect, offsetParamIndex.Value));
        }
        else
        {
            sb.Append('0');
        }
        sb.Append(" ROWS");

        if (limitParamIndex.HasValue)
        {
            sb.Append(" FETCH NEXT ");
            sb.Append(FormatParameter(dialect, limitParamIndex.Value));
            sb.Append(" ROWS ONLY");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the dialect-specific SQL type name for a CLR type.
    /// Dispatches to per-dialect partial file implementations.
    /// </summary>
    public static string GetColumnTypeName(SqlDialect dialect, string clrType, int? maxLength, int? precision, int? scale)
    {
        return dialect switch
        {
            SqlDialect.SQLite => GetSQLiteColumnType(clrType, maxLength, precision, scale),
            SqlDialect.PostgreSQL => GetPostgreSQLColumnType(clrType, maxLength, precision, scale),
            SqlDialect.MySQL => GetMySQLColumnType(clrType, maxLength, precision, scale),
            SqlDialect.SqlServer => GetSqlServerColumnType(clrType, maxLength, precision, scale),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect))
        };
    }
}
