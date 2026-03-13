namespace Quarry;

/// <summary>
/// Fluent builder for configuring foreign key column modifiers.
/// Implicitly converts to Ref&lt;TEntity, TKey&gt;.
/// </summary>
/// <typeparam name="TEntity">The type of the referenced entity.</typeparam>
/// <typeparam name="TKey">The type of the foreign key value.</typeparam>
public readonly struct RefBuilder<TEntity, TKey> where TEntity : Schema
{
    /// <summary>
    /// Marks this column as a foreign key constraint.
    /// </summary>
    public RefBuilder<TEntity, TKey> ForeignKey() => default;

    /// <summary>
    /// Maps this property to a different column name in the database.
    /// </summary>
    /// <param name="columnName">The database column name.</param>
    public RefBuilder<TEntity, TKey> MapTo(string columnName) => default;
}
