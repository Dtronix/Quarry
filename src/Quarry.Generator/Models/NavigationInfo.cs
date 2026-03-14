using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a one-to-many navigation property (Many&lt;T&gt;) defined in a schema.
/// </summary>
internal sealed class NavigationInfo : IEquatable<NavigationInfo>
{
    /// <summary>
    /// The property name in the schema (e.g., "Orders").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The related entity type name (e.g., "Order").
    /// </summary>
    public string RelatedEntityName { get; }

    /// <summary>
    /// The foreign key property name on the related entity (e.g., "UserId").
    /// </summary>
    public string ForeignKeyPropertyName { get; }

    public NavigationInfo(
        string propertyName,
        string relatedEntityName,
        string foreignKeyPropertyName)
    {
        PropertyName = propertyName;
        RelatedEntityName = relatedEntityName;
        ForeignKeyPropertyName = foreignKeyPropertyName;
    }

    public bool Equals(NavigationInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && RelatedEntityName == other.RelatedEntityName
            && ForeignKeyPropertyName == other.ForeignKeyPropertyName;
    }

    public override bool Equals(object? obj) => Equals(obj as NavigationInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, RelatedEntityName, ForeignKeyPropertyName);
    }
}
