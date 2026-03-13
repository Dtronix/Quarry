using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a syntactically extracted expression from a lambda body.
/// Used for deferred clause translation when semantic analysis fails.
/// </summary>
internal abstract class SyntacticExpression
{
    /// <summary>
    /// Gets the kind of syntactic expression.
    /// </summary>
    public abstract SyntacticExpressionKind Kind { get; }
}

/// <summary>
/// Specifies the kind of syntactic expression.
/// </summary>
internal enum SyntacticExpressionKind
{
    PropertyAccess,
    Literal,
    Parameter,
    Binary,
    Unary,
    MethodCall,
    Conditional,
    MemberAccess,
    CapturedVariable,
    Unknown
}

/// <summary>
/// Represents a property access on a lambda parameter (e.g., u.IsActive).
/// </summary>
internal sealed class SyntacticPropertyAccess : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.PropertyAccess;

    /// <summary>
    /// The lambda parameter name (e.g., "u").
    /// </summary>
    public string ParameterName { get; }

    /// <summary>
    /// The property name being accessed (e.g., "IsActive").
    /// </summary>
    public string PropertyName { get; }

    public SyntacticPropertyAccess(string parameterName, string propertyName)
    {
        ParameterName = parameterName;
        PropertyName = propertyName;
    }
}

/// <summary>
/// Represents a literal value in an expression.
/// </summary>
internal sealed class SyntacticLiteral : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.Literal;

    /// <summary>
    /// The literal value as a string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The CLR type of the literal.
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// Whether this is a null literal.
    /// </summary>
    public bool IsNull { get; }

    public SyntacticLiteral(string value, string clrType, bool isNull = false)
    {
        Value = value;
        ClrType = clrType;
        IsNull = isNull;
    }
}

/// <summary>
/// Represents a lambda parameter reference (e.g., just "u" in a boolean context).
/// </summary>
internal sealed class SyntacticParameter : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.Parameter;

    /// <summary>
    /// The parameter name.
    /// </summary>
    public string Name { get; }

    public SyntacticParameter(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Represents a binary expression (e.g., a == b, a && b).
/// </summary>
internal sealed class SyntacticBinary : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.Binary;

    /// <summary>
    /// The left operand.
    /// </summary>
    public SyntacticExpression Left { get; }

    /// <summary>
    /// The operator (e.g., "==", "&&", ">").
    /// </summary>
    public string Operator { get; }

    /// <summary>
    /// The right operand.
    /// </summary>
    public SyntacticExpression Right { get; }

    public SyntacticBinary(SyntacticExpression left, string op, SyntacticExpression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}

/// <summary>
/// Represents a unary expression (e.g., !a).
/// </summary>
internal sealed class SyntacticUnary : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.Unary;

    /// <summary>
    /// The operator (e.g., "!").
    /// </summary>
    public string Operator { get; }

    /// <summary>
    /// The operand.
    /// </summary>
    public SyntacticExpression Operand { get; }

    public SyntacticUnary(string op, SyntacticExpression operand)
    {
        Operator = op;
        Operand = operand;
    }
}

/// <summary>
/// Represents a method call expression (e.g., s.Contains("x")).
/// </summary>
internal sealed class SyntacticMethodCall : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.MethodCall;

    /// <summary>
    /// The target expression the method is called on (null for static methods).
    /// </summary>
    public SyntacticExpression? Target { get; }

    /// <summary>
    /// The method name.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// The method arguments.
    /// </summary>
    public IReadOnlyList<SyntacticExpression> Arguments { get; }

    public SyntacticMethodCall(SyntacticExpression? target, string methodName, IReadOnlyList<SyntacticExpression> arguments)
    {
        Target = target;
        MethodName = methodName;
        Arguments = arguments;
    }
}

/// <summary>
/// Represents a member access that couldn't be resolved to a property (e.g., u.Orders.Count).
/// </summary>
internal sealed class SyntacticMemberAccess : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.MemberAccess;

    /// <summary>
    /// The target expression.
    /// </summary>
    public SyntacticExpression Target { get; }

    /// <summary>
    /// The member name.
    /// </summary>
    public string MemberName { get; }

    public SyntacticMemberAccess(SyntacticExpression target, string memberName)
    {
        Target = target;
        MemberName = memberName;
    }
}

/// <summary>
/// Represents a captured variable from the enclosing scope (e.g., externalValueParameter).
/// These need to be evaluated at runtime and passed as SQL parameters.
/// </summary>
internal sealed class SyntacticCapturedVariable : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.CapturedVariable;

    /// <summary>
    /// The variable name as it appears in source code.
    /// </summary>
    public string VariableName { get; }

    /// <summary>
    /// The full syntax text of the expression (may include member access like "obj.Property").
    /// </summary>
    public string SyntaxText { get; }

    /// <summary>
    /// The path from expression body to this captured variable.
    /// Used for generating direct path navigation code at runtime.
    /// Example: "Body.Right" for the right operand of a binary expression.
    /// </summary>
    public string? ExpressionPath { get; }

    /// <summary>
    /// Whether direct path navigation code can be generated for this captured variable.
    /// </summary>
    public bool CanGenerateDirectPath => ExpressionPath != null;

    public SyntacticCapturedVariable(string variableName, string syntaxText, string? expressionPath = null)
    {
        VariableName = variableName;
        SyntaxText = syntaxText;
        ExpressionPath = expressionPath;
    }
}

/// <summary>
/// Represents an unknown or unsupported expression.
/// </summary>
internal sealed class SyntacticUnknown : SyntacticExpression
{
    public override SyntacticExpressionKind Kind => SyntacticExpressionKind.Unknown;

    /// <summary>
    /// The original syntax text.
    /// </summary>
    public string SyntaxText { get; }

    /// <summary>
    /// Why the expression couldn't be parsed.
    /// </summary>
    public string Reason { get; }

    public SyntacticUnknown(string syntaxText, string reason)
    {
        SyntaxText = syntaxText;
        Reason = reason;
    }
}
