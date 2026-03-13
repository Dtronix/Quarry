namespace Quarry.Internal;

/// <summary>
/// Internal state container for modification operations (INSERT, UPDATE, DELETE).
/// Uses mutable pattern for ease of use with modification builders.
/// </summary>
internal sealed class ModificationState
{
    /// <summary>
    /// Gets the SQL dialect for this operation.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the table name for the operation.
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
    /// Gets the query timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets the parameters for this operation.
    /// </summary>
    public List<ModificationParameter> Parameters { get; } = new();

    /// <summary>
    /// Creates a new modification state with required initial values.
    /// </summary>
    public ModificationState(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
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

}

/// <summary>
/// Represents a modification parameter.
/// </summary>
public readonly struct ModificationParameter
{
    /// <summary>
    /// Gets the parameter index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the parameter value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Optional dialect-aware configurator for this parameter.
    /// When set, called after the DbParameter value is assigned to configure
    /// provider-specific properties (e.g., NpgsqlDbType).
    /// </summary>
    public IDialectAwareTypeMapping? DialectConfigurator { get; }

    /// <summary>
    /// Gets whether this parameter contains sensitive data and should be redacted in logs.
    /// </summary>
    public bool IsSensitive { get; }

    public ModificationParameter(int index, object? value, IDialectAwareTypeMapping? dialectConfigurator = null, bool isSensitive = false)
    {
        Index = index;
        Value = value;
        DialectConfigurator = dialectConfigurator;
        IsSensitive = isSensitive;
    }
}
