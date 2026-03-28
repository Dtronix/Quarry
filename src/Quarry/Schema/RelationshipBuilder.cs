using Quarry.Migration;

namespace Quarry;

/// <summary>
/// Fluent builder for configuring one-to-many relationships.
/// Implicitly converts to Many&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The type of the related entities.</typeparam>
public readonly struct RelationshipBuilder<T> where T : Schema
{
    /// <summary>
    /// Configures the delete behavior for this relationship.
    /// </summary>
    public RelationshipBuilder<T> OnDelete(ForeignKeyAction action) => this;

    /// <summary>
    /// Configures the update behavior for this relationship.
    /// </summary>
    public RelationshipBuilder<T> OnUpdate(ForeignKeyAction action) => this;

    /// <summary>
    /// Maps this relationship to a specific navigation property name.
    /// </summary>
    public RelationshipBuilder<T> MapTo(string propertyName) => this;
}
