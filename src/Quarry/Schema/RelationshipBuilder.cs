namespace Quarry;

/// <summary>
/// Fluent builder for configuring one-to-many relationships.
/// Implicitly converts to Many&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The type of the related entities.</typeparam>
public readonly struct RelationshipBuilder<T> where T : Schema
{
}
