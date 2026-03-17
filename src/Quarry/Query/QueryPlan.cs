namespace Quarry;

/// <summary>
/// Describes the optimization state of a query chain at the current point.
/// Useful for diagnostics and testing — shows the SQL, optimization tier,
/// and dialect without executing the query.
/// </summary>
public sealed class QueryPlan
{
    /// <summary>Gets the SQL that would be executed, or null if not yet available.</summary>
    public string? Sql { get; }

    /// <summary>Gets the optimization tier for this chain.</summary>
    public QueryPlanTier Tier { get; }

    /// <summary>Gets the SQL dialect.</summary>
    public SqlDialect Dialect { get; }

    public QueryPlan(string? sql, QueryPlanTier tier, SqlDialect dialect)
    {
        Sql = sql;
        Tier = tier;
        Dialect = dialect;
    }

    public override string ToString()
        => $"[{Tier}] {Sql ?? "(no SQL)"}";
}

/// <summary>
/// The optimization tier applied to a query chain.
/// </summary>
public enum QueryPlanTier
{
    /// <summary>No optimization — runtime SQL builder constructs the query.</summary>
    RuntimeBuild,

    /// <summary>Pre-quoted fragments assembled at runtime (tier 2).</summary>
    PrequotedFragments,

    /// <summary>Fully pre-built SQL dispatched by clause mask (tier 1).</summary>
    PrebuiltDispatch,

    /// <summary>Carrier-optimized pre-built dispatch (tier 1 + carrier).</summary>
    CarrierOptimized
}
