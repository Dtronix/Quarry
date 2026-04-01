using System;

namespace Quarry.Generators.CodeGen;

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
        bool isEntitySourced = false,
        bool isEnumerableCollection = false)
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
        IsEnumerableCollection = isEnumerableCollection;
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
    /// <summary>Whether the collection is IEnumerable-only (needs materialization at terminal).</summary>
    public bool IsEnumerableCollection { get; }

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
            && IsEntitySourced == other.IsEntitySourced
            && IsEnumerableCollection == other.IsEnumerableCollection;
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierParameter);

    public override int GetHashCode()
    {
        return HashCode.Combine(GlobalIndex, FieldName, FieldType);
    }
}
