using System.Collections.Generic;

namespace Quarry.Generators.Translation;

/// <summary>
/// Represents the result of translating a C# expression to SQL.
/// </summary>
internal sealed class ExpressionTranslationResult
{
    /// <summary>
    /// Indicates successful translation with SQL output.
    /// </summary>
    public static ExpressionTranslationResult Success(string sql) =>
        new(sql, true, null);

    /// <summary>
    /// Indicates successful translation with SQL output and parameters.
    /// </summary>
    public static ExpressionTranslationResult Success(string sql, IReadOnlyList<ParameterInfo> parameters) =>
        new(sql, true, null, parameters);

    /// <summary>
    /// Indicates failed translation with an error message.
    /// </summary>
    public static ExpressionTranslationResult Failure(string error) =>
        new(null, false, error);

    private ExpressionTranslationResult(
        string? sql,
        bool isSuccess,
        string? errorMessage,
        IReadOnlyList<ParameterInfo>? parameters = null)
    {
        Sql = sql;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Parameters = parameters ?? System.Array.Empty<ParameterInfo>();
    }

    /// <summary>
    /// Gets the translated SQL fragment, or null if translation failed.
    /// </summary>
    public string? Sql { get; }

    /// <summary>
    /// Gets whether the translation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the error message if translation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the parameters extracted from the expression.
    /// </summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }
}

/// <summary>
/// Represents a parameter extracted from an expression.
/// </summary>
internal sealed class ParameterInfo
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
    public string? ExpressionPath { get; }

    /// <summary>
    /// Gets whether direct path navigation code can be generated for this parameter.
    /// True when the parameter is a captured variable with a known expression path.
    /// </summary>
    public bool CanGenerateDirectPath => IsCaptured && ExpressionPath != null;

    /// <summary>
    /// Gets or sets the custom type mapping class to apply ToDb() wrapping.
    /// When set, the interceptor generator wraps the parameter value with
    /// {MappingInstance}.ToDb(value) before binding.
    /// </summary>
    public string? CustomTypeMappingClass { get; set; }
}
