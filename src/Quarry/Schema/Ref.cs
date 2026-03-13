namespace Quarry;

/// <summary>
/// Represents a foreign key reference in schema definitions.
/// Used in schema classes to declare FK columns referencing other schema types.
/// </summary>
/// <typeparam name="TEntity">The schema type of the referenced entity (must derive from Schema).</typeparam>
/// <typeparam name="TKey">The type of the foreign key value.</typeparam>
public readonly struct Ref<TEntity, TKey> : IColumnMarker where TEntity : Schema
{
    /// <summary>
    /// Gets the foreign key value.
    /// </summary>
    public TKey Id { get; init; }

    /// <summary>
    /// Gets the navigation property to the referenced entity.
    /// This is null when the related entity has not been fetched via a join.
    /// </summary>
    public TEntity? Value { get; init; }

    /// <summary>
    /// Creates a new Ref with the specified key value.
    /// </summary>
    public Ref(TKey id)
    {
        Id = id;
        Value = default;
    }

    /// <summary>
    /// Creates a new Ref with the specified key value and navigation value.
    /// </summary>
    public Ref(TKey id, TEntity? value)
    {
        Id = id;
        Value = value;
    }

    /// <summary>
    /// Implicitly converts a RefBuilder to a Ref.
    /// </summary>
    public static implicit operator Ref<TEntity, TKey>(RefBuilder<TEntity, TKey> builder) => default;

    /// <summary>
    /// Specifies descending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Desc() => default;

    /// <summary>
    /// Specifies ascending sort direction for this column in an index.
    /// </summary>
    public IndexedColumn Asc() => default;
}
