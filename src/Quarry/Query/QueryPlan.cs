namespace Quarry;

/// <summary>
/// Describes the optimization state of a query chain at the current point.
/// Useful for diagnostics and testing — shows the SQL and dialect without executing the query.
/// </summary>
public sealed class QueryPlan
{
    /// <summary>Gets the SQL that would be executed, or null if not yet available.</summary>
    public string? Sql { get; }

    /// <summary>Gets the SQL dialect.</summary>
    public SqlDialect Dialect { get; }

    public QueryPlan(string? sql, SqlDialect dialect)
    {
        Sql = sql;
        Dialect = dialect;
    }

    public override string ToString()
        => Sql ?? "(no SQL)";
}
