using System;
using System.Text.RegularExpressions;

namespace Quarry.Shared.Scaffold;

internal static partial class ReverseTypeMapper
{
    [GeneratedRegex(@"^(.+?)\s*\(\s*(MAX|\-?\d+)\s*(?:,\s*(\d+)\s*)?\)$", RegexOptions.Compiled)]
    private static partial Regex TypeComponentsRegex();
    public static ReverseTypeResult MapSqlType(
        string sqlType,
        string dialect,
        string columnName,
        bool isNullable,
        bool isIdentity,
        bool isPrimaryKey)
    {
        var normalized = sqlType.Trim();

        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => MapSqlite(normalized, columnName, isNullable),
            "postgresql" or "postgres" => MapPostgreSql(normalized, isNullable),
            "mysql" => MapMySql(normalized, columnName, isNullable, isPrimaryKey),
            "sqlserver" or "mssql" => MapSqlServer(normalized, isNullable),
            _ => new ReverseTypeResult("string", isNullable, warning: $"Unknown dialect '{dialect}'")
        };
    }

    private static ReverseTypeResult MapSqlite(string sqlType, string columnName, bool isNullable)
    {
        var upper = sqlType.ToUpperInvariant();

        if (upper.StartsWith("INT") || upper == "INTEGER")
        {
            if (IsBooleanColumnName(columnName))
                return new ReverseTypeResult("bool", isNullable);
            return new ReverseTypeResult("int", isNullable);
        }

        if (upper.StartsWith("REAL") || upper.StartsWith("FLOAT") || upper.StartsWith("DOUBLE") || upper == "NUMERIC")
            return new ReverseTypeResult("double", isNullable);

        if (upper == "BLOB")
            return new ReverseTypeResult("byte[]", isNullable);

        // TEXT and everything else
        return new ReverseTypeResult("string", isNullable);
    }

    private static ReverseTypeResult MapPostgreSql(string sqlType, bool isNullable)
    {
        var (baseType, length, precision, scale) = ParseTypeComponents(sqlType.ToUpperInvariant());

        return baseType switch
        {
            "INTEGER" or "INT" or "INT4" => new ReverseTypeResult("int", isNullable),
            "BIGINT" or "INT8" => new ReverseTypeResult("long", isNullable),
            "SMALLINT" or "INT2" => new ReverseTypeResult("short", isNullable),
            "BOOLEAN" or "BOOL" => new ReverseTypeResult("bool", isNullable),
            "VARCHAR" or "CHARACTER VARYING" => new ReverseTypeResult("string", isNullable, maxLength: length),
            "TEXT" => new ReverseTypeResult("string", isNullable),
            "CHAR" or "CHARACTER" => new ReverseTypeResult("string", isNullable, maxLength: length),
            "NUMERIC" or "DECIMAL" => new ReverseTypeResult("decimal", isNullable, precision: precision ?? length, scale: scale),
            "REAL" or "FLOAT4" => new ReverseTypeResult("float", isNullable),
            "DOUBLE PRECISION" or "FLOAT8" => new ReverseTypeResult("double", isNullable),
            "FLOAT" => new ReverseTypeResult("double", isNullable),
            "TIMESTAMP" or "TIMESTAMP WITHOUT TIME ZONE" => new ReverseTypeResult("DateTime", isNullable),
            "TIMESTAMPTZ" or "TIMESTAMP WITH TIME ZONE" => new ReverseTypeResult("DateTimeOffset", isNullable),
            "UUID" => new ReverseTypeResult("Guid", isNullable),
            "BYTEA" => new ReverseTypeResult("byte[]", isNullable),
            "INTERVAL" => new ReverseTypeResult("TimeSpan", isNullable),
            "SERIAL" => new ReverseTypeResult("int", false),
            "BIGSERIAL" => new ReverseTypeResult("long", false),
            "SMALLSERIAL" => new ReverseTypeResult("short", false),
            "DATE" => new ReverseTypeResult("DateTime", isNullable),
            "TIME" or "TIME WITHOUT TIME ZONE" => new ReverseTypeResult("TimeSpan", isNullable),
            "TIMETZ" or "TIME WITH TIME ZONE" => new ReverseTypeResult("TimeSpan", isNullable),
            "JSONB" or "JSON" => new ReverseTypeResult("string", isNullable, warning: $"Unmapped PostgreSQL type '{sqlType}' -- mapped as string"),
            "XML" => new ReverseTypeResult("string", isNullable, warning: $"Unmapped PostgreSQL type '{sqlType}' -- mapped as string"),
            _ => new ReverseTypeResult("string", isNullable, warning: $"Unmapped PostgreSQL type '{sqlType}' -- mapped as string")
        };
    }

    private static ReverseTypeResult MapMySql(string sqlType, string columnName, bool isNullable, bool isPrimaryKey)
    {
        var (baseType, length, precision, scale) = ParseTypeComponents(sqlType.ToUpperInvariant());

        // Special case: TINYINT(1) is bool in MySQL convention
        if (baseType == "TINYINT" && length == 1)
            return new ReverseTypeResult("bool", isNullable);

        // Special case: CHAR(36) might be Guid
        if (baseType == "CHAR" && length == 36 && isPrimaryKey)
            return new ReverseTypeResult("Guid", isNullable);

        return baseType switch
        {
            "INT" or "INTEGER" => new ReverseTypeResult("int", isNullable),
            "BIGINT" => new ReverseTypeResult("long", isNullable),
            "SMALLINT" => new ReverseTypeResult("short", isNullable),
            "TINYINT" => new ReverseTypeResult("byte", isNullable),
            "MEDIUMINT" => new ReverseTypeResult("int", isNullable),
            "BIT" when length == 1 => new ReverseTypeResult("bool", isNullable),
            "BIT" => new ReverseTypeResult("long", isNullable),
            "FLOAT" => new ReverseTypeResult("float", isNullable),
            "DOUBLE" or "DOUBLE PRECISION" => new ReverseTypeResult("double", isNullable),
            "DECIMAL" or "NUMERIC" or "DEC" => new ReverseTypeResult("decimal", isNullable, precision: precision ?? length, scale: scale),
            "VARCHAR" => new ReverseTypeResult("string", isNullable, maxLength: length),
            "CHAR" => new ReverseTypeResult("string", isNullable, maxLength: length),
            "TEXT" or "MEDIUMTEXT" or "LONGTEXT" or "TINYTEXT" => new ReverseTypeResult("string", isNullable),
            "BLOB" or "MEDIUMBLOB" or "LONGBLOB" or "TINYBLOB" => new ReverseTypeResult("byte[]", isNullable),
            "VARBINARY" or "BINARY" => new ReverseTypeResult("byte[]", isNullable),
            "DATETIME" or "TIMESTAMP" => new ReverseTypeResult("DateTime", isNullable),
            "DATE" => new ReverseTypeResult("DateTime", isNullable),
            "TIME" => new ReverseTypeResult("TimeSpan", isNullable),
            "YEAR" => new ReverseTypeResult("short", isNullable),
            "ENUM" => new ReverseTypeResult("string", isNullable, warning: $"MySQL ENUM type '{sqlType}' -- mapped as string"),
            "SET" => new ReverseTypeResult("string", isNullable, warning: $"MySQL SET type '{sqlType}' -- mapped as string"),
            "JSON" => new ReverseTypeResult("string", isNullable, warning: $"MySQL JSON type -- mapped as string"),
            _ => new ReverseTypeResult("string", isNullable, warning: $"Unmapped MySQL type '{sqlType}' -- mapped as string")
        };
    }

    private static ReverseTypeResult MapSqlServer(string sqlType, bool isNullable)
    {
        var (baseType, length, precision, scale) = ParseTypeComponents(sqlType.ToUpperInvariant());

        return baseType switch
        {
            "INT" => new ReverseTypeResult("int", isNullable),
            "BIGINT" => new ReverseTypeResult("long", isNullable),
            "SMALLINT" => new ReverseTypeResult("short", isNullable),
            "TINYINT" => new ReverseTypeResult("byte", isNullable),
            "BIT" => new ReverseTypeResult("bool", isNullable),
            "FLOAT" => new ReverseTypeResult("double", isNullable),
            "REAL" => new ReverseTypeResult("float", isNullable),
            "DECIMAL" or "NUMERIC" => new ReverseTypeResult("decimal", isNullable, precision: precision ?? length, scale: scale),
            "MONEY" or "SMALLMONEY" => new ReverseTypeResult("decimal", isNullable),
            "NVARCHAR" => length == -1 ? new ReverseTypeResult("string", isNullable) : new ReverseTypeResult("string", isNullable, maxLength: length),
            "VARCHAR" => length == -1 ? new ReverseTypeResult("string", isNullable) : new ReverseTypeResult("string", isNullable, maxLength: length),
            "NCHAR" or "CHAR" => new ReverseTypeResult("string", isNullable, maxLength: length),
            "TEXT" or "NTEXT" => new ReverseTypeResult("string", isNullable),
            "DATETIME2" => new ReverseTypeResult("DateTime", isNullable),
            "DATETIME" => new ReverseTypeResult("DateTime", isNullable),
            "SMALLDATETIME" => new ReverseTypeResult("DateTime", isNullable),
            "DATE" => new ReverseTypeResult("DateTime", isNullable),
            "DATETIMEOFFSET" => new ReverseTypeResult("DateTimeOffset", isNullable),
            "TIME" => new ReverseTypeResult("TimeSpan", isNullable),
            "UNIQUEIDENTIFIER" => new ReverseTypeResult("Guid", isNullable),
            "VARBINARY" => new ReverseTypeResult("byte[]", isNullable),
            "BINARY" => new ReverseTypeResult("byte[]", isNullable),
            "IMAGE" => new ReverseTypeResult("byte[]", isNullable),
            "XML" => new ReverseTypeResult("string", isNullable, warning: "SQL Server XML type -- mapped as string"),
            "SQL_VARIANT" => new ReverseTypeResult("object", isNullable, warning: "SQL Server sql_variant type -- mapped as object"),
            _ => new ReverseTypeResult("string", isNullable, warning: $"Unmapped SQL Server type '{sqlType}' -- mapped as string")
        };
    }

    private static bool IsBooleanColumnName(string name)
    {
        return name.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Has", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Can", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Should", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Allow", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Enable", StringComparison.OrdinalIgnoreCase);
    }

    internal static (string BaseType, int? Length, int? Precision, int? Scale) ParseTypeComponents(string sqlType)
    {
        // Match types like VARCHAR(255), DECIMAL(10,2), NUMERIC(18), NVARCHAR(MAX)
        var match = TypeComponentsRegex().Match(sqlType);
        if (match.Success)
        {
            var baseType = match.Groups[1].Value.Trim();
            var firstStr = match.Groups[2].Value;
            int? first = firstStr.Equals("MAX", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(firstStr);
            int? second = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : null;

            if (second.HasValue)
                return (baseType, null, first, second);
            return (baseType, first, null, null);
        }

        return (sqlType.Trim(), null, null, null);
    }
}
