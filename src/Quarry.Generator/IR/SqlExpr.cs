using System;

namespace Quarry.Generators.IR;

/// <summary>
/// Abstract base class representing a SQL expression fragment in the unified expression IR.
/// Replaces both ExpressionSyntaxTranslator (semantic path) and SyntacticExpression (syntactic path)
/// with a single IR that is dialect-agnostic until rendering.
/// </summary>
internal abstract class SqlExpr : IEquatable<SqlExpr>
{
    private readonly int _cachedHashCode;

    protected SqlExpr(int hashCode)
    {
        _cachedHashCode = hashCode;
    }

    /// <summary>
    /// Gets the kind of this SQL expression node.
    /// </summary>
    public abstract SqlExprKind Kind { get; }

    public sealed override int GetHashCode() => _cachedHashCode;

    public bool Equals(SqlExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_cachedHashCode != other._cachedHashCode) return false;
        if (Kind != other.Kind) return false;
        return DeepEquals(other);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlExpr);

    /// <summary>
    /// Performs deep structural equality comparison. Called only when Kind and hash match.
    /// </summary>
    protected abstract bool DeepEquals(SqlExpr other);
}

/// <summary>
/// Specifies the kind of SQL expression node.
/// </summary>
internal enum SqlExprKind
{
    ColumnRef,
    ResolvedColumn,
    ParamSlot,
    Literal,
    BinaryOp,
    UnaryOp,
    FunctionCall,
    InExpr,
    IsNullCheck,
    LikeExpr,
    CapturedValue,
    SqlRaw,
    RawCall,
    ExprList,
    Subquery,
    NavigationAccess
}

/// <summary>
/// SQL binary operators.
/// </summary>
internal enum SqlBinaryOperator
{
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    And,
    Or,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor
}

/// <summary>
/// SQL unary operators.
/// </summary>
internal enum SqlUnaryOperator
{
    Not,
    Negate
}

/// <summary>
/// Subquery kinds for navigation property collection methods.
/// </summary>
internal enum SubqueryKind
{
    /// <summary>.Any() or .Any(predicate) -> EXISTS (SELECT 1 FROM ...)</summary>
    Exists,
    /// <summary>.All(predicate) -> NOT EXISTS (SELECT 1 FROM ... AND NOT predicate)</summary>
    All,
    /// <summary>.Count() or .Count(predicate) -> (SELECT COUNT(*) FROM ...)</summary>
    Count
}
