namespace Quarry.Internal;

/// <summary>
/// Internal state container for INSERT operations.
/// Uses mutable pattern for ease of use with InsertBuilder.
/// </summary>
public sealed class InsertState
{
    /// <summary>
    /// Gets the SQL dialect for this operation.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the table name for the INSERT.
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
    /// Gets the column names to insert.
    /// </summary>
    public List<string> Columns { get; } = new();

    /// <summary>
    /// Gets the rows to insert. Each row is a list of parameter indices.
    /// </summary>
    public List<List<int>> Rows { get; } = new();

    /// <summary>
    /// Gets the parameters for this operation.
    /// </summary>
    public List<ModificationParameter> Parameters { get; } = new();

    /// <summary>
    /// Gets or sets the identity column name for RETURNING clause.
    /// </summary>
    public string? IdentityColumn { get; set; }

    /// <summary>
    /// Creates a new insert state with required initial values.
    /// </summary>
    public InsertState(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
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
    /// Gets whether any rows have been added.
    /// </summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// Gets whether columns have been defined.
    /// </summary>
    public bool HasColumns => Columns.Count > 0;
}
