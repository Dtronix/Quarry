#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql;

internal enum SqlDialect
#else
namespace Quarry;

/// <summary>
/// Specifies the target SQL database dialect for code generation and query building.
/// </summary>
public enum SqlDialect
#endif
{
    /// <summary>SQLite dialect.</summary>
    SQLite = 0,
    /// <summary>PostgreSQL dialect.</summary>
    PostgreSQL = 1,
    /// <summary>MySQL dialect.</summary>
    MySQL = 2,
    /// <summary>SQL Server dialect.</summary>
    SqlServer = 3
}
