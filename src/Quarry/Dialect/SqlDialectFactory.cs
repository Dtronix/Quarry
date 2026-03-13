namespace Quarry;

/// <summary>
/// Factory for obtaining SQL dialect values.
/// </summary>
public static class SqlDialectFactory
{
    /// <summary>
    /// Gets the SQL dialect for the specified dialect type.
    /// </summary>
    public static SqlDialect GetDialect(SqlDialect dialect) => dialect;

    /// <summary>
    /// Gets the SQLite dialect.
    /// </summary>
    public static SqlDialect SQLite => SqlDialect.SQLite;

    /// <summary>
    /// Gets the PostgreSQL dialect.
    /// </summary>
    public static SqlDialect PostgreSQL => SqlDialect.PostgreSQL;

    /// <summary>
    /// Gets the MySQL dialect.
    /// </summary>
    public static SqlDialect MySQL => SqlDialect.MySQL;

    /// <summary>
    /// Gets the SQL Server dialect.
    /// </summary>
    public static SqlDialect SqlServer => SqlDialect.SqlServer;
}
