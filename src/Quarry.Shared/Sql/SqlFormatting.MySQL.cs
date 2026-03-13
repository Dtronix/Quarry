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
    private static string GetMySQLColumnType(string clrType, int? maxLength, int? precision, int? scale)
    {
        return clrType switch
        {
            "int" or "Int32" or "System.Int32" => "INT",
            "long" or "Int64" or "System.Int64" => "BIGINT",
            "short" or "Int16" or "System.Int16" => "SMALLINT",
            "byte" or "Byte" or "System.Byte" => "TINYINT UNSIGNED",
            "sbyte" or "SByte" or "System.SByte" => "TINYINT",
            "uint" or "UInt32" or "System.UInt32" => "INT UNSIGNED",
            "ulong" or "UInt64" or "System.UInt64" => "BIGINT UNSIGNED",
            "ushort" or "UInt16" or "System.UInt16" => "SMALLINT UNSIGNED",

            "bool" or "Boolean" or "System.Boolean" => "TINYINT(1)",

            "float" or "Single" or "System.Single" => "FLOAT",
            "double" or "Double" or "System.Double" => "DOUBLE",

            "decimal" or "Decimal" or "System.Decimal" =>
                precision.HasValue ? $"DECIMAL({precision.Value},{scale ?? 0})" : "DECIMAL(18,2)",

            "string" or "String" or "System.String" =>
                maxLength.HasValue ? $"VARCHAR({maxLength.Value})" : "VARCHAR(255)",

            "byte[]" or "Byte[]" or "System.Byte[]" =>
                maxLength.HasValue ? $"VARBINARY({maxLength.Value})" : "LONGBLOB",

            "Guid" or "System.Guid" => "CHAR(36)",
            "DateTime" or "System.DateTime" => "DATETIME",
            "DateTimeOffset" or "System.DateTimeOffset" => "DATETIME",
            "TimeSpan" or "System.TimeSpan" => "TIME",
            "DateOnly" or "System.DateOnly" => "DATE",
            "TimeOnly" or "System.TimeOnly" => "TIME",

            _ => "TEXT"
        };
    }
}
