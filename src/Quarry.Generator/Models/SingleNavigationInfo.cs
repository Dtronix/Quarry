using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a singular (N:1) navigation property (One&lt;T&gt;) defined in a schema.
/// </summary>
internal sealed class SingleNavigationInfo : IEquatable<SingleNavigationInfo>
{
    /// <summary>
    /// The One&lt;T&gt; property name on the schema (e.g., "User").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The target entity type name, normalized from TSchema (e.g., "User" from "UserSchema").
    /// </summary>
    public string TargetEntityName { get; }

    /// <summary>
    /// The Ref&lt;T,K&gt; property name on the current entity (e.g., "UserId").
    /// </summary>
    public string ForeignKeyPropertyName { get; }

    /// <summary>
    /// Whether the FK column is nullable, determining INNER vs LEFT join.
    /// </summary>
    public bool IsNullableFk { get; }

    public SingleNavigationInfo(
        string propertyName,
        string targetEntityName,
        string foreignKeyPropertyName,
        bool isNullableFk)
    {
        PropertyName = propertyName;
        TargetEntityName = targetEntityName;
        ForeignKeyPropertyName = foreignKeyPropertyName;
        IsNullableFk = isNullableFk;
    }

    public bool Equals(SingleNavigationInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && TargetEntityName == other.TargetEntityName
            && ForeignKeyPropertyName == other.ForeignKeyPropertyName
            && IsNullableFk == other.IsNullableFk;
    }

    public override bool Equals(object? obj) => Equals(obj as SingleNavigationInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, TargetEntityName, ForeignKeyPropertyName, IsNullableFk);
    }
}
