using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Describes the carrier optimization strategy for a query chain.
/// Consolidates carrier eligibility decisions that were previously spread
/// across 6+ locations in QuarryGenerator, CarrierClassBuilder, and InterceptorCodeGenerator.
/// </summary>
internal sealed class CarrierStrategy : IEquatable<CarrierStrategy>
{
    public CarrierStrategy(
        bool isEligible,
        string? ineligibleReason,
        string baseClassName,
        IReadOnlyList<CarrierField> fields,
        IReadOnlyList<CarrierStaticField> staticFields,
        IReadOnlyList<CarrierParameter> parameters)
    {
        IsEligible = isEligible;
        IneligibleReason = ineligibleReason;
        BaseClassName = baseClassName;
        Fields = fields;
        StaticFields = staticFields;
        Parameters = parameters;
    }

    /// <summary>Whether the chain is eligible for carrier optimization.</summary>
    public bool IsEligible { get; }

    /// <summary>Human-readable reason if not eligible.</summary>
    public string? IneligibleReason { get; }

    /// <summary>Carrier base class name (e.g., "QueryCarrier", "DeleteCarrier").</summary>
    public string BaseClassName { get; }

    /// <summary>Instance fields on the carrier class (parameters stored per-invocation).</summary>
    public IReadOnlyList<CarrierField> Fields { get; }

    /// <summary>Static fields on the carrier class (SQL strings, FieldInfo caches).</summary>
    public IReadOnlyList<CarrierStaticField> StaticFields { get; }

    /// <summary>Parameters with full extraction metadata for carrier binding.</summary>
    public IReadOnlyList<CarrierParameter> Parameters { get; }

    /// <summary>Creates an ineligible strategy with a reason.</summary>
    public static CarrierStrategy Ineligible(string reason)
    {
        return new CarrierStrategy(
            isEligible: false,
            ineligibleReason: reason,
            baseClassName: "",
            fields: Array.Empty<CarrierField>(),
            staticFields: Array.Empty<CarrierStaticField>(),
            parameters: Array.Empty<CarrierParameter>());
    }

    public bool Equals(CarrierStrategy? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsEligible == other.IsEligible
            && IneligibleReason == other.IneligibleReason
            && BaseClassName == other.BaseClassName
            && EqualityHelpers.SequenceEqual(Fields, other.Fields)
            && EqualityHelpers.SequenceEqual(StaticFields, other.StaticFields)
            && EqualityHelpers.SequenceEqual(Parameters, other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierStrategy);

    public override int GetHashCode()
    {
        return HashCode.Combine(IsEligible, BaseClassName, Fields.Count, Parameters.Count);
    }
}

/// <summary>
/// An instance field on a carrier class.
/// </summary>
internal sealed class CarrierField : IEquatable<CarrierField>
{
    public CarrierField(string name, string type)
    {
        Name = name;
        Type = type;
    }

    /// <summary>Field name (e.g., "P0", "P1").</summary>
    public string Name { get; }

    /// <summary>Field type (e.g., "string", "int").</summary>
    public string Type { get; }

    public bool Equals(CarrierField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Type == other.Type;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierField);
    public override int GetHashCode() => HashCode.Combine(Name, Type);
}

/// <summary>
/// A static field on a carrier class (SQL strings, FieldInfo caches).
/// </summary>
internal sealed class CarrierStaticField : IEquatable<CarrierStaticField>
{
    public CarrierStaticField(string name, string type, string? initializer)
    {
        Name = name;
        Type = type;
        Initializer = initializer;
    }

    /// <summary>Field name.</summary>
    public string Name { get; }

    /// <summary>Field type.</summary>
    public string Type { get; }

    /// <summary>Optional initializer expression.</summary>
    public string? Initializer { get; }

    public bool Equals(CarrierStaticField? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Type == other.Type && Initializer == other.Initializer;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierStaticField);
    public override int GetHashCode() => HashCode.Combine(Name, Type);
}

/// <summary>
/// A carrier parameter with full extraction metadata.
/// </summary>
internal sealed class CarrierParameter : IEquatable<CarrierParameter>
{
    public CarrierParameter(
        int globalIndex,
        string fieldName,
        string fieldType,
        string? extractionCode,
        string? bindingCode,
        string? typeMappingClass = null,
        bool isCollection = false,
        bool isSensitive = false,
        bool isEntitySourced = false)
    {
        GlobalIndex = globalIndex;
        FieldName = fieldName;
        FieldType = fieldType;
        ExtractionCode = extractionCode;
        BindingCode = bindingCode;
        TypeMappingClass = typeMappingClass;
        IsCollection = isCollection;
        IsSensitive = isSensitive;
        IsEntitySourced = isEntitySourced;
    }

    public int GlobalIndex { get; }
    /// <summary>Carrier field name ("P0", "P1", etc.).</summary>
    public string FieldName { get; }
    /// <summary>Normalized CLR type.</summary>
    public string FieldType { get; }
    /// <summary>Code to extract value at the interceptor site.</summary>
    public string? ExtractionCode { get; }
    /// <summary>Code to bind value to DbParameter at terminal.</summary>
    public string? BindingCode { get; }
    /// <summary>Custom type mapping class for ToDb() wrapping.</summary>
    public string? TypeMappingClass { get; }
    public bool IsCollection { get; }
    public bool IsSensitive { get; }
    /// <summary>Read from Entity field, not P{n}.</summary>
    public bool IsEntitySourced { get; }

    public bool Equals(CarrierParameter? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return GlobalIndex == other.GlobalIndex
            && FieldName == other.FieldName
            && FieldType == other.FieldType
            && ExtractionCode == other.ExtractionCode
            && BindingCode == other.BindingCode
            && TypeMappingClass == other.TypeMappingClass
            && IsCollection == other.IsCollection
            && IsSensitive == other.IsSensitive
            && IsEntitySourced == other.IsEntitySourced;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierParameter);

    public override int GetHashCode()
    {
        return HashCode.Combine(GlobalIndex, FieldName, FieldType);
    }
}
