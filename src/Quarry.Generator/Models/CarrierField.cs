using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// The role of a field on a carrier class.
/// </summary>
internal enum FieldRole
{
    ExecutionContext,
    Parameter,
    Collection,
    ClauseMask,
    Limit,
    Offset,
    Timeout,
    Entity
}

/// <summary>
/// Describes a single field on a generated carrier class.
/// </summary>
internal sealed class CarrierField : IEquatable<CarrierField>
{
    public CarrierField(string name, string typeName, FieldRole role, bool isReferenceType = false)
    {
        Name = name;
        TypeName = typeName;
        Role = role;
        IsReferenceType = isReferenceType;
    }

    /// <summary>
    /// Gets the field name (e.g., "P0", "Ctx", "Limit", "Mask", "Timeout").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the C# type name (e.g., "decimal", "int?", "byte").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the role of this field.
    /// </summary>
    public FieldRole Role { get; }

    /// <summary>
    /// Gets whether this field's type is a reference type (e.g., string, byte[], IReadOnlyList&lt;T&gt;).
    /// Used by the emitter to decide whether a <c>= null!</c> initializer is needed.
    /// </summary>
    public bool IsReferenceType { get; }

    public bool Equals(CarrierField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && TypeName == other.TypeName && Role == other.Role && IsReferenceType == other.IsReferenceType;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierField);
    public override int GetHashCode() => HashCode.Combine(Name, TypeName, Role, IsReferenceType);
}

/// <summary>
/// Describes a static field on the carrier class for caching FieldInfo or other
/// per-chain state that should be isolated to the carrier.
/// </summary>
internal sealed class CarrierStaticField : IEquatable<CarrierStaticField>
{
    public CarrierStaticField(string name, string typeName, int parameterIndex)
    {
        Name = name;
        TypeName = typeName;
        ParameterIndex = parameterIndex;
    }

    /// <summary>
    /// Gets the field name (e.g., "F0", "F1").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the C# type name (e.g., "FieldInfo?").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the global parameter index this field caches extraction for.
    /// </summary>
    public int ParameterIndex { get; }

    public bool Equals(CarrierStaticField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && TypeName == other.TypeName && ParameterIndex == other.ParameterIndex;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierStaticField);
    public override int GetHashCode() => HashCode.Combine(Name, TypeName, ParameterIndex);
}
