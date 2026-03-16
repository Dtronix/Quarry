using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Per-parameter type information for carrier class field generation.
/// Each parameter in a PrebuiltDispatch chain gets a typed field on the carrier.
/// </summary>
internal sealed class ChainParameterInfo : IEquatable<ChainParameterInfo>
{
    public ChainParameterInfo(
        int index,
        string typeName,
        string valueExpression,
        string? typeMapping = null,
        bool isSensitive = false,
        bool isEnum = false,
        string? enumUnderlyingType = null,
        bool needsFieldInfoCache = false)
    {
        Index = index;
        TypeName = typeName;
        ValueExpression = valueExpression;
        TypeMapping = typeMapping;
        IsSensitive = isSensitive;
        IsEnum = isEnum;
        EnumUnderlyingType = enumUnderlyingType;
        NeedsFieldInfoCache = needsFieldInfoCache;
    }

    /// <summary>
    /// Gets the parameter index (P0, P1, ...).
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the fully qualified type name for the carrier field (e.g., "decimal", "int", "string").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the expression used to extract the parameter value from the call site closure.
    /// </summary>
    public string ValueExpression { get; }

    /// <summary>
    /// Gets the TypeMapping class name to apply when converting to DB parameter, if any.
    /// </summary>
    public string? TypeMapping { get; }

    /// <summary>
    /// Gets whether this parameter binds to a column marked as Sensitive().
    /// When true, the terminal emits redacted parameter logging.
    /// </summary>
    public bool IsSensitive { get; }

    /// <summary>
    /// Gets whether this parameter's CLR type is an enum (or nullable enum).
    /// When true, the terminal emits an inline cast to the underlying integral type.
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// Gets the underlying integral type name for enum parameters (e.g., "int", "byte").
    /// Only meaningful when <see cref="IsEnum"/> is true.
    /// </summary>
    public string? EnumUnderlyingType { get; }

    /// <summary>
    /// Gets whether this parameter is a captured variable that requires a static
    /// <c>FieldInfo?</c> cache on the carrier class for expression tree extraction.
    /// </summary>
    public bool NeedsFieldInfoCache { get; }

    public bool Equals(ChainParameterInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Index == other.Index
            && TypeName == other.TypeName
            && ValueExpression == other.ValueExpression
            && TypeMapping == other.TypeMapping
            && IsSensitive == other.IsSensitive
            && IsEnum == other.IsEnum
            && EnumUnderlyingType == other.EnumUnderlyingType
            && NeedsFieldInfoCache == other.NeedsFieldInfoCache;
    }

    public override bool Equals(object? obj) => Equals(obj as ChainParameterInfo);

    public override int GetHashCode() => HashCode.Combine(Index, TypeName);
}
