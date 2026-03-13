namespace Quarry.Generators.Models;

/// <summary>
/// Represents an index defined in a schema.
/// </summary>
internal sealed class IndexInfo
{
    /// <summary>
    /// The index name (from the property name in the schema).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The columns in the index with their sort directions.
    /// </summary>
    public IReadOnlyList<IndexColumnInfo> Columns { get; }

    /// <summary>
    /// Whether this is a unique index.
    /// </summary>
    public bool IsUnique { get; }

    /// <summary>
    /// The index type (e.g., BTree, Hash). Null means default (BTree).
    /// </summary>
    public string? IndexType { get; }

    /// <summary>
    /// The filter expression for filtered indexes.
    /// For bool columns, contains the property name prefixed with "bool:".
    /// For raw SQL, contains the raw SQL string.
    /// </summary>
    public string? Filter { get; }

    /// <summary>
    /// Whether the filter is a bool column reference (vs raw SQL).
    /// </summary>
    public bool FilterIsBoolColumn { get; }

    /// <summary>
    /// The include columns (covering index).
    /// </summary>
    public IReadOnlyList<string> IncludeColumns { get; }

    public IndexInfo(
        string name,
        IReadOnlyList<IndexColumnInfo> columns,
        bool isUnique = false,
        string? indexType = null,
        string? filter = null,
        bool filterIsBoolColumn = false,
        IReadOnlyList<string>? includeColumns = null)
    {
        Name = name;
        Columns = columns;
        IsUnique = isUnique;
        IndexType = indexType;
        Filter = filter;
        FilterIsBoolColumn = filterIsBoolColumn;
        IncludeColumns = includeColumns ?? Array.Empty<string>();
    }
}

/// <summary>
/// Represents a column in an index with its sort direction.
/// </summary>
internal sealed class IndexColumnInfo
{
    /// <summary>
    /// The property name of the column.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The sort direction (Ascending or Descending).
    /// </summary>
    public SortDirection Direction { get; }

    public IndexColumnInfo(string propertyName, SortDirection direction = SortDirection.Ascending)
    {
        PropertyName = propertyName;
        Direction = direction;
    }
}

/// <summary>
/// Sort direction for index columns.
/// </summary>
internal enum SortDirection
{
    Ascending,
    Descending
}
