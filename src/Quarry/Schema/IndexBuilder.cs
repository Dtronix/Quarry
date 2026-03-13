namespace Quarry;

/// <summary>
/// Fluent builder for configuring index definitions.
/// Implicitly converts to <see cref="Index"/>.
/// </summary>
public readonly struct IndexBuilder
{
    /// <summary>
    /// Marks this index as a unique index.
    /// </summary>
    public IndexBuilder Unique() => default;

    /// <summary>
    /// Adds a filter predicate using a boolean column.
    /// Emits WHERE "col" = TRUE/1 per dialect.
    /// </summary>
    /// <param name="boolColumn">A Col&lt;bool&gt; column reference.</param>
    public IndexBuilder Where(IColumnMarker boolColumn) => default;

    /// <summary>
    /// Adds a raw SQL filter predicate (escape hatch for complex expressions).
    /// </summary>
    /// <param name="rawSql">The raw SQL filter expression.</param>
    public IndexBuilder Where(string rawSql) => default;

    /// <summary>
    /// Specifies columns to include in the index leaf pages (covering index).
    /// Supported by SQL Server and PostgreSQL.
    /// </summary>
    /// <param name="columns">The columns to include.</param>
    public IndexBuilder Include(params IColumnMarker[] columns) => default;

    /// <summary>
    /// Specifies the index type (e.g., Hash, Gin).
    /// </summary>
    /// <param name="indexType">The index type to use.</param>
    public IndexBuilder Using(IndexType indexType) => default;
}
