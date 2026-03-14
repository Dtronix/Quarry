using System;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Models;

/// <summary>
/// Maps a context property name to its entity information.
/// </summary>
internal sealed class EntityMapping : IEquatable<EntityMapping>
{
    /// <summary>
    /// Gets the property name as declared on the context class.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the entity information for this mapping.
    /// </summary>
    public EntityInfo Entity { get; }

    public EntityMapping(string propertyName, EntityInfo entity)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
    }

    public bool Equals(EntityMapping? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && Entity.Equals(other.Entity);
    }

    public override bool Equals(object? obj) => Equals(obj as EntityMapping);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, Entity.EntityName);
    }
}
