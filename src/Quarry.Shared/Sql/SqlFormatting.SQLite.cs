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
    private static string GetSQLiteColumnType(string clrType, int? maxLength, int? precision, int? scale)
    {
        // SQLite uses type affinity — simplified type names map to one of 5 affinities
        return clrType switch
        {
            "int" or "Int32" or "System.Int32"
                or "long" or "Int64" or "System.Int64"
                or "short" or "Int16" or "System.Int16"
                or "byte" or "Byte" or "System.Byte"
                or "sbyte" or "SByte" or "System.SByte"
                or "uint" or "UInt32" or "System.UInt32"
                or "ulong" or "UInt64" or "System.UInt64"
                or "ushort" or "UInt16" or "System.UInt16"
                or "bool" or "Boolean" or "System.Boolean" => "INTEGER",

            "float" or "Single" or "System.Single"
                or "double" or "Double" or "System.Double" => "REAL",

            "decimal" or "Decimal" or "System.Decimal" =>
                precision.HasValue ? $"NUMERIC({precision.Value},{scale ?? 0})" : "NUMERIC",

            "string" or "String" or "System.String" => "TEXT",

            "byte[]" or "Byte[]" or "System.Byte[]" => "BLOB",

            "Guid" or "System.Guid" => "TEXT",
            "DateTime" or "System.DateTime"
                or "DateTimeOffset" or "System.DateTimeOffset" => "TEXT",
            "TimeSpan" or "System.TimeSpan" => "TEXT",
            "DateOnly" or "System.DateOnly" => "TEXT",
            "TimeOnly" or "System.TimeOnly" => "TEXT",

            _ => "TEXT"
        };
    }
}
