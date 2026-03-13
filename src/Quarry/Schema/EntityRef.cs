namespace Quarry;

/// <summary>
/// Represents a foreign key reference with navigation property support in generated entity classes.
/// This is the runtime type used in generated entities for FK columns.
/// </summary>
/// <typeparam name="TEntity">The type of the referenced entity.</typeparam>
/// <typeparam name="TKey">The type of the foreign key value.</typeparam>
public readonly struct EntityRef<TEntity, TKey> where TEntity : class
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
    /// Creates a new EntityRef with the specified key value.
    /// </summary>
    public EntityRef(TKey id)
    {
        Id = id;
        Value = default;
    }

    /// <summary>
    /// Creates a new EntityRef with the specified key value and navigation value.
    /// </summary>
    public EntityRef(TKey id, TEntity? value)
    {
        Id = id;
        Value = value;
    }

    /// <summary>
    /// Implicitly converts a key value to an EntityRef (for setting FK values).
    /// </summary>
    public static implicit operator EntityRef<TEntity, TKey>(TKey id) => new(id);
}
