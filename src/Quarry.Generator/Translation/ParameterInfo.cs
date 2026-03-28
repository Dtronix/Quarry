using System;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Translation;

/// <summary>
/// Represents a parameter extracted from an expression.
/// </summary>
internal sealed class ParameterInfo : IEquatable<ParameterInfo>
{
    public ParameterInfo(
        int index,
        string name,
        string clrType,
        string valueExpression,
        bool isCollection = false,
        bool isCaptured = false,
        string? expressionPath = null)
    {
        Index = index;
        Name = name;
        ClrType = clrType;
        ValueExpression = valueExpression;
        IsCollection = isCollection;
        IsCaptured = isCaptured;
        ExpressionPath = expressionPath;
    }

    /// <summary>
    /// Gets the parameter index (0-based).
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the parameter name (e.g., "@p0").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the CLR type of the parameter value.
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// Gets the C# expression that produces the parameter value.
    /// Used for generating parameter binder code.
    /// </summary>
    public string ValueExpression { get; }

    /// <summary>
    /// Gets whether this parameter represents a collection (for IN clauses).
    /// </summary>
    public bool IsCollection { get; }

    /// <summary>
    /// Gets whether this parameter is a captured variable that needs runtime extraction.
    /// </summary>
    public bool IsCaptured { get; }

    /// <summary>
    /// Gets the path from expression body to this captured variable.
    /// Used for generating direct path navigation code.
    /// Example: "Body.Right" for the right operand of a binary expression.
    /// </summary>
    public string? ExpressionPath { get; set; }

    /// <summary>
    /// Gets whether direct path navigation code can be generated for this parameter.
    /// True when the parameter is a captured variable with a known expression path.
    /// </summary>
    public bool CanGenerateDirectPath => IsCaptured && ExpressionPath != null;

    /// <summary>
    /// Gets or sets the name of the field on the compiler-generated display class
    /// that holds this captured variable (e.g., "minTotal", "viewModel").
    /// This is the root variable name — for property chain access like viewModel.SearchTerm,
    /// this is "viewModel" and the property chain ".SearchTerm" is derived from ValueExpression.
    /// </summary>
    public string? CapturedFieldName { get; set; }

    /// <summary>
    /// Gets or sets the CLR type of the captured field on the display class.
    /// For simple captures this equals ClrType. For property chain captures
    /// (e.g., viewModel.SearchTerm), this is the type of the root variable (SearchViewModel),
    /// not the type of the final expression (string).
    /// </summary>
    public string? CapturedFieldType { get; set; }

    /// <summary>
    /// Gets or sets the element type name for collection parameters (e.g., "string", "int").
    /// Only meaningful when <see cref="IsCollection"/> is true.
    /// </summary>
    public string? CollectionElementType { get; set; }

    /// <summary>
    /// Gets or sets the custom type mapping class to apply ToDb() wrapping.
    /// When set, the interceptor generator wraps the parameter value with
    /// {MappingInstance}.ToDb(value) before binding.
    /// </summary>
    public string? CustomTypeMappingClass { get; set; }

    /// <summary>
    /// Gets or sets whether this parameter's CLR type is an enum (or nullable enum).
    /// When true, the carrier terminal emits an inline cast to the underlying integral type
    /// instead of relying on runtime <c>GetType().IsEnum</c> + <c>Convert.ChangeType</c>.
    /// </summary>
    public bool IsEnum { get; set; }

    /// <summary>
    /// Gets or sets the underlying integral type name for enum parameters (e.g., "int", "byte").
    /// Only meaningful when <see cref="IsEnum"/> is true.
    /// </summary>
    public string? EnumUnderlyingType { get; set; }

    /// <summary>
    /// Gets or sets the symbol for the collection receiver expression.
    /// Used by the carrier parameter builder to determine direct-access eligibility
    /// (public static fields/properties can be accessed without expression tree extraction).
    /// Only meaningful when <see cref="IsCollection"/> is true.
    /// </summary>
    public ISymbol? CollectionReceiverSymbol { get; set; }

    public bool Equals(ParameterInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Index == other.Index
            && Name == other.Name
            && ClrType == other.ClrType
            && ValueExpression == other.ValueExpression
            && IsCollection == other.IsCollection
            && IsCaptured == other.IsCaptured
            && ExpressionPath == other.ExpressionPath
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && IsEnum == other.IsEnum
            && EnumUnderlyingType == other.EnumUnderlyingType
            && CapturedFieldName == other.CapturedFieldName
            && CapturedFieldType == other.CapturedFieldType;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Name, ClrType, ValueExpression, CapturedFieldName);
    }
}
