namespace Quarry.Internal;

/// <summary>
/// Internal state container for DELETE operations.
/// Uses mutable pattern for ease of use with DeleteBuilder.
/// </summary>
public sealed class DeleteState
{
    /// <summary>
    /// Gets the SQL dialect for this operation.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the table name for the DELETE.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the schema name for qualified table reference.
    /// </summary>
    public string? SchemaName { get; }

    /// <summary>
    /// Gets the query execution context.
    /// </summary>
    public IQueryExecutionContext? ExecutionContext { get; }

    /// <summary>
    /// Gets or sets the query timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets the WHERE clause conditions.
    /// </summary>
    public List<string> WhereConditions { get; } = new();

    /// <summary>
    /// Gets the parameters for this operation.
    /// </summary>
    public List<ModificationParameter> Parameters { get; } = new();

    /// <summary>
    /// Gets or sets whether All() was called to allow deleting all rows.
    /// </summary>
    public bool AllowAll { get; set; }

    /// <summary>
    /// Gets the clause bitmask for conditional clause tracking.
    /// Each bit corresponds to a conditionally-applied clause interceptor.
    /// Used by execution interceptors to select the correct pre-built SQL variant.
    /// </summary>
    public ulong ClauseMask { get; private set; }

    /// <summary>
    /// Sets the specified bit on the ClauseMask.
    /// </summary>
    public void SetClauseBit(int bit)
    {
        ClauseMask |= 1UL << bit;
    }

    /// <summary>
    /// Creates a new delete state with required initial values.
    /// </summary>
    public DeleteState(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        Dialect = dialect;
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        SchemaName = schemaName;
        ExecutionContext = executionContext;
    }

    /// <summary>
    /// Adds a parameter and returns its index.
    /// </summary>
    public int AddParameter<TValue>(TValue value, bool isSensitive = false)
    {
        var index = Parameters.Count;
        Parameters.Add(new ModificationParameter(index, value, isSensitive: isSensitive));
        return index;
    }

    /// <summary>
    /// Adds a parameter (boxed) and returns its index.
    /// Use the generic overload when possible to avoid boxing.
    /// </summary>
    public int AddParameterBoxed(object? value, bool isSensitive = false)
    {
        var index = Parameters.Count;
        Parameters.Add(new ModificationParameter(index, value, isSensitive: isSensitive));
        return index;
    }

    /// <summary>
    /// Gets the next parameter index.
    /// </summary>
    public int NextParameterIndex => Parameters.Count;

    /// <summary>
    /// Gets whether any WHERE conditions have been added.
    /// </summary>
    public bool HasWhereConditions => WhereConditions.Count > 0;

    /// <summary>
    /// Gets whether the delete is safe to execute (has WHERE or All was called).
    /// </summary>
    public bool IsSafeToExecute => HasWhereConditions || AllowAll;
}
