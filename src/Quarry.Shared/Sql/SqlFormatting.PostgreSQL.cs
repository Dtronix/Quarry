#if !QUARRY_GENERATOR
using Quarry;
#endif

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql;
#else
namespace Quarry.Shared.Sql;
#endif

internal static partial class SqlFormatting
{
    private static string GetPostgreSQLColumnType(string clrType, int? maxLength, int? precision, int? scale)
    {
        return clrType switch
        {
            "int" or "Int32" or "System.Int32" => "INTEGER",
            "long" or "Int64" or "System.Int64" => "BIGINT",
            "short" or "Int16" or "System.Int16" => "SMALLINT",
            "byte" or "Byte" or "System.Byte"
                or "sbyte" or "SByte" or "System.SByte" => "SMALLINT",
            "uint" or "UInt32" or "System.UInt32" => "BIGINT",
            "ulong" or "UInt64" or "System.UInt64" => "BIGINT",
            "ushort" or "UInt16" or "System.UInt16" => "INTEGER",

            "bool" or "Boolean" or "System.Boolean" => "BOOLEAN",

            "float" or "Single" or "System.Single" => "REAL",
            "double" or "Double" or "System.Double" => "DOUBLE PRECISION",

            "decimal" or "Decimal" or "System.Decimal" =>
                precision.HasValue ? $"NUMERIC({precision.Value},{scale ?? 0})" : "NUMERIC",

            "string" or "String" or "System.String" =>
                maxLength.HasValue ? $"VARCHAR({maxLength.Value})" : "TEXT",

            "byte[]" or "Byte[]" or "System.Byte[]" => "BYTEA",

            "Guid" or "System.Guid" => "UUID",
            "DateTime" or "System.DateTime" => "TIMESTAMP",
            "DateTimeOffset" or "System.DateTimeOffset" => "TIMESTAMPTZ",
            "TimeSpan" or "System.TimeSpan" => "INTERVAL",
            "DateOnly" or "System.DateOnly" => "DATE",
            "TimeOnly" or "System.TimeOnly" => "TIME",

            _ => "TEXT"
        };
    }
}
