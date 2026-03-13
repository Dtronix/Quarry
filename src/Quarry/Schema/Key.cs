namespace Quarry;

/// <summary>
/// Represents a primary key column in a schema definition.
/// </summary>
/// <typeparam name="T">The CLR type of the key value.</typeparam>
public readonly struct Key<T> : IColumnMarker
{
    /// <summary>
    /// Implicitly converts a ColumnBuilder to a Key.
    /// </summary>
    public static implicit operator Key<T>(ColumnBuilder<T> builder) => default;

    /// <summary>
    /// Specifies descending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Desc() => default;

    /// <summary>
    /// Specifies ascending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Asc() => default;
}
