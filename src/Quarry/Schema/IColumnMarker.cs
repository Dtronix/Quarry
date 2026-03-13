namespace Quarry;

/// <summary>
/// Marker interface implemented by column types (Col&lt;T&gt;, Key&lt;T&gt;, Ref&lt;TEntity, TKey&gt;)
/// to enable direct property references in index definitions.
/// </summary>
public interface IColumnMarker
{
    /// <summary>
    /// Specifies descending sort direction for this column in an index.
    /// </summary>
    IndexedColumn Desc() => default;

    /// <summary>
    /// Specifies ascending sort direction for this column in an index.
    /// </summary>
    IndexedColumn Asc() => default;
}
