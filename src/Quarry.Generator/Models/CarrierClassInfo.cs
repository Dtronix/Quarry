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
    ClauseMask,
    Limit,
    Offset,
    Timeout
}

/// <summary>
/// Describes a single field on a generated carrier class.
/// </summary>
internal sealed class CarrierField : IEquatable<CarrierField>
{
    public CarrierField(string name, string typeName, FieldRole role)
    {
        Name = name;
        TypeName = typeName;
        Role = role;
    }

    /// <summary>
    /// Gets the field name (e.g., "P0", "Ctx", "Limit", "Mask", "Timeout").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the C# type name (e.g., "decimal", "IQueryExecutionContext?", "int?", "byte").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the role of this field.
    /// </summary>
    public FieldRole Role { get; }

    public bool Equals(CarrierField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && TypeName == other.TypeName && Role == other.Role;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierField);
    public override int GetHashCode() => HashCode.Combine(Name, TypeName, Role);
}

/// <summary>
/// Describes an explicit interface method implementation on a carrier class that
/// throws <see cref="InvalidOperationException"/> because the method is not used
/// in this chain.
/// </summary>
internal sealed class CarrierInterfaceStub : IEquatable<CarrierInterfaceStub>
{
    public CarrierInterfaceStub(
        string interfaceName,
        string methodName,
        string fullSignature,
        string returnTypeName,
        IReadOnlyList<string> genericTypeParamNames)
    {
        InterfaceName = interfaceName;
        MethodName = methodName;
        FullSignature = fullSignature;
        ReturnTypeName = returnTypeName;
        GenericTypeParamNames = genericTypeParamNames;
    }

    /// <summary>
    /// Gets the interface name (e.g., "IQueryBuilder&lt;User&gt;").
    /// </summary>
    public string InterfaceName { get; }

    /// <summary>
    /// Gets the method name (e.g., "Where", "Select").
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the complete explicit interface implementation signature.
    /// </summary>
    public string FullSignature { get; }

    /// <summary>
    /// Gets the return type name.
    /// </summary>
    public string ReturnTypeName { get; }

    /// <summary>
    /// Gets method-level type parameter names (e.g., ["TResult"], ["TKey"]).
    /// Empty for non-generic methods.
    /// </summary>
    public IReadOnlyList<string> GenericTypeParamNames { get; }

    public bool Equals(CarrierInterfaceStub? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return InterfaceName == other.InterfaceName
            && MethodName == other.MethodName
            && FullSignature == other.FullSignature
            && ReturnTypeName == other.ReturnTypeName
            && EqualityHelpers.SequenceEqual(GenericTypeParamNames, other.GenericTypeParamNames);
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierInterfaceStub);
    public override int GetHashCode() => HashCode.Combine(InterfaceName, MethodName);
}

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
        IReadOnlyList<CarrierInterfaceStub> deadMethods)
    {
        ClassName = className;
        ImplementedInterfaces = implementedInterfaces;
        Fields = fields;
        DeadMethods = deadMethods;
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

    public bool Equals(CarrierClassInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ClassName == other.ClassName
            && EqualityHelpers.SequenceEqual(ImplementedInterfaces, other.ImplementedInterfaces)
            && EqualityHelpers.SequenceEqual(Fields, other.Fields)
            && EqualityHelpers.SequenceEqual(DeadMethods, other.DeadMethods);
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierClassInfo);
    public override int GetHashCode() => HashCode.Combine(ClassName, ImplementedInterfaces.Count, Fields.Count);
}
