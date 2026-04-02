using System;
using Quarry.Generators.IR;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a single assignment extracted from a Set(Action&lt;T&gt;) lambda body.
/// </summary>
internal sealed class SetActionAssignment : IEquatable<SetActionAssignment>
{
    public SetActionAssignment(string columnSql, string? valueTypeName, string? customTypeMappingClass,
        string? inlinedSqlValue = null, string? inlinedCSharpExpression = null,
        string? columnExpressionText = null, string? columnExpressionLambdaParam = null)
    {
        ColumnSql = columnSql;
        ValueTypeName = valueTypeName;
        CustomTypeMappingClass = customTypeMappingClass;
        InlinedSqlValue = inlinedSqlValue;
        InlinedCSharpExpression = inlinedCSharpExpression;
        ColumnExpressionText = columnExpressionText;
        ColumnExpressionLambdaParam = columnExpressionLambdaParam;
    }

    public string ColumnSql { get; }
    public string? ValueTypeName { get; }
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// When the assignment value is a compile-time constant (literal), this contains the
    /// SQL literal (e.g., "0", "'hello'") that should be inlined directly instead of using a parameter.
    /// Null when the value requires a parameter binding.
    /// </summary>
    public string? InlinedSqlValue { get; }

    /// <summary>
    /// The original C# expression for inlined constants (e.g., "false", "\"hello\"").
    /// Used by the standalone interceptor path to pass the value to AddSetClauseBoxed.
    /// </summary>
    public string? InlinedCSharpExpression { get; }

    /// <summary>
    /// The RHS expression text when it contains column references (e.g., "e.EndTime - e.StartTime + startTimeUnix").
    /// Stored as text (not SqlExpr) to survive incremental pipeline caching.
    /// Null for simple captured-variable or inlined-constant assignments.
    /// </summary>
    public string? ColumnExpressionText { get; }

    /// <summary>
    /// The lambda parameter name for column expression parsing (e.g., "e").
    /// </summary>
    public string? ColumnExpressionLambdaParam { get; }

    /// <summary>
    /// Bound SqlExpr for column expressions, set during translation (not cached through pipeline).
    /// </summary>
    public SqlExpr? BoundValueExpression { get; internal set; }

    /// <summary>
    /// Gets whether this assignment's value is a compile-time constant inlined into the SQL.
    /// </summary>
    public bool IsInlined => InlinedSqlValue != null;

    /// <summary>
    /// Gets whether this assignment contains a computed expression with column references
    /// that must go through the SqlExpr bind/extract pipeline.
    /// </summary>
    public bool HasColumnExpression => ColumnExpressionText != null;

    public bool Equals(SetActionAssignment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ColumnSql == other.ColumnSql
            && ValueTypeName == other.ValueTypeName
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && InlinedSqlValue == other.InlinedSqlValue
            && InlinedCSharpExpression == other.InlinedCSharpExpression
            && ColumnExpressionText == other.ColumnExpressionText
            && ColumnExpressionLambdaParam == other.ColumnExpressionLambdaParam;
    }

    public override bool Equals(object? obj) => Equals(obj as SetActionAssignment);
    public override int GetHashCode() => HashCode.Combine(ColumnSql, ValueTypeName, CustomTypeMappingClass, InlinedSqlValue, ColumnExpressionText);
}
