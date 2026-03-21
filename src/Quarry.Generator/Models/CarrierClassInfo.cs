using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Complete description of a generated carrier class for a PrebuiltDispatch chain.
/// Used by the code generator to emit the <c>file sealed class</c> and to route
/// interceptor methods through the carrier-optimized path.
/// </summary>
internal sealed class CarrierClassInfo : IEquatable<CarrierClassInfo>
{
    public CarrierClassInfo(
        string className,
        IReadOnlyList<string> implementedInterfaces,
        IReadOnlyList<CarrierField> fields,
        IReadOnlyList<CarrierInterfaceStub> deadMethods,
        IReadOnlyList<CarrierStaticField>? staticFields = null)
    {
        ClassName = className;
        ImplementedInterfaces = implementedInterfaces;
        Fields = fields;
        DeadMethods = deadMethods;
        StaticFields = staticFields ?? System.Array.Empty<CarrierStaticField>();
    }

    /// <summary>
    /// Gets the carrier class name (e.g., "Chain_0", "Chain_1").
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// Gets the fully qualified closed interface names this carrier implements.
    /// </summary>
    public IReadOnlyList<string> ImplementedInterfaces { get; }

    /// <summary>
    /// Gets the typed fields on this carrier.
    /// </summary>
    public IReadOnlyList<CarrierField> Fields { get; }

    /// <summary>
    /// Gets the dead interface methods (explicit impls that throw).
    /// </summary>
    public IReadOnlyList<CarrierInterfaceStub> DeadMethods { get; }

    /// <summary>
    /// Gets the static fields on this carrier (FieldInfo caches for captured params).
    /// </summary>
    public IReadOnlyList<CarrierStaticField> StaticFields { get; }

    public bool Equals(CarrierClassInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ClassName == other.ClassName
            && EqualityHelpers.SequenceEqual(ImplementedInterfaces, other.ImplementedInterfaces)
            && EqualityHelpers.SequenceEqual(Fields, other.Fields)
            && EqualityHelpers.SequenceEqual(DeadMethods, other.DeadMethods)
            && EqualityHelpers.SequenceEqual(StaticFields, other.StaticFields);
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierClassInfo);
    public override int GetHashCode() => HashCode.Combine(ClassName, ImplementedInterfaces.Count, Fields.Count);
}
