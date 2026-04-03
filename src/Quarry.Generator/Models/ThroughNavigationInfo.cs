using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a many-to-many skip-navigation (HasManyThrough) defined in a schema.
/// </summary>
internal sealed class ThroughNavigationInfo : IEquatable<ThroughNavigationInfo>
{
    /// <summary>
    /// The skip-navigation property name (e.g., "Addresses").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The target entity name (e.g., "Address").
    /// </summary>
    public string TargetEntityName { get; }

    /// <summary>
    /// The junction entity name (e.g., "UserAddress").
    /// </summary>
    public string JunctionEntityName { get; }

    /// <summary>
    /// The Many&lt;T&gt; property that reaches the junction (e.g., "UserAddresses").
    /// </summary>
    public string JunctionNavigationName { get; }

    /// <summary>
    /// The One&lt;T&gt; property on the junction that reaches the target (e.g., "Address").
    /// </summary>
    public string TargetNavigationName { get; }

    public ThroughNavigationInfo(
        string propertyName,
        string targetEntityName,
        string junctionEntityName,
        string junctionNavigationName,
        string targetNavigationName)
    {
        PropertyName = propertyName;
        TargetEntityName = targetEntityName;
        JunctionEntityName = junctionEntityName;
        JunctionNavigationName = junctionNavigationName;
        TargetNavigationName = targetNavigationName;
    }

    public bool Equals(ThroughNavigationInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && TargetEntityName == other.TargetEntityName
            && JunctionEntityName == other.JunctionEntityName
            && JunctionNavigationName == other.JunctionNavigationName
            && TargetNavigationName == other.TargetNavigationName;
    }

    public override bool Equals(object? obj) => Equals(obj as ThroughNavigationInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, TargetEntityName, JunctionEntityName, JunctionNavigationName, TargetNavigationName);
    }
}
