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
/// Describes a static member on the carrier class — either an [UnsafeAccessor] extern method
/// for zero-alloc closure field extraction, or a legacy FieldInfo cache.
/// </summary>
internal sealed class CarrierStaticField : IEquatable<CarrierStaticField>
{
    public CarrierStaticField(string name, string typeName, int parameterIndex,
        string? displayClassName = null, string? capturedFieldName = null, string? capturedFieldType = null,
        bool isStaticField = false)
    {
        Name = name;
        TypeName = typeName;
        ParameterIndex = parameterIndex;
        DisplayClassName = displayClassName;
        CapturedFieldName = capturedFieldName;
        CapturedFieldType = capturedFieldType;
        IsStaticField = isStaticField;
    }

    /// <summary>
    /// Gets the method/field name (e.g., "__ExtractP0").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the return type for [UnsafeAccessor] methods (e.g., "decimal").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the global parameter index this accessor extracts.
    /// </summary>
    public int ParameterIndex { get; }

    /// <summary>
    /// The fully-qualified display class name (e.g., "Namespace.Class+&lt;&gt;c__DisplayClass0_0").
    /// Used in [UnsafeAccessorType] attribute on the target parameter.
    /// </summary>
    public string? DisplayClassName { get; }

    /// <summary>
    /// The field name on the display class (e.g., "minTotal").
    /// Used as the Name parameter of [UnsafeAccessor].
    /// </summary>
    public string? CapturedFieldName { get; }

    /// <summary>
    /// The CLR type of the captured field (e.g., "decimal").
    /// Used as the return type of the [UnsafeAccessor] extern method.
    /// </summary>
    public string? CapturedFieldType { get; }

    /// <summary>
    /// True when the field is a static field on the containing class (not a closure display class field).
    /// Uses UnsafeAccessorKind.StaticField instead of UnsafeAccessorKind.Field.
    /// </summary>
    public bool IsStaticField { get; }

    public bool Equals(CarrierStaticField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && TypeName == other.TypeName && ParameterIndex == other.ParameterIndex
            && DisplayClassName == other.DisplayClassName
            && CapturedFieldName == other.CapturedFieldName
            && CapturedFieldType == other.CapturedFieldType
            && IsStaticField == other.IsStaticField;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierStaticField);
    public override int GetHashCode() => HashCode.Combine(Name, TypeName, ParameterIndex, DisplayClassName, CapturedFieldName, CapturedFieldType);
}
