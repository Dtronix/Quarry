namespace Quarry;

/// <summary>
/// Represents a standard database column in a schema definition.
/// </summary>
/// <typeparam name="T">The CLR type of the column value.</typeparam>
public readonly struct Col<T> : IColumnMarker
{
    /// <summary>
    /// Implicitly converts a ColumnBuilder to a Col.
    /// </summary>
    public static implicit operator Col<T>(ColumnBuilder<T> builder) => default;

    /// <summary>
    /// Specifies descending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Desc() => default;

    /// <summary>
    /// Specifies ascending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Asc() => default;
}
