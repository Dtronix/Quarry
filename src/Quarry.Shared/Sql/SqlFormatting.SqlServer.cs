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
    private static string GetSqlServerColumnType(string clrType, int? maxLength, int? precision, int? scale)
    {
        return clrType switch
        {
            "int" or "Int32" or "System.Int32" => "INT",
            "long" or "Int64" or "System.Int64" => "BIGINT",
            "short" or "Int16" or "System.Int16" => "SMALLINT",
            "byte" or "Byte" or "System.Byte" => "TINYINT",
            "sbyte" or "SByte" or "System.SByte" => "SMALLINT",
            "uint" or "UInt32" or "System.UInt32" => "INT",
            "ulong" or "UInt64" or "System.UInt64" => "BIGINT",
            "ushort" or "UInt16" or "System.UInt16" => "SMALLINT",

            "bool" or "Boolean" or "System.Boolean" => "BIT",

            "float" or "Single" or "System.Single" => "REAL",
            "double" or "Double" or "System.Double" => "FLOAT",

            "decimal" or "Decimal" or "System.Decimal" =>
                precision.HasValue ? $"DECIMAL({precision.Value},{scale ?? 0})" : "DECIMAL(18,2)",

            "string" or "String" or "System.String" =>
                maxLength.HasValue ? $"NVARCHAR({maxLength.Value})" : "NVARCHAR(MAX)",

            "byte[]" or "Byte[]" or "System.Byte[]" =>
                maxLength.HasValue ? $"VARBINARY({maxLength.Value})" : "VARBINARY(MAX)",

            "Guid" or "System.Guid" => "UNIQUEIDENTIFIER",
            "DateTime" or "System.DateTime" => "DATETIME2",
            "DateTimeOffset" or "System.DateTimeOffset" => "DATETIMEOFFSET",
            "TimeSpan" or "System.TimeSpan" => "TIME",
            "DateOnly" or "System.DateOnly" => "DATE",
            "TimeOnly" or "System.TimeOnly" => "TIME",

            _ => "NVARCHAR(MAX)"
        };
    }
}
