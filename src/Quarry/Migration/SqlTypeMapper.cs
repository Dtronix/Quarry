using System.Diagnostics;

namespace Quarry.Migration;

/// <summary>
/// Maps CLR type names to SQL type names per dialect.
/// </summary>
internal static class SqlTypeMapper
{
    public static string MapClrType(string clrType, SqlDialect dialect, int? length = null, int? precision = null, int? scale = null)
    {
        var normalized = NormalizeType(clrType);
        if (!IsKnownType(normalized))
            Debug.WriteLine($"[Quarry.Migration] Unrecognized CLR type '{clrType}' for {dialect}; using dialect default type.");

        return dialect switch
        {
            SqlDialect.SQLite => MapSqlite(normalized),
            SqlDialect.PostgreSQL => MapPostgres(normalized, length, precision, scale),
            SqlDialect.MySQL => MapMySql(normalized, length, precision, scale),
            SqlDialect.SqlServer => MapSqlServer(normalized, length, precision, scale),
            _ => "TEXT"
        };
    }

    private static bool IsKnownType(string normalizedType)
    {
        return normalizedType is "int" or "long" or "short" or "byte" or "bool"
            or "string" or "decimal" or "float" or "double"
            or "DateTime" or "DateTimeOffset" or "Guid" or "byte[]" or "TimeSpan";
    }

    private static string MapSqlite(string clrType)
    {
        switch (clrType)
        {
            case "int":
            case "long":
            case "short":
            case "byte":
            case "bool":
                return "INTEGER";
            case "decimal":
            case "float":
            case "double":
                return "REAL";
            case "byte[]":
                return "BLOB";
            default:
                return "TEXT";
        }
    }

    private static string MapPostgres(string clrType, int? length, int? precision, int? scale)
    {
        switch (clrType)
        {
            case "int": return "integer";
            case "long": return "bigint";
            case "short": return "smallint";
            case "byte": return "smallint";
            case "bool": return "boolean";
            case "string":
                return length.HasValue ? $"varchar({length.Value})" : "text";
            case "decimal":
                return precision.HasValue ? $"numeric({precision.Value},{scale ?? 0})" : "numeric";
            case "float": return "real";
            case "double": return "double precision";
            case "DateTime": return "timestamp";
            case "DateTimeOffset": return "timestamptz";
            case "Guid": return "uuid";
            case "byte[]": return "bytea";
            case "TimeSpan": return "interval";
            default: return "text";
        }
    }

    private static string MapMySql(string clrType, int? length, int? precision, int? scale)
    {
        switch (clrType)
        {
            case "int": return "INT";
            case "long": return "BIGINT";
            case "short": return "SMALLINT";
            case "byte": return "TINYINT";
            case "bool": return "TINYINT(1)";
            case "string":
                return length.HasValue ? $"VARCHAR({length.Value})" : "TEXT";
            case "decimal":
                return precision.HasValue ? $"DECIMAL({precision.Value},{scale ?? 0})" : "DECIMAL";
            case "float": return "FLOAT";
            case "double": return "DOUBLE";
            case "DateTime": return "DATETIME";
            case "DateTimeOffset": return "DATETIME";
            case "Guid": return "CHAR(36)";
            case "byte[]": return "LONGBLOB";
            case "TimeSpan": return "TIME";
            default: return "TEXT";
        }
    }

    private static string MapSqlServer(string clrType, int? length, int? precision, int? scale)
    {
        switch (clrType)
        {
            case "int": return "INT";
            case "long": return "BIGINT";
            case "short": return "SMALLINT";
            case "byte": return "TINYINT";
            case "bool": return "BIT";
            case "string":
                return length.HasValue ? $"NVARCHAR({length.Value})" : "NVARCHAR(MAX)";
            case "decimal":
                return precision.HasValue ? $"DECIMAL({precision.Value},{scale ?? 0})" : "DECIMAL";
            case "float": return "REAL";
            case "double": return "FLOAT";
            case "DateTime": return "DATETIME2";
            case "DateTimeOffset": return "DATETIMEOFFSET";
            case "Guid": return "UNIQUEIDENTIFIER";
            case "byte[]": return "VARBINARY(MAX)";
            case "TimeSpan": return "TIME";
            default: return "NVARCHAR(MAX)";
        }
    }

    private static string NormalizeType(string clrType)
    {
        // Strip System. prefix and nullable
        var type = clrType;
        if (type.EndsWith("?"))
            type = type.Substring(0, type.Length - 1);
        if (type.StartsWith("System."))
            type = type.Substring(7);

        // Map common aliases
        switch (type)
        {
            case "Int32": return "int";
            case "Int64": return "long";
            case "Int16": return "short";
            case "Byte": return "byte";
            case "Boolean": return "bool";
            case "String": return "string";
            case "Decimal": return "decimal";
            case "Single": return "float";
            case "Double": return "double";
            case "Byte[]": return "byte[]";
            default: return type;
        }
    }
}
