using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a single assignment extracted from a Set(Action&lt;T&gt;) lambda body.
/// </summary>
internal sealed class SetActionAssignment : IEquatable<SetActionAssignment>
{
    public SetActionAssignment(string columnSql, string? valueTypeName, string? customTypeMappingClass,
        string? inlinedSqlValue = null, string? inlinedCSharpExpression = null)
    {
        ColumnSql = columnSql;
        ValueTypeName = valueTypeName;
        CustomTypeMappingClass = customTypeMappingClass;
        InlinedSqlValue = inlinedSqlValue;
        InlinedCSharpExpression = inlinedCSharpExpression;
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
    /// Gets whether this assignment's value is a compile-time constant inlined into the SQL.
    /// </summary>
    public bool IsInlined => InlinedSqlValue != null;

    public bool Equals(SetActionAssignment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ColumnSql == other.ColumnSql
            && ValueTypeName == other.ValueTypeName
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && InlinedSqlValue == other.InlinedSqlValue
            && InlinedCSharpExpression == other.InlinedCSharpExpression;
    }

    public override bool Equals(object? obj) => Equals(obj as SetActionAssignment);
    public override int GetHashCode() => HashCode.Combine(ColumnSql, ValueTypeName, CustomTypeMappingClass, InlinedSqlValue);
}
