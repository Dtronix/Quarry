using System.Collections.Generic;

namespace Quarry;

/// <summary>
/// Contains compile-time analysis results, SQL output, bound parameters,
/// and optimization metadata for a query chain.
/// </summary>
public sealed class QueryDiagnostics
{
    internal QueryDiagnostics(
        string sql,
        IReadOnlyList<DiagnosticParameter> parameters,
        DiagnosticQueryKind kind,
        SqlDialect dialect,
        string tableName,
        DiagnosticOptimizationTier tier = DiagnosticOptimizationTier.RuntimeBuild,
        bool isCarrierOptimized = false,
        IReadOnlyList<ClauseDiagnostic>? clauses = null,
        object? rawState = null,
        int insertRowCount = 0)
    {
        Sql = sql;
        Parameters = parameters;
        Kind = kind;
        Dialect = dialect;
        TableName = tableName;
        Tier = tier;
        IsCarrierOptimized = isCarrierOptimized;
        Clauses = clauses ?? [];
        RawState = rawState;
        InsertRowCount = insertRowCount;
    }

    /// <summary>
    /// Gets the generated SQL string.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the bound parameters for this query.
    /// </summary>
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }

    /// <summary>
    /// Gets the optimization tier applied to this query chain.
    /// </summary>
    public DiagnosticOptimizationTier Tier { get; }

    /// <summary>
    /// Gets whether this chain was optimized using a generated carrier class.
    /// </summary>
    public bool IsCarrierOptimized { get; }

    /// <summary>
    /// Gets the kind of query (Select, Delete, Update, Insert).
    /// </summary>
    public DiagnosticQueryKind Kind { get; }

    /// <summary>
    /// Gets the SQL dialect used for this query.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the primary table name for this query.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the clause breakdown for this query.
    /// </summary>
    public IReadOnlyList<ClauseDiagnostic> Clauses { get; }

    /// <summary>
    /// Internal raw state object for compile-time verification in tests.
    /// </summary>
    internal object? RawState { get; }

    /// <summary>
    /// Internal row count for insert compile-time verification in tests.
    /// </summary>
    internal int InsertRowCount { get; }
}

/// <summary>
/// Represents a bound parameter in a diagnostic query result.
/// </summary>
public sealed class DiagnosticParameter
{
    /// <summary>
    /// Creates a new diagnostic parameter.
    /// </summary>
    public DiagnosticParameter(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Gets the parameter placeholder name (e.g., "@p0").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parameter value.
    /// </summary>
    public object? Value { get; }
}

/// <summary>
/// Describes a single clause in a diagnostic query result.
/// </summary>
public sealed class ClauseDiagnostic
{
    /// <summary>
    /// Creates a new clause diagnostic.
    /// </summary>
    public ClauseDiagnostic(string clauseType, string sqlFragment, bool isConditional = false, bool isActive = true)
    {
        ClauseType = clauseType;
        SqlFragment = sqlFragment;
        IsConditional = isConditional;
        IsActive = isActive;
    }

    /// <summary>
    /// Gets the clause type (e.g., "Where", "OrderBy", "Select", "Limit").
    /// </summary>
    public string ClauseType { get; }

    /// <summary>
    /// Gets the translated SQL fragment for this clause.
    /// </summary>
    public string SqlFragment { get; }

    /// <summary>
    /// Gets whether this clause is conditionally applied.
    /// </summary>
    public bool IsConditional { get; }

    /// <summary>
    /// Gets whether this clause is active (included in the current query variant).
    /// Non-conditional clauses are always active. For conditional clauses, reflects the runtime clause mask bit.
    /// </summary>
    public bool IsActive { get; }
}

/// <summary>
/// The optimization tier applied to a query chain at compile time.
/// </summary>
public enum DiagnosticOptimizationTier
{
    /// <summary>
    /// SQL is built at runtime by the query builder (no compile-time optimization).
    /// </summary>
    RuntimeBuild,

    /// <summary>
    /// SQL is pre-built at compile time with a dispatch table for conditional clauses.
    /// </summary>
    PrebuiltDispatch
}

/// <summary>
/// The kind of query for diagnostic purposes.
/// </summary>
public enum DiagnosticQueryKind
{
    /// <summary>SELECT query.</summary>
    Select,
    /// <summary>DELETE query.</summary>
    Delete,
    /// <summary>UPDATE query.</summary>
    Update,
    /// <summary>INSERT query.</summary>
    Insert
}
