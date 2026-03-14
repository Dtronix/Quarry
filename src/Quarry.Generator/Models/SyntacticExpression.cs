using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a syntactically extracted expression from a lambda body.
/// Used for deferred clause translation when semantic analysis fails.
/// </summary>
internal abstract class SyntacticExpression : IEquatable<SyntacticExpression>
{
    /// <summary>
    /// Gets the kind of syntactic expression.
    /// </summary>
    public abstract SyntacticExpressionKind Kind { get; }

    public bool Equals(SyntacticExpression? other) => DeepEquals(this, other);

    public override bool Equals(object? obj) => Equals(obj as SyntacticExpression);

    public override int GetHashCode() => HashCode.Combine(Kind);

    public static bool DeepEquals(SyntacticExpression? a, SyntacticExpression? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Kind != b.Kind) return false;
        return (a, b) switch
        {
            (SyntacticPropertyAccess x, SyntacticPropertyAccess y) => x.ParameterName == y.ParameterName && x.PropertyName == y.PropertyName,
            (SyntacticLiteral x, SyntacticLiteral y) => x.Value == y.Value && x.ClrType == y.ClrType && x.IsNull == y.IsNull,
            (SyntacticParameter x, SyntacticParameter y) => x.Name == y.Name,
            (SyntacticBinary x, SyntacticBinary y) => x.Operator == y.Operator && DeepEquals(x.Left, y.Left) && DeepEquals(x.Right, y.Right),
            (SyntacticUnary x, SyntacticUnary y) => x.Operator == y.Operator && DeepEquals(x.Operand, y.Operand),
            (SyntacticMethodCall x, SyntacticMethodCall y) => x.MethodName == y.MethodName && DeepEquals(x.Target, y.Target) && ArgumentsEqual(x.Arguments, y.Arguments),
            (SyntacticMemberAccess x, SyntacticMemberAccess y) => x.MemberName == y.MemberName && DeepEquals(x.Target, y.Target),
            (SyntacticCapturedVariable x, SyntacticCapturedVariable y) => x.VariableName == y.VariableName && x.SyntaxText == y.SyntaxText && x.ExpressionPath == y.ExpressionPath,
            (SyntacticUnknown x, SyntacticUnknown y) => x.SyntaxText == y.SyntaxText && x.Reason == y.Reason,
            _ => false
        };
    }

    private static bool ArgumentsEqual(IReadOnlyList<SyntacticExpression> a, IReadOnlyList<SyntacticExpression> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!DeepEquals(a[i], b[i])) return false;
        return true;
    }
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
internal sealed class SyntacticPropertyAccess : SyntacticExpression, IEquatable<SyntacticPropertyAccess>
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

    public bool Equals(SyntacticPropertyAccess? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ParameterName == other.ParameterName && PropertyName == other.PropertyName;
    }

    public override bool Equals(object? obj) => obj is SyntacticPropertyAccess other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, ParameterName, PropertyName);
}

/// <summary>
/// Represents a literal value in an expression.
/// </summary>
internal sealed class SyntacticLiteral : SyntacticExpression, IEquatable<SyntacticLiteral>
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

    public bool Equals(SyntacticLiteral? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value && ClrType == other.ClrType && IsNull == other.IsNull;
    }

    public override bool Equals(object? obj) => obj is SyntacticLiteral other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Value, ClrType, IsNull);
}

/// <summary>
/// Represents a lambda parameter reference (e.g., just "u" in a boolean context).
/// </summary>
internal sealed class SyntacticParameter : SyntacticExpression, IEquatable<SyntacticParameter>
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

    public bool Equals(SyntacticParameter? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name;
    }

    public override bool Equals(object? obj) => obj is SyntacticParameter other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Name);
}

/// <summary>
/// Represents a binary expression (e.g., a == b, a && b).
/// </summary>
internal sealed class SyntacticBinary : SyntacticExpression, IEquatable<SyntacticBinary>
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

    public bool Equals(SyntacticBinary? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Operator == other.Operator && DeepEquals(Left, other.Left) && DeepEquals(Right, other.Right);
    }

    public override bool Equals(object? obj) => obj is SyntacticBinary other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Operator);
}

/// <summary>
/// Represents a unary expression (e.g., !a).
/// </summary>
internal sealed class SyntacticUnary : SyntacticExpression, IEquatable<SyntacticUnary>
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

    public bool Equals(SyntacticUnary? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Operator == other.Operator && DeepEquals(Operand, other.Operand);
    }

    public override bool Equals(object? obj) => obj is SyntacticUnary other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Operator);
}

/// <summary>
/// Represents a method call expression (e.g., s.Contains("x")).
/// </summary>
internal sealed class SyntacticMethodCall : SyntacticExpression, IEquatable<SyntacticMethodCall>
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

    public bool Equals(SyntacticMethodCall? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (MethodName != other.MethodName) return false;
        if (!DeepEquals(Target, other.Target)) return false;
        if (Arguments.Count != other.Arguments.Count) return false;
        for (int i = 0; i < Arguments.Count; i++)
            if (!DeepEquals(Arguments[i], other.Arguments[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is SyntacticMethodCall other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, MethodName, Arguments.Count);
}

/// <summary>
/// Represents a member access that couldn't be resolved to a property (e.g., u.Orders.Count).
/// </summary>
internal sealed class SyntacticMemberAccess : SyntacticExpression, IEquatable<SyntacticMemberAccess>
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

    public bool Equals(SyntacticMemberAccess? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MemberName == other.MemberName && DeepEquals(Target, other.Target);
    }

    public override bool Equals(object? obj) => obj is SyntacticMemberAccess other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, MemberName);
}

/// <summary>
/// Represents a captured variable from the enclosing scope (e.g., externalValueParameter).
/// These need to be evaluated at runtime and passed as SQL parameters.
/// </summary>
internal sealed class SyntacticCapturedVariable : SyntacticExpression, IEquatable<SyntacticCapturedVariable>
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

    public bool Equals(SyntacticCapturedVariable? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return VariableName == other.VariableName && SyntaxText == other.SyntaxText && ExpressionPath == other.ExpressionPath;
    }

    public override bool Equals(object? obj) => obj is SyntacticCapturedVariable other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, VariableName, SyntaxText);
}

/// <summary>
/// Represents an unknown or unsupported expression.
/// </summary>
internal sealed class SyntacticUnknown : SyntacticExpression, IEquatable<SyntacticUnknown>
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

    public bool Equals(SyntacticUnknown? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SyntaxText == other.SyntaxText && Reason == other.Reason;
    }

    public override bool Equals(object? obj) => obj is SyntacticUnknown other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, SyntaxText, Reason);
}
