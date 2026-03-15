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
        string? typeMapping = null)
    {
        Index = index;
        TypeName = typeName;
        ValueExpression = valueExpression;
        TypeMapping = typeMapping;
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

    public bool Equals(ChainParameterInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Index == other.Index
            && TypeName == other.TypeName
            && ValueExpression == other.ValueExpression
            && TypeMapping == other.TypeMapping;
    }

    public override bool Equals(object? obj) => Equals(obj as ChainParameterInfo);

    public override int GetHashCode() => HashCode.Combine(Index, TypeName);
}
